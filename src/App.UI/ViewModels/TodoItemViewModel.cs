using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using App.Core.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace App.UI.ViewModels;

public partial class TodoItemViewModel : ViewModelBase
{
    private readonly Action<Guid> _onComplete;
    private readonly Func<Guid, Task> _onDelete;

    public Guid Id { get; }
    public string Title { get; }
    public DateTime? DueDate { get; }
    public bool IsImportant { get; }
    public bool IsUrgent { get; }
    public string? RecurrenceSummary { get; }
    public bool HasRecurrence => RecurrenceSummary is not null;
    public string? CategoryName { get; }
    public string? CategoryColorHex { get; }
    public bool HasCategory => CategoryName is not null;
    public ObservableCollection<ChecklistItemViewModel> ChecklistItems { get; }

    [ObservableProperty]
    private bool _isCompleted;

    public TodoItemViewModel(
        TodoItem item, Category? category, Action<Guid> onComplete, Action<Guid, Guid, bool> onToggleChecklistItem,
        Func<Guid, Task> onDelete)
    {
        _onComplete = onComplete;
        _onDelete = onDelete;
        Id = item.Id;
        Title = item.Title;
        DueDate = item.DueDate;
        IsImportant = item.IsImportant;
        IsUrgent = item.IsUrgent;
        _isCompleted = item.IsCompleted;
        RecurrenceSummary = BuildRecurrenceSummary(item.Recurrence);
        CategoryName = category?.Name;
        CategoryColorHex = category?.Color;

        var todoId = item.Id;
        ChecklistItems = new ObservableCollection<ChecklistItemViewModel>(
            item.ChecklistItems
                .OrderBy(c => c.SortOrder)
                .Select(c => new ChecklistItemViewModel(
                    c, (checklistItemId, isChecked) => onToggleChecklistItem(todoId, checklistItemId, isChecked))));
    }

    partial void OnIsCompletedChanged(bool value)
    {
        if (value)
            _onComplete(Id);
    }

    [RelayCommand]
    private Task DeleteAsync() => _onDelete(Id);

    // 요일 표시 순서 (docs/todo-design.md: 월=1,화=2,수=4... 와 동일한 순서)
    private static readonly (DayOfWeek Day, string Label)[] DayOrder =
    {
        (DayOfWeek.Monday, "월"), (DayOfWeek.Tuesday, "화"), (DayOfWeek.Wednesday, "수"),
        (DayOfWeek.Thursday, "목"), (DayOfWeek.Friday, "금"), (DayOfWeek.Saturday, "토"), (DayOfWeek.Sunday, "일"),
    };

    private static string? BuildRecurrenceSummary(RecurrenceRule? recurrence)
    {
        if (recurrence is null)
            return null;

        var unit = recurrence.Type switch
        {
            RecurrenceType.Daily => "일",
            RecurrenceType.Weekly => "주",
            RecurrenceType.Monthly => "달",
            _ => "?",
        };

        string body;
        if (recurrence.Type == RecurrenceType.Weekly && recurrence.DaysOfWeek is { Count: > 0 } days)
        {
            var labels = DayOrder.Where(d => days.Contains(d.Day)).Select(d => d.Label);
            body = $"매주 {string.Join(",", labels)}";
        }
        else
        {
            body = recurrence.Interval == 1 ? $"매{unit}" : $"{recurrence.Interval}{unit}마다";
        }

        return recurrence.EndDate is { } end
            ? $"🔁 {body} (종료 {end:yyyy-MM-dd})"
            : $"🔁 {body}";
    }
}
