// SPDX-License-Identifier: GPL-3.0-only
using Cr1140.Avalonia.Devices;

namespace Cr1140.Avalonia.Leds;

/// <summary>
/// Device-agnostic control of the physical keypad button backlight. Resolves the right
/// transport for a <see cref="DeviceProfile"/> with <see cref="For"/> so consumers set the
/// keypad colour the same way on every SKU:
/// <list type="bullet">
/// <item>CR1140/CR1141 — the sysfs <c>*:kbd_backlight</c> PWM LED, driven through
/// <see cref="LedDriver"/>, so it supports the animated <see cref="LedMode"/> curves
/// (<see cref="SupportsAnimation"/> is <see langword="true"/>).</item>
/// <item>CR1102 — the function/nav-key backlights owned by the keyboard MCU over the
/// <c>com.ifm.Keyboard</c> D-Bus interface (see <see cref="IfmKeyboardLeds"/>). The MCU
/// exposes a set-colour call only, so colours are <b>solid</b>
/// (<see cref="SupportsAnimation"/> is <see langword="false"/> and <see cref="SetMode"/> is
/// ignored).</item>
/// </list>
/// </summary>
/// <remarks>
/// Every operation is fail-soft: off-device (no sysfs node / no <c>gdbus</c> / no bus) writes
/// degrade to a no-op, matching <see cref="LedSysfs"/> and <see cref="IfmKeyboardLeds"/>.
/// D-Bus colour writes run fire-and-forget off the UI thread. Call <see cref="Off"/> (or
/// <see cref="Dispose"/>) on teardown so the keys do not stay lit after the app exits — the
/// MCU-owned CR1102 backlights persist independently of the process otherwise.
/// </remarks>
public sealed class KeypadBacklight : IDisposable
{
    // Exactly one transport is non-null on a device that has a keypad backlight; both are
    // null on a SKU that exposes none (every call is then a safe no-op).
    private readonly LedDriver? _sysfs;      // CR1140/CR1141: animated sysfs *:kbd_backlight
    private readonly IfmKeyboardLeds? _dbus; // CR1102: solid D-Bus key backlights
    private (byte R, byte G, byte B) _color;

    private KeypadBacklight(LedDriver? sysfs, IfmKeyboardLeds? dbus)
    {
        _sysfs = sysfs;
        _dbus = dbus;
    }

    /// <summary>
    /// Resolve the keypad backlight for <paramref name="profile"/>: the
    /// <c>com.ifm.Keyboard</c> D-Bus keys when <see cref="DeviceProfile.KeypadBacklightViaDbus"/>
    /// is set (CR1102), otherwise the profile's <see cref="LedRole.KeypadBacklight"/> sysfs LED
    /// (CR1140/CR1141). Returns a no-op instance if the device exposes no keypad backlight.
    /// </summary>
    /// <param name="profile">The active device profile.</param>
    public static KeypadBacklight For(DeviceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        if (profile.KeypadBacklightViaDbus)
            return new KeypadBacklight(null, new IfmKeyboardLeds());

        var led = profile.Leds.FirstOrDefault(l => l.Role == LedRole.KeypadBacklight);
        return new KeypadBacklight(led is null ? null : new LedDriver(led), null);
    }

    /// <summary>Whether this device exposes a keypad button backlight at all.</summary>
    public bool IsPresent => _sysfs is not null || _dbus is not null;

    /// <summary>
    /// Whether the backlight supports animated <see cref="LedMode"/> curves. <see langword="true"/>
    /// for the sysfs PWM path (CR1140/CR1141); <see langword="false"/> for the solid-only D-Bus
    /// path (CR1102), where <see cref="SetMode"/> is ignored.
    /// </summary>
    public bool SupportsAnimation => _sysfs is not null;

    /// <summary>The last requested base colour.</summary>
    public (byte R, byte G, byte B) Color => _color;

    /// <summary>
    /// Set the keypad backlight base colour (0–255 per channel). On the sysfs path the colour
    /// takes effect through the animation curve on the next <see cref="Tick"/>; on the D-Bus
    /// path it is applied immediately (fire-and-forget, solid).
    /// </summary>
    public void SetColor((byte R, byte G, byte B) rgb)
    {
        _color = rgb;
        _sysfs?.SetColor(rgb);
        _dbus?.SetAll(rgb.R, rgb.G, rgb.B);
    }

    /// <summary>
    /// Set the animation <see cref="LedMode"/>. Applies only where <see cref="SupportsAnimation"/>
    /// is <see langword="true"/> (the sysfs PWM path); a no-op on the solid-only D-Bus path.
    /// </summary>
    public void SetMode(LedMode mode) => _sysfs?.SetMode(mode);

    /// <summary>
    /// Advance the animation and write the current instant to the hardware. Call once per frame
    /// (~30 Hz) while the backlight is active. A no-op on the solid D-Bus path (which is written
    /// eagerly by <see cref="SetColor"/>). Returns <see langword="true"/> when nothing needed
    /// writing or the write succeeded.
    /// </summary>
    public bool Tick() => _sysfs?.Tick() ?? true;

    /// <summary>
    /// Turn the keypad backlight off: writes zero on the sysfs path, or issues the keyboard
    /// MCU's <c>ResetLeds</c> on the D-Bus path. Safe to call repeatedly.
    /// </summary>
    public void Off()
    {
        _color = (0, 0, 0);
        if (_sysfs is not null)
        {
            _sysfs.SetMode(LedMode.Solid);
            _sysfs.SetColor((0, 0, 0));
            _sysfs.Tick();
        }
        _dbus?.Reset();
    }

    /// <summary>Turns the backlight off so it does not persist after the owner is gone.</summary>
    public void Dispose() => Off();
}
