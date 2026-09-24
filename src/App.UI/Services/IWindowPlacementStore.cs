using App.Core.Infrastructure.FileSystem;

namespace App.UI.Services;

/// <summary>
/// WindowPlacementTracker가 창 하나의 위치/크기를 읽고 쓰는 곳. 메인/캐릭터 창은 JSON 파일(WindowStateStore),
/// 메모 창은 SQLite(MemoNote 행)에 저장하므로 트래커는 저장처를 이 인터페이스로만 안다(KNOWN_ISSUES #20-1).
/// Save는 실패하면 예외를 던진다 — 알릴지 기록만 남길지는 트래커가 한 곳에서 정한다(KNOWN_ISSUES #21).
/// </summary>
public interface IWindowPlacementStore
{
    WindowPlacement? Load();

    void Save(WindowPlacement placement);
}
