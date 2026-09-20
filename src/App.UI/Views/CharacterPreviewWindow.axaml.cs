using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using App.Core.Domain.Entities;
using App.Core.Domain.Events;
using App.Core.Domain.Services;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

namespace App.UI.Views;

/// <summary>캐릭터팩 렌더링 확인용 창. baseImage + 눈 깜빡임/표정 오버레이, 찌르기/쓰다듬기 반응까지 표시한다.</summary>
public partial class CharacterPreviewWindow : Window
{
    private const double StrokeThresholdPx = 8.0;
    private const int ReactionDurationMs = 1500;
    private const int LineDurationMs = 3000;

    private readonly Random _random = new();

    private CharacterPack? _pack;
    private CharacterReactionService? _reactionService;
    // 창 생명주기 전체에 대응하는 토큰 — 요청마다 새로 만들지 않고 창이 닫힐 때만 취소한다.
    private CancellationTokenSource? _windowCts;
    // 이전 LLM 응답을 기다리는 중이면 새 터치는 호출 자체를 하지 않는다(연타로 인한 과금 방지).
    private bool _reactionInFlight;

    private DispatcherTimer? _blinkTimer;
    private DispatcherTimer? _reactionTimer;
    private DispatcherTimer? _lineTimer;

    // 표정마다 반응할 때 new Bitmap()을 반복 생성/미해제하면 네이티브 리소스가 누적되므로,
    // 창 생성 시 전부 한 번만 디코딩해서 재사용하고 창이 닫힐 때 일괄 Dispose한다.
    private readonly Dictionary<string, Bitmap> _expressionBitmaps = new();

    private Point? _pressStart;
    private Point _lastPoint;
    private double _dragDistance;
    private TouchRegion? _activeRegion;

    public CharacterPreviewWindow()
    {
        InitializeComponent();
    }

    public CharacterPreviewWindow(CharacterPack pack, CharacterReactionService reactionService) : this()
    {
        _pack = pack;
        _reactionService = reactionService;
        _windowCts = new CancellationTokenSource();
        Title = $"캐릭터 미리보기 - {pack.Name}";

        var baseBitmap = new Bitmap(pack.Appearance.BaseImage);
        RootCanvas.Width = baseBitmap.PixelSize.Width;
        RootCanvas.Height = baseBitmap.PixelSize.Height;
        BaseLayer.Source = baseBitmap;
        Canvas.SetLeft(BaseLayer, 0);
        Canvas.SetTop(BaseLayer, 0);

        if (pack.Appearance.EyeClosedImage is not null)
        {
            EyeClosedLayer.Source = new Bitmap(pack.Appearance.EyeClosedImage);
            Canvas.SetLeft(EyeClosedLayer, pack.Appearance.EyeClosedOffset.X);
            Canvas.SetTop(EyeClosedLayer, pack.Appearance.EyeClosedOffset.Y);
        }

        foreach (var expr in pack.Appearance.Expressions)
            _expressionBitmaps[expr.Id] = new Bitmap(expr.Image);

        RootCanvas.PointerPressed += OnPointerPressed;
        RootCanvas.PointerMoved += OnPointerMoved;
        RootCanvas.PointerReleased += OnPointerReleased;
        Closed += OnClosed;

        StartBlinkLoop();
    }

    /// <summary>설정 변경(provider/모델/키)으로 재조립된 서비스를 이미 열려 있는 창에 반영한다.</summary>
    public void UpdateReactionService(CharacterReactionService reactionService)
    {
        _reactionService = reactionService;
    }

    // (B) 눈 깜빡임 — 깜빡일 때마다 다음 간격을 새로 뽑아 규칙적인 리듬이 생기지 않게 한다.
    private void StartBlinkLoop()
    {
        if (_pack!.Appearance.EyeClosedImage is null || _pack.Appearance.Blink is null)
            return;

        ScheduleNextBlink();
    }

    private void ScheduleNextBlink()
    {
        var blink = _pack!.Appearance.Blink!;
        var waitMs = _random.Next(blink.MinIntervalMs, blink.MaxIntervalMs + 1);

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

        var closeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMs) };
        closeTimer.Tick += (_, _) =>
        {
            closeTimer.Stop();
            EyeClosedLayer.IsVisible = false;
            ScheduleNextBlink();
        };
        closeTimer.Start();
    }

    // (C) 찌르기/쓰다듬기 판정 — 포인터 다운 지점의 touchRegion을 고정하고, 뗄 때까지의 누적 이동 거리로 Poke/Stroke를 가른다.
    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetPosition(RootCanvas);
        var region = _pack!.Appearance.TouchRegions
            .FirstOrDefault(r => r.Rect.Contains(point.X, point.Y));
        if (region is null)
            return;

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
            var result = await _reactionService!.ReactToTouchAsync(touchEvent, _windowCts!.Token);
            if (_windowCts.IsCancellationRequested)
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

    /// <summary>App.Mcp의 say 툴 로직 테스트용 최소 오버레이 — 제대로 된 말풍선 UI는 별도 작업.</summary>
    public void ShowLine(string text)
    {
        _lineTimer?.Stop();

        LineLayer.Text = text;
        LineLayer.IsVisible = true;

        _lineTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(LineDurationMs) };
        _lineTimer.Tick += (_, _) =>
        {
            _lineTimer?.Stop();
            LineLayer.IsVisible = false;
        };
        _lineTimer.Start();
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
        ExpressionLayer.IsVisible = true;

        _reactionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ReactionDurationMs) };
        _reactionTimer.Tick += (_, _) =>
        {
            _reactionTimer?.Stop();
            ExpressionLayer.IsVisible = false;
        };
        _reactionTimer.Start();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _blinkTimer?.Stop();
        _reactionTimer?.Stop();
        _lineTimer?.Stop();
        _windowCts?.Cancel();

        foreach (var bitmap in _expressionBitmaps.Values)
            bitmap.Dispose();
        _expressionBitmaps.Clear();
    }
}
