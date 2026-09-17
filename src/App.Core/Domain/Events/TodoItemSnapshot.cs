using System.Linq;
using App.Core.Domain.Entities;

namespace App.Core.Domain.Events;

/// <summary>
/// TodoItem은 참조 타입(mutable class)이라 이벤트에 그대로 담아 넘기면, 구독자가 나중에(비동기로) 읽는
/// 시점에는 이미 다른 값으로 바뀌어 있을 수 있다 (예: 반복 항목이 완료 직후 다음 회차로 롤오버되는 경우).
/// 이벤트는 항상 "발행 시점의 불변 사실"을 담아야 하므로, TodoItem을 발행 직전에 이 스냅샷으로 복사해서 넘긴다.
/// </summary>
public sealed record TodoItemSnapshot(
    Guid Id,
    string Title,
    Guid? CategoryId,
    bool IsImportant,
    bool IsUrgent,
    DateTime? DueDate,
    bool IsCompleted,
    DateTime CreatedAt,
    DateTime? CompletedAt,
    IReadOnlyList<ChecklistItemSnapshot> ChecklistItems,
    RecurrenceRule? Recurrence,
    int CompletionCount)
{
    public static TodoItemSnapshot From(TodoItem item) => new(
        item.Id,
        item.Title,
        item.CategoryId,
        item.IsImportant,
        item.IsUrgent,
        item.DueDate,
        item.IsCompleted,
        item.CreatedAt,
        item.CompletedAt,
        item.ChecklistItems.Select(ChecklistItemSnapshot.From).ToList(),
        item.Recurrence,
        item.CompletionCount);
}

public sealed record ChecklistItemSnapshot(Guid Id, string Text, bool IsChecked, int SortOrder)
{
    public static ChecklistItemSnapshot From(ChecklistItem item) => new(item.Id, item.Text, item.IsChecked, item.SortOrder);
}
