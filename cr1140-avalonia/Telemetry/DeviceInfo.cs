// SPDX-License-Identifier: GPL-3.0-only
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace Cr1140.Avalonia.Telemetry;

/// <summary>
/// Device and OS identity plus network state: things that describe <i>this</i> unit
/// (model/firmware from <c>/etc/os-release</c>, hostname, onboard board sensor) and
/// its links (e.g. <c>eth0</c> / <c>can0</c> operstate and IPv4 address).
/// </summary>
/// <remarks>
/// Kept separate from <see cref="TelemetrySnapshot"/> because these values change on
/// a slower/event cadence than the per-second metrics. All readers degrade
/// gracefully: file reads return <see langword="null"/> (or <c>"?"</c> for the
/// string helpers) when the backing sysfs/procfs entry is missing.
/// </remarks>
public static class DeviceInfo
{
    /// <summary>
    /// Look up a <c>KEY=value</c> entry in <c>/etc/os-release</c> content, stripping
    /// optional surrounding quotes. The key is matched exactly.
    /// </summary>
    /// <param name="content">The contents of an <c>os-release</c> file.</param>
    /// <param name="key">The key to look up, e.g. <c>PRETTY_NAME</c>.</param>
    /// <returns>The value, or <see langword="null"/> if the key is absent.</returns>
    public static string? OsReleaseValue(string content, string key)
    {
        foreach (var line in content.Split('\n'))
        {
            var idx = line.IndexOf('=');
            if (idx < 0)
            {
                continue;
            }

            if (line.Substring(0, idx) == key)
            {
                return line.Substring(idx + 1).Trim().Trim('"');
            }
        }

        return null;
    }

    /// <summary>Read <c>/etc/os-release</c> and return the value for <paramref name="key"/>.</summary>
    public static string? OsRelease(string key)
    {
        var content = ProcFs.TryRead("/etc/os-release");
        return content is null ? null : OsReleaseValue(content, key);
    }

    /// <summary>The kernel hostname (<c>/proc/sys/kernel/hostname</c>); <c>"?"</c> if unreadable.</summary>
    public static string Hostname()
    {
        var content = ProcFs.TryRead("/proc/sys/kernel/hostname");
        return content is null ? "?" : content.Trim();
    }

    /// <summary>
    /// Onboard lm75 board-temperature sensor in °C
    /// (<c>/sys/class/hwmon/hwmon0/temp1_input</c>), distinct from the SoC zone.
    /// </summary>
    public static double? ReadBoardTempC()
    {
        var content = ProcFs.TryRead("/sys/class/hwmon/hwmon0/temp1_input");
        return content is null ? null : ProcFs.ParseMillidegrees(content);
    }

    /// <summary>
    /// The <c>operstate</c> of a network interface, e.g. <c>"up"</c> / <c>"down"</c>
    /// for <c>eth0</c> / <c>can0</c>; <c>"?"</c> if the interface has no such node.
    /// </summary>
    /// <param name="iface">The interface name, e.g. <c>eth0</c>.</param>
    public static string OperState(string iface)
    {
        var content = ProcFs.TryRead($"/sys/class/net/{iface}/operstate");
        return content is null ? "?" : content.Trim();
    }

    /// <summary>
    /// The first IPv4 address bound to an interface, or <see langword="null"/> if the
    /// interface has none. Unlike a route-based lookup this works on an isolated LAN
    /// with no default gateway (the CR1140's typical deployment).
    /// </summary>
    /// <param name="iface">The interface name, e.g. <c>eth0</c>.</param>
    public static string? IPv4(string iface)
    {
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.Name != iface)
                {
                    continue;
                }

                foreach (var addr in ni.GetIPProperties().UnicastAddresses)
                {
                    if (addr.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        return addr.Address.ToString();
                    }
                }
            }
        }
        catch
        {
            // No network stack / interface enumeration unsupported → treat as no address.
        }

        return null;
    }

    /// <summary>
    /// CPU model string: first <c>model name</c> or <c>Hardware</c> line from
    /// <c>/proc/cpuinfo</c>, or <c>/proc/device-tree/model</c> as fallback.
    /// </summary>
    /// <returns>
    /// The CPU model, or <see langword="null"/> if both sources are unavailable.
    /// </returns>
    public static string? CpuModel()
    {
        try
        {
            var cpuinfo = ProcFs.TryRead("/proc/cpuinfo") ?? string.Empty;
            var deviceTreeModel = ProcFs.TryRead("/proc/device-tree/model");
            
            // Strip trailing NUL and whitespace from device-tree model
            if (deviceTreeModel is not null)
            {
                deviceTreeModel = deviceTreeModel.TrimEnd('\0', ' ', '\t', '\n', '\r');
            }
            
            return ParseCpuModel(cpuinfo, deviceTreeModel);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// CPU count: the number of <c>processor</c> entries in <c>/proc/cpuinfo</c>.
    /// </summary>
    /// <returns>
    /// The CPU count, or <see langword="null"/> if <c>/proc/cpuinfo</c> is unavailable
    /// or contains no processor entries.
    /// </returns>
    public static int? CpuCount()
    {
        try
        {
            var cpuinfo = ProcFs.TryRead("/proc/cpuinfo");
            return cpuinfo is null ? null : ParseCpuCount(cpuinfo);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Parse CPU model from <c>/proc/cpuinfo</c> content, with
    /// <c>/proc/device-tree/model</c> as fallback.
    /// </summary>
    /// <param name="cpuinfo">The contents of <c>/proc/cpuinfo</c>.</param>
    /// <param name="deviceTreeModel">
    /// The contents of <c>/proc/device-tree/model</c>, or <see langword="null"/> if unavailable.
    /// </param>
    /// <returns>
    /// The first <c>model name</c> or <c>Hardware</c> value from <paramref name="cpuinfo"/>
    /// (case-insensitive), or <paramref name="deviceTreeModel"/> if no such line exists,
    /// or <see langword="null"/> if both sources are empty.
    /// </returns>
    public static string? ParseCpuModel(string cpuinfo, string? deviceTreeModel)
    {
        foreach (var line in cpuinfo.Split('\n'))
        {
            var idx = line.IndexOf(':');
            if (idx < 0)
            {
                continue;
            }

            var key = line.Substring(0, idx).Trim();
            if (string.Equals(key, "model name", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "Hardware", StringComparison.OrdinalIgnoreCase))
            {
                return line.Substring(idx + 1).Trim();
            }
        }

        // Fall back to device-tree model if available (strip trailing NUL/whitespace).
        var dt = deviceTreeModel?.TrimEnd('\0', ' ', '\t', '\n', '\r');
        if (!string.IsNullOrEmpty(dt))
        {
            return dt;
        }

        return null;
    }

    /// <summary>
    /// Parse CPU count from <c>/proc/cpuinfo</c> content by counting
    /// <c>processor</c> entries.
    /// </summary>
    /// <param name="cpuinfo">The contents of <c>/proc/cpuinfo</c>.</param>
    /// <returns>
    /// The number of <c>processor</c> lines (case-insensitive), or
    /// <see langword="null"/> if none are found.
    /// </returns>
    public static int? ParseCpuCount(string cpuinfo)
    {
        var count = 0;
        foreach (var line in cpuinfo.Split('\n'))
        {
            var idx = line.IndexOf(':');
            if (idx < 0)
            {
                continue;
            }

            var key = line.Substring(0, idx).Trim();
            if (string.Equals(key, "processor", StringComparison.OrdinalIgnoreCase))
            {
                count++;
            }
        }

        return count > 0 ? count : null;
    }
}
