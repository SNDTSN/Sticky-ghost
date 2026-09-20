using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using App.Platform;
using Avalonia;
using Avalonia.Media.Imaging;
using Avalonia.Platform;

namespace App.UI.Services;

/// <summary>레이어 PNG 하나의 알파 채널. 팩을 적용할 때 한 번 추출해 두고 입력 영역을 다시 계산할 때마다 재사용한다.</summary>
public sealed class LayerAlphaMask
{
    public int Width { get; }
    public int Height { get; }
    private readonly byte[] _alpha;

    private LayerAlphaMask(int width, int height, byte[] alpha)
    {
        Width = width;
        Height = height;
        _alpha = alpha;
    }

    public byte AlphaAt(int x, int y) => _alpha[y * Width + x];

    /// <summary>이미 디코딩된 비트맵에서 알파만 복사한다(이미지를 다시 디코딩하지 않음).</summary>
    public static LayerAlphaMask FromBitmap(Bitmap bitmap)
    {
        var size = bitmap.PixelSize;
        // 원본 포맷과 무관하게 알파가 4번째 바이트인 Bgra8888로 받아서 읽는다.
        using var target = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        using var framebuffer = target.Lock();
        bitmap.CopyPixels(framebuffer, AlphaFormat.Unpremul);

        var alpha = new byte[size.Width * size.Height];
        var row = new byte[size.Width * 4];
        for (var y = 0; y < size.Height; y++)
        {
            Marshal.Copy(framebuffer.Address + y * framebuffer.RowBytes, row, 0, row.Length);
            for (var x = 0; x < size.Width; x++)
                alpha[y * size.Width + x] = row[x * 4 + 3];
        }

        return new LayerAlphaMask(size.Width, size.Height, alpha);
    }
}

/// <summary>
/// 레이어들의 불투명 부분의 합집합을 창의 입력 영역(겹치지 않는 사각형 목록)으로 만든다.
/// 결과는 창 픽셀 해상도(원본 좌표 × factor)에서 직접 표본 추출해서 만들기 때문에, 배율을 곱한 뒤 올림/내림으로
/// 사각형 경계가 서로 겹치는 문제가 생기지 않는다.
/// </summary>
public static class InputShapeBuilder
{
    // 안티에일리어싱 가장자리의 옅은 픽셀까지 입력을 받으면 사실상 투명한 부분이 클릭을 막으므로, 어느 정도 보이는 픽셀부터만 포함한다.
    private const byte AlphaThreshold = 16;

    /// <param name="dstWidth">창의 물리 픽셀 폭</param>
    /// <param name="dstHeight">창의 물리 픽셀 높이</param>
    /// <param name="factor">원본 100% 좌표 → 창 물리 픽셀 배율(표시 배율 × DPI)</param>
    /// <param name="layers">레이어 알파 마스크와 원본 좌표계의 배치 오프셋</param>
    public static IReadOnlyList<MaskRect> Build(
        int dstWidth, int dstHeight, double factor, IEnumerable<(LayerAlphaMask Mask, int OffsetX, int OffsetY)> layers)
    {
        var grid = new bool[dstWidth * dstHeight];

        foreach (var (mask, offsetX, offsetY) in layers)
        {
            for (var dy = 0; dy < dstHeight; dy++)
            {
                var sy = (int)(dy / factor) - offsetY;
                if (sy < 0 || sy >= mask.Height)
                    continue;

                for (var dx = 0; dx < dstWidth; dx++)
                {
                    var sx = (int)(dx / factor) - offsetX;
                    if (sx >= 0 && sx < mask.Width && mask.AlphaAt(sx, sy) >= AlphaThreshold)
                        grid[dy * dstWidth + dx] = true;
                }
            }
        }

        return ToRects(grid, dstWidth, dstHeight);
    }

    // 행마다 연속 구간(run)을 뽑고, 같은 x구간이 바로 아래 행에서도 이어지면 세로로 합쳐 사각형 수를 줄인다.
    private static List<MaskRect> ToRects(bool[] grid, int width, int height)
    {
        var rects = new List<MaskRect>();
        var open = new Dictionary<(int X0, int X1), int>();
        var current = new HashSet<(int X0, int X1)>();

        for (var y = 0; y <= height; y++)
        {
            current.Clear();
            if (y < height)
            {
                var x = 0;
                while (x < width)
                {
                    if (!grid[y * width + x])
                    {
                        x++;
                        continue;
                    }

                    var start = x;
                    while (x < width && grid[y * width + x])
                        x++;
                    current.Add((start, x));
                }
            }

            // 이전 행까지 이어지던 구간 중 이번 행에 없는 것은 여기서 닫는다.
            List<(int X0, int X1)>? closed = null;
            foreach (var (run, startY) in open)
            {
                if (current.Contains(run))
                    continue;

                rects.Add(new MaskRect(run.X0, startY, run.X1 - run.X0, y - startY));
                (closed ??= new()).Add(run);
            }

            if (closed is not null)
            {
                foreach (var run in closed)
                    open.Remove(run);
            }

            foreach (var run in current)
                open.TryAdd(run, y);
        }

        return rects;
    }
}
