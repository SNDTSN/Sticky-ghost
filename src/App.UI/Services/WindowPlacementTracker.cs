using System;
using System.Data.Common;
using System.IO;
using App.Core.Diagnostics;
using App.Core.Infrastructure.FileSystem;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;

namespace App.UI.Services;

/// <summary>
/// 창의 마지막 위치(선택적으로 크기)를 IWindowPlacementStore에 저장하고, 다음 기동 때 복원한다.
/// 메인/캐릭터/메모 창이 모두 이 트래커 하나로 저장한다 — 저장처만 어댑터로 다르다(KNOWN_ISSUES #20-1).
/// 이동/리사이즈는 디바운스해서 저장하고, 창이 닫힐 때(Closing)는 즉시 저장한다.
/// </summary>
public sealed class WindowPlacementTracker : IDisposable
{
    private readonly Window _window;
    private readonly IWindowPlacementStore _store;
    private readonly bool _trackSize;
    private readonly DispatcherTimer _saveTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(500),
    };

    public WindowPlacementTracker(Window window, IWindowPlacementStore store, bool trackSize)
    {
        _window = window;
        _store = store;
        _trackSize = trackSize;

        _saveTimer.Tick += (_, _) => SaveNow();
        _window.PositionChanged += (_, _) => RestartSaveTimer();
        if (_trackSize)
            _window.SizeChanged += (_, _) => RestartSaveTimer();

        // 타이머를 Stop하지 않으면 디스패처 타이머 목록에 남아 Tick 람다가 잡은 창이 GC되지 못한다.
        // 메인 창 종료로 다른 창이 강제 종료되는 경로에서도 마지막 위치를 잃지 않도록 Closing에서 즉시 저장한다.
        _window.Closing += (_, _) => SaveNow();
        _window.Closed += (_, _) => Dispose();
    }

    /// <summary>
    /// Show() 전에 호출한다. 저장값이 없거나 화면 밖으로 잘렸다면 defaultPosition(작업 영역, 창 크기[물리 px])이 주는 기본 위치를 쓴다.
    /// 창 크기는 저장값(있으면)으로 복원하되, 어떤 경우든 기본 작업 영역을 넘지 않게 제한한다.
    /// </summary>
    public void Restore(Func<PixelRect, PixelSize, PixelPoint> defaultPosition) =>
        RestoreCore((saved, sizePx, screens, primary) =>
            saved is not null
            && ScreenPlacement.IsReachable(new PixelRect(saved.X, saved.Y, sizePx.Width, sizePx.Height),
                ScreenPlacement.WorkAreas(screens))
                ? new PixelPoint(saved.X, saved.Y)
                : defaultPosition(primary.WorkingArea, sizePx));

    /// <summary>
    /// Show() 전에 호출한다. 메모처럼 사용자가 여러 개를 흩어 놓는 창용 — 화면 밖으로 잘렸으면 기본 위치로 모으지 않고
    /// 가장 가까운 화면 안으로 옮긴다(모니터가 빠지거나 해상도가 바뀐 경우). 저장값이 없으면 OS 기본 위치를 그대로 둔다.
    /// 보정된 위치는 PositionChanged → 디바운스 저장 경로로 저장처에도 반영된다.
    /// </summary>
    public void RestoreClampedToNearest() =>
        RestoreCore((saved, sizePx, screens, _) =>
            saved is null
                ? null
                : ScreenPlacement.RestoreOrClamp(
                    new PixelRect(saved.X, saved.Y, sizePx.Width, sizePx.Height), ScreenPlacement.WorkAreas(screens)));

    // 두 복원 방식이 공유하는 부분: 크기 복원 → 작업 영역에 맞게 제한 → 물리 px 크기 계산. 위치 결정만 호출부가 준다(null이면 위치를 건드리지 않음).
    private void RestoreCore(Func<WindowPlacement?, PixelSize, Screens, Avalonia.Platform.Screen, PixelPoint?> choosePosition)
    {
        var saved = _store.Load();
        var screens = _window.Screens;
        var primary = screens?.Primary;
        if (screens is null || primary is null)
        {
            // 화면 정보를 얻을 수 없으면 검증 없이 저장값만 복원한다(없으면 OS 기본 위치).
            if (saved is not null)
            {
                RestoreSize(saved);
                _window.Position = new PixelPoint(saved.X, saved.Y);
            }
            return;
        }

        var savedPoint = saved is null ? (PixelPoint?)null : new PixelPoint(saved.X, saved.Y);
        var screen = (savedPoint is { } p ? screens.ScreenFromPoint(p) : null) ?? primary;

        RestoreSize(saved);
        LimitSizeToWorkArea(screen);

        var sizePx = new PixelSize(
            (int)Math.Round(_window.Width * screen.Scaling),
            (int)Math.Round(_window.Height * screen.Scaling));

        if (choosePosition(saved, sizePx, screens, primary) is { } position)
            _window.Position = position;
    }

    private void RestoreSize(WindowPlacement? saved)
    {
        if (_trackSize && saved is { Width: { } w, Height: { } h })
        {
            _window.Width = w;
            _window.Height = h;
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

        try
        {
            _store.Save(new WindowPlacement(
                _window.Position.X,
                _window.Position.Y,
                _trackSize ? _window.Width : null,
                _trackSize ? _window.Height : null));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DbException)
        {
            // 사용자가 요청한 저장이 아니라 창을 옮길 때마다 알아서 하는 저장이다. 여기서 다이얼로그를 띄우면
            // 드래그 중에 팝업이 뜨고 Closing에서는 종료가 막힌다. 잃는 것은 창 위치 하나이고 다음 이동 때 다시 저장되므로,
            // 기록만 남기고 넘어간다(KNOWN_ISSUES #21). DbException은 메모 창의 저장처(SQLite)에서 온다(KNOWN_ISSUES #20-1).
            AppLog.Write("window-state", ex);
        }
    }

    public void Dispose() => _saveTimer.Stop();
}
