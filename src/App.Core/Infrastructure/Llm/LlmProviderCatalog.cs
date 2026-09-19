namespace App.Core.Infrastructure.Llm;

/// <summary>
/// LLM provider 식별자와, provider별로 갈라져야 하는 값(시크릿 이름 등)을 한곳에 모아둔다.
/// <see cref="App.Core.Infrastructure.FileSystem.AppSettings.LlmProvider"/>에 저장되는 문자열과 반드시 일치해야 한다.
/// </summary>
public static class LlmProviderCatalog
{
    public const string OpenAi = "OpenAi";
    public const string Gemini = "Gemini";

    public static readonly IReadOnlyList<string> All = new[] { OpenAi, Gemini };

    public static string ApiKeySecretName(string provider) => provider switch
    {
        OpenAi => "llm.openai.apikey",
        Gemini => "llm.gemini.apikey",
        _ => throw new ArgumentOutOfRangeException(nameof(provider), provider, "알 수 없는 LLM provider"),
    };
}
