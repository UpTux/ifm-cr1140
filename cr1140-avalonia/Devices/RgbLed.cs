// SPDX-License-Identifier: GPL-3.0-only

namespace Cr1140.Avalonia.Devices;

/// <summary>
/// The semantic role a <see cref="RgbLed"/> plays on the panel, so consuming UI can
/// present it appropriately (e.g. draw a status dot vs. tint the keypad buttons).
/// </summary>
public enum LedRole
{
    /// <summary>An RGB status/indicator light (e.g. the CR1140/CR1141 binary <c>*:status</c> LED).</summary>
    Status,

    /// <summary>The RGB backlight behind the keypad buttons (e.g. the CR1140/CR1141 <c>*:kbd_backlight</c>).</summary>
    KeypadBacklight,

    /// <summary>The primary RGB indicator LED (e.g. the CR1102's <c>*:pri</c> light).</summary>
    Primary,

    /// <summary>The secondary RGB indicator LED (e.g. the CR1102's <c>*:sec</c> light).</summary>
    Secondary,
}

/// <summary>
/// One tri-colour LED exposed by the kernel as three per-channel <c>/sys/class/leds/</c>
/// nodes. A stable, per-device description of an RGB LED's sysfs leaf names, its
/// <see cref="Max"/> brightness (1 for binary on/off LEDs, 255 for 8-bit PWM), and its
/// semantic <see cref="Role"/>. Consumed by <see cref="Leds.LedSysfs.SetRgb"/> /
/// <see cref="Leds.LedSysfs.ReadRgb"/> and <see cref="Leds.LedDriver"/>, and carried by
/// <see cref="DeviceProfile.Leds"/> so the LED layout is device-specific data, not code.
/// </summary>
/// <remarks>
/// The three channels map to <c>/sys/class/leds/&lt;RedLeaf&gt;/brightness</c> etc. The
/// CR1140/CR1141 has a binary <c>*:status</c> RGB light (<see cref="Max"/> == 1) plus a
/// PWM <c>*:kbd_backlight</c> RGB light (<see cref="Max"/> == 255); the CR1102 has two PWM
/// RGB lights (<c>a0080000.rgbled:*:pri</c> and <c>*:sec</c>, both <see cref="Max"/> == 255).
/// </remarks>
public sealed record RgbLed
{
    /// <summary>A short human-readable name for the LED (e.g. "Status", "Keypad", "Primary", "Secondary").</summary>
    public required string Name { get; init; }

    /// <summary>The LED's semantic role, for presentation.</summary>
    public required LedRole Role { get; init; }

    /// <summary>The sysfs leaf name of the red channel under <c>/sys/class/leds/</c>.</summary>
    public required string RedLeaf { get; init; }

    /// <summary>The sysfs leaf name of the green channel under <c>/sys/class/leds/</c>.</summary>
    public required string GreenLeaf { get; init; }

    /// <summary>The sysfs leaf name of the blue channel under <c>/sys/class/leds/</c>.</summary>
    public required string BlueLeaf { get; init; }

    /// <summary>
    /// The per-channel <c>max_brightness</c>: <c>1</c> for a binary on/off LED, <c>255</c>
    /// for an 8-bit PWM LED. Used to scale 0–255 colour channels onto the hardware range.
    /// </summary>
    public required uint Max { get; init; }

    /// <summary>Whether this LED is binary (on/off) rather than a PWM ramp (<see cref="Max"/> &lt;= 1).</summary>
    public bool IsBinary => Max <= 1;

    /// <summary>The three channel leaf names in red, green, blue order.</summary>
    public IReadOnlyList<string> Leaves => new[] { RedLeaf, GreenLeaf, BlueLeaf };
}
