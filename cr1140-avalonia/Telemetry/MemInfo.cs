// SPDX-License-Identifier: GPL-3.0-only
namespace Cr1140.Avalonia.Telemetry;

/// <summary>
/// Memory totals in kB, with a convenience for the used fraction. Mirrors the
/// <c>MemTotal</c> / <c>MemAvailable</c> fields of <c>/proc/meminfo</c>.
/// </summary>
public readonly struct MemInfo
{
    /// <summary>Create a memory reading from total and available kilobytes.</summary>
    /// <param name="totalKb"><c>MemTotal</c> in kB.</param>
    /// <param name="availableKb"><c>MemAvailable</c> in kB.</param>
    public MemInfo(long totalKb, long availableKb)
    {
        TotalKb = totalKb;
        AvailableKb = availableKb;
    }

    /// <summary>Total physical memory in kB (<c>MemTotal</c>).</summary>
    public long TotalKb { get; }

    /// <summary>Memory available for new allocations in kB (<c>MemAvailable</c>).</summary>
    public long AvailableKb { get; }

    /// <summary>Used memory as a percentage (<c>0.0..=100.0</c>).</summary>
    public double UsedPercent => ProcFs.MemUsedPercent(TotalKb, AvailableKb);
}
