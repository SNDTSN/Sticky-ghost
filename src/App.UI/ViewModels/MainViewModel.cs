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

    // "3 일 마다"처럼 숫자 뒤에 붙는 단위로 읽히게 한다 — 예전에는 "매일" + "간격 3"으로 갈라져 있어
    // 3일에 한 번 반복하는 할 일을 만들려면 "매일, 간격 3"을 조합해야 했다.
    public IReadOnlyList<RecurrenceUnitOption> RecurrenceUnitOptions { get; } = new[]
    {
        new RecurrenceUnitOption(RecurrenceUnit.Day, "일"),
        new RecurrenceUnitOption(RecurrenceUnit.Week, "주"),
        new RecurrenceUnitOption(RecurrenceUnit.Month, "달"),
        new RecurrenceUnitOption(RecurrenceUnit.Weekday, "요일"),
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
    private RecurrenceUnitOption _selectedRecurrenceUnit;

    [ObservableProperty]
    private int _newRecurrenceInterval = 1;

    [ObservableProperty]
    private DateTimeOffset? _newRecurrenceEndDate;

    /// <summary>숫자 칸과 "마다"의 표시 여부. 요일 지정에서는 간격이 의미가 없어 숨긴다.</summary>
    [ObservableProperty]
    private bool _isIntervalVisible = true;

    [ObservableProperty]
    private bool _isWeekdayPickerVisible;

    /// <summary>목록에 뜰 문구를 입력 중에 그대로 보여준다(목록과 같은 포맷터를 쓴다).</summary>
    [ObservableProperty]
    private string? _recurrencePreview;

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
        _selectedRecurrenceUnit = RecurrenceUnitOptions[0];

        // 요일 체크박스는 별도 뷰모델이라 여기서 구독해야 미리보기가 따라 갱신된다.
        // RecurrenceDayOptions는 이 뷰모델과 수명이 같아 해제할 필요가 없다.
        foreach (var day in RecurrenceDayOptions)
            day.PropertyChanged += (_, _) => UpdateRecurrencePreview();

        UpdateRecurrenceFlags();
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

    partial void OnSelectedRecurrenceUnitChanged(RecurrenceUnitOption value) => UpdateRecurrenceFlags();

    partial void OnNewRecurrenceIntervalChanged(int value) => UpdateRecurrencePreview();

    partial void OnNewIsRecurringChanged(bool value) => UpdateRecurrencePreview();

    partial void OnNewRecurrenceEndDateChanged(DateTimeOffset? value) => UpdateRecurrencePreview();

    private void UpdateRecurrenceFlags()
    {
        var isWeekday = SelectedRecurrenceUnit.Unit == RecurrenceUnit.Weekday;
        IsIntervalVisible = !isWeekday;
        IsWeekdayPickerVisible = isWeekday;
        UpdateRecurrencePreview();
    }

    // 입력이 아직 규칙으로 성립하지 않으면(요일 미선택 등) 미리보기를 비운다 — 추가를 누를 때 사유를 안내한다.
    private void UpdateRecurrencePreview() =>
        RecurrencePreview = NewIsRecurring ? RecurrenceSummaryFormatter.Format(TryBuildRecurrence()) : null;

    /// <summary>현재 입력을 도메인 규칙으로 바꾼다. 성립하지 않으면 null.</summary>
    private RecurrenceRule? TryBuildRecurrence()
    {
        var endDate = NewRecurrenceEndDate?.DateTime;

        if (SelectedRecurrenceUnit.Unit != RecurrenceUnit.Weekday)
        {
            var type = SelectedRecurrenceUnit.Unit switch
            {
                RecurrenceUnit.Day => RecurrenceType.Daily,
                RecurrenceUnit.Week => RecurrenceType.Weekly,
                _ => RecurrenceType.Monthly,
            };

            // NumericUpDown을 비우면 0이 들어올 수 있고 RecurrenceRule은 1 미만을 거부한다.
            return NewRecurrenceInterval >= 1 ? new RecurrenceRule(type, NewRecurrenceInterval, null, endDate) : null;
        }

        var selectedDays = RecurrenceDayOptions.Where(d => d.IsSelected).Select(d => d.Day).ToHashSet();
        if (selectedDays.Count == 0)
            return null;

        // 요일을 지정하면 도메인이 Interval을 무시하므로 1로 고정한다.
        return new RecurrenceRule(RecurrenceType.Weekly, 1, selectedDays, endDate);
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

            recurrence = TryBuildRecurrence();
            if (recurrence is null)
            {
                // 요일 단위인데 아무 요일도 안 고르면 조용히 "매주"가 되어버리므로 사유를 알린다.
                ErrorMessage = SelectedRecurrenceUnit.Unit == RecurrenceUnit.Weekday
                    ? "반복할 요일을 하나 이상 선택해 주세요."
                    : "반복 간격은 1 이상이어야 합니다.";
                return;
            }
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
        SelectedRecurrenceUnit = RecurrenceUnitOptions[0];
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
