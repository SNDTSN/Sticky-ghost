namespace App.Platform;

/// <summary>창 좌상단을 (0,0)으로 한 물리 픽셀 사각형. 창의 입력 영역(<see cref="IWindowBehavior.SetInputShape"/>)을 기술하는 용도.</summary>
public readonly record struct MaskRect(int X, int Y, int Width, int Height);

/// <summary>
/// 캐릭터 오버레이 창, 메모 위젯 창처럼 일반적인 앱 창과 다르게 동작해야 하는 네이티브 창 제어. 상세 API 매핑은
/// docs/PORTING.md 참고. Avalonia 속성(Topmost, ShowInTaskbar, TransparencyLevelHint 등)으로 되는 것은 여기 넣지 않는다.
/// </summary>
public interface IWindowBehavior
{
    /// <summary>true면 창이 마우스 이벤트를 받지 않고 아래로 흘려보냄</summary>
    void SetClickThrough(IntPtr handle, bool enabled);

    /// <summary>
    /// 창의 "입력을 받는 모양"을 지정한다. rects 안쪽만 마우스 입력을 받고 렌더링되며, 그 밖은 아래 창으로 통과한다
    /// (투명 PNG 캐릭터의 투명한 부분이 뒤에 있는 창 클릭을 막지 않게 하기 위함). 사각형들은 서로 겹치지 않아야 한다.
    /// null이면 제한을 해제해 창 사각형 전체가 입력을 받는다.
    /// </summary>
    void SetInputShape(IntPtr handle, IReadOnlyList<MaskRect>? rects);

    /// <summary>다른 일반 창들보다 항상 위에 표시</summary>
    void SetAlwaysOnTop(IntPtr handle, bool enabled);

    /// <summary>작업표시줄/Dock에 아이콘이 뜨지 않게 함</summary>
    void ExcludeFromTaskbar(IntPtr handle, bool enabled);
}
