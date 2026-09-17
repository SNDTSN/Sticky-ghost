using System;
using System.Threading.Tasks;
using App.Core.Domain.Entities;
using App.Core.Domain.Repositories;
using App.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaColor = Avalonia.Media.Color;

namespace App.UI.ViewModels;

public partial class MemoNoteViewModel : ViewModelBase
{
    private readonly MemoNote _memo;
    private readonly IMemoRepository _repository;
    private readonly IWindowBehavior _windowBehavior;
    private readonly DispatcherTimer _contentSaveTimer;
    private IntPtr _windowHandle;

    [ObservableProperty]
    private string _content;

    [ObservableProperty]
    private string _colorHex;

    [ObservableProperty]
    private bool _isPinned;

    public double PositionX => _memo.PositionX;
    public double PositionY => _memo.PositionY;
    public double Width => _memo.Width;
    public double Height => _memo.Height;

    public Action? CloseRequested;
    public Func<Task<bool>>? ConfirmDeleteRequested;

    public MemoNoteViewModel(MemoNote memo, IMemoRepository repository, IWindowBehavior windowBehavior)
    {
        _memo = memo;
        _repository = repository;
        _windowBehavior = windowBehavior;
        _content = memo.Content;
        _colorHex = memo.Color;
        _isPinned = memo.IsPinned;

        _contentSaveTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(500),
        };
        _contentSaveTimer.Tick += (_, _) =>
        {
            _contentSaveTimer.Stop();
            _memo.Content = Content;
            _memo.UpdatedAt = DateTime.UtcNow;
            _repository.Save(_memo);
        };
    }

    public MediaColor ColorValue
    {
        get => MediaColor.Parse(ColorHex);
        set => ColorHex = value.ToString();
    }

    public void AttachWindowHandle(IntPtr handle)
    {
        _windowHandle = handle;
        _windowBehavior.SetAlwaysOnTop(_windowHandle, IsPinned);
    }

    public void UpdatePosition(double x, double y)
    {
        _memo.PositionX = x;
        _memo.PositionY = y;
        _repository.Save(_memo);
    }

    public void UpdateSize(double width, double height)
    {
        _memo.Width = width;
        _memo.Height = height;
        _repository.Save(_memo);
    }

    partial void OnContentChanged(string value)
    {
        _contentSaveTimer.Stop();
        _contentSaveTimer.Start();
    }

    partial void OnColorHexChanged(string value)
    {
        _memo.Color = value;
        _repository.Save(_memo);
        OnPropertyChanged(nameof(ColorValue));
    }

    partial void OnIsPinnedChanged(bool value)
    {
        _memo.IsPinned = value;
        _windowBehavior.SetAlwaysOnTop(_windowHandle, value);
        _repository.Save(_memo);
    }

    [RelayCommand]
    private async Task RequestCloseAsync()
    {
        if (string.IsNullOrWhiteSpace(Content))
        {
            _repository.Delete(_memo.Id);
            CloseRequested?.Invoke();
            return;
        }

        var confirmed = ConfirmDeleteRequested is not null && await ConfirmDeleteRequested.Invoke();
        if (confirmed)
        {
            _repository.Delete(_memo.Id);
            CloseRequested?.Invoke();
        }
    }
}
