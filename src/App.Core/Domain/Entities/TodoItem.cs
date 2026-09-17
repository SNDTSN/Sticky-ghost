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
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? CompletedAt { get; set; }
    public List<ChecklistItem> ChecklistItems { get; set; } = new();
    public RecurrenceRule? Recurrence { get; set; }
    public bool NotifiedDueSoon { get; set; }
}
