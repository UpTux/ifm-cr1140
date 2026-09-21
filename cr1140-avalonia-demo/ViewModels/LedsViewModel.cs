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
public sealed class LedsViewModel : ViewModelBase
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
    private readonly RgbLed _ledB;
    private readonly DispatcherTimer _timer;
    private LedDriver? _driver;
    // CR1102 only: the physical function/nav-key backlights (D-Bus, not sysfs). Null elsewhere.
    private readonly IfmKeyboardLeds? _keyLeds;

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
        _ledB = profile.Leds.Count > 1 ? profile.Leds[1] : throw new InvalidOperationException("Profile needs 2 LEDs");

        // CR1102 drives its key backlights over com.ifm.Keyboard D-Bus (they are not sysfs
        // LEDs); LED B's colour also lights the physical keys. Null on CR1140/CR1141.
        _keyLeds = profile.KeypadBacklightViaDbus ? new IfmKeyboardLeds() : null;

        // ~30 Hz so LedMode.Pulse breathes smoothly; the driver writes sysfs only on change.
        _timer = new DispatcherTimer(
            TimeSpan.FromMilliseconds(33),
            DispatcherPriority.Background,
            (_, _) => _driver?.Tick());

        // Compute LED metadata for display
        LedAName = _ledA.Name.ToUpper();
        LedBName = _keyLeds != null ? "KEYPAD" : _ledB.Name.ToUpper();
        LedASubtitle = $"{_ledA.Name} · {(_ledA.IsBinary ? "binary" : "PWM")} RGB · {_ledA.RedLeaf}";
        LedBSubtitle = _keyLeds != null
            ? "Keypad button LEDs · com.ifm.Keyboard D-Bus · solid"
            : $"{_ledB.Name} · {(_ledB.IsBinary ? "binary" : "PWM")} RGB · {_ledB.RedLeaf}";

        // Assert the initial state at construction (app start): LED A goes green,
        // signalling the app is running even before the LEDs screen is opened.
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
        // Fresh driver so the first tick always re-writes LED B (the previous
        // Deactivate turned it off directly, bypassing the driver's change-detection cache).
        _driver = new LedDriver(_ledB);
        ApplyLedA();
        ApplyLedB();
        ApplyMode();
        _timer.Start();
    }

    /// <summary>Screen left: stop the animation and turn LED B off (LED A persists).</summary>
    public void Deactivate()
    {
        _timer.Stop();
        LedSysfs.SetRgb(_ledB, 0, 0, 0);
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
        _driver?.SetColor((c.R, c.G, c.B));
        // CR1102: also light the physical key backlights (solid) over D-Bus.
        _keyLeds?.SetAll(c.R, c.G, c.B);
        BacklightName = c.Name;
        BacklightHex = c.Hex;
    }

    private void ApplyMode()
    {
        var mode = Modes[_modeIndex];
        _driver?.SetMode(mode);
        ModeName = LedAnimation.Name(mode);
    }

    private void RefreshHardwareState(bool ok)
    {
        HardwareState = ok
            ? "sysfs /sys/class/leds — OK"
            : "sysfs /sys/class/leds — no write access";
    }
}
