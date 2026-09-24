using System;
using App.Core.Diagnostics;
using App.Platform;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Diagnostics;
using Avalonia.Markup.Xaml;
using App.UI.Composition;
using App.UI.Views;

namespace App.UI;

public partial class App : Application
{
    // App.UI는 구체 플랫폼 구현(App.Platform.Windows 등)을 참조하지 않는다 — 실행 진입점(App.Windows의
    // Program.cs)이 StartWithClassicDesktopLifetime 호출 전에 이 델리게이트를 반드시 설정해야 한다.
    public static Func<ISecretStore>? SecretStoreFactory { get; set; }
    public static Func<IWindowBehavior>? WindowBehaviorFactory { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // UI 스레드의 처리되지 않은 예외(async void 등)는 기본적으로 프로세스를 죽인다. 상시 떠 있는 위젯이 예상 못 한
        // 예외 하나로 사라지는 것보다 로그를 남기고 계속 도는 쪽을 택한다 — 예외 이후 상태가 어긋날 수 있다는 트레이드오프는
        // docs/stability-hardening.md에 기록. 문제가 생기면 Handled 줄만 지워 예전 동작(종료)으로 되돌릴 수 있다.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            AppLog.Write("ui-unhandled", e.Exception);
            e.Handled = true;
        };

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 기본값(OnLastWindowClose)이면 메모 위젯 창이 떠 있는 동안 메인 창을 닫아도
            // 프로세스가 안 죽고 메모 창들만 남는다 — 메인 창 종료 = 앱 전체 종료로 명시.
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            var secretStore = SecretStoreFactory?.Invoke()
                ?? throw new InvalidOperationException(
                    "App.SecretStoreFactory가 설정되지 않음 — 실행 진입점에서 먼저 지정해야 함");

            var windowBehavior = WindowBehaviorFactory?.Invoke()
                ?? throw new InvalidOperationException(
                    "App.WindowBehaviorFactory가 설정되지 않음 — 실행 진입점에서 먼저 지정해야 함");

            // 창이 아닌 것들은 여기서 한 번 만들어 메인 창에 넘긴다 — 앱의 조립 지점(KNOWN_ISSUES #27).
            var services = AppServices.Create(secretStore);
            desktop.MainWindow = new MainWindow(services, windowBehavior);
#if DEBUG
            desktop.MainWindow.AttachDevTools();
#endif
        }

        base.OnFrameworkInitializationCompleted();
    }
}