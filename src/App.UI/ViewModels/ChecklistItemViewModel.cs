using System;
using App.Core.Domain.Entities;
using CommunityToolkit.Mvvm.ComponentModel;

namespace App.UI.ViewModels;

public partial class ChecklistItemViewModel : ViewModelBase
{
    private readonly Action<Guid, bool> _onToggle;

    public Guid Id { get; }
    public string Text { get; }

    [ObservableProperty]
    private bool _isChecked;

    public ChecklistItemViewModel(ChecklistItem item, Action<Guid, bool> onToggle)
    {
        _onToggle = onToggle;
        Id = item.Id;
        Text = item.Text;
        _isChecked = item.IsChecked;
    }

    partial void OnIsCheckedChanged(bool value) => _onToggle(Id, value);
}
