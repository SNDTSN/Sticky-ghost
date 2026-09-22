using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace App.UI.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    private ConfirmDialog(string message) : this()
    {
        MessageText.Text = message;
    }

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    /// <summary>
    /// 소유자가 항상 위 표시(<see cref="Window.Topmost"/>)면 대화상자도 같이 올린다. Windows의 Z순서는
    /// topmost 대역과 일반 대역이 분리돼 있어서, 일반 대역에 있는 대화상자는 소유자이자 모달인 topmost 창
    /// <b>뒤로</b> 숨어버린다 — 메모 창이 잠긴 채 확인창이 보이지 않아 사용자가 빠져나갈 수 없다(KNOWN_ISSUES #25).
    /// </summary>
    public static Task<bool> ShowAsync(Window owner, string message)
    {
        var dialog = new ConfirmDialog(message) { Topmost = owner.Topmost };
        return dialog.ShowDialog<bool>(owner);
    }
}
