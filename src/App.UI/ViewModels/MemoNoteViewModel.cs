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

public partial class MemoNoteViewModel : ViewModelBase, IDisposable
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
        _contentSaveTimer.Tick += (_, _) => SaveContentNow();
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

    private void SaveContentNow()
    {
        _contentSaveTimer.Stop();
        _memo.Content = Content;
        _memo.UpdatedAt = DateTime.Now;
        _repository.Save(_memo);
    }

    /// <summary>메인 창 종료 등으로 디바운스를 기다릴 수 없을 때 대기 중인 내용 저장을 즉시 실행한다.</summary>
    public void FlushContentSave() => SaveContentNow();

    /// <summary>창이 닫힐 때 호출. Stop()으로 디스패처 타이머 목록에서 빠지지 않으면
    /// Tick 람다가 캡처한 this가 계속 살아남아 창/뷰모델이 GC되지 못한다.</summary>
    public void Dispose()
    {
        _contentSaveTimer.Stop();
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
