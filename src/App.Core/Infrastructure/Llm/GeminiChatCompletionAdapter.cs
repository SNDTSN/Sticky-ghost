using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using App.Core.Domain.Repositories;

namespace App.Core.Infrastructure.Llm;

/// <summary>
/// <see cref="ICharacterLlmAdapter"/>의 Gemini(Google AI Studio Developer API) 구현. OpenAI 어댑터와
/// 대칭 구조를 유지하려고 공식 SDK 대신 직접 HttpClient로 REST 호출(character-widget-design.md 참고).
/// </summary>
public sealed class GeminiChatCompletionAdapter : ICharacterLlmAdapter
{
    private const string EndpointTemplate = "https://generativelanguage.googleapis.com/v1beta/models/{0}:generateContent";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private readonly HttpClient _httpClient;
    private readonly string _model;

    /// <param name="httpClient">호출자가 만들어 공유하는 인스턴스를 주입받는다 — 소켓 고갈 방지를 위해 어댑터가 직접 생성하지 않음.</param>
    /// <param name="model">Gemini 모델 ID(예: gemini-2.5-flash). 모델 라인업이 자주 바뀌므로 하드코딩하지 않고 주입받는다.</param>
    public GeminiChatCompletionAdapter(HttpClient httpClient, string model)
    {
        _httpClient = httpClient;
        _model = model;
    }

    public async Task<LlmReactionResult> GenerateReactionAsync(LlmReactionRequest request, CancellationToken cancellationToken)
    {
        using var timeoutCts = new CancellationTokenSource(RequestTimeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

        HttpRequestMessage httpRequest;
        try
        {
            httpRequest = BuildHttpRequest(request);
        }
        catch (FormatException)
        {
            // API 키에 헤더로 못 보내는 문자(개행 등)가 섞인 경우 — Headers.Add가 FormatException을 던진다.
            // 사실상 잘못된 키이므로 인증 실패로 분류한다.
            // 예외 자체는 메시지에 키가 들어 있어 남길 수 없지만, 사유 식별자만은 안전하게 남긴다.
            LlmFailureLog.Write(LlmProviderCatalog.Gemini, _model, LlmFailure.Unauthorized, reason: "malformed-key-header");
            return LlmReactionResult.Failed(LlmFailure.Unauthorized);
        }

        using var httpRequestScope = httpRequest;

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(httpRequest, linkedCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // 호출자가 취소한 게 아니라면(자체 15초 타임아웃이든, HttpClient 기본 타임아웃이든) 전부 Timeout으로 분류.
            LlmFailureLog.Write(LlmProviderCatalog.Gemini, _model, LlmFailure.Timeout);
            return LlmReactionResult.Failed(LlmFailure.Timeout);
        }
        catch (HttpRequestException)
        {
            LlmFailureLog.Write(LlmProviderCatalog.Gemini, _model, LlmFailure.NetworkError);
            return LlmReactionResult.Failed(LlmFailure.NetworkError);
        }

        using (response)
        {
            // 2026-06 기준 신규 발급 키(AQ. 접두, 구글 클라우드 서비스 계정에 묶인 Auth key)는 인증 실패 시
            // 401을 돌려주는 사례가 보고됨 — 구 Standard key(AIzaSy 접두)의 403/400과 다름. 둘 다 커버해야 한다.
            if (response.StatusCode is HttpStatusCode.Forbidden or HttpStatusCode.Unauthorized)
            {
                LlmFailureLog.Write(LlmProviderCatalog.Gemini, _model, LlmFailure.Unauthorized, response.StatusCode);
                return LlmReactionResult.Failed(LlmFailure.Unauthorized);
            }

            if (!response.IsSuccessStatusCode)
            {
                // 구 Standard key는 키 오류도 400(Bad Request)으로 뭉뚱그려 반환하는 경우가 있었다 — 본문 메시지로
                // "API 키 문제"인지 그냥 잘못된 요청인지 구분해야 사용자에게 정확한 폴백 문구를 보여줄 수 있다.
                if (response.StatusCode == HttpStatusCode.BadRequest && await LooksLikeApiKeyErrorAsync(response, linkedCts.Token))
                {
                    LlmFailureLog.Write(LlmProviderCatalog.Gemini, _model, LlmFailure.Unauthorized, response.StatusCode);
                    return LlmReactionResult.Failed(LlmFailure.Unauthorized);
                }

                var httpFailure = LlmHttpFailure.Classify(response.StatusCode);
                LlmFailureLog.Write(LlmProviderCatalog.Gemini, _model, httpFailure, response.StatusCode);
                return LlmReactionResult.Failed(httpFailure);
            }

            string body;
            try
            {
                body = await response.Content.ReadAsStringAsync(linkedCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                LlmFailureLog.Write(LlmProviderCatalog.Gemini, _model, LlmFailure.Timeout);
                return LlmReactionResult.Failed(LlmFailure.Timeout);
            }

            // ParseResponse는 순수 함수로 두고(본문만 받음), 로깅은 모델명을 아는 이 자리에서 한 번만 한다.
            var result = ParseResponse(body);
            if (!result.IsSuccess && result.Failure is { } failure)
                LlmFailureLog.Write(LlmProviderCatalog.Gemini, _model, failure);

            return result;
        }
    }

    private HttpRequestMessage BuildHttpRequest(LlmReactionRequest request)
    {
        var payload = new
        {
            systemInstruction = new
            {
                parts = new[] { new { text = LlmReactionPrompt.BuildSystemPrompt(request) } },
            },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = request.StimulusText } } },
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema = BuildResponseSchema(request.AvailableExpressionIds),
            },
        };

        var endpoint = string.Format(EndpointTemplate, _model);
        var httpRequest = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(payload),
        };
        // 쿼리스트링 ?key=...는 서버 접근 로그에 API 키가 그대로 남을 수 있어 헤더 방식을 쓴다.
        httpRequest.Headers.Add("x-goog-api-key", request.ApiKey);
        return httpRequest;
    }

    // Gemini responseSchema는 OpenAI structured output과 다른 방언을 쓴다 — 타입명이 대문자이고,
    // null 허용은 type 배열이 아니라 nullable 플래그로 표현한다.
    private static object BuildResponseSchema(IReadOnlyList<string> availableExpressionIds)
    {
        var expressionIdProperty = availableExpressionIds.Count > 0
            ? new Dictionary<string, object>
            {
                ["type"] = "STRING",
                ["nullable"] = true,
                ["enum"] = availableExpressionIds,
            }
            : new Dictionary<string, object>
            {
                ["type"] = "STRING",
                ["nullable"] = true,
            };

        return new Dictionary<string, object>
        {
            ["type"] = "OBJECT",
            ["properties"] = new Dictionary<string, object>
            {
                ["line"] = new Dictionary<string, object> { ["type"] = "STRING" },
                ["expressionId"] = expressionIdProperty,
            },
            ["required"] = new[] { "line" },
        };
    }

    private static async Task<bool> LooksLikeApiKeyErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            using var envelope = JsonDocument.Parse(body);
            var message = envelope.RootElement.GetProperty("error").GetProperty("message").GetString() ?? "";
            return message.Contains("API key", StringComparison.OrdinalIgnoreCase);
        }
        catch (JsonException)
        {
            return false;
        }
        catch (KeyNotFoundException)
        {
            // 본문이 JSON이지만 error.message 구조가 아닌 경우(프록시 응답 등).
            return false;
        }
        catch (InvalidOperationException)
        {
            // 루트가 객체가 아니거나 message가 문자열이 아닌 경우.
            return false;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
    }

    private static LlmReactionResult ParseResponse(string body)
    {
        try
        {
            using var envelope = JsonDocument.Parse(body);
            var content = envelope.RootElement
                .GetProperty("candidates")[0]
                .GetProperty("content")
                .GetProperty("parts")[0]
                .GetProperty("text")
                .GetString();

            return LlmReactionPrompt.ParseReactionContent(content);
        }
        catch (JsonException)
        {
            return LlmReactionResult.Failed(LlmFailure.InvalidResponse);
        }
        catch (KeyNotFoundException)
        {
            return LlmReactionResult.Failed(LlmFailure.InvalidResponse);
        }
        catch (IndexOutOfRangeException)
        {
            return LlmReactionResult.Failed(LlmFailure.InvalidResponse);
        }
        catch (InvalidOperationException)
        {
            return LlmReactionResult.Failed(LlmFailure.InvalidResponse);
        }
    }
}
