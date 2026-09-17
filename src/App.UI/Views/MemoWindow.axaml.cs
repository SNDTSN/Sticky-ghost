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

    public MemoWindow()
    {
        InitializeComponent();
    }

    public MemoWindow(MemoNoteViewModel viewModel) : this()
    {
        DataContext = viewModel;
        Position = new PixelPoint((int)viewModel.PositionX, (int)viewModel.PositionY);
        Width = viewModel.Width;
        Height = viewModel.Height;

        viewModel.CloseRequested += Close;
        viewModel.ConfirmDeleteRequested = () => ConfirmDialog.ShowAsync(this, "이 메모를 삭제하시겠습니까?");

        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            viewModel.UpdatePosition(Position.X, Position.Y);
            viewModel.UpdateSize(Width, Height);
        };
        PositionChanged += (_, _) => RestartSaveTimer();
        SizeChanged += (_, _) => RestartSaveTimer();

        Opened += (_, _) => viewModel.AttachWindowHandle(TryGetPlatformHandle()?.Handle ?? IntPtr.Zero);
    }

    private void RestartSaveTimer()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }
}
