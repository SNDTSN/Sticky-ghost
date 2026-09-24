using System.Text.Json;
using App.Core.Diagnostics;

namespace App.Core.Infrastructure.FileSystem;

/// <summary>창 하나의 마지막 위치/크기. 위치는 물리 픽셀, 크기는 DIP. 캐릭터 창처럼 크기를 스케일 설정이 정하는 창은 크기를 비워둔다.</summary>
public sealed record WindowPlacement(int X, int Y, double? Width = null, double? Height = null);

/// <summary>
/// 창 위치/크기 저장소. AppSettings와 파일을 분리한 이유: SettingsWindow가 자기가 들고 있던 AppSettings 전체를
/// 덮어써서 저장하므로, 위치를 같은 파일에 두면 설정창이 열려 있는 동안 창을 옮겨도 옛 위치로 되돌아간다.
/// 메모 창은 위치를 SQLite(MemoNote)에 저장하므로 여기서 다루지 않는다 — 저장 로직은 WindowPlacementTracker 하나를 공유하고
/// 저장처만 어댑터(WindowStateSlot / MemoPlacementStore)로 나뉜다(KNOWN_ISSUES #20-1).
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

    /// <summary>
    /// 창 하나의 위치를 저장한다. **실패하면 예외를 던진다** — 이벤트 핸들러(이동 디바운스, Closing)에서 부르는
    /// WindowPlacementTracker가 (WindowStateSlot을 거쳐) 잡아서 로그만 남긴다. 다른 자리에서 부를 거면 그쪽도 반드시 잡아야 한다.
    /// </summary>
    public void Save(string key, WindowPlacement placement)
    {
        var all = EnsureLoaded();

        // 읽기에 실패한 상태라면 all은 "저장된 위치가 하나도 없는" 임시 딕셔너리다. 여기에 키 하나를 넣어
        // 파일에 쓰면 다른 창들의 저장 위치까지 통째로 지워진다 — 위치 하나를 잃는 쪽이 낫다.
        if (_cache is null)
        {
            AppLog.Write("window-state", $"위치 파일을 읽지 못한 상태라 저장을 건너뜀 (key={key})");
            return;
        }

        all[key] = placement;
        AtomicFile.WriteAllText(_filePath, JsonSerializer.Serialize(all, SerializerOptions));
    }

    // 파일은 최초 1회만 읽고 이후에는 메모리 사본을 갱신한다 — 창 이동 중 디바운스 저장이 잦아도 읽기 I/O가 반복되지 않게.
    // 캐시(_cache)는 "파일 내용을 실제로 안다"는 뜻이므로 읽기에 성공한 뒤에만 채운다. 읽지 못한 채로 캐시를 확정하면
    // 이후 호출이 전부 "저장된 위치 없음"을 반환하고, Save가 그 상태를 파일에 새겨 넣는다.
    private Dictionary<string, WindowPlacement> EnsureLoaded()
    {
        if (_cache is not null)
            return _cache;

        // 파일이 없는 것은 실패가 아니다(첫 실행) — 빈 상태를 안다고 확정해도 된다.
        if (!File.Exists(_filePath))
            return _cache = new Dictionary<string, WindowPlacement>();

        try
        {
            var json = File.ReadAllText(_filePath);
            return _cache = JsonSerializer.Deserialize<Dictionary<string, WindowPlacement>>(json)
                            ?? new Dictionary<string, WindowPlacement>();
        }
        catch (JsonException ex)
        {
            // 손상된 위치 파일 때문에 앱을 못 띄울 이유는 없다 — 기본 위치로 복구하고, 다음 저장이 파일을 정상화한다.
            AppLog.Write("window-state", ex);
            return _cache = new Dictionary<string, WindowPlacement>();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 일시적으로 못 읽었을 뿐 파일 내용은 멀쩡할 수 있다 — 캐시를 확정하지 않고 다음 호출에서 다시 읽는다.
            AppLog.Write("window-state", ex);
            return new Dictionary<string, WindowPlacement>();
        }
    }
}
