namespace App.Platform;

/// <summary>
/// 캐릭터 오버레이 창, 메모 위젯 창처럼 일반적인 앱 창과 다르게 동작해야 하는
/// 네이티브 창 제어. 상세 API 매핑은 docs/PORTING.md 참고.
/// </summary>
public interface IWindowBehavior
{
    /// <summary>true면 창이 마우스 이벤트를 받지 않고 아래로 흘려보냄</summary>
    void SetClickThrough(IntPtr handle, bool enabled);

    /// <summary>사각형이 아닌 스프라이트(PNG alpha) 모양 그대로 창 외곽선을 렌더링</summary>
    void SetPerPixelTransparency(IntPtr handle, bool enabled);

    /// <summary>다른 일반 창들보다 항상 위에 표시</summary>
    void SetAlwaysOnTop(IntPtr handle, bool enabled);

    /// <summary>작업표시줄/Dock에 아이콘이 뜨지 않게 함</summary>
    void ExcludeFromTaskbar(IntPtr handle, bool enabled);
}
