namespace App.Core.Infrastructure.FileSystem;

/// <summary>
/// 앱이 쓰는 데이터 폴더 경로를 한 곳에 모아둔 것. 예전에는 AppLog·MainWindow·App.Windows의 Program이
/// 각자 %LocalAppData%\StickyGhost를 따로 조립했는데, 그러면 경로를 바꿀 때 세 군데를 다 찾아 고쳐야 한다.
///
/// Mac 이식 때 손볼 지점도 <see cref="DataDir"/> 하나다 — .NET에서 macOS의 LocalApplicationData는
/// ~/.local/share로 매핑되어 맥 관례(~/Library/Application Support)와 다르다(docs/PORTING.md 참고).
/// </summary>
public static class AppPaths
{
    /// <summary>DB·설정·창 위치·비밀 저장소가 들어가는 앱 데이터 루트. 폴더 생성은 쓰는 쪽이 한다.</summary>
    public static string DataDir { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickyGhost");

    /// <summary>진단 로그 파일. <see cref="Diagnostics.AppLog"/>가 쓴다.</summary>
    public static string LogFile { get; } = Path.Combine(DataDir, "logs", "app.log");
}
