namespace App.Core.Domain.Events;

public abstract record TodoEvent;

public sealed record TodoCreated(TodoItemSnapshot Item) : TodoEvent;

public sealed record TodoCompleted(TodoItemSnapshot Item) : TodoEvent;

public sealed record TodoDueSoon(TodoItemSnapshot Item, int MinutesLeft) : TodoEvent;

public sealed record TodoOverdue(TodoItemSnapshot Item) : TodoEvent;
