namespace App.Core.Domain.Entities;

public sealed class TodoItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Title { get; set; }
    public Guid? CategoryId { get; set; }
    public bool IsImportant { get; set; }
    public bool IsUrgent { get; set; }
    public DateTime? DueDate { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }
    public List<ChecklistItem> ChecklistItems { get; set; } = new();
    public RecurrenceRule? Recurrence { get; set; }
    public bool NotifiedDueSoon { get; set; }

    /// <summary>지금까지 완료된 누적 횟수. 0이면 한 번도 완료된 적 없음(반복 항목이 롤오버된 뒤에도
    /// IsCompleted/CompletedAt만으로는 최초 상태와 구분이 안 되기 때문에 별도로 둔다).</summary>
    public int CompletionCount { get; set; }
}
