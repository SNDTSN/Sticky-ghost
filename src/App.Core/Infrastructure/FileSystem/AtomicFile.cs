namespace App.Core.Infrastructure.FileSystem;

/// <summary>
/// 파일을 "통째로 바뀌거나 아예 안 바뀌거나" 둘 중 하나로만 쓰는 헬퍼.
///
/// <see cref="File.WriteAllText(string, string?)"/> 단독으로 쓰면 쓰는 도중 프로세스가 죽었을 때 잘린 JSON이 남고,
/// 다음 기동에서 파싱에 실패해 설정이 조용히 기본값으로 돌아간다(KNOWN_ISSUES #21). 임시 파일에 먼저 쓴 뒤
/// 교체하면 실패해도 원본이 그대로 남는다 — <see cref="Diagnostics.AppLog"/>의 로그 회전이 쓰는 방식과 같다.
///
/// 실패하면 예외를 그대로 던진다. "이 저장이 사용자가 요청한 것인가"는 호출부만 알기 때문에,
/// 알릴지 로그만 남길지도 호출부가 정한다.
/// </summary>
public static class AtomicFile
{
    public static void WriteAllText(string path, string contents)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        // 임시 파일은 반드시 같은 폴더에 만든다 — 다른 볼륨이면 File.Move가 복사+삭제가 되어 원자성이 깨진다.
        var tempPath = path + ".tmp";

        try
        {
            File.WriteAllText(tempPath, contents);
            File.Move(tempPath, path, overwrite: true);
        }
        catch
        {
            // 실패한 자리에 쓰다 만 .tmp를 남기지 않는다. 다음 저장이 어차피 덮어쓰지만,
            // 다시 저장하지 않으면 정체 모를 파일이 데이터 폴더에 계속 남는다.
            TryDeleteTemp(tempPath);
            throw;
        }
    }

    private static void TryDeleteTemp(string tempPath)
    {
        try
        {
            File.Delete(tempPath);
        }
        catch
        {
            // 뒷정리가 실패했다고 원래 예외를 가려서는 안 된다.
        }
    }
}
