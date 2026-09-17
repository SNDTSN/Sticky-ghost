using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Diagnostics;
using Avalonia.Markup.Xaml;
using App.UI.Views;

namespace App.UI;

public partial class App : Application
{
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

            desktop.MainWindow = new MainWindow();
#if DEBUG
            desktop.MainWindow.AttachDevTools();
#endif
        }

        base.OnFrameworkInitializationCompleted();
    }
}