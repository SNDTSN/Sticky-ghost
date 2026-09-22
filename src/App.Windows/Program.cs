using System;
using System.IO;
using App.Core.Diagnostics;
using App.Core.Infrastructure.FileSystem;
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
        // 뮤텍스는 Avalonia 종료 시점까지 잡고 있어야 하므로 Main 끝까지 using으로 들고 있는다.
        using var singleInstance = new SingleInstanceGuard();
        if (!singleInstance.IsFirstInstance)
        {
            singleInstance.NotifyExistingInstance();
            return;
        }

        RegisterGlobalExceptionLogging();

        // App.UI는 플랫폼 구현을 모르므로, 실행 진입점인 여기서 조립해 델리게이트로 넘겨준다.
        App.UI.App.SecretStoreFactory = CreateSecretStore;
        App.UI.App.WindowBehaviorFactory = () => new WindowsWindowBehavior();

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // UI 스레드 예외는 App.UI.App이 Dispatcher.UnhandledException으로 따로 다룬다. 여기는 그 밖의 스레드용 —
    // AppDomain 미처리 예외는 종료를 막을 수 없으므로 죽기 전에 로그만 남기고, 관찰되지 않은 Task 예외는 로그 후 관찰 처리한다.
    private static void RegisterGlobalExceptionLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            AppLog.Write("unhandled", e.ExceptionObject?.ToString() ?? "(null)");

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            AppLog.Write("unobserved-task", e.Exception);
            e.SetObserved();
        };
    }

    private static ISecretStore CreateSecretStore() =>
        new DpapiSecretStore(Path.Combine(AppPaths.DataDir, "secrets"));

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App.UI.App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
