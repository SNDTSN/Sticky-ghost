using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using App.Core.Domain.Entities;
using App.Core.Domain.Events;
using App.Core.Domain.Services;
using App.Core.Infrastructure.FileSystem;
using App.Platform;
using App.UI.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace App.UI.Views;

/// <summary>
/// 상시 떠 있는 캐릭터 오버레이 창. baseImage + 눈 깜빡임/표정 오버레이, 찌르기/쓰다듬기 반응, 말풍선을 담당한다.
/// 팩/배율이 바뀌어도 창을 새로 만들지 않고 <see cref="SetPack"/>으로 내용만 교체한다(하단 중앙 고정).
/// </summary>
public partial class CharacterWindow : Window
{
    private const double StrokeThresholdPx = 8.0;
    // 이 거리(물리 px) 미만으로 움직이고 놓으면 이동이 아니라 클릭으로 본다(더블클릭 판정에서 필요).
    private const int MoveThresholdPx = 4;
    private const int ReactionDurationMs = 1500;

    private readonly Random _random = new();
    private readonly SpeechBubbleWindow _bubble = new();
    // 창 생명주기 전체에 대응하는 토큰 — 요청마다 새로 만들지 않고 창이 닫힐 때만 취소한다.
    private readonly CancellationTokenSource _windowCts = new();

    private WindowStateStore? _stateStore;
    private IWindowBehavior? _windowBehavior;
    private WindowPlacementTracker? _placementTracker;
    // 투명 픽셀이 뒤에 있는 창의 클릭을 막지 않도록 창 모양을 불투명 부분으로 제한하는 데 쓰는 레이어별 알파.
    private LayerAlphaMask? _baseMask;
    private LayerAlphaMask? _eyeClosedMask;
    private readonly Dictionary<string, LayerAlphaMask> _expressionMasks = new();
    // 현재 화면에 떠 있는 표정. 표정 이미지는 base의 투명 영역에 걸쳐 있을 수 있어서 떠 있는 동안만 입력 영역에 포함한다.
    private string? _visibleExpressionId;
    // 창 핸들은 창이 뜬 뒤에야 얻을 수 있다.
    private bool _isOpened;
    // 말풍선 위치 조정값(자동 배치 기준). 캐릭터 원본 100% 좌표 단위로 팩별 저장하고, 화면에 적용할 때 배율/DPI를 곱한다.
    private PixelPoint _balloonOffsetSource;
    private CharacterPack? _pack;
    private CharacterReactionService? _reactionService;
    private int _scalePercent = 100;
    private bool _isClosed;
    // 이전 LLM 응답을 기다리는 중이면 새 터치는 호출 자체를 하지 않는다(연타로 인한 과금 방지).
    private bool _reactionInFlight;

    private DispatcherTimer? _blinkTimer;
    private DispatcherTimer? _blinkCloseTimer;
    private DispatcherTimer? _reactionTimer;

    // 표정마다 반응할 때 new Bitmap()을 반복 생성/미해제하면 네이티브 리소스가 누적되므로,
    // 팩을 적용할 때 전부 한 번만 디코딩해서 재사용하고 팩 교체/창 닫힘 시 일괄 Dispose한다.
    private readonly Dictionary<string, Bitmap> _expressionBitmaps = new();
    private Bitmap? _baseBitmap;
    private Bitmap? _eyeClosedBitmap;

    // 창 이동 — 왼쪽 버튼 드래그. 터치 영역 안팎 구분 없이 어디서나 옮길 수 있다(제목표시줄을 잡는 다른 창들과 같은 감각).
    private bool _moving;
    private bool _moved;
    private PixelPoint _moveStartScreen;
    private PixelPoint _moveStartPosition;
    // 더블클릭의 두 번째 눌림에서 고정한 터치 영역. 그 눌림이 드래그로 이어지지 않고 끝났을 때만 찌르기로 확정한다.
    private TouchRegion? _pendingPokeRegion;

    // 쓰다듬기 — 터치 영역 안에서 오른쪽 버튼을 누른 채 드래그.
    private TouchRegion? _strokeRegion;
    private Point _lastPoint;
    private double _dragDistance;

    public CharacterWindow()
    {
        InitializeComponent();
    }

    public CharacterWindow(
        CharacterPack pack,
        int scalePercent,
        CharacterReactionService reactionService,
        WindowStateStore stateStore,
        IWindowBehavior windowBehavior)
        : this()
    {
        _reactionService = reactionService;
        _stateStore = stateStore;
        _windowBehavior = windowBehavior;

        RootCanvas.PointerPressed += OnPointerPressed;
        RootCanvas.PointerMoved += OnPointerMoved;
        RootCanvas.PointerReleased += OnPointerReleased;
        RootCanvas.PointerCaptureLost += OnPointerCaptureLost;
        // 창을 옮기거나 배율이 바뀌면 떠 있는 말풍선이 캐릭터를 따라가야 한다.
        PositionChanged += (_, _) => _bubble.MoveAnchor(CurrentRectPx());
        // 실제 창 크기가 바뀐 시점에도 입력 영역을 다시 맞춘다(Width/Height 설정과 OS 리사이즈 처리 시점이 다를 수 있다).
        SizeChanged += (_, _) =>
        {
            _bubble.MoveAnchor(CurrentRectPx());
            ApplyInputShape();
        };
        // 창이 뜨기 전에는 DesktopScaling이 실제 화면 배율이 아닐 수 있고 핸들도 없어서, 뜬 직후에 한 번 더 맞춘다.
        Opened += (_, _) =>
        {
            _isOpened = true;
            RefreshScaledState();
        };
        ScalingChanged += (_, _) => RefreshScaledState();
        _bubble.OffsetChanged += OnBalloonOffsetChanged;
        Closed += OnClosed;

        SetPack(pack, scalePercent);

        // 크기가 정해진 뒤에 위치를 복원한다. 처음 실행이거나 화면 밖으로 잘렸으면 우하단(본가 우카가카식)에서 시작.
        _placementTracker = new WindowPlacementTracker(this, stateStore, "character", trackSize: false);
        _placementTracker.Restore(ScreenPlacement.BottomRight);
    }

    public string? PackId => _pack?.Id;

    /// <summary>설정 변경(provider/모델/키)으로 재조립된 서비스를 이미 열려 있는 창에 반영한다.</summary>
    public void UpdateReactionService(CharacterReactionService reactionService)
    {
        _reactionService = reactionService;
    }

    /// <summary>팩과 표시 배율을 적용한다. 이미 떠 있는 창이면 하단 중앙을 고정점으로 크기를 바꾼다.</summary>
    public void SetPack(CharacterPack pack, int scalePercent)
    {
        var keepAnchor = _pack is not null;

        StopAnimationTimers();
        ReleaseBitmaps();
        ResetGestures();

        _pack = pack;
        _scalePercent = scalePercent;
        Title = $"캐릭터 - {pack.Name}";

        _baseBitmap = new Bitmap(pack.Appearance.BaseImage);
        _baseMask = LayerAlphaMask.FromBitmap(_baseBitmap);
        RootCanvas.Width = _baseBitmap.PixelSize.Width;
        RootCanvas.Height = _baseBitmap.PixelSize.Height;
        BaseLayer.Source = _baseBitmap;
        Canvas.SetLeft(BaseLayer, 0);
        Canvas.SetTop(BaseLayer, 0);

        if (pack.Appearance.EyeClosedImage is not null)
        {
            _eyeClosedBitmap = new Bitmap(pack.Appearance.EyeClosedImage);
            _eyeClosedMask = LayerAlphaMask.FromBitmap(_eyeClosedBitmap);
            EyeClosedLayer.Source = _eyeClosedBitmap;
            Canvas.SetLeft(EyeClosedLayer, pack.Appearance.EyeClosedOffset.X);
            Canvas.SetTop(EyeClosedLayer, pack.Appearance.EyeClosedOffset.Y);
        }

        foreach (var expr in pack.Appearance.Expressions)
        {
            var bitmap = new Bitmap(expr.Image);
            _expressionBitmaps[expr.Id] = bitmap;
            _expressionMasks[expr.Id] = LayerAlphaMask.FromBitmap(bitmap);
        }

        LoadBalloonOffset(pack.Id);
        ApplySize(keepAnchor);
        StartBlinkLoop();
    }

    private static string BalloonOffsetKey(string packId) => $"balloon:{packId}";

    private void LoadBalloonOffset(string packId)
    {
        var saved = _stateStore?.Load(BalloonOffsetKey(packId));
        _balloonOffsetSource = saved is null ? default : new PixelPoint(saved.X, saved.Y);
    }

    // 캐릭터 표시 배율/DPI가 바뀌면 말풍선 조정값도 같은 비율로 다시 계산해서 밀어준다.
    private void PushBalloonOffset()
    {
        var f = _scalePercent / 100.0 * DesktopScaling;
        _bubble.Offset = new PixelPoint(
            (int)Math.Round(_balloonOffsetSource.X * f),
            (int)Math.Round(_balloonOffsetSource.Y * f));
    }

    private void OnBalloonOffsetChanged(PixelPoint offsetPx)
    {
        if (_pack is null)
            return;

        var f = _scalePercent / 100.0 * DesktopScaling;
        _balloonOffsetSource = new PixelPoint((int)Math.Round(offsetPx.X / f), (int)Math.Round(offsetPx.Y / f));
        _stateStore?.Save(
            BalloonOffsetKey(_pack.Id), new WindowPlacement(_balloonOffsetSource.X, _balloonOffsetSource.Y));
    }

    /// <summary>표시 배율만 바꾼다.</summary>
    public void SetScale(int scalePercent)
    {
        if (_pack is null || scalePercent == _scalePercent)
            return;

        _scalePercent = scalePercent;
        ApplySize(keepBottomCenter: true);
    }

    private void ApplySize(bool keepBottomCenter)
    {
        var scale = _scalePercent / 100.0;
        var newWidth = RootCanvas.Width * scale;
        var newHeight = RootCanvas.Height * scale;

        if (!keepBottomCenter)
        {
            Width = newWidth;
            Height = newHeight;
            RefreshScaledState();
            return;
        }

        // 하단 중앙을 고정해서 발은 제자리에 두고 위로만 커지고 줄어들게 한다(우카가카처럼).
        var d = DesktopScaling;
        var anchorX = Position.X + Width * d / 2;
        var anchorBottom = Position.Y + Height * d;

        Width = newWidth;
        Height = newHeight;

        var target = new PixelPoint(
            (int)Math.Round(anchorX - newWidth * d / 2),
            (int)Math.Round(anchorBottom - newHeight * d));
        Position = ScreenPlacement.RestoreOrClamp(Screens, target, newWidth, newHeight);
        RefreshScaledState();
    }

    // 표시 배율/DPI에 따라 달라지는 것들(말풍선 조정값, 입력 영역)을 새 크기에 맞춰 다시 계산한다.
    private void RefreshScaledState()
    {
        PushBalloonOffset();
        ApplyInputShape();
    }

    // 창 모양을 base/눈감김/현재 표정 레이어의 불투명 부분으로 제한해서, 투명한 부분은 뒤에 있는 창으로 클릭이 통과하게 한다.
    // SetWindowRgn은 렌더링도 함께 자르므로 표정이 보이기 "전에" 호출해야 새 표정이 잘려 보이지 않는다.
    private void ApplyInputShape()
    {
        if (!_isOpened || _baseMask is null || _pack is null || _windowBehavior is null)
            return;

        var handle = TryGetPlatformHandle()?.Handle ?? IntPtr.Zero;
        if (handle == IntPtr.Zero)
            return;

        var layers = new List<(LayerAlphaMask, int, int)> { (_baseMask, 0, 0) };
        if (_eyeClosedMask is not null)
            layers.Add((_eyeClosedMask, _pack.Appearance.EyeClosedOffset.X, _pack.Appearance.EyeClosedOffset.Y));

        if (_visibleExpressionId is not null
            && _expressionMasks.TryGetValue(_visibleExpressionId, out var expressionMask)
            && _pack.Appearance.Expressions.FirstOrDefault(x => x.Id == _visibleExpressionId) is { } expr)
        {
            layers.Add((expressionMask, expr.Offset.X, expr.Offset.Y));
        }

        var factor = _scalePercent / 100.0 * DesktopScaling;
        var rects = InputShapeBuilder.Build(
            (int)Math.Round(Width * DesktopScaling), (int)Math.Round(Height * DesktopScaling), factor, layers);

        // 불투명 픽셀이 하나도 없으면 영역을 비우는 대신 제한을 해제한다 — 빈 영역이면 창을 잡을 수 없게 된다.
        _windowBehavior.SetInputShape(handle, rects.Count > 0 ? rects : null);
    }

    private PixelRect CurrentRectPx() => new(
        Position,
        new PixelSize((int)Math.Round(Width * DesktopScaling), (int)Math.Round(Height * DesktopScaling)));

    // (B) 눈 깜빡임 — 깜빡일 때마다 다음 간격을 새로 뽑아 규칙적인 리듬이 생기지 않게 한다.
    private void StartBlinkLoop()
    {
        if (_eyeClosedBitmap is null || _pack!.Appearance.Blink is null)
            return;

        ScheduleNextBlink();
    }

    private void ScheduleNextBlink()
    {
        // 닫힌 창이나 교체된 팩에서 예약된 콜백이 뒤늦게 다음 깜빡임을 걸어 타이머가 영원히 도는 것을 막는다.
        if (_isClosed || _pack?.Appearance.Blink is not { } blink)
            return;

        var waitMs = _random.Next(blink.MinIntervalMs, blink.MaxIntervalMs + 1);

        _blinkTimer?.Stop();
        _blinkTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(waitMs) };
        _blinkTimer.Tick += (_, _) =>
        {
            _blinkTimer?.Stop();
            PlayBlink(blink.DurationMs);
        };
        _blinkTimer.Start();
    }

    private void PlayBlink(int durationMs)
    {
        EyeClosedLayer.IsVisible = true;

        _blinkCloseTimer?.Stop();
        _blinkCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
        _blinkCloseTimer.Tick += (_, _) =>
        {
            _blinkCloseTimer?.Stop();
            EyeClosedLayer.IsVisible = false;
            ScheduleNextBlink();
        };
        _blinkCloseTimer.Start();
    }

    // (C) 입력 규칙 — 다른 창들과 같은 감각으로 "왼쪽 드래그 = 창 이동"을 어디서나 쓰고, 터치 반응은 그와 겹치지 않는 입력에 둔다.
    //   · 왼쪽 버튼 드래그(어디서나): 창 이동
    //   · 터치 영역 안 왼쪽 더블클릭: 찌르기(Poke)
    //   · 터치 영역 안 오른쪽 버튼 드래그: 쓰다듬기(Stroke) — 누적 이동 거리가 임계값 이상이면 발동
    // 쓰다듬기를 "버튼을 누르지 않은 호버 왕복"으로 하면 다른 창으로 가려고 지나가기만 해도 LLM이 호출되어 비용이 나가므로 버튼을 누른 경우로 한정한다.
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 이미 진행 중인 제스처가 있으면 다른 버튼의 동시 입력은 무시한다.
        if (_moving || _strokeRegion is not null)
            return;

        var kind = e.GetCurrentPoint(RootCanvas).Properties.PointerUpdateKind;
        // RootCanvas 로컬 좌표라 Viewbox 배율과 무관하게 항상 원본 PNG 100% 기준이다.
        var point = e.GetPosition(RootCanvas);
        var region = _pack!.Appearance.TouchRegions
            .FirstOrDefault(r => r.Rect.Contains(point.X, point.Y));

        if (kind == PointerUpdateKind.LeftButtonPressed)
        {
            // BeginMoveDrag는 눌리자마자 OS 이동 루프에 들어가 더블클릭의 두 번째 눌림을 받을 수 없으므로 화면 좌표 기준으로 직접 옮긴다
            // (창이 움직이면 창 로컬 좌표가 같이 흔들리므로 화면 좌표를 쓴다 — 말풍선 드래그와 같은 방식).
            _moving = true;
            _moved = false;
            _moveStartScreen = this.PointToScreen(e.GetPosition(this));
            _moveStartPosition = Position;
            _pendingPokeRegion = e.ClickCount == 2 ? region : null;
            e.Pointer.Capture(RootCanvas);
        }
        else if (kind == PointerUpdateKind.RightButtonPressed && region is not null)
        {
            _strokeRegion = region;
            _lastPoint = point;
            _dragDistance = 0;
            e.Pointer.Capture(RootCanvas);
        }
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_moving)
        {
            var screenPoint = this.PointToScreen(e.GetPosition(this));
            var dx = screenPoint.X - _moveStartScreen.X;
            var dy = screenPoint.Y - _moveStartScreen.Y;

            if (!_moved && Math.Abs(dx) < MoveThresholdPx && Math.Abs(dy) < MoveThresholdPx)
                return;

            _moved = true;
            Position = new PixelPoint(_moveStartPosition.X + dx, _moveStartPosition.Y + dy);
        }
        else if (_strokeRegion is not null)
        {
            var point = e.GetPosition(RootCanvas);
            _dragDistance += Distance(_lastPoint, point);
            _lastPoint = point;
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (e.InitialPressMouseButton == MouseButton.Left && _moving)
        {
            var moved = _moved;
            var pokeRegion = _pendingPokeRegion;
            ResetGestures();
            e.Pointer.Capture(null);

            // 두 번째 눌림이 드래그로 이어졌다면 더블클릭이 아니라 이동이다.
            if (!moved && pokeRegion is not null)
                HandleTouchEvent(new TouchEvent(pokeRegion.Id, TouchKind.Poke));
        }
        else if (e.InitialPressMouseButton == MouseButton.Right && _strokeRegion is { } strokeRegion)
        {
            var distance = _dragDistance;
            ResetGestures();
            e.Pointer.Capture(null);

            // 임계값 미만의 오른쪽 클릭은 아무 동작도 하지 않는다 — 나중에 컨텍스트 메뉴 자리로 비워 둔다.
            if (distance >= StrokeThresholdPx)
                HandleTouchEvent(new TouchEvent(strokeRegion.Id, TouchKind.Stroke));
        }
    }

    // 다른 창이 포커스를 가져가는 등으로 Released 없이 capture만 풀리면 진행 중 상태가 남아 이후 입력이 전부 막힌다.
    private void OnPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => ResetGestures();

    private void ResetGestures()
    {
        _moving = false;
        _moved = false;
        _pendingPokeRegion = null;
        _strokeRegion = null;
        _dragDistance = 0;
    }

    private static double Distance(Point a, Point b)
    {
        var dx = b.X - a.X;
        var dy = b.Y - a.Y;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private async void HandleTouchEvent(TouchEvent touchEvent)
    {
        if (_reactionInFlight)
            return;

        _reactionInFlight = true;
        try
        {
            var result = await _reactionService!.ReactToTouchAsync(touchEvent, _windowCts.Token);
            if (_isClosed)
                return;

            if (result.ExpressionId is not null)
                ShowExpression(result.ExpressionId);
            if (result.Line is not null)
                ShowLine(result.Line);
        }
        catch (OperationCanceledException)
        {
            // 창이 닫히면서 취소된 경우 — 무시.
        }
        finally
        {
            _reactionInFlight = false;
        }
    }

    /// <summary>말풍선으로 대사를 띄운다. 이미 떠 있으면 텍스트를 교체하고 표시 시간을 다시 잰다.</summary>
    public void ShowLine(string text)
    {
        if (_isClosed)
            return;

        _bubble.Speak(text, CurrentRectPx());
    }

    public void ShowExpression(string expressionId)
    {
        var expr = _pack!.Appearance.Expressions.FirstOrDefault(x => x.Id == expressionId);
        if (expr is null || !_expressionBitmaps.TryGetValue(expressionId, out var bitmap))
            return;

        _reactionTimer?.Stop();

        ExpressionLayer.Source = bitmap;
        Canvas.SetLeft(ExpressionLayer, expr.Offset.X);
        Canvas.SetTop(ExpressionLayer, expr.Offset.Y);
        _visibleExpressionId = expressionId;
        ApplyInputShape();
        ExpressionLayer.IsVisible = true;

        _reactionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ReactionDurationMs) };
        _reactionTimer.Tick += (_, _) =>
        {
            _reactionTimer?.Stop();
            ExpressionLayer.IsVisible = false;
            _visibleExpressionId = null;
            ApplyInputShape();
        };
        _reactionTimer.Start();
    }

    /// <summary>종료 직전 등 디바운스를 기다릴 수 없을 때 대기 중인 위치 저장을 즉시 실행한다.</summary>
    public void FlushPlacement() => _placementTracker?.Flush();

    private void StopAnimationTimers()
    {
        _blinkTimer?.Stop();
        _blinkCloseTimer?.Stop();
        _reactionTimer?.Stop();

        EyeClosedLayer.IsVisible = false;
        ExpressionLayer.IsVisible = false;
        _visibleExpressionId = null;
    }

    // 레이어가 참조 중인 채로 Dispose하면 렌더링 중 죽을 수 있으므로 Source부터 뗀다.
    private void ReleaseBitmaps()
    {
        BaseLayer.Source = null;
        EyeClosedLayer.Source = null;
        ExpressionLayer.Source = null;

        _baseBitmap?.Dispose();
        _baseBitmap = null;
        _eyeClosedBitmap?.Dispose();
        _eyeClosedBitmap = null;

        foreach (var bitmap in _expressionBitmaps.Values)
            bitmap.Dispose();
        _expressionBitmaps.Clear();

        _baseMask = null;
        _eyeClosedMask = null;
        _expressionMasks.Clear();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _isClosed = true;
        StopAnimationTimers();
        _windowCts.Cancel();
        _windowCts.Dispose();
        _bubble.Close();
        ReleaseBitmaps();
    }
}
