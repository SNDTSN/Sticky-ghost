namespace App.Core.Infrastructure.Ipc;

/// <summary>
/// App.Mcp(Claude가 스폰하는 별도 프로세스)와 App.UI(화면에 캐릭터가 떠 있는 상시 실행 프로세스) 사이의
/// 로컬 명명 파이프 IPC 계약. 요청 1개 → 응답 1개 후 연결을 끊는 단발성 프로토콜.
/// </summary>
public static class CharacterIpcContract
{
    public const string PipeName = "StickyGhost.CharacterIpc";
}

/// <summary>Type: "say" | "setExpression" — 종류가 둘뿐이라 문자열로 단순화. 늘어나면 enum으로 바꾼다.</summary>
public sealed record CharacterIpcRequest(string Type, string? Text, string? ExpressionId);

public sealed record CharacterIpcResponse(bool IsSuccess, string? ErrorMessage);
