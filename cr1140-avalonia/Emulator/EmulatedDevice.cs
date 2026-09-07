// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.IO;
using Cr1140.Avalonia.Display;
using Cr1140.Avalonia.Leds;

namespace Cr1140.Avalonia.Emulator;

/// <summary>
/// Off-device emulation shim that redirects the library's sysfs LED and backlight roots
/// to a temporary directory tree, allowing LED and backlight writes to be observable
/// without physical hardware. Dispose restores the roots to empty strings.
/// This is a single-instance class; do not construct multiple instances concurrently
/// as the second would overwrite the shared static roots.
/// </summary>
public sealed class EmulatedDevice : IDisposable
{
    private readonly string _root;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmulatedDevice"/> class,
    /// creating a temporary emulated sysfs tree and redirecting library LED/backlight roots to it.
    /// </summary>
    public EmulatedDevice()
    {
        _root = Path.Combine(Path.GetTempPath(), "cr1140-emu-" + Guid.NewGuid().ToString("N"));

        // Seed LEDs
        foreach (Led led in Enum.GetValues<Led>())
        {
            string name = LedSysfs.Name(led);
            string ledDir = Path.Combine(_root, "sys", "class", "leds", name);
            Directory.CreateDirectory(ledDir);
            File.WriteAllText(Path.Combine(ledDir, "brightness"), "0");
        }

        // Seed backlight
        string backlightDir = Path.Combine(_root, "sys", "class", "backlight", Backlight.Default);
        Directory.CreateDirectory(backlightDir);
        File.WriteAllText(Path.Combine(backlightDir, "max_brightness"), "400");
        File.WriteAllText(Path.Combine(backlightDir, "brightness"), "400");

        // Redirect library roots
        LedSysfs.Root = _root;
        Backlight.Root = _root;
    }

    /// <summary>
    /// Gets the root path of the temporary emulated sysfs directory tree.
    /// </summary>
    public string Root => _root;

    /// <summary>
    /// Gets the backlight device name used by the emulator.
    /// </summary>
    public string BacklightName => Backlight.Default;

    /// <summary>
    /// Gets the current status LED color as an RGB tuple.
    /// Status LEDs are binary; values >= 1 map to 255, 0 or null map to 0.
    /// </summary>
    public (byte R, byte G, byte B) StatusColor
    {
        get
        {
            uint? r = LedSysfs.Read(LedSysfs.Name(Led.StatusRed));
            uint? g = LedSysfs.Read(LedSysfs.Name(Led.StatusGreen));
            uint? b = LedSysfs.Read(LedSysfs.Name(Led.StatusBlue));
            return (
                (byte)((r ?? 0) >= 1 ? 255 : 0),
                (byte)((g ?? 0) >= 1 ? 255 : 0),
                (byte)((b ?? 0) >= 1 ? 255 : 0)
            );
        }
    }

    /// <summary>
    /// Gets the current keypad backlight LED color as an RGB tuple.
    /// Each channel is 0-255.
    /// </summary>
    public (byte R, byte G, byte B) KbdColor
    {
        get
        {
            uint? r = LedSysfs.Read(LedSysfs.Name(Led.KbdRed));
            uint? g = LedSysfs.Read(LedSysfs.Name(Led.KbdGreen));
            uint? b = LedSysfs.Read(LedSysfs.Name(Led.KbdBlue));
            return (
                (byte)Math.Min(255u, r ?? 0u),
                (byte)Math.Min(255u, g ?? 0u),
                (byte)Math.Min(255u, b ?? 0u)
            );
        }
    }

    /// <summary>
    /// Gets the current backlight brightness as a percentage (0-100).
    /// </summary>
    public double BacklightPercent => Backlight.ReadPercent(BacklightName) ?? 100.0;

    /// <summary>
    /// Disposes the emulated device, restoring library LED/backlight roots to empty
    /// and deleting the temporary sysfs directory tree.
    /// </summary>
    public void Dispose()
    {
        LedSysfs.Root = "";
        Backlight.Root = "";

        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup failures
        }
    }
}
