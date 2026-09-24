using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using App.Core.Domain.Entities;
using App.Core.Domain.Services;
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

    /// <summary>목록에서의 정렬 위치. 화면에 표시하지 않고 MainViewModel이 삽입 위치를 정할 때만 쓴다.
    /// 정렬 기준(설정)은 MainViewModel만 알고 키를 계산해 넘겨준다 — 기준이 바뀌면 MainViewModel이 VM을 전부 새로 만든다.</summary>
    public TodoSortKey SortKey { get; }

    [ObservableProperty]
    private bool _isCompleted;

    public TodoItemViewModel(
        TodoItem item, TodoSortKey sortKey, Category? category, Action<Guid> onComplete, Action<Guid, bool> onToggleChecklistItem,
        Func<Guid, Task> onDelete)
    {
        _onComplete = onComplete;
        _onDelete = onDelete;
        Id = item.Id;
        SortKey = sortKey;
        Title = item.Title;
        DueDate = item.DueDate;
        IsImportant = item.IsImportant;
        IsUrgent = item.IsUrgent;
        _isCompleted = item.IsCompleted;
        RecurrenceSummary = RecurrenceSummaryFormatter.Format(item.Recurrence);
        CategoryName = category?.Name;
        CategoryColorHex = category?.Color;

        ChecklistItems = new ObservableCollection<ChecklistItemViewModel>(
            item.ChecklistItems
                .OrderBy(c => c.SortOrder)
                .Select(c => new ChecklistItemViewModel(c, onToggleChecklistItem)));
    }

    partial void OnIsCompletedChanged(bool value)
    {
        if (value)
            _onComplete(Id);
    }

    [RelayCommand]
    private Task DeleteAsync() => _onDelete(Id);
}
