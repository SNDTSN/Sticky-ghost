using System.Text.Json;

namespace App.Core.Infrastructure.FileSystem;

/// <summary>창 하나의 마지막 위치/크기. 위치는 물리 픽셀, 크기는 DIP. 캐릭터 창처럼 크기를 스케일 설정이 정하는 창은 크기를 비워둔다.</summary>
public sealed record WindowPlacement(int X, int Y, double? Width = null, double? Height = null);

/// <summary>
/// 창 위치/크기 저장소. AppSettings와 파일을 분리한 이유: SettingsWindow가 자기가 들고 있던 AppSettings 전체를
/// 덮어써서 저장하므로, 위치를 같은 파일에 두면 설정창이 열려 있는 동안 창을 옮겨도 옛 위치로 되돌아간다.
/// 메모 창은 위치를 SQLite(MemoNote)에 저장하므로 여기서 다루지 않는다.
/// </summary>
public sealed class WindowStateStore
{
    private static readonly JsonSerializerOptions SerializerOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private Dictionary<string, WindowPlacement>? _cache;

    public WindowStateStore(string filePath)
    {
        _filePath = filePath;
    }

    public WindowPlacement? Load(string key) =>
        EnsureLoaded().TryGetValue(key, out var placement) ? placement : null;

    public void Save(string key, WindowPlacement placement)
    {
        var all = EnsureLoaded();
        all[key] = placement;

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(_filePath, JsonSerializer.Serialize(all, SerializerOptions));
    }

    // 파일은 최초 1회만 읽고 이후에는 메모리 사본을 갱신한다 — 창 이동 중 디바운스 저장이 잦아도 읽기 I/O가 반복되지 않게.
    private Dictionary<string, WindowPlacement> EnsureLoaded()
    {
        if (_cache is not null)
            return _cache;

        _cache = new Dictionary<string, WindowPlacement>();
        if (!File.Exists(_filePath))
            return _cache;

        try
        {
            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<Dictionary<string, WindowPlacement>>(json);
            if (loaded is not null)
                _cache = loaded;
        }
        catch (JsonException)
        {
            // 손상된 위치 파일 때문에 앱을 못 띄울 이유는 없다 — 기본 위치로 복구.
        }

        return _cache;
    }
}
