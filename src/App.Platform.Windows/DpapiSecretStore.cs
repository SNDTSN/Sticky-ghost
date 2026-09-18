using System.Security.Cryptography;

namespace App.Platform.Windows;

/// <summary>
/// ISecretStore의 Windows 구현. DPAPI(<see cref="ProtectedData"/>)로 값을 암호화해서 로컬 파일에 저장한다.
/// DPAPI는 사용자 계정+머신에 바인딩되므로 다른 PC로 마이그레이션 시 재입력이 필요함 — 의도된 동작(비밀 유출 방지).
/// 상세 스펙은 docs/PORTING.md 참고.
/// </summary>
public sealed class DpapiSecretStore : ISecretStore
{
    private readonly string _secretsDir;

    public DpapiSecretStore(string secretsDir)
    {
        _secretsDir = secretsDir;
        Directory.CreateDirectory(_secretsDir);
    }

    public void SaveSecret(string key, string value)
    {
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(value);
        var protectedBytes = ProtectedData.Protect(plainBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(PathFor(key), protectedBytes);
    }

    public string? TryGetSecret(string key)
    {
        var path = PathFor(key);
        if (!File.Exists(path))
            return null;

        try
        {
            var protectedBytes = File.ReadAllBytes(path);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
            return System.Text.Encoding.UTF8.GetString(plainBytes);
        }
        catch (CryptographicException)
        {
            // 다른 사용자 계정/머신에서 파일만 복사되어온 경우 등 복호화 불가 — "저장된 값 없음"과 동일하게 취급.
            return null;
        }
    }

    public void DeleteSecret(string key)
    {
        var path = PathFor(key);
        if (File.Exists(path))
            File.Delete(path);
    }

    // key는 전부 내부 상수("llm.openai.apikey" 등)라 사용자 입력이 파일명에 섞이지 않음 — 새니타이즈 불필요.
    private string PathFor(string key) => Path.Combine(_secretsDir, $"{key}.secret");
}
