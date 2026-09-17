namespace App.Core.Domain.Entities;

public sealed class ChecklistItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required Guid TodoItemId { get; set; }
    public required string Text { get; set; }
    public bool IsChecked { get; set; }
    public int SortOrder { get; set; }
}
