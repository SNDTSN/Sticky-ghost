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

    private Point? _pressStart;
    private Point _lastPoint;
    private double _dragDistance;
    private TouchRegion? _activeRegion;

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
        _pressStart = null;
        _activeRegion = null;

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

    // (C) 찌르기/쓰다듬기 판정 — 포인터 다운 지점의 touchRegion을 고정하고, 뗄 때까지의 누적 이동 거리로 Poke/Stroke를 가른다.
    // 클릭/드래그가 이미 터치에 쓰이므로 창 이동은 "터치 영역 밖을 드래그" 또는 "Ctrl+드래그"로 구분한다.
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(RootCanvas).Properties.IsLeftButtonPressed)
            return;

        // RootCanvas 로컬 좌표라 Viewbox 배율과 무관하게 항상 원본 PNG 100% 기준이다.
        var point = e.GetPosition(RootCanvas);
        var region = _pack!.Appearance.TouchRegions
            .FirstOrDefault(r => r.Rect.Contains(point.X, point.Y));

        if (region is null || e.KeyModifiers.HasFlag(KeyModifiers.Control))
        {
            BeginMoveDrag(e);
            return;
        }

        _activeRegion = region;
        _pressStart = point;
        _lastPoint = point;
        _dragDistance = 0;
        e.Pointer.Capture(RootCanvas);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressStart is null)
            return;

        var point = e.GetPosition(RootCanvas);
        _dragDistance += Distance(_lastPoint, point);
        _lastPoint = point;
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_pressStart is null || _activeRegion is null)
            return;

        var kind = _dragDistance >= StrokeThresholdPx ? TouchKind.Stroke : TouchKind.Poke;
        HandleTouchEvent(new TouchEvent(_activeRegion.Id, kind));

        _pressStart = null;
        _activeRegion = null;
        e.Pointer.Capture(null);
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
