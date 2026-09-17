using System;
using App.Core.Domain.Entities;
using App.Core.Domain.Repositories;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MediaColor = Avalonia.Media.Color;

namespace App.UI.ViewModels;

public partial class CategoryViewModel : ViewModelBase
{
    private readonly Category _category;
    private readonly ICategoryRepository _repository;
    private readonly Action? _onEdited;
    private readonly Action? _onDeleted;

    public Guid Id { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private string _colorHex;

    public CategoryViewModel(
        Category category, ICategoryRepository repository, Action? onEdited = null, Action? onDeleted = null)
    {
        _category = category;
        _repository = repository;
        _onEdited = onEdited;
        _onDeleted = onDeleted;
        Id = category.Id;
        _name = category.Name;
        _colorHex = category.Color;
    }

    public MediaColor ColorValue
    {
        get => MediaColor.Parse(ColorHex);
        set => ColorHex = value.ToString();
    }

    partial void OnNameChanged(string value)
    {
        _category.Name = value;
        _repository.Save(_category);
        _onEdited?.Invoke();
    }

    partial void OnColorHexChanged(string value)
    {
        _category.Color = value;
        _repository.Save(_category);
        OnPropertyChanged(nameof(ColorValue));
        _onEdited?.Invoke();
    }

    [RelayCommand]
    private void Delete()
    {
        _repository.Delete(Id);
        _onDeleted?.Invoke();
    }
}
