using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
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
        foreach (var item in _todoRepository.GetIncomplete().OrderBy(i => i.DueDate ?? DateTime.MaxValue))
        {
            Category? category = item.CategoryId is { } categoryId && categoryLookup.TryGetValue(categoryId, out var found)
                ? found
                : null;
            Items.Add(new TodoItemViewModel(item, category, CompleteTodo, ToggleChecklistItem));
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

        _todoService.AddTodo(
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

        LoadItems();
    }

    private void CompleteTodo(Guid id)
    {
        _todoService.CompleteTodo(id);
        LoadItems();
    }

    private void ToggleChecklistItem(Guid todoId, Guid checklistItemId, bool isChecked)
    {
        var item = _todoRepository.Get(todoId);
        var checklistItem = item?.ChecklistItems.FirstOrDefault(c => c.Id == checklistItemId);
        if (item is null || checklistItem is null)
            return;

        checklistItem.IsChecked = isChecked;
        _todoRepository.Save(item);
        LoadItems();
    }
}
