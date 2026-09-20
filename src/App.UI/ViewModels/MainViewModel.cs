using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using App.Core.Domain.Entities;
using App.Core.Domain.Repositories;
using App.Core.Domain.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace App.UI.ViewModels;

public partial class MainViewModel : ViewModelBase
{
    private readonly ITodoRepository _todoRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly TodoService _todoService;

    public ObservableCollection<TodoItemViewModel> Items { get; } = new();

    /// <summary>실제 삭제 전 사용자 확인을 받는 콜백. View(MainWindow)가 생성 직후 채워준다 — ViewModel이
    /// Window를 직접 참조하지 않기 위해 MemoNoteViewModel.ConfirmDeleteRequested와 같은 패턴을 사용.</summary>
    public Func<Task<bool>>? ConfirmDeleteTodoRequested { get; set; }

    [ObservableProperty]
    private string _newTitle = string.Empty;

    [ObservableProperty]
    private DateTimeOffset? _newDueDate;

    [ObservableProperty]
    private bool _newIsImportant;

    [ObservableProperty]
    private bool _newIsUrgent;

    [ObservableProperty]
    private string _newChecklistText = string.Empty;

    public ObservableCollection<string> NewChecklistItems { get; } = new();

    public IReadOnlyList<RecurrenceTypeOption> RecurrenceTypeOptions { get; } = new[]
    {
        new RecurrenceTypeOption(RecurrenceType.Daily, "매일"),
        new RecurrenceTypeOption(RecurrenceType.Weekly, "매주"),
        new RecurrenceTypeOption(RecurrenceType.Monthly, "매달"),
    };

    public ObservableCollection<DayOfWeekOptionViewModel> RecurrenceDayOptions { get; } = new()
    {
        new(DayOfWeek.Monday, "월"),
        new(DayOfWeek.Tuesday, "화"),
        new(DayOfWeek.Wednesday, "수"),
        new(DayOfWeek.Thursday, "목"),
        new(DayOfWeek.Friday, "금"),
        new(DayOfWeek.Saturday, "토"),
        new(DayOfWeek.Sunday, "일"),
    };

    [ObservableProperty]
    private bool _newIsRecurring;

    [ObservableProperty]
    private RecurrenceTypeOption _selectedRecurrenceType;

    [ObservableProperty]
    private int _newRecurrenceInterval = 1;

    [ObservableProperty]
    private DateTimeOffset? _newRecurrenceEndDate;

    [ObservableProperty]
    private bool _isWeeklyRecurrenceSelected;

    [ObservableProperty]
    private bool _canEditRecurrenceDays;

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _hasErrorMessage;

    partial void OnErrorMessageChanged(string? value) => HasErrorMessage = !string.IsNullOrEmpty(value);

    public ObservableCollection<CategoryViewModel> Categories { get; } = new();

    [ObservableProperty]
    private string _newCategoryName = string.Empty;

    [ObservableProperty]
    private CategoryViewModel? _selectedCategory;

    public MainViewModel(ITodoRepository todoRepository, ICategoryRepository categoryRepository, TodoService todoService)
    {
        _todoRepository = todoRepository;
        _categoryRepository = categoryRepository;
        _todoService = todoService;
        _selectedRecurrenceType = RecurrenceTypeOptions[0];
        LoadCategories();
        LoadItems();
    }

    private void LoadItems()
    {
        Items.Clear();
        var categoryLookup = _categoryRepository.GetAll().ToDictionary(c => c.Id);
        foreach (var item in _todoRepository.GetIncomplete().OrderBy(TodoSortKey.From))
        {
            Category? category = item.CategoryId is { } categoryId && categoryLookup.TryGetValue(categoryId, out var found)
                ? found
                : null;
            Items.Add(new TodoItemViewModel(item, category, CompleteTodo, ToggleChecklistItem, DeleteTodoAsync));
        }
    }

    private void LoadCategories()
    {
        Categories.Clear();
        foreach (var category in _categoryRepository.GetAll())
        {
            Categories.Add(new CategoryViewModel(
                category, _categoryRepository,
                onEdited: LoadItems,
                onDeleted: () => { LoadCategories(); LoadItems(); }));
        }
    }

    [RelayCommand]
    private void AddCategory()
    {
        if (string.IsNullOrWhiteSpace(NewCategoryName))
            return;

        _categoryRepository.Save(new Category { Name = NewCategoryName, Color = "#9E9E9E" });
        NewCategoryName = string.Empty;
        LoadCategories();
    }

    [RelayCommand]
    private void AddChecklistItemToDraft()
    {
        if (string.IsNullOrWhiteSpace(NewChecklistText))
            return;

        NewChecklistItems.Add(NewChecklistText);
        NewChecklistText = string.Empty;
    }

    [RelayCommand]
    private void RemoveChecklistItemFromDraft(string text) => NewChecklistItems.Remove(text);

    partial void OnSelectedRecurrenceTypeChanged(RecurrenceTypeOption value) => UpdateRecurrenceFlags();

    partial void OnNewRecurrenceIntervalChanged(int value) => UpdateRecurrenceFlags();

    private void UpdateRecurrenceFlags()
    {
        IsWeeklyRecurrenceSelected = SelectedRecurrenceType.Value == RecurrenceType.Weekly;
        CanEditRecurrenceDays = IsWeeklyRecurrenceSelected && NewRecurrenceInterval == 1;
    }

    // 변경된 항목 하나만 갱신한다. LoadItems()처럼 GetIncomplete() 전체를 다시 쿼리하고
    // 모든 TodoItemViewModel을 새로 만드는 대신, 영향받은 항목만 반영한다.
    private int FindItemIndex(Guid id)
    {
        for (var i = 0; i < Items.Count; i++)
        {
            if (Items[i].Id == id)
                return i;
        }
        return -1;
    }

    private void UpsertItem(TodoItem item)
    {
        var existingIndex = FindItemIndex(item.Id);

        if (item.IsCompleted)
        {
            if (existingIndex >= 0)
                Items.RemoveAt(existingIndex);
            return;
        }

        Category? category = item.CategoryId is { } categoryId
            ? _categoryRepository.GetAll().FirstOrDefault(c => c.Id == categoryId)
            : null;
        var vm = new TodoItemViewModel(item, category, CompleteTodo, ToggleChecklistItem, DeleteTodoAsync);
        var key = vm.SortKey;

        // 정렬 키가 그대로면 자리를 옮기지 않는다 — 제거 후 재삽입은 ListBox의 스크롤 위치를 튀게 한다.
        if (existingIndex >= 0 && Items[existingIndex].SortKey == key)
        {
            Items[existingIndex] = vm;
            return;
        }

        if (existingIndex >= 0)
            Items.RemoveAt(existingIndex);

        // 키가 전순서(마감일 → 사분면 → 생성순 → Id)라 들어갈 자리가 유일하게 정해진다.
        // 그래서 이렇게 하나씩 끼워 넣은 순서는 LoadItems가 전체를 다시 정렬한 결과와 항상 같다.
        var insertAt = 0;
        while (insertAt < Items.Count && Items[insertAt].SortKey <= key)
            insertAt++;
        Items.Insert(insertAt, vm);
    }

    private void RemoveItem(Guid id)
    {
        var existingIndex = FindItemIndex(id);
        if (existingIndex >= 0)
            Items.RemoveAt(existingIndex);
    }

    private void RefreshItem(Guid id)
    {
        var item = _todoRepository.Get(id);
        if (item is null)
            RemoveItem(id);
        else
            UpsertItem(item);
    }

    [RelayCommand]
    private void AddTodo()
    {
        if (string.IsNullOrWhiteSpace(NewTitle))
            return;

        RecurrenceRule? recurrence = null;
        if (NewIsRecurring)
        {
            if (NewDueDate is null)
            {
                ErrorMessage = "반복 항목은 마감일이 필요합니다.";
                return;
            }

            IReadOnlySet<DayOfWeek>? daysOfWeek = null;
            if (CanEditRecurrenceDays)
            {
                var selectedDays = RecurrenceDayOptions.Where(d => d.IsSelected).Select(d => d.Day).ToHashSet();
                daysOfWeek = selectedDays.Count > 0 ? selectedDays : null;
            }

            recurrence = new RecurrenceRule(
                SelectedRecurrenceType.Value,
                NewRecurrenceInterval,
                daysOfWeek,
                NewRecurrenceEndDate?.DateTime);
        }

        ErrorMessage = null;

        var newItem = _todoService.AddTodo(
            NewTitle,
            categoryId: SelectedCategory?.Id,
            NewIsImportant,
            NewIsUrgent,
            NewDueDate?.DateTime,
            checklistTexts: NewChecklistItems.Count > 0 ? NewChecklistItems.ToList() : null,
            recurrence: recurrence);

        NewTitle = string.Empty;
        NewDueDate = null;
        NewIsImportant = false;
        NewIsUrgent = false;
        NewChecklistItems.Clear();
        SelectedCategory = null;
        NewIsRecurring = false;
        SelectedRecurrenceType = RecurrenceTypeOptions[0];
        NewRecurrenceInterval = 1;
        NewRecurrenceEndDate = null;
        foreach (var day in RecurrenceDayOptions)
            day.IsSelected = false;

        UpsertItem(newItem);
    }

    private void CompleteTodo(Guid id)
    {
        _todoService.CompleteTodo(id);
        RefreshItem(id);
    }

    private async Task DeleteTodoAsync(Guid id)
    {
        var confirmed = ConfirmDeleteTodoRequested is not null && await ConfirmDeleteTodoRequested.Invoke();
        if (!confirmed)
            return;

        _todoService.DeleteTodo(id);
        RemoveItem(id);
    }

    // 저장만 하고 목록은 건드리지 않는다. 체크 상태는 누른 ChecklistItemViewModel에 이미 반영돼 있고,
    // 할 일 템플릿에는 체크리스트에서 파생되는 표시(진행률 배지 등)가 없어서 VM을 다시 만들 이유가 없다.
    // 예전에는 여기서 UpsertItem을 불러 VM을 새로 만들었는데, 그 바람에 정렬 키가 그대로인데도
    // 항목이 목록 아래로 밀려났다(KNOWN_ISSUES #17). 나중에 진행률 표시를 넣는다면 그때는
    // UpsertItem을 다시 불러야 하고, 제자리 교체 분기가 순서를 지켜준다.
    private void ToggleChecklistItem(Guid todoId, Guid checklistItemId, bool isChecked)
    {
        var item = _todoRepository.Get(todoId);
        var checklistItem = item?.ChecklistItems.FirstOrDefault(c => c.Id == checklistItemId);
        if (item is null || checklistItem is null)
            return;

        checklistItem.IsChecked = isChecked;
        _todoRepository.Save(item);
    }
}
