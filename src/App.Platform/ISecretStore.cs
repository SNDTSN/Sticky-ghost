namespace App.Platform;

/// <summary>
/// LLM 어댑터(OpenAI/Gemini 등)의 API 키를 암호화해서 로컬에 저장.
/// 절대 평문으로 저장하지 않는다. 상세는 docs/PORTING.md 참고.
/// </summary>
public interface ISecretStore
{
    /// <summary>값을 암호화해서 로컬에 영구 저장 (기존 값 덮어쓰기)</summary>
    void SaveSecret(string key, string value);

    /// <summary>저장된 값을 복호화해서 반환, 없으면 null</summary>
    string? TryGetSecret(string key);

    /// <summary>저장된 값 삭제</summary>
    void DeleteSecret(string key);
}
