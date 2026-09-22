using System.Text.Json;
using App.Core.Diagnostics;

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

    /// <summary>
    /// 저장된 설정을 읽는다. **어떤 이유로든 실패하면 예외 대신 기본값을 돌려준다** —
    /// 이 호출은 MainWindow 생성자 경로에 있어서, 예외가 올라가면 설정 파일 하나 때문에 앱이 아예 못 뜬다.
    /// 무엇 때문에 기본값으로 돌아갔는지는 AppLog에 남긴다(증상이 "설정이 저절로 초기화됨"이라 기록이 없으면 추적이 어렵다).
    /// </summary>
    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
        }
        catch (JsonException ex)
        {
            // 설정 파일이 손상됐다고 앱을 못 띄우게 막을 이유는 없다 — 기본값으로 복구.
            AppLog.Write("settings", ex);
            return new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 백신/동기화 도구가 파일을 잠깐 잡고 있거나 권한이 없는 경우. 읽기 실패도 기동을 막지 않는다.
            AppLog.Write("settings", ex);
            return new AppSettings();
        }
    }

    /// <summary>
    /// 설정을 저장한다. **실패하면 예외를 던진다** — 이 저장은 사용자가 "저장" 버튼을 눌러 요청한 것이므로
    /// 조용히 묻히면 안 된다(SettingsWindow가 잡아서 알린다). 알릴 수 없는 자리에서 부를 거면 호출부가 잡아야 한다.
    /// </summary>
    public void Save(AppSettings settings) =>
        AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(settings, SerializerOptions));
}
