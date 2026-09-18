using System;
using System.Linq;
using App.Core.Domain.Entities;
using App.Core.Domain.Services;
using App.Core.Infrastructure.Ipc;
using App.UI.Views;
using Avalonia.Threading;

namespace App.UI.Services;

/// <summary>
/// 명명 파이프로 들어온 요청을 실제 창 조작으로 바꾸는 App.UI 쪽 구현. 지금은 "캐릭터 미리보기" 창에
/// 텍스트 오버레이/표정을 띄우는 것까지만 — 제대로 된 상시 캐릭터 오버레이 창은 별도 작업.
/// 파이프 서버 콜백은 UI 스레드가 아니라서, 실제 창 조작은 Dispatcher.UIThread로 넘겨야 한다.
/// </summary>
public sealed class CharacterIpcRequestHandler : ICharacterIpcRequestHandler
{
    private readonly CharacterPackService _packService;
    private readonly Func<CharacterPreviewWindow?> _getActiveWindow;

    public CharacterIpcRequestHandler(CharacterPackService packService, Func<CharacterPreviewWindow?> getActiveWindow)
    {
        _packService = packService;
        _getActiveWindow = getActiveWindow;
    }

    public CharacterIpcResponse Handle(CharacterIpcRequest request)
    {
        var pack = _packService.CurrentPack;
        if (pack is null)
            return new CharacterIpcResponse(false, "캐릭터팩이 로드되지 않음");

        var window = _getActiveWindow();
        if (window is null)
            return new CharacterIpcResponse(false, "캐릭터 미리보기 창이 열려 있지 않음");

        return request.Type switch
        {
            "say" => HandleSay(window, request.Text),
            "setExpression" => HandleSetExpression(pack, window, request.ExpressionId),
            _ => new CharacterIpcResponse(false, $"알 수 없는 type: {request.Type}"),
        };
    }

    private static CharacterIpcResponse HandleSay(CharacterPreviewWindow window, string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return new CharacterIpcResponse(false, "text 없음");

        Dispatcher.UIThread.Post(() => window.ShowLine(text));
        return new CharacterIpcResponse(true, null);
    }

    private static CharacterIpcResponse HandleSetExpression(
        CharacterPack pack, CharacterPreviewWindow window, string? expressionId)
    {
        if (string.IsNullOrWhiteSpace(expressionId))
            return new CharacterIpcResponse(false, "expressionId 없음");

        if (pack.Appearance.Expressions.All(e => e.Id != expressionId))
            return new CharacterIpcResponse(false, $"정의되지 않은 expressionId: {expressionId}");

        Dispatcher.UIThread.Post(() => window.ShowExpression(expressionId));
        return new CharacterIpcResponse(true, null);
    }
}
