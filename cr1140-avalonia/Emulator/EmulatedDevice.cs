// SPDX-License-Identifier: GPL-3.0-only

using System;
using System.Collections.Generic;
using System.IO;
using Cr1140.Avalonia.Devices;
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
    private readonly DeviceProfile _profile;

    /// <summary>
    /// Initializes a new instance of the <see cref="EmulatedDevice"/> class,
    /// creating a temporary emulated sysfs tree and redirecting library LED/backlight roots to it.
    /// </summary>
    /// <param name="profile">The device profile whose LED and backlight nodes should be seeded.</param>
    public EmulatedDevice(DeviceProfile profile)
    {
        _profile = profile;
        _root = Path.Combine(Path.GetTempPath(), "cr1140-emu-" + Guid.NewGuid().ToString("N"));

        // Seed backlight
        string backlightDir = Path.Combine(_root, "sys", "class", "backlight", profile.BacklightNode);
        Directory.CreateDirectory(backlightDir);
        File.WriteAllText(Path.Combine(backlightDir, "max_brightness"), profile.BacklightMaxHint.ToString());
        File.WriteAllText(Path.Combine(backlightDir, "brightness"), profile.BacklightMaxHint.ToString());

        // Seed LEDs
        foreach (RgbLed led in profile.Leds)
        {
            foreach (string leaf in led.Leaves)
            {
                string ledDir = Path.Combine(_root, "sys", "class", "leds", leaf);
                Directory.CreateDirectory(ledDir);
                File.WriteAllText(Path.Combine(ledDir, "brightness"), "0");
                File.WriteAllText(Path.Combine(ledDir, "max_brightness"), led.Max.ToString());
            }
        }

        // Redirect library roots
        LedSysfs.Root = _root;
        Backlight.Root = _root;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="EmulatedDevice"/> class for CR1140,
    /// creating a temporary emulated sysfs tree and redirecting library LED/backlight roots to it.
    /// </summary>
    public EmulatedDevice()
        : this(DeviceProfiles.Cr1140)
    {
    }

    /// <summary>
    /// Gets the root path of the temporary emulated sysfs directory tree.
    /// </summary>
    public string Root => _root;

    /// <summary>
    /// Gets the backlight device name used by the emulator.
    /// </summary>
    public string BacklightName => _profile.BacklightNode;

    /// <summary>
    /// Gets all RGB LEDs available on the emulated device profile.
    /// </summary>
    public IReadOnlyList<RgbLed> Leds => _profile.Leds;

    /// <summary>
    /// Gets the current color of an RGB LED as an RGB tuple.
    /// Returns (0, 0, 0) if the LED cannot be read.
    /// </summary>
    /// <param name="led">The LED to read.</param>
    /// <returns>An RGB tuple representing the current LED color.</returns>
    public (byte R, byte G, byte B) LedColor(RgbLed led) => LedSysfs.ReadRgb(led) ?? (0, 0, 0);

    /// <summary>
    /// Gets the current status LED color as an RGB tuple.
    /// Returns (0, 0, 0) if the profile has no Status-role LED.
    /// </summary>
    public (byte R, byte G, byte B) StatusColor
    {
        get
        {
            foreach (var led in _profile.Leds)
            {
                if (led.Role == LedRole.Status)
                {
                    return LedColor(led);
                }
            }
            return (0, 0, 0);
        }
    }
    /// <summary>
    /// Gets the current keypad backlight LED color as an RGB tuple.
    /// Returns (0, 0, 0) if the profile has no KeypadBacklight-role LED.
    /// </summary>
    public (byte R, byte G, byte B) KbdColor
    {
        get
        {
            foreach (var led in _profile.Leds)
            {
                if (led.Role == LedRole.KeypadBacklight)
                {
                    return LedColor(led);
                }
            }
            return (0, 0, 0);
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
