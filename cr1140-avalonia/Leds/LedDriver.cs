// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;

namespace Cr1140.Avalonia.Leds;

/// <summary>
/// Drives the RGB keypad button backlight from a base color and an animation
/// <see cref="LedMode"/>. Call <see cref="Tick"/> once per frame; it samples the mode's
/// brightness curve, scales the color, and writes the three sysfs channels only when
/// the resulting value changes (so a steady color costs nothing after the first write).
/// Mirrors the Rust <c>cr1140-sdk</c> <c>led::LedDriver</c>.
/// </summary>
/// <remarks>
/// A new driver is off (<c>(0,0,0)</c>) and <see cref="LedMode.Solid"/>; no hardware
/// write happens until the first <see cref="Tick"/>. Not thread-safe — drive it from a
/// single loop (a 1 Hz–60 Hz <c>DispatcherTimer</c> is typical; higher for smooth
/// <see cref="LedMode.Pulse"/>).
/// </remarks>
public sealed class LedDriver
{
    private (byte R, byte G, byte B) _color;
    private LedMode _mode = LedMode.Solid;
    private long _modeStart = Stopwatch.GetTimestamp();
    private (byte R, byte G, byte B)? _last;

    /// <summary>The current base color (before the mode's brightness curve is applied).</summary>
    public (byte R, byte G, byte B) Color => _color;

    /// <summary>The current animation mode.</summary>
    public LedMode Mode => _mode;

    /// <summary>Set the base color. Does not restart the animation phase.</summary>
    public void SetColor((byte R, byte G, byte B) rgb) => _color = rgb;

    /// <summary>Set the animation mode and restart its phase from now.</summary>
    public void SetMode(LedMode mode)
    {
        _mode = mode;
        _modeStart = Stopwatch.GetTimestamp();
    }

    /// <summary>
    /// Apply the current color × mode to the hardware for this instant. Writes sysfs
    /// only when the computed channel values differ from the last write.
    /// </summary>
    /// <returns><see langword="true"/> if nothing needed writing or the write succeeded;
    /// <see langword="false"/> if the sysfs write failed (e.g. off-device or no
    /// permission), in which case the value is not cached and the next <see cref="Tick"/>
    /// retries.</returns>
    public bool Tick()
    {
        var elapsed = (Stopwatch.GetTimestamp() - _modeStart) / (double)Stopwatch.Frequency;
        var target = LedAnimation.Scale(_color, LedAnimation.Level(_mode, elapsed));
        if (_last is { } last && last == target)
        {
            return true;
        }

        if (!LedSysfs.SetKbdBacklight(target.R, target.G, target.B))
        {
            return false;
        }

        _last = target;
        return true;
    }
}
