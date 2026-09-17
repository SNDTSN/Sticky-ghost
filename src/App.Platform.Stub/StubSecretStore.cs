using App.Platform;

namespace App.Platform.Stub;

/// <summary>
/// ISecretStore의 인메모리 구현체. 프로세스 종료 시 소실되며,
/// 암호화를 전혀 하지 않으므로 테스트/개발 전용 — 실제 시크릿 저장에는 쓰지 않는다.
/// UI 스레드에서만 호출된다고 가정한다 (동시성 처리 없음).
/// </summary>
public sealed class StubSecretStore : ISecretStore
{
    private readonly Dictionary<string, string> _store = new();

    public void SaveSecret(string key, string value) => _store[key] = value;

    public string? TryGetSecret(string key) => _store.GetValueOrDefault(key);

    public void DeleteSecret(string key) => _store.Remove(key);
}
