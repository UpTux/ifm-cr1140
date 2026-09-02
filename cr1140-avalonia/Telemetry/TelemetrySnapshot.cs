// SPDX-License-Identifier: GPL-3.0-only
namespace Cr1140.Avalonia.Telemetry;

/// <summary>
/// A single point-in-time read of the system telemetry an operator panel typically
/// shows. Every field degrades to <see langword="null"/> independently, so a
/// missing <c>/proc</c> file or thermal zone never fails the whole sample.
/// </summary>
/// <remarks>
/// Network state (eth0/can0) and OS identity are intentionally not here — they live
/// in <see cref="DeviceInfo"/> because they change at a different cadence. Produce a
/// snapshot with <see cref="SystemTelemetry.Sample"/>.
/// </remarks>
public readonly struct TelemetrySnapshot
{
    /// <summary>Create a snapshot from the individual (independently optional) fields.</summary>
    public TelemetrySnapshot(
        double? cpuPercent,
        MemInfo? memory,
        double? socTempC,
        double? boardTempC,
        double? uptimeSeconds,
        double? load1)
    {
        CpuPercent = cpuPercent;
        Memory = memory;
        SocTempC = socTempC;
        BoardTempC = boardTempC;
        UptimeSeconds = uptimeSeconds;
        Load1 = load1;
    }

    /// <summary>CPU utilisation % over the last sampling interval (<c>0.0..=100.0</c>).</summary>
    public double? CpuPercent { get; }

    /// <summary>Physical memory totals and used fraction.</summary>
    public MemInfo? Memory { get; }

    /// <summary>SoC package temperature in °C (thermal zone, default 0).</summary>
    public double? SocTempC { get; }

    /// <summary>Onboard board-sensor temperature in °C (hwmon lm75), distinct from the SoC zone.</summary>
    public double? BoardTempC { get; }

    /// <summary>Seconds since boot.</summary>
    public double? UptimeSeconds { get; }

    /// <summary>1-minute load average.</summary>
    public double? Load1 { get; }
}
