using System.IO.Pipes;
using System.Text.Json;

namespace App.Core.Infrastructure.Ipc;

/// <summary>
/// App.UI 프로세스에서 상시 리스닝하는 명명 파이프 서버. App.Mcp가 짧게 연결해서 요청 1개를 보내고
/// 응답 1개를 받은 뒤 끊는 방식이라, 연결을 하나 처리할 때마다 새 <see cref="NamedPipeServerStream"/>을 열고
/// 다시 대기하는 루프로 동작한다.
/// </summary>
public sealed class CharacterIpcServer
{
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
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                using var pipeServer = new NamedPipeServerStream(
                    CharacterIpcContract.PipeName,
                    PipeDirection.InOut,
                    maxNumberOfServerInstances: 1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipeServer.WaitForConnectionAsync(cancellationToken);
                await HandleOneConnectionAsync(pipeServer, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                // 연결 하나가 이상하게 끊기거나 파싱이 깨져도 루프 자체는 계속 살아있어야 한다 — 다음 연결을 계속 받음.
            }
        }
    }

    private async Task HandleOneConnectionAsync(NamedPipeServerStream pipeServer, CancellationToken cancellationToken)
    {
        // leaveOpen: true 필수 — CharacterIpcClient와 같은 이유(같은 스트림을 감싼 reader/writer가
        // 서로의 Dispose로 파이프를 먼저 닫아버리는 문제 방지).
        using var reader = new StreamReader(pipeServer, leaveOpen: true);
        using var writer = new StreamWriter(pipeServer, leaveOpen: true) { AutoFlush = true };

        var line = await reader.ReadLineAsync(cancellationToken);
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

        await writer.WriteLineAsync(JsonSerializer.Serialize(response));
    }
}
