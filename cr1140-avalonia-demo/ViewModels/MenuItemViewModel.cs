// SPDX-License-Identifier: GPL-3.0-only
namespace Cr1140.AvaloniaDemo.ViewModels;

/// <summary>
/// Represents one item in the menu list.
/// </summary>
public sealed class MenuItemViewModel : ViewModelBase
{
    private string _label;
    private bool _isSelected;

    public MenuItemViewModel(string label)
    {
        _label = label;
    }

    public string Label
    {
        get => _label;
        set => SetField(ref _label, value);
    }

    public bool IsSelected
    {
        get => _isSelected;
        set => SetField(ref _isSelected, value);
    }
}
