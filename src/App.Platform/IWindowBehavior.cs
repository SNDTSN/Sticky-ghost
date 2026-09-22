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

    /// <summary>
    /// IME(한글 조합) 입력을 받을 창을 "지금 포커스를 가진 창"으로 되돌린다. 포커스를 빼앗지 않는 창
    /// (<c>ShowActivated="False"</c>인 캐릭터·말풍선 창)을 만들거나 닫은 직후에 불러야 한다.
    ///
    /// Avalonia는 IME 상태를 프로세스 전역 싱글턴 하나(<c>Imm32InputMethod.Current</c>)에 두고, 창을 새로
    /// 만들 때마다 그 싱글턴이 가리키는 창을 새 창으로 바꿔 쓴다. 보통은 새 창이 활성화되고 사용자가 원래 창으로
    /// 돌아올 때 WM_ACTIVATE로 원위치되지만, 활성화 없이 뜨는 창은 그 복구 시점이 영영 오지 않아 원래 창의 한글
    /// 입력이 계속 깨진다. 자세한 근거는 KNOWN_ISSUES.md #24.
    ///
    /// 포커스나 창 순서(z-order)는 건드리지 않으므로 사용자가 조합 중일 때 불러도 안전하고, 이미 올바른 창을
    /// 가리키고 있으면 아무 일도 하지 않는다.
    /// </summary>
    void RestoreImeBinding();
}
