// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;

namespace Cr1140.Avalonia.Telemetry;

/// <summary>
/// Pure parsers and thin readers for generic Linux system telemetry from
/// <c>/proc</c> and <c>/sys/class/thermal</c>. Nothing here is CR1140-specific —
/// it works on any Linux host.
/// </summary>
/// <remarks>
/// Every reader degrades to <see langword="null"/> when the backing file is
/// missing or unreadable (e.g. on a non-Linux host), so a single missing
/// <c>/proc</c> entry never throws. The parsers are pure functions over the file
/// contents, so they can be unit-tested without touching the filesystem.
/// </remarks>
public static class ProcFs
{
    /// <summary>
    /// Parse the aggregate <c>cpu</c> line of <c>/proc/stat</c> into
    /// <c>(idle, total)</c> jiffies. <c>idle</c> includes <c>iowait</c>, matching
    /// the conventional <c>top</c>-style calculation.
    /// </summary>
    /// <param name="content">The contents of <c>/proc/stat</c>.</param>
    /// <returns>The idle and total jiffies, or <see langword="null"/> if the first
    /// line is not a well-formed aggregate <c>cpu</c> line.</returns>
    public static (long Idle, long Total)? ParseStat(string content)
    {
        var line = FirstLine(content);
        if (line is null)
        {
            return null;
        }

        var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 1 || parts[0] != "cpu")
        {
            return null;
        }

        long total = 0;
        int fields = 0;
        long field3 = 0; // idle
        long field4 = 0; // iowait
        for (int i = 1; i < parts.Length; i++)
        {
            if (!long.TryParse(parts[i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var v))
            {
                continue;
            }

            if (fields == 3)
            {
                field3 = v;
            }
            else if (fields == 4)
            {
                field4 = v;
            }

            total += v;
            fields++;
        }

        if (fields < 4)
        {
            return null;
        }

        return (field3 + field4, total);
    }

    /// <summary>Parse <c>(MemTotal, MemAvailable)</c> in kB from <c>/proc/meminfo</c>.</summary>
    /// <param name="content">The contents of <c>/proc/meminfo</c>.</param>
    /// <returns>Total and available memory in kB, or <see langword="null"/> if
    /// either line is absent.</returns>
    public static (long TotalKb, long AvailableKb)? ParseMeminfo(string content)
    {
        long? total = null;
        long? avail = null;
        foreach (var line in content.Split('\n'))
        {
            if (TryPrefixedValue(line, "MemTotal:", out var t))
            {
                total = t;
            }
            else if (TryPrefixedValue(line, "MemAvailable:", out var a))
            {
                avail = a;
            }
        }

        return total is long tt && avail is long aa ? (tt, aa) : null;
    }

    /// <summary>
    /// Percentage of memory in use (<c>0.0..=100.0</c>); returns <c>0.0</c> if
    /// <paramref name="totalKb"/> is not positive.
    /// </summary>
    public static double MemUsedPercent(long totalKb, long availableKb)
    {
        if (totalKb <= 0)
        {
            return 0.0;
        }

        return Math.Clamp((1.0 - (double)availableKb / totalKb) * 100.0, 0.0, 100.0);
    }

    /// <summary>First field of <c>/proc/uptime</c> = seconds since boot.</summary>
    public static double? ParseUptime(string content) => ParseFirstDouble(content);

    /// <summary>First field of <c>/proc/loadavg</c> = the 1-minute load average.</summary>
    public static double? ParseLoadavg(string content) => ParseFirstDouble(content);

    /// <summary>
    /// Parse a sysfs thermal/hwmon temperature file (<c>"42000\n"</c> millidegrees)
    /// to °C.
    /// </summary>
    public static double? ParseMillidegrees(string content)
    {
        return int.TryParse(content.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var m)
            ? m / 1000.0
            : null;
    }

    /// <summary>
    /// Format a seconds-since-boot value as <c>"1h 02m 05s"</c> (with hours) or
    /// <c>"1m 05s"</c> (under an hour).
    /// </summary>
    public static string FormatUptime(double seconds)
    {
        var s = (long)seconds;
        long h = s / 3600;
        long m = (s % 3600) / 60;
        long sec = s % 60;
        return h > 0
            ? string.Format(CultureInfo.InvariantCulture, "{0}h {1:D2}m {2:D2}s", h, m, sec)
            : string.Format(CultureInfo.InvariantCulture, "{0}m {1:D2}s", m, sec);
    }

    /// <summary>Read <c>(MemTotal, MemAvailable)</c> straight from <c>/proc/meminfo</c>.</summary>
    /// <returns>A <see cref="MemInfo"/>, or <see langword="null"/> if unreadable.</returns>
    public static MemInfo? ReadMeminfo()
    {
        var content = TryRead("/proc/meminfo");
        if (content is null)
        {
            return null;
        }

        return ParseMeminfo(content) is (long total, long avail) ? new MemInfo(total, avail) : null;
    }

    /// <summary>Read seconds since boot straight from <c>/proc/uptime</c>.</summary>
    public static double? ReadUptime()
    {
        var content = TryRead("/proc/uptime");
        return content is null ? null : ParseUptime(content);
    }

    /// <summary>Read the 1-minute load average straight from <c>/proc/loadavg</c>.</summary>
    public static double? ReadLoadavg()
    {
        var content = TryRead("/proc/loadavg");
        return content is null ? null : ParseLoadavg(content);
    }

    /// <summary>
    /// Read a thermal zone temperature in °C from
    /// <c>/sys/class/thermal/thermal_zone{zone}/temp</c>.
    /// </summary>
    /// <param name="zone">The thermal-zone number (the CR1140 SoC zone is 0).</param>
    public static double? ReadTempC(uint zone)
    {
        var content = TryRead($"/sys/class/thermal/thermal_zone{zone}/temp");
        return content is null ? null : ParseMillidegrees(content);
    }

    /// <summary>
    /// Compute a Xilinx/ZynqMP System Monitor (AMS/XADC) IIO temperature in °C from the
    /// three sysfs channel files. The IIO convention for a processed temperature is
    /// <c>(raw + offset) × scale</c> in milli-degrees Celsius, so this returns
    /// <c>(raw + offset) × scale / 1000</c>. Returns <see langword="null"/> if any input is
    /// not a number.
    /// </summary>
    public static double? ParseIioTempC(string rawContent, string offsetContent, string scaleContent)
    {
        if (!int.TryParse(rawContent.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var raw))
            return null;
        if (!double.TryParse(offsetContent.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var offset))
            return null;
        if (!double.TryParse(scaleContent.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var scale))
            return null;
        return (raw + offset) * scale / 1000.0;
    }

    /// <summary>
    /// Read the on-chip SoC temperature in °C from a Xilinx/ZynqMP <b>System Monitor</b>
    /// exposed via IIO — a <c>/sys/bus/iio/devices/iio:deviceN</c> whose <c>name</c> is
    /// <c>xilinx-system-monitor</c> (also <c>xadc</c> / <c>ams</c>), from its
    /// <c>in_temp0_{raw,offset,scale}</c> channel. Used where the SoC exposes no
    /// <c>/sys/class/thermal</c> zone (e.g. the CR1102's ZynqMP, whose thermal class is
    /// empty). Degrades to <see langword="null"/> when no such device/channel is present.
    /// </summary>
    public static double? ReadXilinxSysmonTempC()
    {
        var dir = FindXilinxSysmonDir();
        if (dir is null)
            return null;

        var raw = TryRead($"{dir}/in_temp0_raw");
        var offset = TryRead($"{dir}/in_temp0_offset");
        var scale = TryRead($"{dir}/in_temp0_scale");
        return raw is not null && offset is not null && scale is not null
            ? ParseIioTempC(raw, offset, scale)
            : null;
    }

    /// <summary>Locate the IIO device directory of the Xilinx system monitor, or <see langword="null"/>.</summary>
    private static string? FindXilinxSysmonDir()
    {
        try
        {
            foreach (var dir in Directory.EnumerateDirectories("/sys/bus/iio/devices", "iio:device*"))
            {
                var name = TryRead($"{dir}/name")?.Trim();
                if (name is "xilinx-system-monitor" or "xadc" or "ams")
                    return dir;
            }
        }
        catch
        {
            // No IIO subsystem (non-Linux / no sysmon) — treat as unavailable.
        }

        return null;
    }

    /// <summary>
    /// Read a whole file, returning <see langword="null"/> for any I/O failure
    /// (missing file, wrong platform, permission denied). Shared by the readers in
    /// this package.
    /// </summary>
    internal static string? TryRead(string path)
    {
        try
        {
            return File.ReadAllText(path);
        }
        catch
        {
            return null;
        }
    }

    private static double? ParseFirstDouble(string content)
    {
        var token = FirstToken(content);
        return token is not null
            && double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }

    private static string? FirstLine(string content)
    {
        var nl = content.IndexOf('\n');
        return nl < 0 ? content : content.Substring(0, nl);
    }

    private static string? FirstToken(string content)
    {
        var parts = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length > 0 ? parts[0] : null;
    }

    private static bool TryPrefixedValue(string line, string prefix, out long value)
    {
        value = 0;
        if (!line.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var rest = line.Substring(prefix.Length)
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        return rest.Length > 0
            && long.TryParse(rest[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }
}
