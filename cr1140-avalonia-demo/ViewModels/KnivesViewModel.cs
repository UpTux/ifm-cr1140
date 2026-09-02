// SPDX-License-Identifier: GPL-3.0-only
namespace Cr1140.AvaloniaDemo.ViewModels;

/// <summary>
/// View model for the Knives screen — IN/OUT toggle.
/// </summary>
public sealed class KnivesViewModel : ViewModelBase
{
    private bool _in;

    public KnivesViewModel()
    {
        _in = false; // Starts OUT
    }

    public bool In
    {
        get => _in;
        private set => SetField(ref _in, value);
    }

    public void Toggle()
    {
        In = !In;
    }
}
