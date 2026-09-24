using System.ComponentModel;
using App.Core.Infrastructure.Ipc;
using ModelContextProtocol.Server;

namespace App.Mcp.Tools;

/// <summary>
/// Claude가 직접 호출하는 캐릭터 반응 툴. 여기서 하는 일은 명명 파이프로 App.UI에 요청을 전달하는 것뿐이고,
/// 실제로 "표정을 바꾼다"가 뭘 의미하는지는 App.UI 쪽 <see cref="ICharacterIpcRequestHandler"/> 구현이 안다.
/// </summary>
public sealed class CharacterTools
{
    [McpServerTool, Description("캐릭터가 짧은 대사 한 줄을 말하게 한다.")]
    public static async Task<string> Say(
        CharacterIpcClient ipcClient,
        [Description("캐릭터가 말할 대사 한 줄")] string text)
    {
        var response = await ipcClient.SendAsync(new CharacterIpcRequest(CharacterIpcRequest.TypeSay, text, null), CancellationToken.None);
        return response.IsSuccess ? "OK" : $"실패: {response.ErrorMessage}";
    }

    [McpServerTool, Description("캐릭터의 표정을 바꾼다.")]
    public static async Task<string> SetExpression(
        CharacterIpcClient ipcClient,
        [Description("표정 id — 현재 로드된 캐릭터팩의 expressions 중 하나")] string expressionId)
    {
        var response = await ipcClient.SendAsync(new CharacterIpcRequest(CharacterIpcRequest.TypeSetExpression, null, expressionId), CancellationToken.None);
        return response.IsSuccess ? "OK" : $"실패: {response.ErrorMessage}";
    }
}
