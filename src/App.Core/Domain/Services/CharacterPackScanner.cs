using App.Core.Diagnostics;
using App.Core.Domain.Repositories;

namespace App.Core.Domain.Services;

/// <summary>주어진 상위 폴더 아래의 각 하위 폴더를 캐릭터팩 후보로 보고 스캔한다.
/// manifest.json이 없거나 검증에 실패한 폴더(PNG가 아닌 이미지 포함)는 목록에서 빼되, 사유는 진단 로그(AppLog "pack")에 남긴다 —
/// 팩 이름조차 못 읽었을 수 있어서 UI에 표시할 게 마땅치 않으므로 화면에는 알리지 않는다.
/// 같은 폴더의 같은 사유는 프로세스당 한 번만 기록한다(설정창을 열 때마다 스캔하므로 로그 반복 방지).</summary>
public sealed class CharacterPackScanner
{
    private static readonly HashSet<string> LoggedExclusions = new();

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
            {
                entries.Add(new CharacterPackScanEntry(
                    result.Pack!.Id, result.Pack.Name, folder, result.Pack.EstimatedMemoryBytes));
            }
            else
            {
                LogExclusionOnce(folder, result.Errors);
            }
        }

        return entries;
    }

    private static void LogExclusionOnce(string folder, List<string> errors)
    {
        var message = $"팩 목록에서 제외: {folder} — {string.Join(", ", errors)}";
        lock (LoggedExclusions)
        {
            if (!LoggedExclusions.Add(message))
                return;
        }

        AppLog.Write("pack", message);
    }
}

/// <param name="EstimatedMemoryBytes">팩이 상주시킬 메모리 추정치(<see cref="Entities.CharacterPack.EstimatedMemoryBytes"/>).</param>
public sealed record CharacterPackScanEntry(string Id, string Name, string FolderPath, long EstimatedMemoryBytes = 0)
{
    // 이미지 용량에는 상한을 두지 않고, 이 값을 넘으면 설정창 목록에 경고를 붙인다. 앱 기본 사용량(약 160MB)의 40% 정도를 기준으로 잡았다.
    // 팩 제작자가 조정할 필요 없는 값이라 매니페스트가 아니라 코드 상수로 고정(다른 임계값들과 같은 방침).
    public const long HeavyThresholdBytes = 64L * 1024 * 1024;

    public bool IsHeavy => EstimatedMemoryBytes > HeavyThresholdBytes;

    /// <summary>설정창 목록에 보여줄 이름. 무거운 팩에는 예상 메모리와 함께 "용량 최적화 필요"를 붙인다.</summary>
    public string DisplayName =>
        IsHeavy ? $"{Name} (용량 최적화 필요 · 약 {EstimatedMemoryBytes / (1024.0 * 1024.0):0}MB)" : Name;
}
