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

    public static Task<bool> ShowAsync(Window owner, string message)
        => new ConfirmDialog(message).ShowDialog<bool>(owner);
}
