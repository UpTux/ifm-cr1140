// SPDX-License-Identifier: GPL-3.0-only
namespace Cr1140.AvaloniaDemo.ViewModels;

/// <summary>
/// View model for the Bale Counter screen — session + total counts.
/// </summary>
public sealed class BaleCounterViewModel : ViewModelBase
{
    private int _session;
    private int _total;

    public BaleCounterViewModel()
    {
        _session = 0;
        _total = 0;
    }

    public int Session
    {
        get => _session;
        private set => SetField(ref _session, value);
    }

    public int Total
    {
        get => _total;
        private set => SetField(ref _total, value);
    }

    public void AddBale()
    {
        Session++;
        Total++;
    }

    public void ResetSession()
    {
        Session = 0;
    }
}
