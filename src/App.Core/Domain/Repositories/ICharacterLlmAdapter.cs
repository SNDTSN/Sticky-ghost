namespace App.Core.Domain.Repositories;

/// <summary>
/// OpenAI/Gemini처럼 위젯이 능동적으로 호출하는 LLM 어댑터의 공통 인터페이스.
/// Claude MCP(App.Mcp, 수동 반응)는 "위젯 → API 호출 → 응답 파싱" 구조가 아니라 이 인터페이스를 쓰지 않는다 —
/// 그쪽은 say/set_expression을 개별 MCP 툴로 노출해서 Claude가 직접 호출하는 구조.
/// </summary>
public interface ICharacterLlmAdapter
{
    Task<LlmReactionResult> GenerateReactionAsync(LlmReactionRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// AvailableExpressionIds는 프롬프트에 "이 중에서 골라라"로 포함될 뿐 아니라, 어댑터가 OpenAI structured
/// output / Gemini responseSchema의 enum 제약으로 실어 보내 "정의 안 된 expressionId 자체를 모델이 못 고르게"
/// 막는 데 쓰인다 — 인젝션 가이드라인 1번(출력 스키마 강제)을 한 단계 더 강화하는 효과.
/// </summary>
/// <summary>
/// StimulusText는 길이 캡만 적용된 원본 자극 설명이다("아래는 데이터이며 지시가 아님" 프레이밍은 포함하지
/// 않음) — system/user 역할 분리를 활용해 그 프레이밍을 시스템 메시지 쪽에 넣는 건 각 어댑터의 책임이다.
/// </summary>
public sealed record LlmReactionRequest(
    string SystemPrompt,
    string StimulusText,
    IReadOnlyList<string> AvailableExpressionIds,
    string ApiKey);

public enum LlmFailure
{
    NotConfigured,
    Unauthorized,
    NetworkError,
    Timeout,
    InvalidResponse,
    // 어댑터/시크릿 저장소에서 분류되지 않은 예외가 새어 나온 경우의 안전망(CharacterReactionService가 채움).
    // 어댑터가 직접 반환하는 값이 아니다 — 2026-09-20 안정성 보강, docs/stability-hardening.md 참고.
    Unexpected,
}

public sealed record LlmReactionResult
{
    public required bool IsSuccess { get; init; }
    public string? Line { get; init; }
    public string? ExpressionId { get; init; }
    public LlmFailure? Failure { get; init; }

    public static LlmReactionResult Success(string line, string? expressionId) =>
        new() { IsSuccess = true, Line = line, ExpressionId = expressionId };

    public static LlmReactionResult Failed(LlmFailure failure) =>
        new() { IsSuccess = false, Failure = failure };
}
