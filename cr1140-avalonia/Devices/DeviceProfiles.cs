// SPDX-License-Identifier: GPL-3.0-only
using System;
using System.Collections.Generic;
using System.Linq;

namespace Cr1140.Avalonia.Devices;

/// <summary>
/// Built-in device profiles for ifm ecomatDisplay SKUs: <see cref="Cr1140"/>,
/// <see cref="Cr1141"/>, and <see cref="Cr1102"/>. Use <see cref="ByName"/> to resolve
/// a profile from a string (case-insensitive, with or without "CR" prefix), or pick
/// from <see cref="All"/> for enumeration.
/// </summary>
public static class DeviceProfiles
{
    /// <summary>
    /// CR1140 profile: 4.3" keypad-only 800×480 panel (ecomatDisplay/4.3"/STD./E),
    /// NXP i.MX 8M Nano, dual A53, no GPU, 6 function keys + 4-way nav + Enter.
    /// </summary>
    public static readonly DeviceProfile Cr1140 = new()
    {
        Name = "CR1140",
        Article = "ecomatDisplay/4.3\"/STD./E",
        Summary = "4.3\" keypad-only 800×480 panel, i.MX 8M Nano dual A53",
        PanelWidth = 800,
        PanelHeight = 480,
        HasTouch = false,
        HasKeypad = true,
        FunctionKeyCount = 6,
        HasNavKey = true,
        KeypadDevicePath = "/dev/input/event1",
        BacklightNode = Display.Backlight.Default,
        BacklightMaxHint = Display.Backlight.MaxHint,
        SocThermalZone = Telemetry.SystemTelemetry.DefaultSocThermalZone,
        HasStatusLed = true,
        HasKeypadBacklight = true,
        Leds = new[]
        {
            new RgbLed
            {
                Name = "Status", Role = LedRole.Status,
                RedLeaf = "red:status", GreenLeaf = "green:status", BlueLeaf = "blue:status",
                Max = 1,
            },
            new RgbLed
            {
                Name = "Keypad", Role = LedRole.KeypadBacklight,
                RedLeaf = "red:kbd_backlight", GreenLeaf = "green:kbd_backlight", BlueLeaf = "blue:kbd_backlight",
                Max = 255,
            },
        },
    };

    /// <summary>
    /// CR1141 profile: identical hardware to <see cref="Cr1140"/> (4.3" 800×480),
    /// different article label (ecomatDisplay/4.3"/STD./U).
    /// </summary>
    public static readonly DeviceProfile Cr1141 = Cr1140 with
    {
        Name = "CR1141",
        Article = "ecomatDisplay/4.3\"/STD./U",
        Summary = "4.3\" keypad-only 800×480 panel, i.MX 8M Nano dual A53 (variant)",
    };

    /// <summary>
    /// CR1102 profile: 10" PCAP-touchscreen + keypad 1280×800 panel (ecomatDisplay/10"/Touch),
    /// ifm PDM3 platform on a Xilinx Zynq UltraScale+ (ZynqMP) quad-core Cortex-A53 + Mali-400
    /// GPU, 8 RGB backlit function keys (F1–F8) + 1 RGB backlit 4-way nav + Enter, two RGB
    /// indicator LEDs (primary/secondary), Embedded Linux 5.10, CODESYS 3.5 / Qt.
    /// </summary>
    /// <remarks>
    /// Low-level details were confirmed on a live CR1102 (2026-09-21): model
    /// <c>pdm3_10_001-2</c>, kernel 5.10.127. The keypad emits the standard evdev codes
    /// (F1–F8 = 59–66, arrows, Enter = 28) via the <c>ifm_service_keyboard</c> daemon onto
    /// uinput devices named "PDM3 virtual keyboard"; the backlight is <c>a00e0400.panel</c>
    /// (max 255); the two RGB indicator LEDs are <c>a0080000.rgbled:*:pri</c> / <c>*:sec</c>
    /// (PWM, max 255); there is no SoC thermal zone. The KMS display is <c>/dev/dri/card1</c>
    /// (ifm_dc); <c>card0</c> is the lima GPU render node. The output backends are
    /// resolution-agnostic and auto-detect the 1280×800 mode.
    /// </remarks>
    public static readonly DeviceProfile Cr1102 = new()
    {
        Name = "CR1102",
        Article = "ecomatDisplay/10\"/Touch",
        Summary = "10\" PCAP touchscreen + keypad 1280×800 panel, Zynq UltraScale+ quad-core A53 + Mali-400",
        PanelWidth = 1280,
        PanelHeight = 800,
        HasTouch = true,
        HasKeypad = true,
        FunctionKeyCount = 8,
        HasNavKey = true,
        // [live ✓ 2026-09-21] The eight function keys are a vertical column on the right
        // bezel (four keys, nav/OK cluster, four keys) — the footer docks right.
        SoftKeyEdge = SoftKeyEdge.Right,
        // [live ✓ 2026-09-21] PCAP touchscreen is an Atmel maXTouch on /dev/input/event0.
        TouchDevicePath = "/dev/input/event0",
        TouchDeviceName = "Atmel maXTouch Touchscreen",
        // [live ✓ 2026-09-21] Physical keys are injected by the ifm_service_keyboard daemon
        // onto uinput devices named "PDM3 virtual keyboard"; the eventN index is not stable, so
        // EvdevKeypadInput.ForDevice discovers them by KeypadDeviceName. This path is the fallback.
        KeypadDevicePath = "/dev/input/event1",
        KeypadDeviceName = "PDM3 virtual keyboard",
        // [live ✓ 2026-09-21] Backlight node is a00e0400.panel (type raw, max_brightness 255).
        BacklightNode = "a00e0400.panel",
        BacklightMaxHint = 255,
        // [live ✓ 2026-09-21] /sys/class/thermal/ is empty — no SoC thermal zone.
        SocThermalZone = null,
        HasStatusLed = true,
        HasKeypadBacklight = false,
        // [live ✓ 2026-09-21] Key backlights are driven by the keyboard MCU over
        // com.ifm.Keyboard D-Bus (SetLedColor), not sysfs — see Leds.IfmKeyboardLeds.
        KeypadBacklightViaDbus = true,
        // [live ✓ 2026-09-21] SoC-core (rCore0) + mainboard (rBoard) temps come from the IO
        // MCU over com.ifm.Io.Temperature D-Bus — the ZynqMP has no thermal-zone/hwmon node.
        TemperatureViaDbus = true,
        // [live ✓ 2026-09-21] Two PWM RGB indicator LEDs (primary + secondary). The keypad
        // button backlights are driven by the keyboard MCU over D-Bus (com.ifm.Keyboard), not
        // sysfs, so they are not represented here.
        Leds = new[]
        {
            new RgbLed
            {
                Name = "Primary", Role = LedRole.Primary,
                RedLeaf = "a0080000.rgbled:red:pri", GreenLeaf = "a0080000.rgbled:green:pri", BlueLeaf = "a0080000.rgbled:blue:pri",
                Max = 255,
            },
            new RgbLed
            {
                Name = "Secondary", Role = LedRole.Secondary,
                RedLeaf = "a0080000.rgbled:red:sec", GreenLeaf = "a0080000.rgbled:green:sec", BlueLeaf = "a0080000.rgbled:blue:sec",
                Max = 255,
            },
        },
    };

    /// <summary>
    /// All built-in device profiles: <see cref="Cr1140"/>, <see cref="Cr1141"/>,
    /// <see cref="Cr1102"/>.
    /// </summary>
    public static IReadOnlyList<DeviceProfile> All { get; } = new[] { Cr1140, Cr1141, Cr1102 };

    /// <summary>
    /// Resolve a device profile by name. Accepts the SKU name (case-insensitive) with or
    /// without the "CR" prefix: "CR1102", "cr1102", "1102" all resolve to <see cref="Cr1102"/>.
    /// </summary>
    /// <param name="name">The device name to look up.</param>
    /// <returns>The matching <see cref="DeviceProfile"/>, or <see langword="null"/> if
    /// no profile matches.</returns>
    public static DeviceProfile? ByName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;

        // Normalize: strip "CR" prefix, uppercase
        var normalized = name.Trim();
        if (normalized.StartsWith("CR", StringComparison.OrdinalIgnoreCase))
            normalized = normalized.Substring(2);

        return All.FirstOrDefault(p =>
            string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(p.Name, "CR" + normalized, StringComparison.OrdinalIgnoreCase));
    }
}
