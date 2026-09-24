using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using App.Core.Diagnostics;
using App.Core.Domain.Services;
using App.Core.Infrastructure.FileSystem;
using App.Core.Infrastructure.Llm;
using App.Platform;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace App.UI.Views;

/// <summary>할 일 정렬 기준·LLM provider·모델·API 키·캐릭터 표시 설정을 입력하는 설정 창.
/// 저장된 API 키 값은 보안상 다시 보여주지 않고 상태만 표시한다.</summary>
public partial class SettingsWindow : Window
{
    private ISecretStore? _secretStore;
    private AppSettingsStore? _settingsStore;
    private AppSettings _settings = new();
    private List<CharacterPackScanEntry> _availablePacks = new();
    private bool _isLoading;
    // 이용자가 팩 목록을 직접 바꿨는지. 안 바꿨으면 저장할 때 SelectedCharacterPackId를 건드리지 않는다 —
    // 목록이 보여주는 기본 캐릭터 id를 저장해 버리면 깨진 팩의 id가 사라지고(#9), 선택한 적 없는 사용자의 표시 팩이 바뀐다.
    private bool _packSelectionTouched;

    public SettingsWindow()
    {
        InitializeComponent();
    }

    public SettingsWindow(
        ISecretStore secretStore, AppSettingsStore settingsStore, CharacterPackScanner packScanner,
        string characterPacksRootDir, string builtInPackPath)
        : this()
    {
        _secretStore = secretStore;
        _settingsStore = settingsStore;
        _settings = _settingsStore.Load();
        _availablePacks = packScanner.ScanAvailablePacks(characterPacksRootDir);

        _isLoading = true;
        ImportantFirstRadioButton.IsChecked = _settings.TodoSortOrder == TodoSortOrder.ImportantFirst;
        UrgentFirstRadioButton.IsChecked = _settings.TodoSortOrder != TodoSortOrder.ImportantFirst;
        ProviderComboBox.SelectedIndex = _settings.LlmProvider == LlmProviderCatalog.Gemini ? 1 : 0;
        CharacterVisibleToggle.IsChecked = _settings.CharacterVisible;
        SetScaleRadio(_settings.CharacterScale);
        CharacterPackComboBox.ItemsSource = _availablePacks;
        // 저장된 팩이 없거나 스캔에서 빠졌으면 캐릭터 표시(CharacterOverlayController.Apply)와 같은 규칙으로 내장 팩을 가리킨다.
        // 내장 팩은 id가 아니라 폴더로 찾는다 — 다른 팩이 같은 id를 쓰면 엉뚱한 쪽이 잡힌다(KNOWN_ISSUES #11).
        var savedPack = _availablePacks.FirstOrDefault(p => p.Id == _settings.SelectedCharacterPackId);
        CharacterPackComboBox.SelectedItem = savedPack ?? _availablePacks.FirstOrDefault(p => IsSameFolder(p.FolderPath, builtInPackPath));
        if (_settings.SelectedCharacterPackId is { } savedId && savedPack is null)
        {
            MissingPackText.Text = $"선택했던 캐릭터({savedId})를 찾지 못해 기본 캐릭터로 표시 중입니다.";
            MissingPackText.IsVisible = true;
        }
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

    private void OnCharacterPackChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_isLoading)
            return;

        _packSelectionTouched = true;
    }

    private static bool IsSameFolder(string a, string b) =>
        string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

    private void ApplyCharacterToggleState()
    {
        CharacterScalePanel.IsEnabled = CharacterVisibleToggle.IsChecked ?? true;
    }

    private void SetScaleRadio(int scale)
    {
        Scale50RadioButton.IsChecked = scale == 50;
        Scale150RadioButton.IsChecked = scale == 150;
        Scale200RadioButton.IsChecked = scale == 200;
        // Load가 허용 값(AppSettings.AllowedCharacterScales)으로 바로잡아 주지만, 방어적으로 그 밖의 값은 100으로 본다.
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
            TodoSortOrder = ImportantFirstRadioButton.IsChecked == true ? TodoSortOrder.ImportantFirst : TodoSortOrder.UrgentFirst,
            CharacterVisible = CharacterVisibleToggle.IsChecked ?? true,
            CharacterScale = GetSelectedScale(),
            SelectedCharacterPackId = _packSelectionTouched
                ? (CharacterPackComboBox.SelectedItem as CharacterPackScanEntry)?.Id
                : _settings.SelectedCharacterPackId,
        };

        // 저장은 사용자가 버튼을 눌러 요청한 것이라 실패를 조용히 묻으면 안 된다(KNOWN_ISSUES #21).
        // 백신/동기화 도구가 파일을 잡고 있는 등으로 실패하면, 창을 닫지 않고 그 자리에서 알린다 —
        // 입력한 값이 남아 있어야 다시 시도할 수 있다.
        try
        {
            _settingsStore!.Save(_settings);

            // 비워두면 기존 키를 그대로 둔다 — 매번 재입력을 강요하지 않기 위해.
            // 붙여넣기로 딸려온 앞뒤 공백/개행은 HTTP 헤더 값으로 못 쓰는 경우가 있어 저장 전에 잘라낸다.
            var key = ApiKeyTextBox.Text?.Trim();
            if (!string.IsNullOrEmpty(key))
                _secretStore!.SaveSecret(LlmProviderCatalog.ApiKeySecretName(provider), key);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLog.Write("settings", ex);
            SaveErrorText.Text = "설정을 저장하지 못했습니다. 다른 프로그램(백신·동기화 도구 등)이 파일을 사용 중일 수 있습니다.\n"
                                 + $"잠시 후 다시 시도해 주세요. ({ex.Message})";
            SaveErrorText.IsVisible = true;
            return;
        }

        SaveErrorText.IsVisible = false;
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
