// SPDX-License-Identifier: GPL-3.0-only
using Cr1140.AvaloniaDemo;
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Threading;
using Cr1140.Avalonia.Telemetry;

namespace Cr1140.AvaloniaDemo.ViewModels;

/// <summary>
/// A single telemetry row: a fixed <see cref="Name"/> and a live <see cref="Value"/>
/// (raises change notifications so the UI refreshes in place).
/// </summary>
public sealed class TelemetryRow : ViewModelBase
{
    private string _value;

    public TelemetryRow(string name, string value)
    {
        Name = name;
        _value = value;
    }

    public string Name { get; }

    public string Value
    {
        get => _value;
        set => SetField(ref _value, value);
    }
}

/// <summary>
/// View model for the Telemetry screen. Backed by the <c>Cr1140.Avalonia</c>
/// package's <see cref="SystemTelemetry"/> collector and <see cref="DeviceInfo"/> —
/// real CPU, memory, SoC/board temperature, load, uptime and network state,
/// refreshed once a second on the UI thread. Nothing here is mocked.
/// </summary>
public sealed class TelemetryViewModel : ViewModelBase
{
    private const string Dash = "—";
    private const double ScrollStep = 96;

    private readonly SystemTelemetry _telemetry = new(
        Program.Profile.SocThermalZone,
        Program.Profile.TemperatureViaDbus ? new IfmSystemTemperatures() : null);
    private readonly DispatcherTimer _timer;

    private readonly TelemetryRow _can = new("CAN can0", Dash);
    private readonly TelemetryRow _eth = new("Ethernet eth0", Dash);
    private readonly TelemetryRow _socTemp = new("SoC temp", Dash);
    private readonly TelemetryRow _boardTemp = new("Board temp", Dash);
    private readonly TelemetryRow _cpu = new("CPU usage", Dash);
    private readonly TelemetryRow _memory = new("Memory", Dash);
    private readonly TelemetryRow _load = new("Load (1m)", Dash);
    private readonly TelemetryRow _uptime = new("Uptime", Dash);

    public TelemetryViewModel()
    {
        Rows = new ObservableCollection<TelemetryRow>
        {
            _can, _eth, _socTemp, _boardTemp, _cpu, _memory, _load, _uptime
        };

        Refresh();
        _timer = new DispatcherTimer(
            TimeSpan.FromSeconds(1),
            DispatcherPriority.Background,
            (_, _) => Refresh());
        _timer.Start();
    }

    public ObservableCollection<TelemetryRow> Rows { get; }

    /// <summary>
    /// Raised when the operator asks to scroll the telemetry list, with a signed
    /// vertical delta in pixels (negative = up, positive = down). The view moves
    /// its <c>ScrollViewer</c> and clamps to the content bounds.
    /// </summary>
    public event Action<double>? ScrollRequested;

    /// <summary>Scroll the list up one step (keypad Up on the Telemetry screen).</summary>
    public void ScrollUp() => ScrollRequested?.Invoke(-ScrollStep);

    /// <summary>Scroll the list down one step (keypad Down on the Telemetry screen).</summary>
    public void ScrollDown() => ScrollRequested?.Invoke(ScrollStep);

    private void Refresh()
    {
        var s = _telemetry.Sample();
        var c = CultureInfo.InvariantCulture;

        _can.Value = DeviceInfo.OperState("can0").ToUpperInvariant();
        _eth.Value = FormatLink("eth0");
        _socTemp.Value = s.SocTempC is double soc ? string.Format(c, "{0:F1} °C", soc) : Dash;
        _boardTemp.Value = s.BoardTempC is double board ? string.Format(c, "{0:F1} °C", board) : Dash;
        _cpu.Value = s.CpuPercent is double cpu ? string.Format(c, "{0:F0} %", cpu) : Dash;
        _memory.Value = s.Memory is MemInfo m
            ? string.Format(c, "{0:F0} % ({1} / {2} MB)", m.UsedPercent, (m.TotalKb - m.AvailableKb) / 1024, m.TotalKb / 1024)
            : Dash;
        _load.Value = s.Load1 is double load ? string.Format(c, "{0:F2}", load) : Dash;
        _uptime.Value = s.UptimeSeconds is double up ? ProcFs.FormatUptime(up) : Dash;
    }

    private static string FormatLink(string iface)
    {
        var state = DeviceInfo.OperState(iface).ToUpperInvariant();
        var ip = DeviceInfo.IPv4(iface);
        return ip is null ? state : $"{state} ({ip})";
    }
}
