// SPDX-License-Identifier: GPL-3.0-only
using Avalonia.Threading;
using Cr1140.Avalonia.Leds;
using Cr1140.Avalonia.Devices;
using Cr1140.AvaloniaDemo;

namespace Cr1140.AvaloniaDemo.ViewModels;

/// <summary>
/// Live demonstration of the <c>Cr1140.Avalonia.Leds</c> API driving the panel's real
/// RGB LEDs via <see cref="LedSysfs.SetRgb"/>. The screen adapts to the active
/// <see cref="DeviceProfile"/>: CR1140/CR1141 drive the status light and keypad backlight,
/// while CR1102 drives the primary and secondary LEDs. F1 cycles LED A colour, F2 cycles
/// LED B colour, F3 cycles the animation mode.
/// </summary>
public sealed class LedsViewModel : ViewModelBase, IDisposable
{
    // LED A: first profile LED (CR1140 status binary, CR1102 primary PWM).
    private static readonly (string Name, string Hex, byte R, byte G, byte B)[] LedAColors =
    {
        ("Off", "#2a2a2a", 0, 0, 0),
        ("Red", "#ff3b30", 255, 0, 0),
        ("Green", "#34c759", 0, 255, 0),
        ("Blue", "#0a84ff", 0, 0, 255),
        ("Yellow", "#ffd60a", 255, 255, 0),
        ("Cyan", "#40c8e0", 0, 255, 255),
        ("Magenta", "#ff2d92", 255, 0, 255),
        ("White", "#f2f2f2", 255, 255, 255),
    };

    // LED B: second profile LED (CR1140 keypad PWM, CR1102 secondary PWM).
    private static readonly (string Name, string Hex, byte R, byte G, byte B)[] LedBColors =
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

    private readonly RgbLed _ledA;
    private readonly DispatcherTimer _timer;
    // Device-agnostic keypad button backlight: sysfs *:kbd_backlight (animated) on CR1140/CR1141,
    // com.ifm.Keyboard D-Bus (solid) on the CR1102. Resolved from the active device profile.
    private readonly KeypadBacklight _keypad;
    private readonly EventHandler _onProcessExit;

    private int _ledAIndex = 2;      // Green — "app running" by default.
    private int _ledBIndex = 4;      // Amber.
    private int _modeIndex;          // Solid.

    private string _ledAName = "";
    private string _ledBName = "";
    private string _ledASubtitle = "";
    private string _ledBSubtitle = "";
    private string _statusName = "";
    private string _statusHex = "";
    private string _backlightName = "";
    private string _backlightHex = "";
    private string _modeName = "";
    private string _hardwareState = "";

    public LedsViewModel()
    {
        var profile = Program.Profile;
        _ledA = profile.Leds.Count > 0 ? profile.Leds[0] : throw new InvalidOperationException("Profile has no LEDs");

        // The keypad button backlight is device-agnostic: sysfs *:kbd_backlight (animated) on
        // CR1140/CR1141, com.ifm.Keyboard D-Bus (solid) on the CR1102. The facade hides the
        // transport so this screen drives "the keypad backlight" the same way everywhere.
        _keypad = KeypadBacklight.For(profile);

        // Turn the keypad backlight off when the process exits so the MCU-owned CR1102 keys do
        // not stay lit after the app quits (best-effort: runs on normal exit / SIGTERM).
        _onProcessExit = (_, _) => _keypad.Off();
        AppDomain.CurrentDomain.ProcessExit += _onProcessExit;

        // ~30 Hz so LedMode.Pulse breathes smoothly; the driver writes only on change.
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(33),
            DispatcherPriority.Background,
            (_, _) => _keypad.Tick());

        // LED metadata for display.
        var kb = profile.Leds.FirstOrDefault(l => l.Role == LedRole.KeypadBacklight);
        LedAName = _ledA.Name.ToUpper();
        LedBName = "KEYPAD";
        LedASubtitle = $"{_ledA.Name} · {(_ledA.IsBinary ? "binary" : "PWM")} RGB · {_ledA.RedLeaf}";
        LedBSubtitle = profile.KeypadBacklightViaDbus
            ? "Keypad button LEDs · com.ifm.Keyboard D-Bus · solid"
            : kb is not null
                ? $"Keypad backlight · {(kb.IsBinary ? "binary" : "PWM")} RGB · {kb.RedLeaf}"
                : "No keypad backlight on this device";

        // Assert the initial state at construction (app start): LED A goes green, signalling the
        // app is running even before the LEDs screen is opened.
        ApplyLedA();
        ApplyLedB();
        ApplyMode();
    }

    /// <summary>Name of the LED A card header (e.g. <c>"PRIMARY"</c>).</summary>
    public string LedAName { get => _ledAName; private set => SetField(ref _ledAName, value); }

    /// <summary>Name of the LED B card header (e.g. <c>"SECONDARY"</c>).</summary>
    public string LedBName { get => _ledBName; private set => SetField(ref _ledBName, value); }

    /// <summary>Subtitle for LED A card (e.g. <c>"Primary · PWM RGB · a0080000.rgbled:red:pri"</c>).</summary>
    public string LedASubtitle { get => _ledASubtitle; private set => SetField(ref _ledASubtitle, value); }

    /// <summary>Subtitle for LED B card (e.g. <c>"Secondary · PWM RGB · a0080000.rgbled:red:sec"</c>).</summary>
    public string LedBSubtitle { get => _ledBSubtitle; private set => SetField(ref _ledBSubtitle, value); }

    /// <summary>Name of the current LED A colour (e.g. <c>"Green"</c>). Aliased as StatusName for binding compat.</summary>
    public string StatusName { get => _statusName; private set => SetField(ref _statusName, value); }

    /// <summary>Hex preview of the current LED A colour (bound to a swatch via a converter). Aliased as StatusHex.</summary>
    public string StatusHex { get => _statusHex; private set => SetField(ref _statusHex, value); }

    /// <summary>Name of the current LED B colour. Aliased as BacklightName for binding compat.</summary>
    public string BacklightName { get => _backlightName; private set => SetField(ref _backlightName, value); }

    /// <summary>Hex preview of the current LED B colour. Aliased as BacklightHex.</summary>
    public string BacklightHex { get => _backlightHex; private set => SetField(ref _backlightHex, value); }

    /// <summary>Name of the current LED B animation mode (e.g. <c>"pulse"</c>).</summary>
    public string ModeName { get => _modeName; private set => SetField(ref _modeName, value); }

    /// <summary>Whether the sysfs LED nodes are writable (hint shown off-device / without permission).</summary>
    public string HardwareState { get => _hardwareState; private set => SetField(ref _hardwareState, value); }

    /// <summary>F1 — advance LED A colour and write it to the hardware.</summary>
    public void CycleStatus()
    {
        _ledAIndex = (_ledAIndex + 1) % LedAColors.Length;
        ApplyLedA();
    }

    /// <summary>F2 — advance LED B base colour.</summary>
    public void CycleBacklight()
    {
        _ledBIndex = (_ledBIndex + 1) % LedBColors.Length;
        ApplyLedB();
    }

    /// <summary>F3 — advance LED B animation mode.</summary>
    public void CycleMode()
    {
        _modeIndex = (_modeIndex + 1) % Modes.Length;
        ApplyMode();
    }

    /// <summary>Screen entered: re-assert the LEDs and start ticking the LED B animation.</summary>
    public void Activate()
    {
        // Re-assert LED B and its mode on entry; the keypad backlight facade persists across
        // screen switches (Deactivate turned it off), so the next tick re-writes the colour.
        ApplyLedA();
        ApplyLedB();
        ApplyMode();
        _timer.Start();
    }

    /// <summary>Screen left: stop the animation and turn the keypad backlight off (LED A persists).</summary>
    public void Deactivate()
    {
        _timer.Stop();
        _keypad.Off();
    }

    private void ApplyLedA()
    {
        var c = LedAColors[_ledAIndex];
        bool ok = LedSysfs.SetRgb(_ledA, c.R, c.G, c.B);
        StatusName = c.Name;
        StatusHex = c.Hex;
        RefreshHardwareState(ok);
    }

    private void ApplyLedB()
    {
        var c = LedBColors[_ledBIndex];
        _keypad.SetColor((c.R, c.G, c.B));
        BacklightName = c.Name;
        BacklightHex = c.Hex;
    }

    private void ApplyMode()
    {
        var mode = Modes[_modeIndex];
        _keypad.SetMode(mode);
        ModeName = _keypad.SupportsAnimation ? LedAnimation.Name(mode) : "solid (fixed)";
    }

    private void RefreshHardwareState(bool ok)
    {
        HardwareState = ok
            ? "sysfs /sys/class/leds — OK"
            : "sysfs /sys/class/leds — no write access";
    }

    /// <summary>Unhooks the process-exit reset and turns the keypad backlight off.</summary>
    public void Dispose()
    {
        AppDomain.CurrentDomain.ProcessExit -= _onProcessExit;
        _timer.Stop();
        _keypad.Off();
    }
}
