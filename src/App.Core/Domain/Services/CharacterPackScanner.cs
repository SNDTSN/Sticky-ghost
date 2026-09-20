using App.Core.Diagnostics;
using App.Core.Domain.Repositories;

namespace App.Core.Domain.Services;

/// <summary>주어진 상위 폴더 아래의 각 하위 폴더를 캐릭터팩 후보로 보고 스캔한다.
/// manifest.json이 없거나 검증에 실패한 폴더(PNG가 아닌 이미지 포함)는 목록에서 빼되, 사유는 진단 로그(AppLog "pack")에 남긴다 —
/// 팩 이름조차 못 읽었을 수 있어서 UI에 표시할 게 마땅치 않으므로 화면에는 알리지 않는다.
/// 같은 폴더의 같은 사유는 프로세스당 한 번만 기록한다(설정창을 열 때마다 스캔하므로 로그 반복 방지).
///
/// <para><b>이 메서드는 예외를 밖으로 내보내지 않는다.</b> 호출부가 캐릭터 표시(CharacterOverlayController.Apply)와
/// 설정창 생성자라서, 여기서 예외가 새면 잘못된 팩 폴더 하나 때문에 정상 팩까지 전부 안 보이고 설정창도 열리지 않는다 —
/// 즉 이용자가 문제의 팩을 바꾸러 들어갈 UI 자체가 사라진다(KNOWN_ISSUES #16).</para></summary>
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

        // 폴더 목록을 얻는 것 자체도 실패할 수 있다 — Exists를 통과한 뒤에도 권한/IO 문제로 던질 수 있고,
        // 그 사이에 폴더가 사라질 수도 있다.
        string[] folders;
        try
        {
            if (!Directory.Exists(packsRootDir))
                return entries;

            folders = Directory.GetDirectories(packsRootDir);
        }
        catch (Exception ex)
        {
            AppLog.Write("pack", $"팩 폴더 목록을 읽지 못함: {packsRootDir} — {ex}");
            return entries;
        }

        foreach (var folder in folders)
        {
            try
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
            catch (Exception ex)
            {
                // 로더가 검증 오류로 바꾸지 못한 예외까지 여기서 받아낸다. 폴더 하나만 빼고 스캔은 계속한다 —
                // 이 catch가 위 클래스 주석의 "예외를 밖으로 내보내지 않는다"를 실제로 보장하는 지점이다.
                LogExclusionOnce(folder, [$"예상 못 한 오류: {ex}"]);
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
