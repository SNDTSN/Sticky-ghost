using System;
using System.IO;
using App.Core.Domain.Entities;
using App.Core.Domain.Events;
using App.Core.Domain.Repositories;
using App.Core.Domain.Services;
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

        var viewModel = new MemoNoteViewModel(memo, _memoRepository, _windowBehavior);
        new MemoWindow(viewModel).Show();
    }
}
