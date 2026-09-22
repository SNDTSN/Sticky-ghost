using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using App.Core.Diagnostics;
using App.Core.Domain.Entities;
using App.Core.Domain.Events;
using App.Core.Domain.Repositories;
using App.Core.Domain.Services;
using App.Core.Infrastructure.FileSystem;
using App.Core.Infrastructure.Ipc;
using App.Core.Infrastructure.Llm;
using App.Core.Infrastructure.Sqlite;
using App.Platform;
using App.UI.Services;
using App.UI.ViewModels;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace App.UI.Views;

public partial class MainWindow : Window
{
    // HttpClient는 소켓 고갈 방지를 위해 앱 수명 동안 하나만 재사용한다.
    private static readonly HttpClient HttpClient = new();

    // TODO: 임시 배선. DI 컨테이너가 생기면 정식 조립 방식으로 교체.
    private readonly IMemoRepository _memoRepository;
    private readonly IWindowBehavior _windowBehavior;
    private readonly List<MemoWindow> _memoWindows = new();
    private readonly CharacterPackService _characterPackService;
    // 캐릭터 팩 폴더는 생성자와 설정창 열기 두 곳에서 쓴다 — 같은 경로를 두 번 조립하지 않도록 한 번만 계산한다.
    private readonly string _packsRootDir;
    private readonly CharacterIpcServer _characterIpcServer;
    private readonly ISecretStore _secretStore;
    private readonly AppSettingsStore _appSettingsStore;
    private readonly CharacterOverlayController _characterOverlay;
    private PixelPoint? _lastMemoPosition;
    private readonly WindowStateStore _windowStateStore;
    private readonly WindowPlacementTracker _placementTracker;

    private const int MemoCascadeOffset = 24;
    private const int MemoCascadeBasePos = 100;
    private const int DefaultMemoSize = 220;

    // ISecretStore/IWindowBehavior는 실행 진입점(App.Windows)이 조립해서 넘겨준다 — App.UI는 구체 구현(DPAPI, Win32 등)을 모른다.
    public MainWindow(ISecretStore secretStore, IWindowBehavior windowBehavior)
    {
        InitializeComponent();

        _secretStore = secretStore;
        _windowBehavior = windowBehavior;

        var dataDir = AppPaths.DataDir;
        Directory.CreateDirectory(dataDir);

        // 마지막에 놓았던 위치/크기 복원. 처음 실행이거나 화면 밖으로 잘렸다면 우상단(본가 우카가카처럼 오른쪽)에서 시작한다.
        _windowStateStore = new WindowStateStore(Path.Combine(dataDir, "window-state.json"));
        _placementTracker = new WindowPlacementTracker(this, _windowStateStore, "main", trackSize: true);
        _placementTracker.Restore(ScreenPlacement.TopRight);

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

        _packsRootDir = Path.Combine(AppContext.BaseDirectory, "CharacterPacks");
        var builtInPackPath = Path.Combine(_packsRootDir, "default");
        _characterPackService = new CharacterPackService(new JsonCharacterPackLoader(), builtInPackPath);

        _appSettingsStore = new AppSettingsStore(Path.Combine(dataDir, "settings.json"));
        _characterOverlay = new CharacterOverlayController(
            _characterPackService, _packsRootDir, builtInPackPath, _windowStateStore, _windowBehavior,
            BuildReactionService(_appSettingsStore.Load()));

        // App.Mcp(Claude가 스폰하는 별도 프로세스)가 명명 파이프로 say/setExpression을 보내면 여기서 받는다.
        var ipcHandler = new CharacterIpcRequestHandler(_characterPackService, _characterOverlay, ActivateSelf);
        _characterIpcServer = new CharacterIpcServer(ipcHandler);
        _characterIpcServer.Start();

        foreach (var memo in _memoRepository.GetAll())
            OpenMemoWindow(memo);

        // 캐릭터 창은 메인 창이 뜬 뒤에 띄운다 — 뜨는 순서가 바뀌면 캐릭터가 메인 창에 가려지거나 포커스를 가져간다.
        Opened += (_, _) => ApplyCharacterSettings();
        Closing += OnClosing;
    }

    // 이중 실행된 두 번째 인스턴스가 IPC("activate")로 요청했을 때 UI 스레드에서 호출된다.
    // 최소화되어 있으면 복원하고 앞으로 가져온다. 다른 프로세스가 포그라운드 권한을 넘겨준 직후(AllowSetForegroundWindow)에만
    // Windows가 실제로 앞으로 올려 준다 — App.Windows의 SingleInstanceGuard 참고.
    private void ActivateSelf()
    {
        if (WindowState == WindowState.Minimized)
            WindowState = WindowState.Normal;

        Activate();
    }

    private void ApplyCharacterSettings()
    {
        try
        {
            var result = _characterOverlay.Apply(_appSettingsStore.Load());
            if (result.UsedFallback)
            {
                // 선택한 팩을 그리지 못해 기본 캐릭터로 대체했다 — 이유를 모르면 팩이 왜 안 바뀌는지 알 수 없다.
                // 기동 때마다 뜰 수 있다(선택된 팩 id는 설정에 그대로 남으므로): docs/stability-hardening.md 참고.
                _ = ConfirmDialog.ShowAsync(
                    this, $"선택한 캐릭터를 불러오지 못해 기본 캐릭터로 표시합니다.\n{result.FailureReason}");
            }
        }
        catch (Exception ex)
        {
            // 내장 캐릭터팩까지 깨진 배포 오류 등 — 앱 자체는 계속 쓸 수 있게 알림만 띄운다.
            AppLog.Write("pack", ex);
            _ = ConfirmDialog.ShowAsync(this, ex.Message);
        }
    }

    private async void OnSettingsClick(object? sender, RoutedEventArgs e)
    {
        // async void라 여기서 새는 예외는 프로세스 종료로 이어지므로 전체를 감싼다.
        try
        {
            await new SettingsWindow(_secretStore, _appSettingsStore, _packsRootDir).ShowDialog(this);

            // provider/모델/키가 바뀌었을 수 있으니 재시작 없이 바로 반영되도록 다시 조립한다.
            _characterOverlay.SetReactionService(BuildReactionService(_appSettingsStore.Load()));
            // 캐릭터 표시 여부/배율/팩 선택도 재시작 없이 바로 반영한다.
            ApplyCharacterSettings();
        }
        catch (Exception ex)
        {
            AppLog.Write("settings", ex);
            _ = ConfirmDialog.ShowAsync(this, $"설정을 적용하는 중 오류가 발생했습니다.\n{ex.Message}");
        }
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
        var position = NextMemoCascadePosition();
        var memo = new MemoNote
        {
            Color = "#FFF9C4",
            PositionX = position.X,
            PositionY = position.Y,
            Width = DefaultMemoSize,
            Height = DefaultMemoSize,
        };
        _memoRepository.Save(memo);
        _lastMemoPosition = new PixelPoint((int)position.X, (int)position.Y);
        OpenMemoWindow(memo);
    }

    // 새 메모마다 조금씩 어긋나게 배치(캐스케이드)한다. 이번 실행의 첫 메모는 메인 창 옆에서 시작하고,
    // 캐스케이드가 화면 작업 영역을 벗어나면 화면 좌측 원점에서 다시 시작한다.
    private PixelPoint NextMemoCascadePosition()
    {
        var origin = new PixelPoint(MemoCascadeBasePos, MemoCascadeBasePos);
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null)
            return origin;

        var memoSizePx = (int)Math.Round(DefaultMemoSize * screen.Scaling);

        if (_lastMemoPosition is not { } last)
            return NearMainWindow(screen.WorkingArea, screen.Scaling, memoSizePx) ?? origin;

        var next = new PixelPoint(last.X + MemoCascadeOffset, last.Y + MemoCascadeOffset);
        if (next.X + memoSizePx > screen.WorkingArea.Right || next.Y + memoSizePx > screen.WorkingArea.Bottom)
            return origin;

        return next;
    }

    // 메인 창 바로 옆(왼쪽 → 오른쪽 순)에 메모가 통째로 들어갈 자리가 있으면 그 위치. 메인 창이 우상단에 있는
    // 기본 배치에서는 왼쪽에 붙는다. 양쪽 다 안 들어가면 null.
    private PixelPoint? NearMainWindow(PixelRect workArea, double scaling, int memoSizePx)
    {
        var mainWidthPx = (int)Math.Round(Width * scaling);
        PixelPoint[] candidates =
        [
            new(Position.X - memoSizePx - ScreenPlacement.Margin, Position.Y),
            new(Position.X + mainWidthPx + ScreenPlacement.Margin, Position.Y),
        ];

        foreach (var candidate in candidates)
        {
            if (ScreenPlacement.Fits(new PixelRect(candidate.X, candidate.Y, memoSizePx, memoSizePx), workArea))
                return candidate;
        }

        return null;
    }

    private void OpenMemoWindow(MemoNote memo)
    {
        var viewModel = new MemoNoteViewModel(memo, _memoRepository);
        var window = new MemoWindow(viewModel);
        _memoWindows.Add(window);
        window.Closed += (_, _) => _memoWindows.Remove(window);
        window.Show();
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        foreach (var window in _memoWindows)
            window.FlushPendingSave();

        _characterOverlay.FlushPlacement();
        _characterIpcServer.Stop();
    }
}
