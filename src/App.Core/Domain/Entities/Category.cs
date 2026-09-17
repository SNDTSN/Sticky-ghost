namespace App.Core.Domain.Entities;

public sealed class Category
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Name { get; set; }
    public required string Color { get; set; }
    public int SortOrder { get; set; }
}
