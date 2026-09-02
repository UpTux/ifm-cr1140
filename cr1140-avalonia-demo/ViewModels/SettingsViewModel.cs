// SPDX-License-Identifier: GPL-3.0-only
namespace Cr1140.AvaloniaDemo.ViewModels;

/// <summary>
/// View model for the Settings screen — fieldbus toggle.
/// </summary>
public sealed class SettingsViewModel : ViewModelBase
{
    private bool _etherCatEnabled;

    public SettingsViewModel()
    {
        _etherCatEnabled = false; // Starts on Ethernet
    }

    public bool EtherCatEnabled
    {
        get => _etherCatEnabled;
        private set
        {
            if (SetField(ref _etherCatEnabled, value))
            {
                OnPropertyChanged(nameof(FieldbusText));
            }
        }
    }

    public string FieldbusText => EtherCatEnabled ? "EtherCAT" : "Ethernet";

    public void ToggleFieldbus()
    {
        EtherCatEnabled = !EtherCatEnabled;
    }
}
