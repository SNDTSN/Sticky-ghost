using System.Text.Json;
using System.Text.Json.Serialization;
using App.Core.Diagnostics;
using App.Core.Domain.Services;
using App.Core.Infrastructure.Llm;

namespace App.Core.Infrastructure.FileSystem;

/// <summary>LLM provider/모델명처럼 비밀이 아닌 설정값. API 키는 여기 넣지 않는다 — ISecretStore 몫.</summary>
public sealed record AppSettings
{
    public string LlmProvider { get; init; } = LlmProviderCatalog.OpenAi;
    public string OpenAiModel { get; init; } = "gpt-4o-mini";
    // gemini-2.5-flash처럼 버전 고정된 이름은 구글이 조용히 단종시키면 generateContent가 404로 죽는다
    // (models.list엔 여전히 뜨는데 실제 호출만 막힘 — 2026-09 AQ 키로 실측). "latest" 별칭은 그 문제를 피한다.
    public string GeminiModel { get; init; } = "gemini-flash-latest";

    /// <summary>설정창 라디오 버튼과 같은 값들. 파일에 그 밖의 값이 있으면 Load가 100으로 바로잡는다.</summary>
    public static readonly IReadOnlyList<int> AllowedCharacterScales = [50, 100, 150, 200];

    public bool CharacterVisible { get; init; } = true;
    public int CharacterScale { get; init; } = 100;
    public string? SelectedCharacterPackId { get; init; }

    // 파일에는 숫자가 아니라 이름으로 저장한다 — 숫자면 enum 순서가 바뀔 때 뜻이 조용히 뒤집힌다.
    // 이 필드가 없는 예전 settings.json은 기본값(긴급 우선)으로 읽혀 기존 사용자의 순서가 그대로다.
    // 모르는 이름이면 다른 필드처럼 읽기 실패로 설정 전체가 기본값이 된다(Load 참고, 원인은 AppLog에 남는다).
    [JsonConverter(typeof(JsonStringEnumConverter<TodoSortOrder>))]
    public TodoSortOrder TodoSortOrder { get; init; } = TodoSortOrder.UrgentFirst;
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
    /// 파싱은 됐지만 값이 쓸 수 없는 것이면(모르는 provider 등) 그 필드만 바로잡는다 — <see cref="Normalize"/> 참고.
    /// </summary>
    public AppSettings Load()
    {
        if (!File.Exists(_filePath))
            return new AppSettings();

        try
        {
            var json = File.ReadAllText(_filePath);
            return Normalize(JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings());
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

    /// <summary>
    /// JSON으로는 읽혔지만 앱이 쓸 수 없는 값을 필드 단위로 기본값(팩 id는 null)으로 바꾼다. 이게 없으면 모르는 provider 하나가
    /// MainWindow 생성자의 LlmProviderCatalog.ApiKeySecretName에서 예외가 되어 앱이 뜨지 않는다.
    /// 고친 값은 파일에 다시 쓰지 않는다 — 다음에 사용자가 설정을 저장할 때 자연히 반영된다. 그래서 Load마다 같은 로그가 남을 수 있다.
    /// </summary>
    private static AppSettings Normalize(AppSettings s)
    {
        var defaults = new AppSettings();

        // 대소문자만 틀린 값("openai")은 의도가 분명하므로 정식 이름으로 바로잡는다.
        var provider = LlmProviderCatalog.All.FirstOrDefault(p => string.Equals(p, s.LlmProvider, StringComparison.OrdinalIgnoreCase));
        if (provider is null)
        {
            AppLog.Write("settings", $"알 수 없는 LlmProvider '{s.LlmProvider}' — {defaults.LlmProvider}로 대체");
            provider = defaults.LlmProvider;
        }

        var scale = s.CharacterScale;
        if (!AppSettings.AllowedCharacterScales.Contains(scale))
        {
            AppLog.Write("settings", $"허용되지 않는 CharacterScale {scale} — {defaults.CharacterScale}로 대체");
            scale = defaults.CharacterScale;
        }

        // JSON에 null이 적혀 있으면 non-nullable string에도 null이 들어온다.
        var openAiModel = s.OpenAiModel;
        if (string.IsNullOrWhiteSpace(openAiModel))
        {
            AppLog.Write("settings", $"OpenAiModel이 비어 있음 — {defaults.OpenAiModel}로 대체");
            openAiModel = defaults.OpenAiModel;
        }

        var geminiModel = s.GeminiModel;
        if (string.IsNullOrWhiteSpace(geminiModel))
        {
            AppLog.Write("settings", $"GeminiModel이 비어 있음 — {defaults.GeminiModel}로 대체");
            geminiModel = defaults.GeminiModel;
        }

        // 문자열 변환기도 숫자는 그대로 받아들여서(예: 5) 정의되지 않은 enum 값이 들어올 수 있다.
        var sortOrder = s.TodoSortOrder;
        if (!Enum.IsDefined(sortOrder))
        {
            AppLog.Write("settings", $"알 수 없는 TodoSortOrder {(int)sortOrder} — {defaults.TodoSortOrder}로 대체");
            sortOrder = defaults.TodoSortOrder;
        }

        // 값만 지운 "" 도 "선택한 팩 없음"이다. 그대로 두면 설정창이 "선택했던 캐릭터()를 찾지 못함"으로 안내한다.
        // 의도가 분명해서 provider 대소문자처럼 로그 없이 바로잡는다.
        var packId = string.IsNullOrWhiteSpace(s.SelectedCharacterPackId) ? null : s.SelectedCharacterPackId;

        return s with
        {
            LlmProvider = provider,
            SelectedCharacterPackId = packId,
            CharacterScale = scale,
            OpenAiModel = openAiModel,
            GeminiModel = geminiModel,
            TodoSortOrder = sortOrder,
        };
    }
}
