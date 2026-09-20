using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace App.Platform.Windows;

/// <summary>
/// IWindowBehavior의 Windows 구현(user32/gdi32 P/Invoke). 지금 필요한 SetInputShape와 SetAlwaysOnTop만 구현했고,
/// SetClickThrough/ExcludeFromTaskbar는 필요해지는 시점까지 no-op이다(Stub과 동일 동작).
/// </summary>
public sealed class WindowsWindowBehavior : IWindowBehavior
{
    private const int RdhRectangles = 1;
    private const int RgnDataHeaderSize = 32;
    private const int RectSize = 16;

    private static readonly IntPtr HwndTopmost = new(-1);
    private static readonly IntPtr HwndNoTopmost = new(-2);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;

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

    public void SetAlwaysOnTop(IntPtr handle, bool enabled)
    {
        if (handle == IntPtr.Zero)
            return;

        SetWindowPos(handle, enabled ? HwndTopmost : HwndNoTopmost, 0, 0, 0, 0, SwpNoMove | SwpNoSize | SwpNoActivate);
    }

    public void ExcludeFromTaskbar(IntPtr handle, bool enabled) { }

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
    private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, [MarshalAs(UnmanagedType.Bool)] bool bRedraw);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);
}
