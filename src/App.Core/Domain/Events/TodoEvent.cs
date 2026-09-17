using App.Core.Domain.Entities;

namespace App.Core.Domain.Events;

public abstract record TodoEvent;

public sealed record TodoCreated(TodoItem Item) : TodoEvent;

public sealed record TodoCompleted(TodoItem Item) : TodoEvent;

public sealed record TodoDueSoon(TodoItem Item, int MinutesLeft) : TodoEvent;

public sealed record TodoOverdue(TodoItem Item) : TodoEvent;
