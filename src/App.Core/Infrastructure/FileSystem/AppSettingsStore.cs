using System.Text.Json;

namespace App.Core.Infrastructure.FileSystem;

/// <summary>LLM provider/모델명처럼 비밀이 아닌 설정값. API 키는 여기 넣지 않는다 — ISecretStore 몫.</summary>
public sealed record AppSettings
{
    public string LlmProvider { get; init; } = "OpenAi";
    public string OpenAiModel { get; init; } = "gpt-4o-mini";
    // gemini-2.5-flash처럼 버전 고정된 이름은 구글이 조용히 단종시키면 generateContent가 404로 죽는다
    // (models.list엔 여전히 뜨는데 실제 호출만 막힘 — 2026-09 AQ 키로 실측). "latest" 별칭은 그 문제를 피한다.
    public string GeminiModel { get; init; } = "gemini-flash-latest";

    public bool CharacterVisible { get; init; } = true;
    // 50 | 100 | 150 | 200 외 값은 SettingsWindow가 100으로 취급한다.
    public int CharacterScale { get; init; } = 100;
    public string? SelectedCharacterPackId { get; init; }
}

/// <summary>
/// 평문 JSON 파일로 저장하는 비밀 아닌 설정 저장소. OS 종속 API를 쓰지 않으므로 App.Platform 뒤로 격리할
/// 필요 없이 App.Core에 그대로 둔다(Windows/Mac 겸용).
/// </summary>
public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;

    public AppSettingsStore(string filePath)
    {
        _filePath = filePath;
    }

    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (JsonException)
        {
            // 설정 파일이 손상됐다고 앱을 못 띄우게 막을 이유는 없다 — 기본값으로 복구.
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(_filePath, JsonSerializer.Serialize(settings, SerializerOptions));
    }
}
