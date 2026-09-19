using App.Core.Domain.Repositories;

namespace App.Core.Domain.Services;

/// <summary>주어진 상위 폴더 아래의 각 하위 폴더를 캐릭터팩 후보로 보고 스캔한다.
/// manifest.json이 없거나 검증에 실패한 폴더는 조용히 건너뛴다 — 개별 에러는 호출부에 알리지 않는다
/// (팩 이름조차 못 읽었을 수 있어서 UI에 표시할 게 마땅치 않다).</summary>
public sealed class CharacterPackScanner
{
    private readonly ICharacterPackLoader _loader;

    public CharacterPackScanner(ICharacterPackLoader loader)
    {
        _loader = loader;
    }

    public List<CharacterPackScanEntry> ScanAvailablePacks(string packsRootDir)
    {
        var entries = new List<CharacterPackScanEntry>();
        if (!Directory.Exists(packsRootDir))
            return entries;

        foreach (var folder in Directory.GetDirectories(packsRootDir))
        {
            var result = _loader.Load(folder);
            if (result.IsSuccess)
                entries.Add(new CharacterPackScanEntry(result.Pack!.Id, result.Pack.Name, folder));
        }

        return entries;
    }
}

public sealed record CharacterPackScanEntry(string Id, string Name, string FolderPath);
