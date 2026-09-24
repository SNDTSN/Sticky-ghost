using System.IO.Pipes;
using System.Text.Json;

namespace App.Core.Infrastructure.Ipc;

/// <summary>
/// App.Mcp 프로세스가 App.UI에 떠 있는 캐릭터에게 명령을 보낼 때 쓰는 클라이언트. 툴이 호출될 때마다 새로
/// 연결해서 요청 1개 → 응답 1개를 주고받고 끊는다.
/// </summary>
public sealed class CharacterIpcClient
{
    // 서버의 연결 타임아웃(CharacterIpcServer.ConnectionTimeoutMs, 2초)보다 길어야 한다 — 그쪽 주석 참고.
    private const int ConnectTimeoutMs = 3000;
    private const int ResponseTimeoutMs = 5000;

    public async Task<CharacterIpcResponse> SendAsync(CharacterIpcRequest request, CancellationToken cancellationToken)
    {
        // 보내기 전에 길이를 검사한다. 서버도 상한을 넘으면 거절 응답을 보내지만, 크게 넘치면 서버가 읽기를 멈춘 사이 이쪽이
        // 쓰기에서 막혀 응답을 못 읽고 "응답하지 않습니다"로 끝난다 — 거절 이유가 전달되지 않는다(KNOWN_ISSUES #14).
        var requestJson = JsonSerializer.Serialize(request);
        if (requestJson.Length > CharacterIpcContract.MaxLineChars)
            return new CharacterIpcResponse(false, "요청이 너무 깁니다(대사를 줄여 주세요).");

        // CurrentUserOnly: 연결한 파이프의 주인이 현재 사용자인지 확인한다 — 다른 사용자가 같은 이름을 먼저 잡아둔 파이프에 요청을 보내지 않게(KNOWN_ISSUES #22).
        using var pipeClient = new NamedPipeClientStream(
            ".", CharacterIpcContract.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

        try
        {
            await pipeClient.ConnectAsync(ConnectTimeoutMs, cancellationToken);
        }
        catch (TimeoutException)
        {
            return new CharacterIpcResponse(false, "캐릭터 위젯이 실행 중이지 않습니다.");
        }
        catch (UnauthorizedAccessException)
        {
            // 파이프 주인이 현재 사용자가 아님 — 다른 사용자의 파이프이거나, 앱만 관리자 권한으로 실행되어 주인이 Administrators 그룹인 경우.
            return new CharacterIpcResponse(false, "캐릭터 위젯 파이프의 주인이 현재 사용자가 아닙니다(앱을 관리자 권한으로 실행했는지 확인).");
        }

        // leaveOpen: true 필수 — reader의 Dispose가 파이프를 먼저 닫아버리지 않게.
        using var reader = new StreamReader(pipeClient, leaveOpen: true);

        // 연결된 뒤 응답을 받기까지의 시간 제한. SingleInstanceGuard는 이 결과를 동기로 기다리므로, 없으면 서버가 멈췄을 때
        // 두 번째 인스턴스도 영원히 멈춘다(KNOWN_ISSUES #14).
        using var responseCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        responseCts.CancelAfter(ResponseTimeoutMs);

        string? line;
        try
        {
            await IpcLineWriter.WriteLineAsync(pipeClient, requestJson, responseCts.Token);
            line = await IpcLineReader.ReadLineAsync(reader, CharacterIpcContract.MaxLineChars, responseCts.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new CharacterIpcResponse(false, "캐릭터 위젯이 응답하지 않습니다.");
        }
        catch (IpcLineTooLongException)
        {
            return new CharacterIpcResponse(false, "응답이 너무 깁니다.");
        }

        if (line is null)
            return new CharacterIpcResponse(false, "응답 없음");

        return JsonSerializer.Deserialize<CharacterIpcResponse>(line)
            ?? new CharacterIpcResponse(false, "응답 파싱 실패");
    }
}
