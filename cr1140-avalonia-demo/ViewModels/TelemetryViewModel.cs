// SPDX-License-Identifier: GPL-3.0-only
using System.Collections.ObjectModel;

namespace Cr1140.AvaloniaDemo.ViewModels;

/// <summary>
/// Simple telemetry row for the Telemetry screen.
/// </summary>
public sealed class TelemetryRow
{
    public TelemetryRow(string name, string value)
    {
        Name = name;
        Value = value;
    }

    public string Name { get; }
    public string Value { get; }
}

/// <summary>
/// View model for the Telemetry screen — mock system status rows.
/// </summary>
public sealed class TelemetryViewModel : ViewModelBase
{
    public TelemetryViewModel()
    {
        Rows = new ObservableCollection<TelemetryRow>
        {
            new("CAN can0", "DOWN (mock)"),
            new("Ethernet eth0", "UP"),
            new("SoC temp", "— (mock)"),
            new("CPU usage", "— (mock)"),
            new("Memory", "— (mock)"),
            new("Uptime", "— (mock)")
        };
    }

    public ObservableCollection<TelemetryRow> Rows { get; }
}
