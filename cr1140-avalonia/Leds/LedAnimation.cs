// SPDX-License-Identifier: GPL-3.0-only
namespace Cr1140.Avalonia.Leds;

/// <summary>
/// Pure keypad-LED animation math: the per-mode brightness curve
/// (<see cref="Level"/>), a short display <see cref="Name"/>, and the color
/// <see cref="Scale"/> helper. No hardware and no Avalonia dependency — host-testable
/// like <c>FramebufferRotator</c> / <c>CpuSampler</c>. Mirrors the Rust
/// <c>cr1140-sdk</c> <c>led</c> module (<c>LedMode::name</c> / <c>LedMode::level</c> /
/// <c>scale</c>).
/// </summary>
public static class LedAnimation
{
    /// <summary>A short display name for <paramref name="mode"/> (e.g. <c>"pulse"</c>, <c>"50%"</c>).</summary>
    public static string Name(LedMode mode) => mode switch
    {
        LedMode.Solid => "solid",
        LedMode.Dim => "50%",
        LedMode.Pulse => "pulse",
        LedMode.Blink => "blink",
        LedMode.Flash => "flash",
        LedMode.Heartbeat => "heartbeat",
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    /// <summary>
    /// The brightness multiplier in <c>0.0..=1.0</c> for <paramref name="mode"/> at
    /// <paramref name="t"/> seconds since the mode began.
    /// </summary>
    public static double Level(LedMode mode, double t) => mode switch
    {
        LedMode.Solid => 1.0,
        LedMode.Dim => 0.5,
        // 2 s breathe; starts at 0, peaks at 1 at t = 1 s.
        LedMode.Pulse => 0.5 - 0.5 * Math.Cos(t * Math.Tau / 2.0),
        // 1 Hz square wave.
        LedMode.Blink => t % 1.0 < 0.5 ? 1.0 : 0.0,
        // ~4 Hz strobe: short on, longer off.
        LedMode.Flash => t % 0.25 < 0.1 ? 1.0 : 0.0,
        // Two quick beats then a pause, ~1.2 s period.
        LedMode.Heartbeat => Heartbeat(t),
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, null),
    };

    /// <summary>
    /// Scale a base RGB color by a <c>0.0..=1.0</c> brightness level into channel bytes,
    /// rounding half away from zero (matching the Rust <c>scale</c>).
    /// </summary>
    public static (byte R, byte G, byte B) Scale((byte R, byte G, byte B) rgb, double level)
    {
        var l = Math.Clamp(level, 0.0, 1.0);
        return (ScaleChannel(rgb.R, l), ScaleChannel(rgb.G, l), ScaleChannel(rgb.B, l));
    }

    private static byte ScaleChannel(byte c, double level) =>
        (byte)Math.Round(c * level, MidpointRounding.AwayFromZero);

    private static double Heartbeat(double t)
    {
        var p = t % 1.2;
        return p < 0.12 || (p >= 0.22 && p < 0.34) ? 1.0 : 0.0;
    }
}
