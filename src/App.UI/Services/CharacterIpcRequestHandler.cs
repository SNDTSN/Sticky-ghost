using System;
using System.Collections.Generic;
using System.Linq;
using App.Core.Domain.Services;
using App.Core.Infrastructure.Ipc;
using Avalonia.Threading;

namespace App.UI.Services;

/// <summary>
/// 명명 파이프로 들어온 요청을 실제 창 조작으로 바꾸는 App.UI 쪽 구현. 캐릭터 오버레이 창(말풍선/표정)은
/// CharacterOverlayController를 거쳐 조작한다.
/// 파이프 서버 콜백은 UI 스레드가 아니라서, 실제 창 조작은 Dispatcher.UIThread로 넘겨야 한다.
/// </summary>
public sealed class CharacterIpcRequestHandler : ICharacterIpcRequestHandler
{
    private readonly CharacterPackService _packService;
    private readonly CharacterOverlayController _overlay;
    private readonly Action _activateMainWindow;

    /// <param name="activateMainWindow">이중 실행된 두 번째 인스턴스의 요청("activate")으로 메인 창을 앞으로 가져오는 동작. UI 스레드에서 실행된다.</param>
    public CharacterIpcRequestHandler(
        CharacterPackService packService, CharacterOverlayController overlay, Action activateMainWindow)
    {
        _packService = packService;
        _overlay = overlay;
        _activateMainWindow = activateMainWindow;
    }

    public CharacterIpcResponse Handle(CharacterIpcRequest request)
    {
        // 캐릭터 팩 로드/표시 여부와 무관한 앱 제어 요청이라 아래 검사보다 먼저 처리한다.
        if (request.Type == CharacterIpcRequest.TypeActivate)
        {
            Dispatcher.UIThread.Post(_activateMainWindow);
            return new CharacterIpcResponse(true, null);
        }

        var pack = _packService.CurrentPack;
        if (pack is null)
            return new CharacterIpcResponse(false, "캐릭터팩이 로드되지 않음");

        if (!_overlay.IsVisible)
            return new CharacterIpcResponse(false, "캐릭터가 표시되고 있지 않음 (설정에서 캐릭터 표시가 꺼져 있을 수 있음)");

        return request.Type switch
        {
            CharacterIpcRequest.TypeSay => HandleSay(request.Text),
            CharacterIpcRequest.TypeSetExpression => HandleSetExpression(pack.Appearance.Expressions.Select(e => e.Id), request.ExpressionId),
            _ => new CharacterIpcResponse(false, $"알 수 없는 type: {request.Type}"),
        };
    }

    private CharacterIpcResponse HandleSay(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new CharacterIpcResponse(false, "text 없음");

        Dispatcher.UIThread.Post(() => _overlay.Say(text));
        return new CharacterIpcResponse(true, null);
    }

    private CharacterIpcResponse HandleSetExpression(IEnumerable<string> availableIds, string? expressionId)
    {
        if (string.IsNullOrWhiteSpace(expressionId))
            return new CharacterIpcResponse(false, "expressionId 없음");

        if (!availableIds.Contains(expressionId))
            return new CharacterIpcResponse(false, $"정의되지 않은 expressionId: {expressionId}");

        Dispatcher.UIThread.Post(() => _overlay.SetExpression(expressionId));
        return new CharacterIpcResponse(true, null);
    }
}
