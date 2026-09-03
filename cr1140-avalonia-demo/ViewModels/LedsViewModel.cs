// SPDX-License-Identifier: GPL-3.0-only
using Avalonia.Threading;
using Cr1140.Avalonia.Leds;

namespace Cr1140.AvaloniaDemo.ViewModels;

/// <summary>
/// Live demonstration of the <c>Cr1140.Avalonia.Leds</c> API driving the panel's real
/// hardware: the RGB <b>status light</b> (three binary channels, set via
/// <see cref="LedSysfs.SetTyped"/>) and the RGB <b>keypad button backlight</b> (three PWM
/// channels animated by a <see cref="LedDriver"/> ticked on a timer). F1 cycles the status
/// colour, F2 the keypad colour, F3 the animation mode.
/// </summary>
public sealed class LedsViewModel : ViewModelBase
{
    // Status light: binary channels, so each value is 0/1 (on/off per red/green/blue).
    private static readonly (string Name, string Hex, byte R, byte G, byte B)[] StatusColors =
    {
        ("Off", "#2a2a2a", 0, 0, 0),
        ("Red", "#ff3b30", 1, 0, 0),
        ("Green", "#34c759", 0, 1, 0),
        ("Blue", "#0a84ff", 0, 0, 1),
        ("Yellow", "#ffd60a", 1, 1, 0),
        ("Cyan", "#40c8e0", 0, 1, 1),
        ("Magenta", "#ff2d92", 1, 0, 1),
        ("White", "#f2f2f2", 1, 1, 1),
    };

    // Keypad backlight: PWM channels 0..255, so any colour is a mix.
    private static readonly (string Name, string Hex, byte R, byte G, byte B)[] BacklightColors =
    {
        ("Off", "#2a2a2a", 0, 0, 0),
        ("Red", "#ff3b30", 255, 0, 0),
        ("Green", "#34c759", 0, 255, 0),
        ("Blue", "#0a84ff", 0, 0, 255),
        ("Amber", "#ff9f0a", 255, 90, 0),
        ("Cyan", "#40c8e0", 0, 200, 255),
        ("Magenta", "#ff2d92", 255, 0, 160),
        ("White", "#f2f2f2", 255, 255, 255),
    };

    private static readonly LedMode[] Modes =
    {
        LedMode.Solid, LedMode.Dim, LedMode.Pulse, LedMode.Blink, LedMode.Flash, LedMode.Heartbeat
    };

    private readonly DispatcherTimer _timer;
    private LedDriver _driver = new();

    private int _statusIndex = 2;    // Green — "app running" by default.
    private int _backlightIndex = 4; // Amber.
    private int _modeIndex;          // Solid.

    private string _statusName = "";
    private string _statusHex = "";
    private string _backlightName = "";
    private string _backlightHex = "";
    private string _modeName = "";
    private string _hardwareState = "";

    public LedsViewModel()
    {
        // ~30 Hz so LedMode.Pulse breathes smoothly; the driver writes sysfs only on change.
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(33),
            DispatcherPriority.Background,
            (_, _) => _driver.Tick());

        // Assert the initial state at construction (app start): status light goes green,
        // signalling the app is running even before the LEDs screen is opened.
        ApplyStatus();
        ApplyBacklight();
        ApplyMode();
    }

    /// <summary>Name of the current status-light colour (e.g. <c>"Green"</c>).</summary>
    public string StatusName { get => _statusName; private set => SetField(ref _statusName, value); }

    /// <summary>Hex preview of the current status-light colour (bound to a swatch via a converter).</summary>
    public string StatusHex { get => _statusHex; private set => SetField(ref _statusHex, value); }

    /// <summary>Name of the current keypad-backlight colour.</summary>
    public string BacklightName { get => _backlightName; private set => SetField(ref _backlightName, value); }

    /// <summary>Hex preview of the current keypad-backlight colour.</summary>
    public string BacklightHex { get => _backlightHex; private set => SetField(ref _backlightHex, value); }

    /// <summary>Name of the current keypad-backlight animation mode (e.g. <c>"pulse"</c>).</summary>
    public string ModeName { get => _modeName; private set => SetField(ref _modeName, value); }

    /// <summary>Whether the sysfs LED nodes are writable (hint shown off-device / without permission).</summary>
    public string HardwareState { get => _hardwareState; private set => SetField(ref _hardwareState, value); }

    /// <summary>F1 — advance the status-light colour and write it to the hardware.</summary>
    public void CycleStatus()
    {
        _statusIndex = (_statusIndex + 1) % StatusColors.Length;
        ApplyStatus();
    }

    /// <summary>F2 — advance the keypad-backlight base colour.</summary>
    public void CycleBacklight()
    {
        _backlightIndex = (_backlightIndex + 1) % BacklightColors.Length;
        ApplyBacklight();
    }

    /// <summary>F3 — advance the keypad-backlight animation mode.</summary>
    public void CycleMode()
    {
        _modeIndex = (_modeIndex + 1) % Modes.Length;
        ApplyMode();
    }

    /// <summary>Screen entered: re-assert the LEDs and start ticking the keypad-backlight animation.</summary>
    public void Activate()
    {
        // Fresh driver so the first tick always re-writes the keypad backlight (the previous
        // Deactivate turned it off directly, bypassing the driver's change-detection cache).
        _driver = new LedDriver();
        ApplyStatus();
        ApplyBacklight();
        ApplyMode();
        _timer.Start();
    }

    /// <summary>Screen left: stop the animation and turn the keypad backlight off (status light persists).</summary>
    public void Deactivate()
    {
        _timer.Stop();
        LedSysfs.SetKbdBacklight(0, 0, 0);
    }

    private void ApplyStatus()
    {
        var c = StatusColors[_statusIndex];
        // Non-short-circuit & — attempt all three channels regardless of individual failures.
        bool ok = LedSysfs.SetTyped(Led.StatusRed, c.R)
            & LedSysfs.SetTyped(Led.StatusGreen, c.G)
            & LedSysfs.SetTyped(Led.StatusBlue, c.B);
        StatusName = c.Name;
        StatusHex = c.Hex;
        HardwareState = ok ? "sysfs /sys/class/leds — OK" : "sysfs /sys/class/leds — no write access";
    }

    private void ApplyBacklight()
    {
        var c = BacklightColors[_backlightIndex];
        _driver.SetColor((c.R, c.G, c.B));
        BacklightName = c.Name;
        BacklightHex = c.Hex;
    }

    private void ApplyMode()
    {
        var mode = Modes[_modeIndex];
        _driver.SetMode(mode);
        ModeName = LedAnimation.Name(mode);
    }
}
