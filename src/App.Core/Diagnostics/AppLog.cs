namespace App.Core.Diagnostics;

/// <summary>
/// 진단용 최소 파일 로그. 예외를 삼키는 안전망(전역 예외 핸들러, 폴백 경로 등)이 "삼켰다"는 사실만이라도 남기려고 만들었다.
/// 위치는 %LocalAppData%\StickyGhost\logs\app.log이고, 1MB를 넘으면 app.log.old 하나로 교체한다(1세대만 보관).
/// 로깅 자체가 실패해도 절대 예외를 밖으로 던지지 않는다 — 안전망이 다시 장애 원인이 되면 안 되므로.
/// 스레드 안전: 예외 핸들러는 비UI 스레드에서도 호출되므로 lock으로 직렬화한다.
/// </summary>
public static class AppLog
{
    private const long MaxBytes = 1024 * 1024;
    private static readonly object Gate = new();

    public static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickyGhost", "logs", "app.log");

    public static void Write(string category, Exception exception) =>
        Write(category, exception.ToString());

    public static void Write(string category, string message)
    {
        try
        {
            lock (Gate)
            {
                var directory = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                RotateIfTooLarge();
                File.AppendAllText(FilePath, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{category}] {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 로그를 못 남겨도 앱 동작에는 영향이 없어야 한다.
        }
    }

    private static void RotateIfTooLarge()
    {
        var info = new FileInfo(FilePath);
        if (!info.Exists || info.Length < MaxBytes)
            return;

        File.Move(FilePath, FilePath + ".old", overwrite: true);
    }
}
