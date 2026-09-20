using System.Linq;
using App.Core.Domain.Services;
using App.Core.Infrastructure.FileSystem;
using App.Platform;
using App.UI.Views;

namespace App.UI.Services;

/// <summary>
/// 상시 캐릭터 오버레이 창의 생명주기(표시/숨김, 팩 교체, 배율)를 설정값에 맞춰 관리한다.
/// MainWindow는 Apply(settings)만 호출하고, App.Mcp IPC 핸들러도 창을 직접 붙잡지 않고 이 컨트롤러를 거친다.
/// 창은 하나만 유지하며 설정이 바뀌어도 새로 만들지 않고 내용만 교체한다(끄면 닫고, 다시 켜면 새로 만든다).
/// </summary>
public sealed class CharacterOverlayController
{
    private readonly CharacterPackService _packService;
    private readonly CharacterPackScanner _scanner = new(new JsonCharacterPackLoader());
    private readonly string _packsRootDir;
    private readonly string _builtInPackPath;
    private readonly WindowStateStore _stateStore;
    private readonly IWindowBehavior _windowBehavior;
    private CharacterReactionService _reactionService;
    private CharacterWindow? _window;

    public CharacterOverlayController(
        CharacterPackService packService,
        string packsRootDir,
        string builtInPackPath,
        WindowStateStore stateStore,
        IWindowBehavior windowBehavior,
        CharacterReactionService reactionService)
    {
        _packService = packService;
        _packsRootDir = packsRootDir;
        _builtInPackPath = builtInPackPath;
        _stateStore = stateStore;
        _windowBehavior = windowBehavior;
        _reactionService = reactionService;
    }

    public bool IsVisible => _window is not null;

    /// <summary>
    /// 설정(표시 여부/배율/선택된 팩)을 창에 반영한다. 선택된 팩이 없거나 스캔에서 사라졌다면 내장 팩을 쓰고,
    /// 로딩에 실패하면 CharacterPackService의 폴백 정책이 내장 팩으로 대체한다.
    /// 내장 팩까지 깨졌으면 InvalidOperationException이 그대로 올라간다(배포 버그).
    /// </summary>
    public void Apply(AppSettings settings)
    {
        if (!settings.CharacterVisible)
        {
            _window?.Close();
            return;
        }

        var folder = _scanner.ScanAvailablePacks(_packsRootDir)
            .FirstOrDefault(p => p.Id == settings.SelectedCharacterPackId)?.FolderPath ?? _builtInPackPath;
        var pack = _packService.LoadPack(folder).Pack;

        if (_window is null)
        {
            var window = new CharacterWindow(
                pack, settings.CharacterScale, _reactionService, _stateStore, _windowBehavior);
            window.Closed += (_, _) =>
            {
                if (ReferenceEquals(_window, window))
                    _window = null;
            };
            _window = window;
            window.Show();
        }
        else if (_window.PackId != pack.Id)
        {
            _window.SetPack(pack, settings.CharacterScale);
        }
        else
        {
            _window.SetScale(settings.CharacterScale);
        }
    }

    /// <summary>설정 변경(provider/모델/키)으로 재조립된 서비스를 이후 창 생성과 이미 떠 있는 창 양쪽에 반영한다.</summary>
    public void SetReactionService(CharacterReactionService reactionService)
    {
        _reactionService = reactionService;
        _window?.UpdateReactionService(reactionService);
    }

    /// <summary>UI 스레드에서 호출해야 한다.</summary>
    public void Say(string text) => _window?.ShowLine(text);

    /// <summary>UI 스레드에서 호출해야 한다.</summary>
    public void SetExpression(string expressionId) => _window?.ShowExpression(expressionId);

    public void FlushPlacement() => _window?.FlushPlacement();
}
