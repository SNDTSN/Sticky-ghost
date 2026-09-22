using System.Net;
using App.Core.Domain.Repositories;

namespace App.Core.Infrastructure.Llm;

/// <summary>
/// 성공이 아닌 HTTP 응답을 <see cref="LlmFailure"/>로 분류한다. 두 어댑터가 같은 규칙을 쓰도록 한곳에 모았다
/// — 규칙이 두 벌로 갈라지면 한쪽에만 반영되는 사고가 난다(KNOWN_ISSUES #20-1과 같은 종류의 문제).
///
/// 인증 실패(401/403)와 Gemini의 400(키 오류 본문) 판정은 provider마다 사정이 달라 각 어댑터가 먼저 처리하고,
/// 여기로는 그 외의 상태 코드만 넘어온다.
/// </summary>
internal static class LlmHttpFailure
{
    internal static LlmFailure Classify(HttpStatusCode status) => status switch
    {
        HttpStatusCode.NotFound => LlmFailure.ModelNotFound,
        HttpStatusCode.TooManyRequests => LlmFailure.RateLimited,
        // 5xx는 제공자 쪽 장애다 — 사용자 탓처럼 들리는 "인터넷" 안내를 하지 않는다.
        _ when (int)status >= 500 => LlmFailure.ProviderError,
        _ => LlmFailure.NetworkError,
    };
}
