using System.Collections.Generic;
using System.Linq;
using App.Core.Domain.Services;
using App.Core.Infrastructure.FileSystem;
using App.Core.Infrastructure.Llm;
using App.Platform;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace App.UI.Views;

/// <summary>LLM provider·모델·API 키·캐릭터 표시 설정을 입력하는 설정 창.
/// 저장된 API 키 값은 보안상 다시 보여주지 않고 상태만 표시한다.</summary>
public partial class SettingsWindow : Window
{
    private ISecretStore? _secretStore;
    private AppSettingsStore? _settingsStore;
    private AppSettings _settings = new();
    private List<CharacterPackScanEntry> _availablePacks = new();
    private bool _isLoading;

    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(ISecretStore secretStore, AppSettingsStore settingsStore, string characterPacksRootDir)
        : this()
    {
        _secretStore = secretStore;
        _settingsStore = settingsStore;
        _settings = _settingsStore.Load();
        _availablePacks = new CharacterPackScanner(new JsonCharacterPackLoader()).ScanAvailablePacks(characterPacksRootDir);

        _isLoading = true;
        ProviderComboBox.SelectedIndex = _settings.LlmProvider == LlmProviderCatalog.Gemini ? 1 : 0;
        CharacterVisibleToggle.IsChecked = _settings.CharacterVisible;
        SetScaleRadio(_settings.CharacterScale);
        CharacterPackComboBox.ItemsSource = _availablePacks;
        CharacterPackComboBox.SelectedItem =
            _availablePacks.FirstOrDefault(p => p.Id == _settings.SelectedCharacterPackId)
            ?? _availablePacks.FirstOrDefault();
        _isLoading = false;

        RefreshForSelectedProvider();
        ApplyCharacterToggleState();
    }

    private void OnCharacterVisibleToggled(object? sender, RoutedEventArgs e)
    {
        if (_isLoading)
            return;

        ApplyCharacterToggleState();
    }

    private void ApplyCharacterToggleState()
    {
        CharacterScalePanel.IsEnabled = CharacterVisibleToggle.IsChecked ?? true;
    }

    private void SetScaleRadio(int scale)
    {
        Scale50RadioButton.IsChecked = scale == 50;
        Scale150RadioButton.IsChecked = scale == 150;
        Scale200RadioButton.IsChecked = scale == 200;
        Scale100RadioButton.IsChecked = scale != 50 && scale != 150 && scale != 200;
    }

    private int GetSelectedScale()
    {
        if (Scale50RadioButton.IsChecked == true) return 50;
        if (Scale150RadioButton.IsChecked == true) return 150;
        if (Scale200RadioButton.IsChecked == true) return 200;
        return 100;
    }

    // provider를 바꾸면 다른 provider의 키를 실수로 덮어쓰지 않도록 입력창을 비운다.
    private void OnProviderChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
            return;

        ApiKeyTextBox.Text = string.Empty;
        RefreshForSelectedProvider();
    }

    private void RefreshForSelectedProvider()
    {
        var provider = SelectedProvider();
        ModelTextBox.Text = provider == LlmProviderCatalog.Gemini ? _settings.GeminiModel : _settings.OpenAiModel;
        ClearModelError();
        StatusText.Text = _secretStore!.TryGetSecret(LlmProviderCatalog.ApiKeySecretName(provider)) is not null
            ? "저장된 키 있음 (저장하면 덮어씀)"
            : "설정된 키 없음";
    }

    private string SelectedProvider() =>
        (ProviderComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? LlmProviderCatalog.OpenAi;

    private void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var provider = SelectedProvider();

        // 붙여넣기로 딸려온 앞뒤 공백/개행은 요청 URL에 %20으로 인코딩되어 404를 만든다(실측 확인).
        // API 키는 이미 아래에서 Trim하는데 모델명만 빠져 있었다.
        var model = ModelTextBox.Text?.Trim();
        if (string.IsNullOrEmpty(model))
        {
            // 예전에는 여기서 조용히 return이라 캐릭터 표시/배율/팩 선택과 API 키까지 통째로 저장되지 않고
            // 창도 닫히지 않았다 — 사용자에게는 "저장 버튼이 안 먹는다"로만 보였다.
            ShowModelError("모델명을 입력해 주세요.");
            ModelTextBox.Focus();
            return;
        }

        // 잘라낸 값을 화면에도 돌려준다 — 무엇이 저장됐는지 보이게.
        ModelTextBox.Text = model;
        ClearModelError();

        _settings = provider == LlmProviderCatalog.Gemini
            ? _settings with { LlmProvider = provider, GeminiModel = model }
            : _settings with { LlmProvider = provider, OpenAiModel = model };

        _settings = _settings with
        {
            CharacterVisible = CharacterVisibleToggle.IsChecked ?? true,
            CharacterScale = GetSelectedScale(),
            SelectedCharacterPackId = (CharacterPackComboBox.SelectedItem as CharacterPackScanEntry)?.Id,
        };

        _settingsStore!.Save(_settings);

        // 비워두면 기존 키를 그대로 둔다 — 매번 재입력을 강요하지 않기 위해.
        // 붙여넣기로 딸려온 앞뒤 공백/개행은 HTTP 헤더 값으로 못 쓰는 경우가 있어 저장 전에 잘라낸다.
        var key = ApiKeyTextBox.Text?.Trim();
        if (!string.IsNullOrEmpty(key))
            _secretStore!.SaveSecret(LlmProviderCatalog.ApiKeySecretName(provider), key);

        Close();
    }

    // 사용자가 고치기 시작하면 경고를 바로 치운다 — 입력 중에 빨간 글씨가 남아 있으면 안 고쳐진 것처럼 보인다.
    private void OnModelTextChanged(object? sender, TextChangedEventArgs e) => ClearModelError();

    private void ShowModelError(string message)
    {
        ModelErrorText.Text = message;
        ModelErrorText.IsVisible = true;
    }

    private void ClearModelError() => ModelErrorText.IsVisible = false;

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}
