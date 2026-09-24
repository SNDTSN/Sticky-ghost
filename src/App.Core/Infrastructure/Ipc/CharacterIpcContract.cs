using System.Diagnostics;

namespace App.Core.Infrastructure.Ipc;

/// <summary>
/// App.Mcp(Claude가 스폰하는 별도 프로세스)와 App.UI(화면에 캐릭터가 떠 있는 상시 실행 프로세스) 사이의
/// 로컬 명명 파이프 IPC 계약. 요청 1개 → 응답 1개 후 연결을 끊는 단발성 프로토콜.
/// </summary>
public static class CharacterIpcContract
{
    private const string PipeBaseName = "StickyGhost.CharacterIpc";

    /// <summary>
    /// 서버(App.UI)와 클라이언트(App.Mcp, 이중 실행된 두 번째 인스턴스)가 반드시 같은 규칙을 써야 하므로 이름은 여기서만 만든다.
    /// </summary>
    public static string PipeName { get; } = BuildPipeName();

    private static string BuildPipeName()
    {
        if (OperatingSystem.IsWindows())
        {
            // 명명 파이프 이름공간은 머신 전역이다. 단일 인스턴스 뮤텍스(Local\)와 같은 범위로 맞추려고 로그인 세션 id를 붙인다
            // (KNOWN_ISSUES #22 — 안 붙이면 두 사용자가 동시에 로그인했을 때 파이프를 두고 다툰다).
            using var process = Process.GetCurrentProcess();
            return $"{PipeBaseName}.{process.SessionId}";
        }

        // Unix 계열: .NET이 파이프를 사용자별 TMPDIR 아래 소켓 파일로 만들어 이미 사용자 단위로 분리된다.
        // Process.SessionId는 여기서 POSIX 세션(getsid)이라 앱과 App.Mcp의 값이 다를 수 있으므로 쓰지 않는다(Mac 미확인, docs/PORTING.md).
        return PipeBaseName;
    }
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
