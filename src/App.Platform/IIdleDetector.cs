namespace App.Platform;

/// <summary>
/// 캐릭터의 Idle 상태(가만히 있을 때의 애니메이션/대사) 트리거용으로
/// 사용자의 유휴 시간을 조회. 상세는 docs/PORTING.md 참고.
/// </summary>
public interface IIdleDetector
{
    /// <summary>마지막 키보드/마우스 입력 이후 경과 시간</summary>
    TimeSpan GetIdleDuration();
}
