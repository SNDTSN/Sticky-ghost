using System;
using System.IO;
using System.Linq;
using App.Core.Diagnostics;
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
/// <summary>UsedFallback이면 사용자가 고른 팩을 그리지 못해 내장 팩으로 대체했다는 뜻이고, FailureReason은 원인 메시지.</summary>
public sealed record PackApplyResult(bool UsedFallback, string? FailureReason = null)
{
    public static PackApplyResult Applied { get; } = new(false);
}

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
    /// 매니페스트 로딩에 실패하면 CharacterPackService의 폴백 정책이 내장 팩으로 대체한다.
    /// 매니페스트는 통과했지만 이미지 디코딩 등 렌더링 단계에서 실패한 경우(손상된 PNG)도 내장 팩으로 폴백하고
    /// 그 사실을 반환값으로 알린다. 내장 팩까지 실패하면 InvalidOperationException을 던진다(배포 버그).
    /// </summary>
    public PackApplyResult Apply(AppSettings settings)
    {
        if (!settings.CharacterVisible)
        {
            if (_window is not null)
            {
                _window.Close();
                // 창이 닫히면 Avalonia가 전역 IME 상태를 정리하는데, 그 상태가 이 창을 가리키고 있으면
                // 입력 대상까지 같이 지워져 메인 창의 한글 입력이 죽는다(#24). 닫은 직후에 되돌려 둔다.
                _windowBehavior.RestoreImeBinding();
            }

            return PackApplyResult.Applied;
        }

        var folder = _scanner.ScanAvailablePacks(_packsRootDir)
            .FirstOrDefault(p => p.Id == settings.SelectedCharacterPackId)?.FolderPath ?? _builtInPackPath;

        try
        {
            ApplyFolder(folder, settings);
            return PackApplyResult.Applied;
        }
        catch (Exception ex) when (!IsBuiltIn(folder))
        {
            AppLog.Write("pack", ex);
            try
            {
                ApplyFolder(_builtInPackPath, settings);
            }
            catch (Exception fallbackEx)
            {
                AppLog.Write("pack", fallbackEx);
                throw new InvalidOperationException("내장 캐릭터팩 렌더링 실패: " + fallbackEx.Message, fallbackEx);
            }

            return new PackApplyResult(UsedFallback: true, FailureReason: ex.Message);
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Write("pack", ex);
            throw new InvalidOperationException("내장 캐릭터팩 렌더링 실패: " + ex.Message, ex);
        }
    }

    private bool IsBuiltIn(string folder) =>
        string.Equals(Path.GetFullPath(folder), Path.GetFullPath(_builtInPackPath), StringComparison.OrdinalIgnoreCase);

    private void ApplyFolder(string folder, AppSettings settings)
    {
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
