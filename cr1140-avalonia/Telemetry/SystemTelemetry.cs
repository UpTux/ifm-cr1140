// SPDX-License-Identifier: GPL-3.0-only
namespace Cr1140.Avalonia.Telemetry;

/// <summary>
/// Pull-based collector for generic Linux system telemetry (CPU, memory, SoC and
/// board temperature, uptime, load). One <see cref="Sample"/> call yields a whole
/// <see cref="TelemetrySnapshot"/>.
/// </summary>
/// <remarks>
/// This is a plain, framework-agnostic class — <b>not</b> an Avalonia control. It
/// starts no threads and no timers: hold one instance and call <see cref="Sample"/>
/// on whatever cadence you like (a 1 Hz <c>DispatcherTimer</c> is typical). Because
/// it keeps CPU-sampler state between calls, reuse the same instance rather than
/// constructing one per sample. The first call primes the CPU baseline and reports
/// <c>0</c>%.
/// </remarks>
public sealed class SystemTelemetry
{
    /// <summary>
    /// The thermal zone backing the CR1140/CR1141 SoC temperature
    /// (<c>/sys/class/thermal/thermal_zone0</c>).
    /// </summary>
    public const uint DefaultSocThermalZone = 0;

    private readonly CpuSampler _cpu = new();
    private readonly uint _socZone;

    /// <summary>New collector reading the default SoC thermal zone (<see cref="DefaultSocThermalZone"/>).</summary>
    public SystemTelemetry()
        : this(DefaultSocThermalZone)
    {
    }

    /// <summary>New collector reading a specific thermal zone for the SoC temperature.</summary>
    /// <param name="socThermalZone">The <c>N</c> in <c>/sys/class/thermal/thermal_zoneN</c>.</param>
    public SystemTelemetry(uint socThermalZone)
    {
        _socZone = socThermalZone;
    }

    /// <summary>
    /// Sample every metric now. The first call primes CPU % and reports <c>0</c>%.
    /// Each field is independently optional (see <see cref="TelemetrySnapshot"/>).
    /// </summary>
    public TelemetrySnapshot Sample()
    {
        return new TelemetrySnapshot(
            cpuPercent: _cpu.Sample(),
            memory: ProcFs.ReadMeminfo(),
            socTempC: ProcFs.ReadTempC(_socZone),
            boardTempC: DeviceInfo.ReadBoardTempC(),
            uptimeSeconds: ProcFs.ReadUptime(),
            load1: ProcFs.ReadLoadavg());
    }
}
