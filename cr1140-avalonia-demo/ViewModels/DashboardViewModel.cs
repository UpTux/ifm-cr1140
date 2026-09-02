// SPDX-License-Identifier: GPL-3.0-only
using Avalonia.Threading;

namespace Cr1140.AvaloniaDemo.ViewModels;

/// <summary>
/// View model for the Dashboard screen — live clock + system info.
/// </summary>
public sealed class DashboardViewModel : ViewModelBase
{
    private readonly DispatcherTimer _clockTimer;
    private string _clock;

    public DashboardViewModel()
    {
        _clock = DateTime.Now.ToString("HH:mm:ss");
        AppName = "Cr1140.AvaloniaDemo";
        Resolution = "800 x 480";
        Backend = "fbdev / Skia (software)";

        _clockTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _clockTimer.Tick += (_, _) =>
        {
            Clock = DateTime.Now.ToString("HH:mm:ss");
        };
        _clockTimer.Start();
    }

    public string Clock
    {
        get => _clock;
        private set => SetField(ref _clock, value);
    }

    public string AppName { get; }
    public string Resolution { get; }
    public string Backend { get; }
}
