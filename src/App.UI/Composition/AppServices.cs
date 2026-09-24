using System;
using System.IO;
using System.Net.Http;
using App.Core.Domain.Events;
using App.Core.Domain.Repositories;
using App.Core.Domain.Services;
using App.Core.Infrastructure.FileSystem;
using App.Core.Infrastructure.Llm;
using App.Core.Infrastructure.Sqlite;
using App.Platform;

namespace App.UI.Composition;

/// <summary>
/// 창이 아닌 것들(저장소, 도메인 서비스, 캐릭터 팩 서비스, HttpClient)을 한 곳에서 만들어 들고 있는다.
/// 예전에는 MainWindow 생성자가 이 조립을 전부 했다(KNOWN_ISSUES #27). 지금은 만드는 곳만 창 밖(App)으로 옮겼고,
/// 수명은 아직 메인 창과 같다 — 창이 닫혀도 서비스가 살아 있어야 하는 기능(트레이 상주 등)을 만들 때 수명을 나눈다.
/// 창·오버레이·IPC 서버처럼 창이나 UI 스레드에 기대는 것은 여기에 넣지 않는다.
/// </summary>
public sealed class AppServices
{
    public required ISecretStore SecretStore { get; init; }
    public required WindowStateStore WindowStateStore { get; init; }
    public required AppSettingsStore AppSettingsStore { get; init; }

    public required IMemoRepository MemoRepository { get; init; }
    public required ITodoRepository TodoRepository { get; init; }
    public required ICategoryRepository CategoryRepository { get; init; }
    public required TodoEventBus TodoEventBus { get; init; }
    public required TodoService TodoService { get; init; }

    // 캐릭터 팩 폴더는 오버레이와 설정창 두 곳에서 쓴다 — 같은 경로를 두 번 조립하지 않도록 여기서 한 번만 계산한다.
    public required string PacksRootDir { get; init; }
    public required string BuiltInPackPath { get; init; }
    public required CharacterPackService CharacterPackService { get; init; }
    // 팩 목록 스캔은 캐릭터 표시(오버레이)와 설정창 두 곳에서 한다 — 각자 만들지 않고 하나를 같이 쓴다(KNOWN_ISSUES #28 B-8).
    public required CharacterPackScanner PackScanner { get; init; }

    // HttpClient는 소켓 고갈 방지를 위해 앱 수명 동안 하나만 재사용한다.
    public required HttpClient HttpClient { get; init; }

    // ISecretStore는 실행 진입점(App.Windows)이 만들어 넘겨준다 — App.UI는 구체 구현(DPAPI 등)을 모른다.
    public static AppServices Create(ISecretStore secretStore)
    {
        var dataDir = AppPaths.DataDir;
        Directory.CreateDirectory(dataDir);

        var connectionString = $"Data Source={Path.Combine(dataDir, "stickyghost.db")}";
        SqliteSchemaInitializer.EnsureCreated(connectionString);

        var todoRepository = new SqliteTodoRepository(connectionString);
        // 버스를 먼저 만들어 TodoService에 주입한다. 구독(캐릭터 반응)은 오버레이를 만드는 MainWindow가 건다.
        var todoEventBus = new TodoEventBus();
        var todoService = new TodoService(
            todoRepository,
            new SqliteCompletionLogStore(connectionString),
            todoEventBus,
            new SystemClock(),
            TimeSpan.FromMinutes(30));

        var packsRootDir = Path.Combine(AppContext.BaseDirectory, "CharacterPacks");
        var builtInPackPath = Path.Combine(packsRootDir, "default");
        // 로더는 상태가 없어 팩 서비스와 스캐너가 하나를 같이 쓴다.
        var packLoader = new JsonCharacterPackLoader();

        return new AppServices
        {
            SecretStore = secretStore,
            WindowStateStore = new WindowStateStore(Path.Combine(dataDir, "window-state.json")),
            AppSettingsStore = new AppSettingsStore(Path.Combine(dataDir, "settings.json")),
            MemoRepository = new SqliteMemoRepository(connectionString),
            TodoRepository = todoRepository,
            CategoryRepository = new SqliteCategoryRepository(connectionString),
            TodoEventBus = todoEventBus,
            TodoService = todoService,
            PacksRootDir = packsRootDir,
            BuiltInPackPath = builtInPackPath,
            CharacterPackService = new CharacterPackService(packLoader, builtInPackPath),
            PackScanner = new CharacterPackScanner(packLoader),
            HttpClient = new HttpClient(),
        };
    }

    /// <summary>설정의 provider/모델로 캐릭터 반응 서비스를 조립한다. 설정이 바뀌면 다시 불러 새로 만든다.</summary>
    public CharacterReactionService BuildReactionService(AppSettings settings)
    {
        ICharacterLlmAdapter llmAdapter = settings.LlmProvider == LlmProviderCatalog.Gemini
            ? new GeminiChatCompletionAdapter(HttpClient, settings.GeminiModel)
            : new OpenAiChatCompletionAdapter(HttpClient, settings.OpenAiModel);

        return new CharacterReactionService(
            llmAdapter, SecretStore, CharacterPackService, LlmProviderCatalog.ApiKeySecretName(settings.LlmProvider));
    }
}
