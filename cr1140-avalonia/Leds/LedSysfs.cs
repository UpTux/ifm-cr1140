// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;

namespace Cr1140.Avalonia.Leds;

/// <summary>
/// Thin, typed reader/writer for the CR1140/CR1141 onboard LEDs under
/// <c>/sys/class/leds/</c>. The .NET counterpart of the Rust <c>cr1140-hal</c>
/// <c>sys</c> LED functions (<c>set_led</c> / <c>read_led</c> / <c>set_led_typed</c> /
/// <c>set_kbd_backlight</c> / <c>list_leds</c>).
/// </summary>
/// <remarks>
/// Writing needs write access to the sysfs <c>brightness</c> node (run as root, or add
/// a udev rule granting the target group). Every call degrades gracefully off-device
/// and on permission failure: writes return <see langword="false"/> and reads return
/// <see langword="null"/> when the backing node is missing or unwritable, rather than
/// throwing — the same ethos as <see cref="Telemetry.ProcFs"/>.
/// </remarks>
public static class LedSysfs
{
    /// <summary>
    /// Filesystem prefix prepended to every <c>/sys/class/leds/</c> path. Empty (the
    /// default) targets the real device sysfs; the desktop emulator
    /// (<c>Cr1140.Avalonia.Emulator.EmulatedDevice</c>) points it at a seeded temp tree
    /// so LED writes are observable off-device. Not thread-safe to change while in use.
    /// </summary>
    internal static string Root { get; set; } = "";

    /// <summary>The sysfs leaf name of <paramref name="led"/> under <c>/sys/class/leds/</c>.</summary>
    public static string Name(Led led) => led switch
    {
        Led.StatusRed => "red:status",
        Led.StatusGreen => "green:status",
        Led.StatusBlue => "blue:status",
        Led.KbdRed => "red:kbd_backlight",
        Led.KbdGreen => "green:kbd_backlight",
        Led.KbdBlue => "blue:kbd_backlight",
        _ => throw new ArgumentOutOfRangeException(nameof(led), led, null),
    };

    /// <summary>
    /// The <c>max_brightness</c> of <paramref name="led"/>: <c>1</c> for the binary
    /// status LEDs, <c>255</c> for the PWM keypad-backlight channels.
    /// </summary>
    public static uint Max(Led led) => led switch
    {
        Led.StatusRed or Led.StatusGreen or Led.StatusBlue => 1u,
        _ => 255u,
    };

    /// <summary>
    /// Set an LED brightness via <c>/sys/class/leds/&lt;name&gt;/brightness</c>.
    /// </summary>
    /// <returns><see langword="true"/> on success; <see langword="false"/> if the node
    /// is missing or unwritable.</returns>
    public static bool Set(string name, uint value)
    {
        try
        {
            File.WriteAllText(
                $"{Root}/sys/class/leds/{name}/brightness",
                value.ToString(CultureInfo.InvariantCulture));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Read an LED's current brightness (for save/restore); <see langword="null"/> if
    /// the node is missing or unparseable.
    /// </summary>
    public static uint? Read(string name)
    {
        string content;
        try
        {
            content = File.ReadAllText($"{Root}/sys/class/leds/{name}/brightness");
        }
        catch
        {
            return null;
        }

        return uint.TryParse(content.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var v)
            ? v
            : null;
    }

    /// <summary>Set a typed <see cref="Led"/>, clamping the value to that LED's <see cref="Max"/>.</summary>
    public static bool SetTyped(Led led, uint value) => Set(Name(led), Math.Min(value, Max(led)));

    /// <summary>
    /// Set the RGB keypad button backlight. The keypad LED is three PWM channels
    /// (<c>red</c> / <c>green</c> / <c>blue:kbd_backlight</c>, each 0–255), so any color
    /// is a mix: e.g. yellow = (255,255,0), orange = (255,90,0), off = (0,0,0). Writes
    /// stop at the first channel that fails.
    /// </summary>
    public static bool SetKbdBacklight(byte r, byte g, byte b) =>
        Set(Name(Led.KbdRed), r) && Set(Name(Led.KbdGreen), g) && Set(Name(Led.KbdBlue), b);

    /// <summary>
    /// The available LED leaf names under <c>/sys/class/leds/</c> (sorted), or
    /// <see langword="null"/> off-device / if the directory is unreadable.
    /// </summary>
    public static IReadOnlyList<string>? ListLeds()
    {
        try
        {
            var names = Directory.GetFileSystemEntries($"{Root}/sys/class/leds")
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
}
