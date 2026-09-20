using System;
using App.Core.Infrastructure.FileSystem;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace App.UI.Services;

/// <summary>
/// 창의 마지막 위치(선택적으로 크기)를 WindowStateStore에 저장하고, 다음 기동 때 복원한다.
/// 저장값이 없거나 화면 밖으로 잘렸다면 호출부가 준 기본 위치를 쓴다.
/// 이동/리사이즈는 디바운스해서 저장하고, 창이 닫힐 때(Closing)는 즉시 저장한다.
/// </summary>
public sealed class WindowPlacementTracker : IDisposable
{
    private readonly Window _window;
    private readonly WindowStateStore _store;
    private readonly string _key;
    private readonly bool _trackSize;
    private readonly DispatcherTimer _saveTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(500),
    };

    public WindowPlacementTracker(Window window, WindowStateStore store, string key, bool trackSize)
    {
        _window = window;
        _store = store;
        _key = key;
        _trackSize = trackSize;

        _saveTimer.Tick += (_, _) => SaveNow();
        _window.PositionChanged += (_, _) => RestartSaveTimer();
        if (_trackSize)
            _window.SizeChanged += (_, _) => RestartSaveTimer();

        // MemoWindow와 같은 이유 — 타이머를 Stop하지 않으면 Tick 람다가 잡은 창이 GC되지 못한다.
        // 메인 창 종료로 다른 창이 강제 종료되는 경로에서도 마지막 위치를 잃지 않도록 Closing에서 즉시 저장한다.
        _window.Closing += (_, _) => SaveNow();
        _window.Closed += (_, _) => Dispose();
    }

    /// <summary>
    /// Show() 전에 호출한다. defaultPosition은 (작업 영역, 창 크기[물리 px]) → 기본 위치.
    /// 창 크기는 저장값(있으면)으로 복원하되, 어떤 경우든 기본 작업 영역을 넘지 않게 제한한다.
    /// </summary>
    public void Restore(Func<PixelRect, PixelSize, PixelPoint> defaultPosition)
    {
        var screens = _window.Screens;
        var primary = screens?.Primary;
        if (screens is null || primary is null)
        {
            // 화면 정보를 얻을 수 없으면 검증 없이 저장값만 복원한다(없으면 OS 기본 위치).
            if (_store.Load(_key) is { } fallbackSaved)
                _window.Position = new PixelPoint(fallbackSaved.X, fallbackSaved.Y);
            return;
        }

        var saved = _store.Load(_key);
        var savedPoint = saved is null ? (PixelPoint?)null : new PixelPoint(saved.X, saved.Y);
        var screen = (savedPoint is { } p ? screens.ScreenFromPoint(p) : null) ?? primary;

        if (_trackSize && saved is { Width: { } w, Height: { } h })
        {
            _window.Width = w;
            _window.Height = h;
        }
        LimitSizeToWorkArea(screen);

        var sizePx = new PixelSize(
            (int)Math.Round(_window.Width * screen.Scaling),
            (int)Math.Round(_window.Height * screen.Scaling));

        if (saved is not null
            && ScreenPlacement.IsReachable(new PixelRect(saved.X, saved.Y, sizePx.Width, sizePx.Height),
                ScreenPlacement.WorkAreas(screens)))
        {
            _window.Position = new PixelPoint(saved.X, saved.Y);
        }
        else
        {
            _window.Position = defaultPosition(primary.WorkingArea, sizePx);
        }
    }

    private void LimitSizeToWorkArea(Avalonia.Platform.Screen screen)
    {
        var maxWidth = screen.WorkingArea.Width / screen.Scaling - 2 * ScreenPlacement.Margin;
        var maxHeight = screen.WorkingArea.Height / screen.Scaling - 2 * ScreenPlacement.Margin;

        if (!double.IsNaN(_window.Width))
            _window.Width = Math.Min(_window.Width, maxWidth);
        if (!double.IsNaN(_window.Height))
            _window.Height = Math.Min(_window.Height, maxHeight);
    }

    /// <summary>디바운스 중이던 저장을 즉시 실행한다.</summary>
    public void Flush() => SaveNow();

    private void RestartSaveTimer()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveNow()
    {
        _saveTimer.Stop();

        // 최소화된 창의 Position은 (-32000,-32000)이고 최대화 상태의 위치/크기는 사용자가 정한 값이 아니다.
        if (_window.WindowState != WindowState.Normal)
            return;

        _store.Save(_key, new WindowPlacement(
            _window.Position.X,
            _window.Position.Y,
            _trackSize ? _window.Width : null,
            _trackSize ? _window.Height : null));
    }

    public void Dispose() => _saveTimer.Stop();
}
