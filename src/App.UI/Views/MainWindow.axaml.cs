using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using App.Core.Domain.Entities;
using App.Core.Domain.Events;
using App.Core.Domain.Repositories;
using App.Core.Domain.Services;
using App.Core.Infrastructure.FileSystem;
using App.Core.Infrastructure.Ipc;
using App.Core.Infrastructure.Llm;
using App.Core.Infrastructure.Sqlite;
using App.Platform;
using App.Platform.Stub;
using App.UI.Services;
using App.UI.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace App.UI.Views;

public partial class MainWindow : Window
{
    // HttpClient는 소켓 고갈 방지를 위해 앱 수명 동안 하나만 재사용한다.
    private static readonly HttpClient HttpClient = new();

    // TODO: 임시 배선. DI 컨테이너가 생기면 정식 조립 방식으로 교체.
    private readonly IMemoRepository _memoRepository;
    private readonly IWindowBehavior _windowBehavior = new StubWindowBehavior();
    private readonly List<MemoWindow> _memoWindows = new();
    private readonly CharacterPackService _characterPackService;
    private readonly CharacterIpcServer _characterIpcServer;
    private readonly ISecretStore _secretStore;
    private readonly AppSettingsStore _appSettingsStore;
    private CharacterReactionService _characterReactionService;
    private CharacterPreviewWindow? _activeCharacterWindow;

    // ISecretStore는 실행 진입점(App.Windows)이 조립해서 넘겨준다 — App.UI는 구체 구현(DPAPI 등)을 모른다.
    public MainWindow(ISecretStore secretStore)
    {
        InitializeComponent();

        _secretStore = secretStore;

        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickyGhost");
        Directory.CreateDirectory(dataDir);

        var connectionString = $"Data Source={Path.Combine(dataDir, "stickyghost.db")}";
        SqliteSchemaInitializer.EnsureCreated(connectionString);

        _memoRepository = new SqliteMemoRepository(connectionString);

        var todoRepository = new SqliteTodoRepository(connectionString);
        var categoryRepository = new SqliteCategoryRepository(connectionString);
        var todoService = new TodoService(
            todoRepository,
            new SqliteCompletionLogStore(connectionString),
            new NoOpTodoEventBus(),
            new SystemClock(),
            TimeSpan.FromMinutes(30));

        var mainViewModel = new MainViewModel(todoRepository, categoryRepository, todoService)
        {
            ConfirmDeleteTodoRequested = () => ConfirmDialog.ShowAsync(this, "이 할 일을 삭제하시겠습니까?"),
        };
        DataContext = mainViewModel;

        var builtInPackPath = Path.Combine(AppContext.BaseDirectory, "CharacterPacks", "default");
        _characterPackService = new CharacterPackService(new JsonCharacterPackLoader(), builtInPackPath);

        _appSettingsStore = new AppSettingsStore(Path.Combine(dataDir, "settings.json"));
        _characterReactionService = BuildReactionService(_appSettingsStore.Load());

        // App.Mcp(Claude가 스폰하는 별도 프로세스)가 명명 파이프로 say/setExpression을 보내면 여기서 받는다.
        // 지금은 "캐릭터 미리보기" 창에만 반영 — 상시 캐릭터 오버레이 창은 별도 작업.
        var ipcHandler = new CharacterIpcRequestHandler(_characterPackService, () => _activeCharacterWindow);
        _characterIpcServer = new CharacterIpcServer(ipcHandler);
        _characterIpcServer.Start();

        foreach (var memo in _memoRepository.GetAll())
            OpenMemoWindow(memo);

        Closing += OnClosing;
    }

    // TODO: 로더 검증용 임시 버튼 핸들러 — 표정 렌더링 붙으면 정식 캐릭터 창 조립 로직으로 교체.
    private void OnCharacterPreviewClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            var builtInPackPath = Path.Combine(AppContext.BaseDirectory, "CharacterPacks", "default");
            var outcome = _characterPackService.LoadPack(builtInPackPath);
            var window = new CharacterPreviewWindow(outcome.Pack, _characterReactionService);
            _activeCharacterWindow = window;
            window.Closed += (_, _) =>
            {
                if (_activeCharacterWindow == window)
                    _activeCharacterWindow = null;
            };
            window.Show();
        }
        catch (InvalidOperationException ex)
        {
            _ = ConfirmDialog.ShowAsync(this, ex.Message);
        }
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        var packsRootDir = Path.Combine(AppContext.BaseDirectory, "CharacterPacks");
        await new SettingsWindow(_secretStore, _appSettingsStore, packsRootDir).ShowDialog(this);

        // provider/모델/키가 바뀌었을 수 있으니 재시작 없이 바로 반영되도록 다시 조립한다.
        _characterReactionService = BuildReactionService(_appSettingsStore.Load());
        // 설정창을 여는 동안 미리보기 창이 이미 떠 있었다면 그 창은 새로 조립된 서비스를 모르므로 직접 밀어준다.
        _activeCharacterWindow?.UpdateReactionService(_characterReactionService);
    }

    private CharacterReactionService BuildReactionService(AppSettings settings)
    {
        ICharacterLlmAdapter llmAdapter = settings.LlmProvider == LlmProviderCatalog.Gemini
            ? new GeminiChatCompletionAdapter(HttpClient, settings.GeminiModel)
            : new OpenAiChatCompletionAdapter(HttpClient, settings.OpenAiModel);

        return new CharacterReactionService(
            llmAdapter, _secretStore, _characterPackService, LlmProviderCatalog.ApiKeySecretName(settings.LlmProvider));
    }

    private void OnNewMemoClick(object? sender, RoutedEventArgs e)
    {
        var memo = new MemoNote
        {
            Color = "#FFF9C4",
            PositionX = 100,
            PositionY = 100,
            Width = 220,
            Height = 220,
        };
        _memoRepository.Save(memo);
        OpenMemoWindow(memo);
    }

    private void OpenMemoWindow(MemoNote memo)
    {
        var viewModel = new MemoNoteViewModel(memo, _memoRepository, _windowBehavior);
        var window = new MemoWindow(viewModel);
        _memoWindows.Add(window);
        window.Closed += (_, _) => _memoWindows.Remove(window);
        window.Show();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        foreach (var window in _memoWindows)
            window.FlushPendingSave();

        _characterIpcServer.Stop();
    }
}
