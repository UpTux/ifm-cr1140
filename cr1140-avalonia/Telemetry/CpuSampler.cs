// SPDX-License-Identifier: GPL-3.0-only

namespace Cr1140.Avalonia.Telemetry;

/// <summary>
/// CPU utilisation from <c>/proc/stat</c>, computed as the busy fraction between
/// two samples. The first sample primes the baseline and reports <c>0</c>%.
/// </summary>
/// <remarks>
/// Hold one instance and call <see cref="Sample"/> at a fixed cadence (e.g. 1 Hz);
/// each call returns the average utilisation over the interval since the previous
/// call. <see cref="SystemTelemetry"/> owns one of these internally.
/// </remarks>
public sealed class CpuSampler
{
    private long _prevIdle;
    private long _prevTotal;
    private bool _primed;

    /// <summary>
    /// Read <c>/proc/stat</c> and return CPU usage % since the previous call, or
    /// <see langword="null"/> if <c>/proc/stat</c> could not be read/parsed.
    /// </summary>
    public double? Sample()
    {
        var content = ProcFs.TryRead("/proc/stat");
        if (content is null)
        {
            return null;
        }

        return ProcFs.ParseStat(content) is (long idle, long total) ? Update(idle, total) : null;
    }

    /// <summary>
    /// Pure state transition: fold one <c>(idle, total)</c> jiffy reading into the
    /// sampler and return the busy percentage (<c>0.0..=100.0</c>) since the last
    /// reading. Exposed so the delta logic can be unit-tested without a filesystem.
    /// </summary>
    public double Update(long idle, long total)
    {
        double pct;
        if (_primed && total > _prevTotal)
        {
            var deltaIdle = Math.Max(0, idle - _prevIdle);
            var deltaTotal = total - _prevTotal;
            pct = (1.0 - (double)deltaIdle / deltaTotal) * 100.0;
        }
        else
        {
            pct = 0.0;
        }

        _prevIdle = idle;
        _prevTotal = total;
        _primed = true;
        return Math.Clamp(pct, 0.0, 100.0);
    }
}
