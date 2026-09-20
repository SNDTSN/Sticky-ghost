using System.Buffers.Binary;

namespace App.Core.Infrastructure.FileSystem;

/// <summary>
/// PNG 파일의 앞 24바이트(시그니처 + IHDR 청크의 가로/세로)만 읽는다. 이미지를 디코딩하지 않으므로 팩을 스캔할 때마다 불러도 비용이 거의 없다.
/// 캐릭터 팩 이미지는 PNG만 허용하는데(설계: "PNG 레이어 + JSON 매니페스트"), 이 검사가 그 강제와 손상 파일의 조기 탐지를 겸한다.
/// </summary>
public static class PngHeader
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] IhdrType = "IHDR"u8.ToArray();
    private const int HeaderLength = 24;
    private const int IhdrDataLength = 13;

    /// <summary>PNG가 아니거나 헤더가 손상됐거나 읽을 수 없으면 false. 성공하면 가로/세로는 항상 1 이상이다.</summary>
    public static bool TryReadSize(string path, out int width, out int height)
    {
        width = 0;
        height = 0;

        try
        {
            using var stream = File.OpenRead(path);
            Span<byte> header = stackalloc byte[HeaderLength];
            if (stream.ReadAtLeast(header, HeaderLength, throwOnEndOfStream: false) < HeaderLength)
                return false;

            if (!header[..8].SequenceEqual(Signature))
                return false;

            // 첫 청크는 반드시 IHDR(데이터 13바이트)이다.
            if (BinaryPrimitives.ReadInt32BigEndian(header[8..12]) != IhdrDataLength || !header[12..16].SequenceEqual(IhdrType))
                return false;

            var w = BinaryPrimitives.ReadInt32BigEndian(header[16..20]);
            var h = BinaryPrimitives.ReadInt32BigEndian(header[20..24]);
            if (w <= 0 || h <= 0)
                return false;

            width = w;
            height = h;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}
