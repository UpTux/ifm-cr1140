// SPDX-License-Identifier: GPL-3.0-only
namespace Cr1140.Avalonia.Leds;

/// <summary>
/// A keypad-LED animation mode: a pure brightness curve over time, sampled by
/// <see cref="LedDriver"/>. Mirrors the Rust <c>cr1140-sdk</c> <c>led::LedMode</c>.
/// </summary>
public enum LedMode
{
    /// <summary>Always on at full brightness.</summary>
    Solid,

    /// <summary>Always on at 50%.</summary>
    Dim,

    /// <summary>Smooth 2 s breathe.</summary>
    Pulse,

    /// <summary>1 Hz on/off.</summary>
    Blink,

    /// <summary>Fast (~4 Hz) strobe: short on, longer off.</summary>
    Flash,

    /// <summary>Two quick beats then a pause (~1.2 s period).</summary>
    Heartbeat
}
