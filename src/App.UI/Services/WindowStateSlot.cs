using App.Core.Infrastructure.FileSystem;

namespace App.UI.Services;

/// <summary>WindowStateStore(window-state.json)의 키 하나를 창 하나의 저장처로 쓴다.</summary>
public sealed class WindowStateSlot : IWindowPlacementStore
{
    private readonly WindowStateStore _store;
    private readonly string _key;

    public WindowStateSlot(WindowStateStore store, string key)
    {
        _store = store;
        _key = key;
    }

    public WindowPlacement? Load() => _store.Load(_key);

    public void Save(WindowPlacement placement) => _store.Save(_key, placement);
}
