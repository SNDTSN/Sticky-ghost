using App.Core.Domain.Entities;

namespace App.Core.Domain.Repositories;

public interface ITodoRepository
{
    void Save(TodoItem item);

    TodoItem? Get(Guid id);

    /// <summary>미완료 + 마감일이 지정된 항목 조회 (CheckDueSoon 백그라운드 검사용)</summary>
    IEnumerable<TodoItem> GetActiveWithDueDate();
}
