namespace App.Core.Domain.Entities;

public sealed class CharacterPack
{
    public required string Id { get; set; }
    public required string Name { get; set; }
    public string? Author { get; set; }
    public required Appearance Appearance { get; set; }
    public required Personality Personality { get; set; }

    /// <summary>
    /// 이 팩이 상주시킬 메모리의 추정치(바이트) — 모든 이미지 레이어의 가로*세로*5(디코딩된 비트맵 4 + 알파 마스크 1).
    /// 로더가 PNG 헤더만 읽어 계산한다(디코딩 없음). 상한 검사에는 쓰지 않고, 설정창 목록의 "용량 최적화 필요" 표시에만 쓴다.
    /// </summary>
    public long EstimatedMemoryBytes { get; set; }
}

public sealed class Appearance
{
    public required string BaseImage { get; set; }
    public string? EyeClosedImage { get; set; }
    public Offset EyeClosedOffset { get; set; } = new();
    public BlinkSettings? Blink { get; set; }
    public List<Expression> Expressions { get; set; } = new();
    public List<TouchRegion> TouchRegions { get; set; } = new();
}

public sealed class BlinkSettings
{
    public int MinIntervalMs { get; set; }
    public int MaxIntervalMs { get; set; }
    public int DurationMs { get; set; }
}

public sealed class Expression
{
    public required string Id { get; set; }
    public required string Image { get; set; }
    public Offset Offset { get; set; } = new();
}

public sealed class TouchRegion
{
    public required string Id { get; set; }
    public required Rect Rect { get; set; }
}

public sealed class Offset
{
    public int X { get; set; }
    public int Y { get; set; }
}

public sealed class Rect
{
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }

    public bool Contains(double x, double y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}

public sealed class Personality
{
    public required string SystemPrompt { get; set; }
}
