using System;
using System.IO;
using App.Platform;
using App.Platform.Windows;
using Avalonia;

namespace App.Windows;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // App.UI는 플랫폼 구현을 모르므로, 실행 진입점인 여기서 조립해 델리게이트로 넘겨준다.
        App.UI.App.SecretStoreFactory = CreateSecretStore;

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    private static ISecretStore CreateSecretStore()
    {
        var dataDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "StickyGhost");
        return new DpapiSecretStore(Path.Combine(dataDir, "secrets"));
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App.UI.App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
