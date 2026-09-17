using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using App.UI.ViewModels;

namespace App.UI.Views;

public partial class MemoWindow : Window
{
    private readonly DispatcherTimer _saveTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(500),
    };
    private MemoNoteViewModel? _viewModel;

    public MemoWindow()
    {
        InitializeComponent();
    }

    public MemoWindow(MemoNoteViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        Position = new PixelPoint((int)viewModel.PositionX, (int)viewModel.PositionY);
        Width = viewModel.Width;
        Height = viewModel.Height;

        viewModel.CloseRequested += Close;
        viewModel.ConfirmDeleteRequested = () => ConfirmDialog.ShowAsync(this, "이 메모를 삭제하시겠습니까?");

        _saveTimer.Tick += (_, _) => SaveGeometryNow();
        PositionChanged += (_, _) => RestartSaveTimer();
        SizeChanged += (_, _) => RestartSaveTimer();

        Opened += (_, _) => viewModel.AttachWindowHandle(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
    }

    private void RestartSaveTimer()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveGeometryNow()
    {
        _saveTimer.Stop();
        _viewModel?.UpdatePosition(Position.X, Position.Y);
        _viewModel?.UpdateSize(Width, Height);
    }

    /// <summary>메인 창 종료 등으로 강제 종료되기 전에, 디바운스 중이던 위치/크기/내용 저장을 즉시 실행한다.</summary>
    public void FlushPendingSave()
    {
        SaveGeometryNow();
        _viewModel?.FlushContentSave();
    }
}
