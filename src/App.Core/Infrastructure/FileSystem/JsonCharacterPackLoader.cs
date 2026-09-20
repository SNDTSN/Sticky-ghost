using System.Text.Json;
using System.Text.Json.Serialization;
using App.Core.Domain.Entities;
using App.Core.Domain.Repositories;

namespace App.Core.Infrastructure.FileSystem;

/// <summary>캐릭터팩 manifest.json을 읽어 검증하는 로더. 폴백 정책은 다루지 않는다 — <see cref="Domain.Services.CharacterPackService"/> 참고.</summary>
public sealed class JsonCharacterPackLoader : ICharacterPackLoader
{
    /// <summary>레이어 1픽셀이 상주시키는 메모리: 디코딩된 비트맵 4바이트(BGRA) + 입력 영역용 알파 마스크 1바이트.</summary>
    public const int MemoryBytesPerPixel = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public CharacterPackLoadResult Load(string packFolderPath)
    {
        var manifestPath = Path.Combine(packFolderPath, "manifest.json");
        if (!File.Exists(manifestPath))
            return CharacterPackLoadResult.Fail(["manifest.json 없음"]);

        ManifestDto? manifest;
        try
        {
            var json = File.ReadAllText(manifestPath);
            manifest = JsonSerializer.Deserialize<ManifestDto>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return CharacterPackLoadResult.Fail([$"JSON 파싱 실패: {ex.Message}"]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 파일이 다른 프로그램에 잡혀 있거나 권한이 없는 경우 — 예외로 터뜨리지 않고 검증 오류로 돌려준다.
            return CharacterPackLoadResult.Fail([$"manifest.json을 읽지 못함: {ex.Message}"]);
        }

        if (manifest is null)
            return CharacterPackLoadResult.Fail(["JSON 파싱 실패: manifest.json이 비어 있음"]);

        var errors = new List<string>();

        if (manifest.SchemaVersion != 1)
            errors.Add($"지원하지 않는 schemaVersion: {manifest.SchemaVersion}");

        if (string.IsNullOrWhiteSpace(manifest.Id))
            errors.Add("id가 비어 있음");
        if (string.IsNullOrWhiteSpace(manifest.Name))
            errors.Add("name이 비어 있음");
        if (string.IsNullOrWhiteSpace(manifest.Personality?.SystemPrompt))
            errors.Add("personality.systemPrompt가 비어 있음");

        var appearance = manifest.Appearance;
        if (appearance is null)
        {
            errors.Add("appearance가 비어 있음");
            return CharacterPackLoadResult.Fail(errors);
        }

        var packRoot = Path.GetFullPath(packFolderPath);
        long estimatedMemoryBytes = 0;

        if (string.IsNullOrWhiteSpace(appearance.BaseImage))
            errors.Add("appearance.baseImage가 비어 있음");
        else
            ValidateImage(packRoot, appearance.BaseImage, errors, ref estimatedMemoryBytes);

        // eyeClosedImage는 optional — 필드 자체가 없으면(null) 정상, 지정됐는데 빈 문자열이면 에러.
        if (appearance.EyeClosedImage is not null)
        {
            if (string.IsNullOrWhiteSpace(appearance.EyeClosedImage))
                errors.Add("appearance.eyeClosedImage가 비어 있음");
            else
                ValidateImage(packRoot, appearance.EyeClosedImage, errors, ref estimatedMemoryBytes);
        }

        if (appearance.EyeClosedImage is not null)
        {
            // blink 객체 자체가 통째로 빠져 있어도 필드가 전부 0인 것과 동일하게 취급해 아래 검사에서 걸러지게 함.
            var blink = appearance.Blink ?? new BlinkDto();
            if (blink.MinIntervalMs <= 0)
                errors.Add("blink.minIntervalMs는 0보다 커야 함");
            if (blink.MaxIntervalMs < blink.MinIntervalMs)
                errors.Add("blink.maxIntervalMs는 minIntervalMs 이상이어야 함");
            if (blink.DurationMs <= 0)
                errors.Add("blink.durationMs는 0보다 커야 함");
        }

        var expressionIds = new HashSet<string>();
        foreach (var expr in appearance.Expressions ?? [])
        {
            if (string.IsNullOrWhiteSpace(expr.Id))
                errors.Add("expressions[].id가 비어 있음");
            else if (!expressionIds.Add(expr.Id))
                errors.Add($"중복된 expression id: {expr.Id}");

            if (string.IsNullOrWhiteSpace(expr.Image))
                errors.Add($"expressions[{expr.Id}].image가 비어 있음");
            else
                ValidateImage(packRoot, expr.Image, errors, ref estimatedMemoryBytes);
        }

        var touchRegionIds = new HashSet<string>();
        foreach (var region in appearance.TouchRegions ?? [])
        {
            if (string.IsNullOrWhiteSpace(region.Id))
                errors.Add("touchRegions[].id가 비어 있음");
            else if (!touchRegionIds.Add(region.Id))
                errors.Add($"중복된 touchRegion id: {region.Id}");

            var rect = region.Rect;
            if (rect is null)
            {
                errors.Add($"touchRegions[{region.Id}].rect가 비어 있음");
            }
            else
            {
                if (rect.Width <= 0)
                    errors.Add($"touchRegions[{region.Id}].rect.width는 0보다 커야 함");
                if (rect.Height <= 0)
                    errors.Add($"touchRegions[{region.Id}].rect.height는 0보다 커야 함");
                if (rect.X < 0)
                    errors.Add($"touchRegions[{region.Id}].rect.x는 0 이상이어야 함");
                if (rect.Y < 0)
                    errors.Add($"touchRegions[{region.Id}].rect.y는 0 이상이어야 함");
            }
        }

        if (errors.Count > 0)
            return CharacterPackLoadResult.Fail(errors);

        var pack = new CharacterPack
        {
            Id = manifest.Id!,
            Name = manifest.Name!,
            Author = manifest.Author,
            Appearance = new Appearance
            {
                BaseImage = Path.GetFullPath(Path.Combine(packRoot, appearance.BaseImage!)),
                EyeClosedImage = appearance.EyeClosedImage is null
                    ? null
                    : Path.GetFullPath(Path.Combine(packRoot, appearance.EyeClosedImage)),
                EyeClosedOffset = new Offset
                {
                    X = appearance.EyeClosedOffset?.X ?? 0,
                    Y = appearance.EyeClosedOffset?.Y ?? 0,
                },
                Blink = appearance.Blink is null
                    ? null
                    : new BlinkSettings
                    {
                        MinIntervalMs = appearance.Blink.MinIntervalMs,
                        MaxIntervalMs = appearance.Blink.MaxIntervalMs,
                        DurationMs = appearance.Blink.DurationMs,
                    },
                Expressions = (appearance.Expressions ?? []).Select(e => new Expression
                {
                    Id = e.Id!,
                    Image = Path.GetFullPath(Path.Combine(packRoot, e.Image!)),
                    Offset = new Offset { X = e.Offset?.X ?? 0, Y = e.Offset?.Y ?? 0 },
                }).ToList(),
                TouchRegions = (appearance.TouchRegions ?? []).Select(r => new TouchRegion
                {
                    Id = r.Id!,
                    Rect = new Rect
                    {
                        X = r.Rect!.X,
                        Y = r.Rect.Y,
                        Width = r.Rect.Width,
                        Height = r.Rect.Height,
                    },
                }).ToList(),
            },
            Personality = new Personality { SystemPrompt = manifest.Personality!.SystemPrompt! },
            EstimatedMemoryBytes = estimatedMemoryBytes,
        };

        return CharacterPackLoadResult.Success(pack);
    }

    /// <summary>
    /// 이미지 필드 하나를 검증한다: 경로 해석(<see cref="TryResolveInsidePack"/>) → 존재 확인 → PNG 헤더를 읽어
    /// PNG 전용을 강제하고 예상 메모리를 누적한다. 크기 상한은 두지 않는다 — 큰 팩은 설정창에서
    /// "용량 최적화 필요"로 알린다(docs/character-widget-design.md).
    /// </summary>
    private static void ValidateImage(string packRoot, string imageField, List<string> errors, ref long estimatedMemoryBytes)
    {
        if (!TryResolveInsidePack(packRoot, imageField, errors, out var resolvedPath))
            return;

        if (!File.Exists(resolvedPath))
        {
            errors.Add($"파일 없음: {imageField}");
            return;
        }

        if (!PngHeader.TryReadSize(resolvedPath, out var width, out var height))
        {
            errors.Add($"PNG 형식이 아니거나 헤더가 손상됨: {imageField}");
            return;
        }

        estimatedMemoryBytes += (long)width * height * MemoryBytesPerPixel;
    }

    /// <summary>
    /// 매니페스트에 적힌 이미지 경로를 실제 경로로 바꾸고, 팩 폴더를 벗어나는 경로(절대경로, "..\" 등)를 차단한다.
    /// 경로로 쓸 수 없는 값(널 문자가 섞였거나 지나치게 긴 경로 등)은 예외로 터뜨리지 않고 검증 오류로 돌려준다 —
    /// 이용자가 직접 쓰는 매니페스트에는 무엇이든 들어올 수 있고, 여기서 예외가 새면 스캔 전체가 멈춘다(KNOWN_ISSUES #16).
    /// </summary>
    private static bool TryResolveInsidePack(string packRoot, string imageField, List<string> errors, out string resolvedPath)
    {
        try
        {
            resolvedPath = Path.GetFullPath(Path.Combine(packRoot, imageField));
        }
        catch (Exception ex) when (ex is ArgumentException or PathTooLongException or NotSupportedException)
        {
            resolvedPath = string.Empty;
            // 지나치게 긴 값이 그대로 로그로 가지 않도록 잘라서 보여준다(사유 문자열은 AppLog에 실린다).
            var shown = imageField.Length <= 80 ? imageField : imageField[..80] + "…";
            errors.Add($"경로로 쓸 수 없는 값: {shown} ({ex.GetType().Name})");
            return false;
        }

        var rootWithSeparator = packRoot.EndsWith(Path.DirectorySeparatorChar)
            ? packRoot
            : packRoot + Path.DirectorySeparatorChar;

        if (!resolvedPath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add($"팩 폴더를 벗어남: {imageField}");
            return false;
        }

        return true;
    }

    private sealed class ManifestDto
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        [JsonPropertyName("author")]
        public string? Author { get; set; }
        [JsonPropertyName("appearance")]
        public AppearanceDto? Appearance { get; set; }
        [JsonPropertyName("personality")]
        public PersonalityDto? Personality { get; set; }
    }

    private sealed class AppearanceDto
    {
        [JsonPropertyName("baseImage")]
        public string? BaseImage { get; set; }
        [JsonPropertyName("eyeClosedImage")]
        public string? EyeClosedImage { get; set; }
        [JsonPropertyName("eyeClosedOffset")]
        public OffsetDto? EyeClosedOffset { get; set; }
        [JsonPropertyName("blink")]
        public BlinkDto? Blink { get; set; }
        [JsonPropertyName("expressions")]
        public List<ExpressionDto>? Expressions { get; set; }
        [JsonPropertyName("touchRegions")]
        public List<TouchRegionDto>? TouchRegions { get; set; }
    }

    private sealed class BlinkDto
    {
        [JsonPropertyName("minIntervalMs")]
        public int MinIntervalMs { get; set; }
        [JsonPropertyName("maxIntervalMs")]
        public int MaxIntervalMs { get; set; }
        [JsonPropertyName("durationMs")]
        public int DurationMs { get; set; }
    }

    private sealed class ExpressionDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("image")]
        public string? Image { get; set; }
        [JsonPropertyName("offset")]
        public OffsetDto? Offset { get; set; }
    }

    private sealed class OffsetDto
    {
        [JsonPropertyName("x")]
        public int X { get; set; }
        [JsonPropertyName("y")]
        public int Y { get; set; }
    }

    private sealed class TouchRegionDto
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        [JsonPropertyName("rect")]
        public RectDto? Rect { get; set; }
    }

    private sealed class RectDto
    {
        [JsonPropertyName("x")]
        public int X { get; set; }
        [JsonPropertyName("y")]
        public int Y { get; set; }
        [JsonPropertyName("width")]
        public int Width { get; set; }
        [JsonPropertyName("height")]
        public int Height { get; set; }
    }

    private sealed class PersonalityDto
    {
        [JsonPropertyName("systemPrompt")]
        public string? SystemPrompt { get; set; }
    }
}
