using App.Platform;

namespace App.Platform.Stub;

/// <summary>
/// IIdleDetector의 테스트용 구현체. 기본값은 항상 TimeSpan.Zero이며,
/// SetSimulatedIdleDuration으로 테스트에서 임의의 값을 주입할 수 있다.
/// </summary>
public sealed class StubIdleDetector : IIdleDetector
{
    private TimeSpan _simulatedIdle = TimeSpan.Zero;

    public TimeSpan GetIdleDuration() => _simulatedIdle;

    public void SetSimulatedIdleDuration(TimeSpan value) => _simulatedIdle = value;
}
