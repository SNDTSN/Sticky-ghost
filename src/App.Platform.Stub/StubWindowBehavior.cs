using App.Platform;

namespace App.Platform.Stub;

/// <summary>
/// IWindowBehavior의 무동작(no-op) 구현체. 일반 창처럼 동작한다.
/// </summary>
public sealed class StubWindowBehavior : IWindowBehavior
{
    public void SetClickThrough(IntPtr handle, bool enabled) { }

    public void SetInputShape(IntPtr handle, IReadOnlyList<MaskRect>? rects) { }

    public void RestoreImeBinding() { }
}
