using System.Text;

namespace App.Core.Infrastructure.Ipc;

/// <summary>
/// 길이 상한이 있는 한 줄 읽기. <see cref="StreamReader.ReadLineAsync(CancellationToken)"/>는 개행이 올 때까지 버퍼를 끝없이 키우므로
/// 파이프 서버·클라이언트가 이것을 대신 쓴다(KNOWN_ISSUES #14).
/// 한 연결에 한 줄만 오가는 프로토콜이라, 개행이 나오면 거기까지만 쓰고 나머지는 버린다.
/// </summary>
internal static class IpcLineReader
{
    private const int ChunkChars = 1024;

    /// <returns>줄(끝의 \r 제거). 아무것도 받지 못하고 연결이 끝나면 null — 기존 ReadLineAsync와 같은 동작.</returns>
    /// <exception cref="IpcLineTooLongException">개행 전까지 <paramref name="maxChars"/>를 넘었을 때.</exception>
    public static async Task<string?> ReadLineAsync(StreamReader reader, int maxChars, CancellationToken cancellationToken)
    {
        var sb = new StringBuilder();
        var buffer = new char[ChunkChars];

        while (true)
        {
            var read = await reader.ReadAsync(buffer.AsMemory(), cancellationToken);
            if (read == 0)
                return sb.Length == 0 ? null : sb.ToString();

            var newline = Array.IndexOf(buffer, '\n', 0, read);
            var take = newline >= 0 ? newline : read;
            if (sb.Length + take > maxChars)
                throw new IpcLineTooLongException();

            sb.Append(buffer, 0, take);
            if (newline >= 0)
                return sb.ToString().TrimEnd('\r');
        }
    }
}

internal sealed class IpcLineTooLongException : Exception
{
}

/// <summary>
/// 직렬화된 JSON 한 줄을 파이프에 쓴다. StreamWriter를 쓰지 않는 이유: 쓰기가 타임아웃으로 취소되면 남은 버퍼를
/// StreamWriter.Dispose가 동기로 flush하려다가, 응답을 읽지 않는 상대 앞에서 다시 멈출 수 있다(KNOWN_ISSUES #14).
/// 직렬화는 호출부가 한다 — 클라이언트가 보내기 전에 길이를 검사해야 해서.
/// </summary>
internal static class IpcLineWriter
{
    public static async Task WriteLineAsync(Stream stream, string json, CancellationToken cancellationToken)
    {
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await stream.WriteAsync(bytes, cancellationToken);
        await stream.FlushAsync(cancellationToken);
    }
}
