using System;
using App.Platform;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Diagnostics;
using Avalonia.Markup.Xaml;
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

            desktop.MainWindow = new MainWindow(secretStore, windowBehavior);
#if DEBUG
            desktop.MainWindow.AttachDevTools();
#endif
        }

        base.OnFrameworkInitializationCompleted();
    }
}