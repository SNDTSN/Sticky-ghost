using App.Core.Domain.Entities;

namespace App.Core.Domain.Repositories;

public interface ITodoRepository
{
    void Save(TodoItem item);

    void Delete(Guid id);

    TodoItem? Get(Guid id);

    /// <summary>
    /// 체크리스트 행 하나의 체크 상태만 바꾼다. 행이 없으면(그 사이 할 일이 삭제됨) 아무 일도 하지 않는다.
    /// Get → 전체 Save로 하지 않는 이유: 다른 경로(마감 알림 검사 등)가 읽어 둔 옛 값으로 체크를 되돌리거나,
    /// 반대로 이 저장이 남의 변경을 덮어쓰지 않게(KNOWN_ISSUES #28 B-9).
    /// </summary>
    void SetChecklistItemChecked(Guid checklistItemId, bool isChecked);

    /// <summary>
    /// "마감 임박을 알렸음"을 표시한다. 읽은 뒤 사정이 바뀌었으면(완료됨, 이미 표시됨, 삭제됨, 반복 항목이 다음 회차로 넘어감)
    /// 표시하지 않고 false를 돌려준다. UPDATE만 하므로 삭제된 항목을 되살리지 않는다(KNOWN_ISSUES #28 B-9).
    /// </summary>
    bool MarkNotifiedDueSoon(Guid id, DateTime dueDate);

    /// <summary>미완료 + 마감일이 지정된 항목 조회 (CheckDueSoon 백그라운드 검사용)</summary>
    IEnumerable<TodoItem> GetActiveWithDueDate();

    /// <summary>미완료 항목 전체 조회 (마감일 유무 무관, 목록 화면용)</summary>
    IEnumerable<TodoItem> GetIncomplete();
}
