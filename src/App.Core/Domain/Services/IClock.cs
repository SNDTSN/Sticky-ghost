namespace App.Core.Domain.Services;

public interface IClock
{
    /// <summary>
    /// 현재 시각 (로컬 벽시계 시간, KST). 이 앱은 한국 사용자 전용이라 UTC 변환 없이
    /// 시스템 로컬 시간을 그대로 사용한다 — docs/timezone-policy.md 참고.
    /// </summary>
    DateTime Now { get; }
}
