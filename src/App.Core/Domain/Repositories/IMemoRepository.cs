using App.Core.Domain.Entities;

namespace App.Core.Domain.Repositories;

public interface IMemoRepository
{
    void Save(MemoNote memo);

    /// <summary>
    /// 위치/크기만 갱신한다. 행이 없으면 아무 일도 하지 않는다 — 삭제 직후 닫히는 메모 창이 Closing에서 위치를 저장해도
    /// Save(UPSERT)처럼 행을 되살리지 않는다. Content/UpdatedAt은 건드리지 않는다(KNOWN_ISSUES #20-1).
    /// </summary>
    void UpdateGeometry(Guid id, double positionX, double positionY, double width, double height);

    void Delete(Guid id);

    IEnumerable<MemoNote> GetAll();
}
