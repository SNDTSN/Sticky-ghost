using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;

namespace App.UI.Views;

/// <summary>
/// 캐릭터 옆에 뜨는 말풍선. 캐릭터 창과 별개의 투명 창이며, 한 번 만들어 두고 Show/Hide로 재사용한다.
/// 캐릭터가 화면 오른쪽 절반에 있으면 왼쪽에, 왼쪽 절반에 있으면 오른쪽에 띄우고, 공간이 모자라면 반대쪽으로 뒤집는다.
/// 사용자가 드래그해서 읽기 편한 위치로 옮길 수 있고, 그 조정값(<see cref="Offset"/>)은 자동 배치 위치 기준이라
/// 캐릭터를 옮기거나 좌우로 뒤집혀도 같은 관계를 유지한다.
/// </summary>
public partial class SpeechBubbleWindow : Window
{
    private const int MaxChars = 200;
    private const int MinDurationMs = 3000;
    private const int MaxDurationMs = 12000;
    // 캐릭터 상단에서 이 비율만큼 내려온 높이에 말풍선 상단을 맞춘다. 예제 팩 기준으로 꼬리 끝이 대략 입 높이에 온다.
    // 팩마다 정답이 다르므로 초기값일 뿐이고, 실제 위치는 사용자가 드래그로 조정한다.
    private const double AnchorHeightRatio = 0.45;
    // 이 거리(물리 px) 미만으로 움직이고 놓으면 드래그가 아니라 클릭(닫기)으로 본다.
    private const int DragThresholdPx = 4;

    private readonly DispatcherTimer _hideTimer = new();
    private PixelRect _anchor;

    // 마지막 자동 배치 결과 — 드래그가 끝났을 때 사용자가 옮긴 만큼(Offset)을 역산하는 기준.
    private PixelPoint _autoPosition;
    private bool _onLeft;

    private bool _pressed;
    private bool _dragged;
    private PixelPoint _pressScreenPoint;
    private PixelPoint _pressWindowPosition;

    /// <summary>
    /// 자동 배치 위치에서 사용자가 옮긴 만큼(물리 px). dx는 캐릭터에서 멀어지는 방향이 +라서, 말풍선이 캐릭터 반대편으로
    /// 뒤집혀도 의미가 유지된다. 값을 설정해도 <see cref="OffsetChanged"/>는 발생하지 않는다(사용자 드래그로만 발생).
    /// </summary>
    public PixelPoint Offset
    {
        get => _offset;
        set
        {
            _offset = value;
            if (IsVisible)
                Place();
        }
    }
    private PixelPoint _offset;

    public event Action<PixelPoint>? OffsetChanged;

    public SpeechBubbleWindow()
    {
        InitializeComponent();

        _hideTimer.Tick += (_, _) =>
        {
            _hideTimer.Stop();
            // 잡고 있는 동안에는 닫지 않는다 — 놓을 때 타이머를 다시 시작한다.
            if (!_pressed)
                Dismiss();
        };
        // 텍스트가 바뀌어 SizeToContent 크기가 달라지면 다시 배치한다.
        SizeChanged += (_, _) => Place();
        Closed += (_, _) => _hideTimer.Stop();
    }

    /// <summary>anchor는 캐릭터 창의 물리 픽셀 사각형. 이미 떠 있다면 텍스트를 교체하고 타이머를 다시 시작한다.</summary>
    public void Speak(string text, PixelRect anchor)
    {
        _anchor = anchor;
        LineText.Text = text.Length > MaxChars ? text[..MaxChars] + "…" : text;

        _hideTimer.Stop();
        // 긴 문장을 읽을 시간이 모자라지 않게 글자 수에 비례해 늘린다.
        _hideTimer.Interval = TimeSpan.FromMilliseconds(Math.Clamp(2000 + 120 * text.Length, MinDurationMs, MaxDurationMs));
        _hideTimer.Start();

        if (IsVisible)
        {
            Place();
            return;
        }

        // 첫 배치 전에 엉뚱한 위치에서 잠깐 보이지 않도록 투명하게 띄운 뒤 Place가 위치를 잡으면 불투명으로 돌린다.
        Opacity = 0;
        Show();
        Place();
    }

    /// <summary>캐릭터 창이 움직이거나 크기가 바뀌었을 때 호출. 떠 있는 동안만 따라간다.</summary>
    public void MoveAnchor(PixelRect anchor)
    {
        _anchor = anchor;
        if (IsVisible)
            Place();
    }

    public void Dismiss()
    {
        _hideTimer.Stop();
        Hide();
    }

    private void OnBubblePointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            return;

        // BeginMoveDrag는 OS 이동 루프에 들어가 놓았을 때의 이벤트를 받을 수 없어 "클릭으로 닫기"와 구분이 안 된다.
        // 그래서 화면 좌표 기준으로 직접 옮긴다(창이 움직이면 창 로컬 좌표가 같이 흔들리므로 화면 좌표를 쓴다).
        _pressed = true;
        _dragged = false;
        _pressScreenPoint = this.PointToScreen(e.GetPosition(this));
        _pressWindowPosition = Position;
        _hideTimer.Stop();
        e.Pointer.Capture(sender as IInputElement);
    }

    private void OnBubblePointerMoved(object? sender, PointerEventArgs e)
    {
        if (!_pressed)
            return;

        var screenPoint = this.PointToScreen(e.GetPosition(this));
        var dx = screenPoint.X - _pressScreenPoint.X;
        var dy = screenPoint.Y - _pressScreenPoint.Y;

        if (!_dragged && Math.Abs(dx) < DragThresholdPx && Math.Abs(dy) < DragThresholdPx)
            return;

        _dragged = true;
        Position = new PixelPoint(_pressWindowPosition.X + dx, _pressWindowPosition.Y + dy);
    }

    private void OnBubblePointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (!_pressed)
            return;

        var dragged = _dragged;
        _pressed = false;
        _dragged = false;
        e.Pointer.Capture(null);

        if (!dragged)
        {
            Dismiss();
            return;
        }

        // 놓은 위치를 자동 배치 위치 기준의 조정값으로 바꿔 저장 대상으로 알린다. 작업 영역 밖에 놓았다면 Place가 clamp한
        // 실제 위치를 기준으로 다시 역산해서 저장값이 화면 밖을 가리키지 않게 한다.
        _offset = OffsetFrom(Position);
        Place();
        _offset = OffsetFrom(Position);
        OffsetChanged?.Invoke(_offset);

        _hideTimer.Start();
    }

    // 다른 창이 포커스를 가져가는 등으로 Released 없이 capture만 풀리면 _pressed가 남아 Place와 닫기 타이머가 영원히 막힌다.
    private void OnBubblePointerCaptureLost(object? sender, PointerCaptureLostEventArgs e)
    {
        if (!_pressed)
            return;

        _pressed = false;
        _dragged = false;
        _hideTimer.Start();
    }

    private PixelPoint OffsetFrom(PixelPoint position)
    {
        var dx = position.X - _autoPosition.X;
        return new PixelPoint(_onLeft ? -dx : dx, position.Y - _autoPosition.Y);
    }

    private void Place()
    {
        if (_pressed)
            return;

        var screens = Screens;
        var screen = screens?.ScreenFromPoint(_anchor.Center) ?? screens?.Primary;
        if (screen is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var workArea = screen.WorkingArea;
        var width = (int)Math.Round(Bounds.Width * DesktopScaling);
        var height = (int)Math.Round(Bounds.Height * DesktopScaling);

        var leftX = _anchor.X - width;
        var rightX = _anchor.Right;
        var fitsLeft = leftX >= workArea.X;
        var fitsRight = rightX + width <= workArea.Right;

        var preferLeft = _anchor.Center.X > workArea.Center.X;
        // 선호하는 쪽에 자리가 있으면 그쪽, 없고 반대쪽에 있으면 뒤집기, 양쪽 다 없으면 선호 쪽에 두고 아래에서 clamp.
        _onLeft = preferLeft ? fitsLeft || !fitsRight : !fitsRight && fitsLeft;

        var autoX = Math.Clamp(_onLeft ? leftX : rightX, workArea.X, Math.Max(workArea.X, workArea.Right - width));
        var autoY = Math.Clamp(
            _anchor.Y + (int)(_anchor.Height * AnchorHeightRatio),
            workArea.Y,
            Math.Max(workArea.Y, workArea.Bottom - height));
        _autoPosition = new PixelPoint(autoX, autoY);

        // 사용자가 옮겨 둔 만큼을 얹되, 그 결과도 화면 안에 있어야 다시 잡을 수 있다.
        var x = Math.Clamp(autoX + (_onLeft ? -_offset.X : _offset.X), workArea.X, Math.Max(workArea.X, workArea.Right - width));
        var y = Math.Clamp(autoY + _offset.Y, workArea.Y, Math.Max(workArea.Y, workArea.Bottom - height));

        // 꼬리는 캐릭터를 향한다 — 말풍선이 캐릭터 왼쪽에 있으면 오른쪽 변에 꼬리가 붙는다.
        TailRight.IsVisible = _onLeft;
        TailLeft.IsVisible = !_onLeft;

        Position = new PixelPoint(x, y);
        Opacity = 1;
    }
}
