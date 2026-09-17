using System.Text.Json;
using App.Core.Domain.Entities;
using App.Core.Domain.Events;
using App.Core.Domain.Repositories;

namespace App.Core.Domain.Services;

public sealed class TodoService
{
    private readonly ITodoRepository _repository;
    private readonly ICompletionLogStore _completionLog;
    private readonly ITodoEventBus _eventBus;
    private readonly IClock _clock;
    private readonly TimeSpan _dueSoonThreshold;

    public TodoService(
        ITodoRepository repository,
        ICompletionLogStore completionLog,
        ITodoEventBus eventBus,
        IClock clock,
        TimeSpan dueSoonThreshold)
    {
        _repository = repository;
        _completionLog = completionLog;
        _eventBus = eventBus;
        _clock = clock;
        _dueSoonThreshold = dueSoonThreshold;
    }

    public TodoItem AddTodo(
        string title,
        Guid? categoryId,
        bool isImportant,
        bool isUrgent,
        DateTime? dueDate,
        IEnumerable<string>? checklistTexts,
        RecurrenceRule? recurrence)
    {
        if (recurrence is not null && dueDate is null)
            throw new ArgumentException("반복 항목은 첫 마감일(dueDate)이 필요합니다.", nameof(dueDate));

        var item = new TodoItem
        {
            Title = title,
            CategoryId = categoryId,
            IsImportant = isImportant,
            IsUrgent = isUrgent,
            DueDate = dueDate,
            Recurrence = recurrence,
            CreatedAt = _clock.Now,
        };

        if (checklistTexts is not null)
        {
            var sortOrder = 0;
            foreach (var text in checklistTexts)
            {
                item.ChecklistItems.Add(new ChecklistItem
                {
                    TodoItemId = item.Id,
                    Text = text,
                    SortOrder = sortOrder++,
                });
            }
        }

        _repository.Save(item);
        _eventBus.Publish(new TodoCreated(TodoItemSnapshot.From(item)));
        return item;
    }

    public void CompleteTodo(Guid id)
    {
        var item = _repository.Get(id)
            ?? throw new InvalidOperationException($"존재하지 않는 TodoItem: {id}");

        var checklistSnapshot = JsonSerializer.Serialize(item.ChecklistItems);
        var now = _clock.Now;

        item.CompletionCount += 1;
        item.IsCompleted = true;
        item.CompletedAt = now;
        _completionLog.Append(id, now, checklistSnapshot);

        // 반복 롤오버로 item이 더 바뀌기 전, "방금 완료된 회차" 그대로 이벤트용 스냅샷을 떠 둔다.
        var completedEventSnapshot = TodoItemSnapshot.From(item);

        if (item.Recurrence is { } recurrence)
        {
            var next = recurrence.ComputeNext(item.DueDate!.Value);
            var recurrenceEnded = recurrence.EndDate is { } endDate && next > endDate;

            // EndDate를 지났으면 이 회차를 마지막으로 더 굴리지 않고 완료 상태로 남겨둔다.
            // Recurrence 필드 자체는 지우지 않음 (예전에 반복이었다는 이력 보존).
            if (!recurrenceEnded)
            {
                item.DueDate = next;
                item.IsCompleted = false;
                item.CompletedAt = null;
                item.NotifiedDueSoon = false;
                foreach (var checklistItem in item.ChecklistItems)
                    checklistItem.IsChecked = false;
            }
        }

        _repository.Save(item);
        _eventBus.Publish(new TodoCompleted(completedEventSnapshot));
    }

    public void CheckDueSoon()
    {
        var now = _clock.Now;

        foreach (var item in _repository.GetActiveWithDueDate())
        {
            if (item.DueDate is not { } dueDate)
                continue;

            var minutesLeft = (int)Math.Ceiling((dueDate - now).TotalMinutes);

            if (minutesLeft <= _dueSoonThreshold.TotalMinutes && !item.NotifiedDueSoon)
            {
                item.NotifiedDueSoon = true;
                _repository.Save(item);
                _eventBus.Publish(new TodoDueSoon(TodoItemSnapshot.From(item), minutesLeft));
            }
            else if (dueDate < now)
            {
                _eventBus.Publish(new TodoOverdue(TodoItemSnapshot.From(item)));
            }
        }
    }
}
