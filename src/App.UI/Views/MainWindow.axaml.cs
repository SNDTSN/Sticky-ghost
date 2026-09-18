using System;
using System.Collections.Generic;
using System.IO;
using App.Core.Domain.Entities;
using App.Core.Domain.Events;
using App.Core.Domain.Repositories;
using App.Core.Domain.Services;
using App.Core.Infrastructure.FileSystem;
using App.Core.Infrastructure.Sqlite;
using App.Platform;
using App.Platform.Stub;
using App.UI.ViewModels;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace App.UI.Views;

public partial class MainWindow : Window
{
    // TODO: 임시 배선. App.Platform.Windows 구현/DI 컨테이너가 생기면 정식 조립 방식으로 교체.
    private readonly IMemoRepository _memoRepository;
    private readonly IWindowBehavior _windowBehavior = new StubWindowBehavior();
    private readonly List<MemoWindow> _memoWindows = new();
    private readonly CharacterPackService _characterPackService;

    public MainWindow()
    {
        InitializeComponent();

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

        DataContext = new MainViewModel(todoRepository, categoryRepository, todoService);

        var builtInPackPath = Path.Combine(AppContext.BaseDirectory, "CharacterPacks", "default");
        _characterPackService = new CharacterPackService(new JsonCharacterPackLoader(), builtInPackPath);

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
            new CharacterPreviewWindow(outcome.Pack).Show();
        }
        catch (InvalidOperationException ex)
        {
            _ = ConfirmDialog.ShowAsync(this, ex.Message);
        }
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
    }
}
