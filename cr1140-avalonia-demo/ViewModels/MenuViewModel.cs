// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.ObjectModel;

namespace Cr1140.AvaloniaDemo.ViewModels;

/// <summary>
/// View model for the home menu screen.
/// </summary>
public sealed class MenuViewModel : ViewModelBase
{
    private int _selectedIndex;

    public MenuViewModel()
    {
        Items = new ObservableCollection<MenuItemViewModel>
        {
            new("Dashboard"),
            new("Bale Counter"),
            new("Knives"),
            new("Wrapping"),
            new("Telemetry"),
            new("Settings"),
            new("Key Events"),
            new("LEDs")
        };
        SelectedIndex = 0;
    }

    public ObservableCollection<MenuItemViewModel> Items { get; }

    public int SelectedIndex
    {
        get => _selectedIndex;
        set
        {
            if (SetField(ref _selectedIndex, value))
            {
                UpdateSelection();
            }
        }
    }

    private void UpdateSelection()
    {
        for (int i = 0; i < Items.Count; i++)
        {
            Items[i].IsSelected = i == _selectedIndex;
        }
    }
}
