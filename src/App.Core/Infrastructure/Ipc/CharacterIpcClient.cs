using System.IO.Pipes;
using System.Text.Json;

namespace App.Core.Infrastructure.Ipc;

/// <summary>
/// App.Mcp 프로세스가 App.UI에 떠 있는 캐릭터에게 명령을 보낼 때 쓰는 클라이언트. 툴이 호출될 때마다 새로
/// 연결해서 요청 1개 → 응답 1개를 주고받고 끊는다.
/// </summary>
public sealed class CharacterIpcClient
{
    private const int ConnectTimeoutMs = 3000;

    public async Task<CharacterIpcResponse> SendAsync(CharacterIpcRequest request, CancellationToken cancellationToken)
    {
        using var pipeClient = new NamedPipeClientStream(
            ".", CharacterIpcContract.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous);

        try
        {
            await pipeClient.ConnectAsync(ConnectTimeoutMs, cancellationToken);
        }
        catch (TimeoutException)
        {
            return new CharacterIpcResponse(false, "캐릭터 위젯이 실행 중이지 않습니다.");
        }

        // leaveOpen: true 필수 — 안 그러면 reader/writer 둘 다 같은 pipeClient를 감싸고 있어서, 먼저
        // Dispose되는 쪽이 파이프를 닫아버리고 나머지 하나가 닫힌 파이프에 Flush를 시도하다 ObjectDisposedException.
        using var writer = new StreamWriter(pipeClient, leaveOpen: true) { AutoFlush = true };
        using var reader = new StreamReader(pipeClient, leaveOpen: true);

        await writer.WriteLineAsync(JsonSerializer.Serialize(request));

        var line = await reader.ReadLineAsync(cancellationToken);
        if (line is null)
            return new CharacterIpcResponse(false, "응답 없음");

        return JsonSerializer.Deserialize<CharacterIpcResponse>(line)
            ?? new CharacterIpcResponse(false, "응답 파싱 실패");
    }
}
