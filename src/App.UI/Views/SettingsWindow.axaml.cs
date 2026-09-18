using App.Platform;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace App.UI.Views;

/// <summary>OpenAI API 키를 입력/저장하는 최소 설정 창. 저장된 값은 보안상 다시 보여주지 않고 상태만 표시한다.</summary>
public partial class SettingsWindow : Window
{
    private ISecretStore? _secretStore;
    private string? _apiKeySecretName;

    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(ISecretStore secretStore, string apiKeySecretName) : this()
    {
        _secretStore = secretStore;
        _apiKeySecretName = apiKeySecretName;

        StatusText.Text = _secretStore.TryGetSecret(_apiKeySecretName) is not null
            ? "저장된 키 있음 (저장하면 덮어씀)"
            : "설정된 키 없음";
    }

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var key = ApiKeyTextBox.Text;
        if (string.IsNullOrWhiteSpace(key))
            return;

        _secretStore!.SaveSecret(_apiKeySecretName!, key);
        Close();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
