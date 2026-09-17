using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace App.UI.ViewModels;

public partial class DayOfWeekOptionViewModel : ViewModelBase
{
    public DayOfWeek Day { get; }
    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;

    public DayOfWeekOptionViewModel(DayOfWeek day, string label)
    {
        Day = day;
        Label = label;
    }
}
