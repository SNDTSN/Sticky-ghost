using System.IO.Pipes;
using System.Text.Json;
using App.Core.Diagnostics;

namespace App.Core.Infrastructure.Ipc;

/// <summary>
/// App.UI 프로세스에서 상시 리스닝하는 명명 파이프 서버. App.Mcp가 짧게 연결해서 요청 1개를 보내고
/// 응답 1개를 받은 뒤 끊는 방식이라, 연결을 하나 처리할 때마다 새 <see cref="NamedPipeServerStream"/>을 열고
/// 다시 대기하는 루프로 동작한다.
/// </summary>
public sealed class CharacterIpcServer
{
    private const int InitialFailDelayMs = 500;
    private const int MaxFailDelayMs = 10_000;
    // 클라이언트의 연결 대기(CharacterIpcClient.ConnectTimeoutMs, 3초)보다 짧아야 한다. 연결을 하나씩 처리하므로
    // 멈춘 연결을 붙잡고 있는 동안 들어온 요청이 연결 대기를 다 쓰고 "실행 중이지 않습니다"로 끝나지 않게(KNOWN_ISSUES #14).
    // 정상 요청은 수 ms면 끝난다 — 클라이언트는 연결 전에 직렬화를 마치고, 핸들러는 창 조작을 Post로 넘기고 바로 응답한다.
    private const int ConnectionTimeoutMs = 2000;

    private readonly ICharacterIpcRequestHandler _handler;
    private CancellationTokenSource? _cts;
    private Task? _acceptLoopTask;

    public CharacterIpcServer(ICharacterIpcRequestHandler handler)
    {
        _handler = handler;
    }

    public void Start()
    {
        _cts = new CancellationTokenSource();
        _acceptLoopTask = RunAcceptLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
    }

    private async Task RunAcceptLoopAsync(CancellationToken cancellationToken)
    {
        // Start()를 부른 UI 스레드에서 첫 await 전까지 동기로 도는 것을 막는다. 파이프 생성이 계속 실패하는 경우
        // (예: 다른 인스턴스가 이미 파이프를 잡고 있음) 이전 구현은 catch에서 곧바로 재시도해 UI 스레드가 무한 루프에 빠졌다.
        await Task.Yield();

        var failDelayMs = InitialFailDelayMs;
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipeServer = new NamedPipeServerStream(
                    CharacterIpcContract.PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    // CurrentUserOnly: 파이프 접근을 현재 사용자로 제한한다. 이름의 세션 id는 예측 가능해서 이름만으로는 다른 사용자를 막지 못한다(KNOWN_ISSUES #22).
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);

                // 연결 대기 자체에는 시간 제한이 없다(평소 상태). 제한은 연결된 뒤부터 응답을 다 쓸 때까지만 건다 —
                // 연결을 하나씩 순서대로 처리하므로 응답 없는 연결 하나가 뒤의 요청을 전부 막지 않게(KNOWN_ISSUES #14).
                await pipeServer.WaitForConnectionAsync(cancellationToken);

                using var connectionCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                connectionCts.CancelAfter(ConnectionTimeoutMs);
                try
                {
                    await HandleOneConnectionAsync(pipeServer, connectionCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    // 연결 시간 초과. 아래의 OperationCanceledException(앱 종료)과 구분하지 않으면 서버 루프 전체가 끝나 버린다.
                    AppLog.Write("ipc", $"연결이 {ConnectionTimeoutMs}ms 안에 끝나지 않아 끊음");
                }

                failDelayMs = InitialFailDelayMs;
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                // 연결 하나가 이상하게 끊기거나 파싱이 깨져도 루프 자체는 계속 살아있어야 한다 — 다음 연결을 계속 받음.
                // 단, 같은 원인으로 계속 실패해도 CPU를 태우지 않도록 지수 백오프로 쉰다.
                AppLog.Write("ipc", ex);
                try
                {
                    await Task.Delay(failDelayMs, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                failDelayMs = Math.Min(failDelayMs * 2, MaxFailDelayMs);
            }
        }
    }

    private async Task HandleOneConnectionAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
    {
        // leaveOpen: true 필수 — reader의 Dispose가 파이프를 먼저 닫아버리지 않게.
        using var reader = new StreamReader(pipeServer, leaveOpen: true);

        string? line;
        try
        {
            line = await IpcLineReader.ReadLineAsync(reader, CharacterIpcContract.MaxLineChars, cancellationToken);
        }
        catch (IpcLineTooLongException)
        {
            // 정상 클라이언트라면 이유를 알 수 있게 거절 응답을 보내고 끊는다.
            AppLog.Write("ipc", $"요청 줄이 {CharacterIpcContract.MaxLineChars}자를 넘어 거절");
            await IpcLineWriter.WriteLineAsync(
                pipeServer, JsonSerializer.Serialize(new CharacterIpcResponse(false, "요청이 너무 깁니다.")), cancellationToken);
            return;
        }

        if (line is null)
            return;

        CharacterIpcResponse response;
        try
        {
            var request = JsonSerializer.Deserialize<CharacterIpcRequest>(line)
                ?? new CharacterIpcRequest("", null, null);
            response = _handler.Handle(request);
        }
        catch (JsonException)
        {
            response = new CharacterIpcResponse(false, "요청 파싱 실패");
        }

        await IpcLineWriter.WriteLineAsync(pipeServer, JsonSerializer.Serialize(response), cancellationToken);
    }
}
