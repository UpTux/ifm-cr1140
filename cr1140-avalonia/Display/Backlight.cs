// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;

namespace Cr1140.Avalonia.Display;

/// <summary>
/// Thin, typed reader/writer for the CR1140/CR1141 display backlight under
/// <c>/sys/class/backlight/</c>. The .NET counterpart of the Rust <c>cr1140-hal</c>
/// <c>sys</c> backlight functions (<c>set_backlight</c> / <c>read_backlight</c> /
/// <c>backlight_max</c> / <c>list_backlights</c>), plus percentage helpers so an app
/// can offer the operator a simple 0–100 % brightness control without knowing the
/// panel's raw <c>max_brightness</c>.
/// </summary>
/// <remarks>
/// Writing needs write access to the sysfs <c>brightness</c> node (run as root, or add
/// a udev rule granting the target group). Every call degrades gracefully off-device
/// and on permission failure: writes return <see langword="false"/> and reads return
/// <see langword="null"/> when the backing node is missing or unwritable, rather than
/// throwing — the same ethos as <see cref="Leds.LedSysfs"/> and <see cref="Telemetry.ProcFs"/>.
/// </remarks>
public static class Backlight
{
    /// <summary>
    /// Filesystem prefix prepended to every <c>/sys/class/backlight/</c> path. Empty
    /// (the default) targets the real device sysfs; the desktop emulator
    /// (<c>Cr1140.Avalonia.Emulator.EmulatedDevice</c>) points it at a seeded temp tree
    /// so backlight writes are observable off-device. Not thread-safe to change while in use.
    /// </summary>
    internal static string Root { get; set; } = "";

    /// <summary>
    /// The display backlight node name under <c>/sys/class/backlight/</c> on the
    /// CR1140/CR1141. Mirrors the Rust <c>cr1140-hal</c> <c>sys::BACKLIGHT</c>.
    /// </summary>
    public const string Default = "backlight";

    /// <summary>
    /// The documented <c>max_brightness</c> of <see cref="Default"/> on the CR1140/CR1141.
    /// Prefer reading it at runtime with <see cref="Max"/>; this is a fallback hint for
    /// off-device previews. Mirrors the Rust <c>sys::BACKLIGHT_MAX_HINT</c>.
    /// </summary>
    public const uint MaxHint = 400;

    /// <summary>
    /// Set the backlight brightness via <c>/sys/class/backlight/&lt;name&gt;/brightness</c>.
    /// </summary>
    /// <returns><see langword="true"/> on success; <see langword="false"/> if the node
    /// is missing or unwritable.</returns>
    public static bool Set(string name, uint value)
    {
        try
        {
            File.WriteAllText(
                $"{Root}/sys/class/backlight/{name}/brightness",
                value.ToString(CultureInfo.InvariantCulture));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Read the backlight's current brightness (for save/restore); <see langword="null"/>
    /// if the node is missing or unparseable.
    /// </summary>
    public static uint? Read(string name) => ReadUint($"{Root}/sys/class/backlight/{name}/brightness");

    /// <summary>
    /// Read the backlight's <c>max_brightness</c> (for scaling); <see langword="null"/> if
    /// the node is missing or unparseable.
    /// </summary>
    public static uint? Max(string name) => ReadUint($"{Root}/sys/class/backlight/{name}/max_brightness");

    /// <summary>
    /// Set the backlight to <paramref name="percent"/> (0–100, clamped) of its
    /// <see cref="Max"/>. The scaled raw value is rounded to the nearest count.
    /// </summary>
    /// <returns><see langword="true"/> on success; <see langword="false"/> if the node is
    /// missing/unwritable or its <c>max_brightness</c> could not be read.</returns>
    public static bool SetPercent(string name, double percent)
    {
        var max = Max(name);
        if (max is not > 0)
            return false;

        var p = Math.Clamp(percent, 0.0, 100.0);
        var value = (uint)Math.Round(p / 100.0 * max.Value, MidpointRounding.AwayFromZero);
        return Set(name, value);
    }

    /// <summary>
    /// Read the current brightness as a percentage (0–100) of <see cref="Max"/>;
    /// <see langword="null"/> if the current value or <c>max_brightness</c> is unavailable.
    /// </summary>
    public static double? ReadPercent(string name)
    {
        var value = Read(name);
        var max = Max(name);
        if (value is null || max is not > 0)
            return null;

        return (double)value.Value / max.Value * 100.0;
    }

    /// <summary>
    /// The available backlight names under <c>/sys/class/backlight/</c> (sorted), or
    /// <see langword="null"/> off-device / if the directory is unreadable.
    /// </summary>
    public static IReadOnlyList<string>? ListBacklights()
    {
        try
        {
            var names = Directory.GetFileSystemEntries($"{Root}/sys/class/backlight")
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .ToList();
            names.Sort(StringComparer.Ordinal);
            return names;
        }
        catch
        {
            return null;
        }
    }

    private static uint? ReadUint(string path)
    {
        string content;
        try
        {
            content = File.ReadAllText(path);
        }
        catch
        {
            return null;
        }

        return uint.TryParse(content.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }
}
