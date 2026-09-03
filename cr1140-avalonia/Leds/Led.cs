// SPDX-License-Identifier: GPL-3.0-only
namespace Cr1140.Avalonia.Leds;

/// <summary>
/// The CR1140/CR1141's onboard LEDs, exposed by the kernel under
/// <c>/sys/class/leds/</c>. The three <c>*:status</c> channels drive the single RGB
/// status light (binary, <see cref="LedSysfs.Max"/> == 1); the three
/// <c>*:kbd_backlight</c> channels drive the RGB keypad button backlight (8-bit PWM,
/// max == 255), so only the keypad channels give a visible brightness ramp.
/// </summary>
/// <remarks>Mirrors the Rust <c>cr1140-hal</c> <c>sys::Led</c> enum.</remarks>
public enum Led
{
    /// <summary>Red channel of the RGB status LED (<c>red:status</c>, binary).</summary>
    StatusRed,

    /// <summary>Green channel of the RGB status LED (<c>green:status</c>, binary).</summary>
    StatusGreen,

    /// <summary>Blue channel of the RGB status LED (<c>blue:status</c>, binary).</summary>
    StatusBlue,

    /// <summary>Red channel of the RGB keypad button backlight (<c>red:kbd_backlight</c>, 0–255).</summary>
    KbdRed,

    /// <summary>Green channel of the RGB keypad button backlight (<c>green:kbd_backlight</c>, 0–255).</summary>
    KbdGreen,

    /// <summary>Blue channel of the RGB keypad button backlight (<c>blue:kbd_backlight</c>, 0–255).</summary>
    KbdBlue
}
