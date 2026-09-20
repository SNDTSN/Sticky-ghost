namespace App.Core.Infrastructure.Ipc;

/// <summary>
/// App.Mcp(Claude가 스폰하는 별도 프로세스)와 App.UI(화면에 캐릭터가 떠 있는 상시 실행 프로세스) 사이의
/// 로컬 명명 파이프 IPC 계약. 요청 1개 → 응답 1개 후 연결을 끊는 단발성 프로토콜.
/// </summary>
public static class CharacterIpcContract
{
    public const string PipeName = "StickyGhost.CharacterIpc";
}

/// <summary>
/// Type: "say" | "setExpression" | "activate" — 종류가 적어 문자열로 단순화. 늘어나면 enum으로 바꾼다.
/// "activate"는 캐릭터 조작이 아니라 이중 실행된 두 번째 인스턴스가 기존 인스턴스의 메인 창을 앞으로 가져오게
/// 요청하는 용도다(파이프 이름은 역사적 이유로 CharacterIpc 그대로). App.Mcp는 이 타입을 툴로 노출하지 않는다 —
/// 툴 표면 최소화 원칙(character-widget-design.md "프롬프트 인젝션") 유지.
/// </summary>
public sealed record CharacterIpcRequest(string Type, string? Text, string? ExpressionId)
{
    public const string TypeActivate = "activate";
}

public sealed record CharacterIpcResponse(bool IsSuccess, string? ErrorMessage);
