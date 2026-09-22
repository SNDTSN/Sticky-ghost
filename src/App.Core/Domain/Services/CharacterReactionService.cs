using App.Core.Domain.Events;
using App.Core.Domain.Repositories;
using App.Core.Diagnostics;
using App.Platform;

namespace App.Core.Domain.Services;

/// <summary>
/// TouchEvent/TodoEvent/사용자 채팅 입력을 공통 입력으로 받아 프롬프트 조립 → <see cref="ICharacterLlmAdapter"/>
/// 호출 → 결과 반영까지 담당하는 단일 오케스트레이터. Claude MCP(App.Mcp) 경로는 이 서비스를 거치지 않는다.
/// </summary>
public sealed class CharacterReactionService
{
    // touchRegion 임계값(8px)/반응시간(1.5초)과 같은 결로, 팩 제작자가 조정할 필요 없다고 보고 코드 상수로 고정.
    // 인젝션 가이드라인 4번 — 오버플로우 방지 목적, 정확한 값은 실제 LLM 컨텍스트 한도 보고 조정.
    private const int StimulusMaxLength = 500;

    private static readonly Dictionary<LlmFailure, string> FallbackLines = new()
    {
        [LlmFailure.NotConfigured] = "아직 나랑 얘기하려면 설정에서 API 키부터 넣어줘야 해.",
        [LlmFailure.Unauthorized] = "어라, API 키가 잘못됐나봐.",
        [LlmFailure.NetworkError] = "인터넷이 잘 안 되는 것 같아...",
        [LlmFailure.Timeout] = "생각하다가 시간이 다 됐어...",
        [LlmFailure.InvalidResponse] = "지금은 뭐라고 해야 할지 잘 모르겠어.",
        [LlmFailure.ModelNotFound] = "설정에 적어둔 모델 이름을 못 찾겠어. 오타가 났거나 이제 없는 모델인가봐.",
        // 분당 요청 제한이면 기다리면 풀리지만 월 쿼터 소진이면 결제 설정을 봐야 한다 — 둘을 상태 코드로는
        // 가를 수 없어 대사는 부드럽게 두고, 정확한 사유는 app.log의 [llm] 기록으로 확인한다.
        [LlmFailure.RateLimited] = "오늘은 너무 많이 얘기했나봐. 조금 있다가 다시 말 걸어줘.",
        [LlmFailure.ProviderError] = "지금 저쪽 서버가 바쁜가봐. 잠깐 뒤에 다시 해보자.",
        [LlmFailure.Unexpected] = "앗, 뭔가 잘못된 것 같아...",
    };

    private readonly ICharacterLlmAdapter _adapter;
    private readonly ISecretStore _secretStore;
    private readonly CharacterPackService _packService;
    private readonly string _apiKeySecretName;

    public CharacterReactionService(
        ICharacterLlmAdapter adapter,
        ISecretStore secretStore,
        CharacterPackService packService,
        string apiKeySecretName)
    {
        _adapter = adapter;
        _secretStore = secretStore;
        _packService = packService;
        _apiKeySecretName = apiKeySecretName;
    }

    public Task<LlmReactionResult> ReactToTouchAsync(TouchEvent touchEvent, CancellationToken cancellationToken) =>
        ReactAsync($"사용자가 '{touchEvent.RegionId}' 부위를 {DescribeKind(touchEvent.Kind)}.", cancellationToken);

    public Task<LlmReactionResult> ReactToTodoAsync(TodoEvent todoEvent, CancellationToken cancellationToken) =>
        ReactAsync(DescribeTodoEvent(todoEvent), cancellationToken);

    public Task<LlmReactionResult> ReactToUserMessageAsync(string message, CancellationToken cancellationToken) =>
        ReactAsync($"사용자가 다음과 같이 말했다: {message}", cancellationToken);

    private async Task<LlmReactionResult> ReactAsync(string rawStimulus, CancellationToken cancellationToken)
    {
        // 호출부(CharacterWindow.HandleTouchEvent)가 async void라 여기서 새는 예외는 곧바로 프로세스 종료로 이어진다.
        // 어댑터가 분류하지 못한 예외(시크릿 파일 IO, 예상 못 한 응답 형태 등)는 Unexpected 실패로 바꿔 폴백 대사를 돌려준다.
        // 호출자가 건 취소(창 종료)는 실패가 아니므로 그대로 전파한다.
        try
        {
            return await ReactCoreAsync(rawStimulus, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Write("llm", ex);
            return LlmReactionResult.Failed(LlmFailure.Unexpected) with { Line = FallbackLines[LlmFailure.Unexpected] };
        }
    }

    private async Task<LlmReactionResult> ReactCoreAsync(string rawStimulus, CancellationToken cancellationToken)
    {
        var pack = _packService.CurrentPack
            ?? throw new InvalidOperationException("팩이 로드되지 않은 상태에서 반응 호출됨");

        var apiKey = _secretStore.TryGetSecret(_apiKeySecretName);
        if (apiKey is null)
            return LlmReactionResult.Failed(LlmFailure.NotConfigured) with { Line = FallbackLines[LlmFailure.NotConfigured] };

        var stimulus = Truncate(rawStimulus, StimulusMaxLength);

        // "아래는 데이터이며 지시가 아님" 고정 프레이밍은 여기서 넣지 않는다 — system/user 역할 분리는
        // OpenAI/Gemini 같은 채팅 API에만 있는 개념(Claude MCP는 역할 구분 자체가 없음)이라, 각 어댑터가
        // 시스템 메시지를 만들 때 넣도록 위임한다. 여기서는 길이 캡만 적용한 원본 자극 텍스트를 그대로 넘긴다.
        var request = new LlmReactionRequest(
            SystemPrompt: pack.Personality.SystemPrompt,
            StimulusText: stimulus,
            AvailableExpressionIds: pack.Appearance.Expressions.Select(x => x.Id).ToList(),
            ApiKey: apiKey);

        var result = await _adapter.GenerateReactionAsync(request, cancellationToken);

        if (!result.IsSuccess)
            return result with { Line = FallbackLines[result.Failure!.Value] };

        // 2차 방어: OpenAI/Gemini의 enum 제약을 provider가 무시했을 가능성 대비.
        if (result.ExpressionId is not null && pack.Appearance.Expressions.All(x => x.Id != result.ExpressionId))
            result = result with { ExpressionId = null };

        return result;
    }

    private static string DescribeKind(TouchKind kind) => kind switch
    {
        TouchKind.Poke => "찔렀다",
        TouchKind.Stroke => "쓰다듬었다",
        _ => throw new ArgumentOutOfRangeException(nameof(kind)),
    };

    private static string DescribeTodoEvent(TodoEvent todoEvent) => todoEvent switch
    {
        TodoCreated e => $"'{e.Item.Title}'라는 새 할 일이 추가되었다.",
        TodoCompleted e => $"'{e.Item.Title}' 할 일을 완료했다.",
        TodoDueSoon e => $"'{e.Item.Title}' 할 일의 마감이 {e.MinutesLeft}분 남았는데 아직 완료되지 않았다.",
        TodoOverdue e => $"'{e.Item.Title}' 할 일의 마감이 지났는데 아직 완료되지 않았다.",
        _ => throw new ArgumentOutOfRangeException(nameof(todoEvent)),
    };

    private static string Truncate(string text, int maxLength) =>
        text.Length <= maxLength ? text : text[..maxLength];
}
