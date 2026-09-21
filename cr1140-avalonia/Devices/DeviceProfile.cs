// SPDX-License-Identifier: GPL-3.0-only

namespace Cr1140.Avalonia.Devices;

/// <summary>
/// Device-specific hardware profile: panel dimensions, input capabilities, and low-level
/// sysfs/evdev node hints for a single ifm ecomatDisplay SKU. A stable single source of
/// truth for per-device constants consumed by emulator factories, input backends, and
/// on-device drivers. See <see cref="DeviceProfiles"/> for the built-in CR1140 / CR1141 /
/// CR1102 profiles.
/// </summary>
/// <remarks>
/// The constants in a profile are <b>hints and defaults</b>: the real output backends
/// (<see cref="Output.RotatingFbdevOutput"/> and <see cref="Output.RotatingDrmOutput"/>)
/// read the panel mode from DRM/fbdev at runtime, so the library is resolution-agnostic.
/// Similarly, <see cref="Display.Backlight.Max"/> reads the real <c>max_brightness</c>,
/// and <see cref="Telemetry.SystemTelemetry.Sample"/> reads the actual thermal zone.
/// The profile documents and centralizes per-device defaults; it does not override
/// runtime reads.
/// </remarks>
public sealed record DeviceProfile
{
    /// <summary>
    /// The device SKU name (e.g. "CR1140", "CR1141", "CR1102").
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The ecomatDisplay article label printed on the device (e.g. "ecomatDisplay/4.3\"/STD./E").
    /// </summary>
    public required string Article { get; init; }

    /// <summary>
    /// A one-line human-readable description summarizing the device's key characteristics.
    /// </summary>
    public required string Summary { get; init; }

    /// <summary>
    /// The native panel width in pixels, measured in landscape orientation.
    /// </summary>
    public required int PanelWidth { get; init; }

    /// <summary>
    /// The native panel height in pixels, measured in landscape orientation.
    /// </summary>
    public required int PanelHeight { get; init; }

    /// <summary>
    /// Whether the device has a PCAP touchscreen.
    /// </summary>
    public required bool HasTouch { get; init; }

    /// <summary>
    /// The evdev device node path for the touchscreen (e.g. <c>/dev/input/event0</c>), or
    /// <see langword="null"/> on non-touch devices. Used as the fallback when
    /// <see cref="TouchDeviceName"/> discovery finds nothing.
    /// </summary>
    public string? TouchDevicePath { get; init; }

    /// <summary>
    /// An optional evdev device <b>name</b> (as reported at
    /// <c>/sys/class/input/eventN/device/name</c>) used to auto-discover the touchscreen
    /// node. On the CR1102 the PCAP touchscreen reports <c>"Atmel maXTouch Touchscreen"</c>.
    /// <see langword="null"/> on non-touch devices.
    /// </summary>
    public string? TouchDeviceName { get; init; }

    /// <summary>
    /// Whether the device has physical keypad buttons (function keys and/or navigation key).
    /// </summary>
    public required bool HasKeypad { get; init; }

    /// <summary>
    /// The number of dedicated function keys: 6 (F1..F6) on the CR1140/CR1141, 8 (F1..F8) on the CR1102.
    /// </summary>
    public required int FunctionKeyCount { get; init; }

    /// <summary>
    /// Whether the device has a 4-way navigation key plus Enter.
    /// </summary>
    public required bool HasNavKey { get; init; }

    /// <summary>
    /// The panel edge along which the physical function keys are arranged, in native
    /// landscape orientation. The CR1140/CR1141 keys run along the
    /// <see cref="SoftKeyEdge.Bottom"/> (horizontal footer); the CR1102's eight keys are a
    /// vertical column on the <see cref="SoftKeyEdge.Right"/>. The demo shell docks its
    /// <see cref="Controls.SoftKeyFooter"/> on this edge so each label sits by its button.
    /// Defaults to <see cref="SoftKeyEdge.Bottom"/>.
    /// </summary>
    public SoftKeyEdge SoftKeyEdge { get; init; } = SoftKeyEdge.Bottom;

    /// <summary>
    /// The evdev device node path for the gpio-keys keypad (e.g. <c>/dev/input/event1</c>).
    /// </summary>
    public required string KeypadDevicePath { get; init; }

    /// <summary>
    /// An optional evdev device <b>name</b> (as reported at
    /// <c>/sys/class/input/eventN/device/name</c>) used to auto-discover the keypad node
    /// when the numeric <see cref="KeypadDevicePath"/> is not stable. On the CR1102 the
    /// physical keys are injected by the <c>ifm_service_keyboard</c> daemon onto uinput
    /// devices named <c>"PDM3 virtual keyboard"</c>, whose <c>eventN</c> index is not
    /// guaranteed, so <see cref="Input.EvdevKeypadInput.ForDevice"/> discovers them by
    /// this name. <see langword="null"/> (the CR1140/CR1141 default) means "use
    /// <see cref="KeypadDevicePath"/> directly".
    /// </summary>
    public string? KeypadDeviceName { get; init; }

    /// <summary>
    /// The backlight node name under <c>/sys/class/backlight/</c> (e.g. "backlight").
    /// </summary>
    public required string BacklightNode { get; init; }

    /// <summary>
    /// A hint for the backlight's <c>max_brightness</c> value. Prefer reading it at runtime
    /// with <see cref="Display.Backlight.Max"/>; this is a fallback for off-device previews.
    /// </summary>
    public required uint BacklightMaxHint { get; init; }

    /// <summary>
    /// The thermal zone index (N in <c>/sys/class/thermal/thermal_zoneN</c>) for the SoC
    /// temperature sensor, or <see langword="null"/> if the device exposes no SoC thermal
    /// zone (e.g. the CR1102, whose <c>/sys/class/thermal/</c> is empty). When
    /// <see langword="null"/>, <see cref="Telemetry.SystemTelemetry"/> reports no SoC
    /// temperature rather than reading a non-existent zone.
    /// </summary>
    public required uint? SocThermalZone { get; init; }

    /// <summary>
    /// Whether the device has an RGB status LED.
    /// </summary>
    public required bool HasStatusLed { get; init; }

    /// <summary>
    /// Whether the device has RGB backlit keypad buttons.
    /// </summary>
    public required bool HasKeypadBacklight { get; init; }

    /// <summary>
    /// Whether the keypad button backlights are driven over the <c>com.ifm.Keyboard</c>
    /// D-Bus interface (the keyboard MCU) rather than sysfs. <see langword="true"/> on the
    /// CR1102, whose function/nav-key LEDs are <b>not</b> exposed under
    /// <c>/sys/class/leds/</c>; consumers drive them via
    /// <see cref="Leds.IfmKeyboardLeds"/>. <see langword="false"/> (default) on the
    /// CR1140/CR1141, whose keypad backlight is the sysfs <c>*:kbd_backlight</c> LED.
    /// </summary>
    public bool KeypadBacklightViaDbus { get; init; }

    /// <summary>
    /// Whether the SoC-core and mainboard temperatures are read over the
    /// <c>com.ifm.Io.Temperature</c> D-Bus interface (the <c>ifm_service_io</c> MCU) rather
    /// than from <c>/sys/class/thermal</c> + hwmon. <see langword="true"/> on the CR1102, whose
    /// ZynqMP exposes no thermal-zone/hwmon node (its <c>rCore0</c>/<c>rBoard</c> come from the
    /// IO MCU); consumers read them via <see cref="Telemetry.IfmSystemTemperatures"/>.
    /// <see langword="false"/> (default) on the CR1140/CR1141, which use thermal zone 0 + an
    /// <c>lm75</c> hwmon sensor.
    /// </summary>
    public bool TemperatureViaDbus { get; init; }

    /// <summary>
    /// The RGB LEDs this device exposes under <c>/sys/class/leds/</c>, in a stable order.
    /// The CR1140/CR1141 has a binary <c>*:status</c> light plus a PWM <c>*:kbd_backlight</c>;
    /// the CR1102 has two PWM lights (<c>a0080000.rgbled:*:pri</c> and <c>*:sec</c>). Consumed
    /// by <see cref="Leds.LedSysfs.SetRgb"/> / <see cref="Leds.LedSysfs.ReadRgb"/> and the demo
    /// LEDs screen so LED addressing is per-device data rather than hard-coded leaf names.
    /// </summary>
    public required IReadOnlyList<RgbLed> Leds { get; init; }
}
