using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace App.Platform.Windows;

/// <summary>
/// IWindowBehavior의 Windows 구현(user32/gdi32 P/Invoke). 지금 필요한 SetInputShape와 RestoreImeBinding만 구현했고,
/// SetClickThrough는 필요해지는 시점까지 no-op이다(Stub과 동일 동작).
/// </summary>
public sealed class WindowsWindowBehavior : IWindowBehavior
{
    private const int RdhRectangles = 1;
    private const int RgnDataHeaderSize = 32;
    private const int RectSize = 16;

    private const uint WmInputLangChange = 0x0051;

    public void SetClickThrough(IntPtr handle, bool enabled) { }

    /// <summary>
    /// SetWindowRgn으로 창 영역을 rects의 합집합으로 자른다. 영역 밖은 렌더링뿐 아니라 마우스 입력도 OS가 아래 창으로 넘긴다.
    /// </summary>
    public void SetInputShape(IntPtr handle, IReadOnlyList<MaskRect>? rects)
    {
        if (handle == IntPtr.Zero)
            return;

        if (rects is null)
        {
            SetWindowRgn(handle, IntPtr.Zero, true);
            return;
        }

        // 빈 영역을 지정하면 창이 보이지도 눌리지도 않게 되어 사용자가 복구할 수 없다 — 호출부 버그로 보고 무시한다.
        if (rects.Count == 0)
            return;

        var hRegion = CreateRegion(rects);
        if (hRegion == IntPtr.Zero)
            return;

        // 성공하면 리전의 소유권이 OS로 넘어가므로 삭제하면 안 되고, 실패했을 때만 직접 해제한다.
        if (SetWindowRgn(handle, hRegion, true) == 0)
            DeleteObject(hRegion);
    }

    /// <summary>
    /// 포커스를 가진 창에 WM_INPUTLANGCHANGE를 한 번 보낸다. Avalonia의 WndProc이 이 메시지를 받으면
    /// 전역 IME 싱글턴을 그 창으로 다시 묶는다(<c>WindowImpl.AppWndProc.cs</c>의 WM_INPUTLANGCHANGE →
    /// <c>UpdateInputMethod</c>). 키보드 레이아웃은 실제 현재 값을 그대로 넘기므로 IME를 껐다 켜는 경로
    /// (<c>DisableImm</c>/<c>EnableImm</c>)를 타지 않고 가리키는 창만 바뀐다 — 조합 중에 불러도 안전하다.
    /// </summary>
    public void RestoreImeBinding()
    {
        // GetFocus는 "호출한 스레드가 포커스를 쥐고 있을 때"만 핸들을 준다. 다른 앱이 포커스면 0이고,
        // 그때는 고칠 것도 없다 — 사용자가 우리 창으로 돌아오는 순간 WM_ACTIVATE가 알아서 되돌린다.
        var focused = GetFocus();
        if (focused == IntPtr.Zero)
            return;

        SendMessage(focused, WmInputLangChange, IntPtr.Zero, GetKeyboardLayout(0));
    }

    // RGNDATA = RGNDATAHEADER(32바이트) + RECT[] (left, top, right, bottom 각 int32, 우/하는 배타적 경계).
    private static IntPtr CreateRegion(IReadOnlyList<MaskRect> rects)
    {
        var data = new byte[RgnDataHeaderSize + rects.Count * RectSize];
        var span = data.AsSpan();

        int minX = int.MaxValue, minY = int.MaxValue, maxX = int.MinValue, maxY = int.MinValue;
        for (var i = 0; i < rects.Count; i++)
        {
            var r = rects[i];
            var offset = RgnDataHeaderSize + i * RectSize;
            BinaryPrimitives.WriteInt32LittleEndian(span[offset..], r.X);
            BinaryPrimitives.WriteInt32LittleEndian(span[(offset + 4)..], r.Y);
            BinaryPrimitives.WriteInt32LittleEndian(span[(offset + 8)..], r.X + r.Width);
            BinaryPrimitives.WriteInt32LittleEndian(span[(offset + 12)..], r.Y + r.Height);

            minX = Math.Min(minX, r.X);
            minY = Math.Min(minY, r.Y);
            maxX = Math.Max(maxX, r.X + r.Width);
            maxY = Math.Max(maxY, r.Y + r.Height);
        }

        BinaryPrimitives.WriteInt32LittleEndian(span, RgnDataHeaderSize);          // dwSize
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], RdhRectangles);         // iType
        BinaryPrimitives.WriteInt32LittleEndian(span[8..], rects.Count);           // nCount
        BinaryPrimitives.WriteInt32LittleEndian(span[12..], rects.Count * RectSize); // nRgnSize
        BinaryPrimitives.WriteInt32LittleEndian(span[16..], minX);                 // rcBound
        BinaryPrimitives.WriteInt32LittleEndian(span[20..], minY);
        BinaryPrimitives.WriteInt32LittleEndian(span[24..], maxX);
        BinaryPrimitives.WriteInt32LittleEndian(span[28..], maxY);

        return ExtCreateRegion(IntPtr.Zero, (uint)data.Length, data);
    }

    [DllImport("gdi32.dll")]
    private static extern IntPtr ExtCreateRegion(IntPtr lpx, uint nCount, byte[] lpData);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("user32.dll")]
    private static extern IntPtr GetFocus();

    /// <summary>0이면 호출한 스레드의 현재 키보드 레이아웃(HKL).</summary>
    [DllImport("user32.dll")]
    private static extern IntPtr GetKeyboardLayout(uint idThread);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);
}
