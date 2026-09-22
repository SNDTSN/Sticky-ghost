using System.Net;
using App.Core.Diagnostics;
using App.Core.Domain.Repositories;

namespace App.Core.Infrastructure.Llm;

/// <summary>
/// LLM 호출 실패를 <c>app.log</c>의 <c>[llm]</c>으로 남긴다. 두 어댑터가 같은 형식을 쓰도록 한곳에 모았다.
///
/// 왜 필요한가: 실패는 대부분 캐릭터 폴백 대사 한 줄로만 드러나는데, 그 대사만으로는 원인을 알 수 없다.
/// 특히 모델명 오타/단종(404)과 쿼터 초과(429)는 화면에서 구분이 안 돼 "인터넷 문제"로 오해하기 쉬웠다.
///
/// 남기지 않는 것: API 키, 예외 객체(ToString()에 키가 섞일 수 있음 — docs/stability-hardening.md C3-b),
/// 프롬프트와 자극 문구(할 일 제목·사용자 채팅이 들어가므로 프라이버시), 응답 본문(요청 내용이 되비칠 수 있음).
/// 상태 코드와 모델명만으로 원인 구분이 되므로 그 이상은 남기지 않는다.
/// </summary>
internal static class LlmFailureLog
{
    // 터치 반응마다 요청이 나가므로 모델명을 한 번 잘못 적으면 같은 줄이 끝없이 쌓인다.
    // app.log는 1MB에서 1세대만 남기고 회전하므로, 도배를 두면 [pack] 같은 다른 기록이 밀려난다.
    private static readonly TimeSpan SuppressWindow = TimeSpan.FromSeconds(30);

    private static readonly object Gate = new();
    private static readonly Dictionary<string, DateTime> LastWrittenUtc = new();

    /// <param name="status">HTTP 응답이 있었던 경우의 상태 코드. 전송 자체가 실패한 경우(타임아웃 등)는 null.</param>
    /// <param name="reason">상태 코드로 설명되지 않는 사유의 짧은 식별자. 키 값이 들어가면 안 된다.</param>
    internal static void Write(
        string provider,
        string model,
        LlmFailure failure,
        HttpStatusCode? status = null,
        string? reason = null)
    {
        // 모델명까지 키에 넣는다 — 모델명을 고쳐 다시 시도했을 때 새 줄이 바로 보여야 오타를 고쳤는지 알 수 있다.
        var key = $"{provider}|{model}|{failure}|{(int?)status}|{reason}";

        lock (Gate)
        {
            if (LastWrittenUtc.TryGetValue(key, out var last) && DateTime.UtcNow - last < SuppressWindow)
                return;

            LastWrittenUtc[key] = DateTime.UtcNow;
        }

        var statusPart = status is { } s ? $" http={(int)s}" : string.Empty;
        var reasonPart = reason is { } r ? $" reason={r}" : string.Empty;
        AppLog.Write("llm", $"{provider} {failure}{statusPart} model={model}{reasonPart}");
    }
}
