// SPDX-License-Identifier: GPL-3.0-only
using System.Diagnostics;
using System.Globalization;

namespace Cr1140.Avalonia.Leds;

/// <summary>
/// Controls the CR1102's physical function/navigation-key backlights. Unlike the
/// CR1140/CR1141 keypad backlight (a sysfs <c>*:kbd_backlight</c> LED driven by
/// <see cref="LedSysfs"/>), the CR1102's key LEDs are owned by the keyboard MCU and driven
/// over the <c>com.ifm.Keyboard</c> D-Bus interface. This class wraps that interface's
/// <c>SetLedColor</c> / <c>ResetLeds</c> methods (with <c>GetHardwareConfig</c> used only as a
/// service-reachable probe), invoked through the platform <c>gdbus</c> CLI on the system bus.
/// </summary>
/// <remarks>
/// <para>
/// Every call degrades to a safe no-op off-device (no <c>gdbus</c> on <c>PATH</c>, or no
/// system bus / service) — the same fail-soft contract as <see cref="LedSysfs"/> and
/// <see cref="Cr1140.Avalonia.Display.Backlight"/>. Color writes run on a background thread
/// (fire-and-forget), so they never block the UI thread.
/// </para>
/// <para>
/// Confirmed on a live CR1102 (2026-09-21): the keypad MCU exposes <b>12 backlight LEDs</b>
/// addressed by <b>LED ID</b> — the F1–F8 function keys are IDs 0–7 and the navigation
/// (D-pad) cluster is IDs 8–11 (the MCU rejects any ID outside <c>[0,11]</c>). These
/// <b>LED IDs are distinct from the evdev input key IDs</b>: <c>GetHardwareConfig(0)</c> /
/// <c>GetMapping(0)</c> report the nav <i>input</i> keys as 11–15
/// (Up/Down/Left/Right/Enter), but the nav <i>LEDs</i> are 8–11 — so the input key IDs must
/// <b>not</b> be used to address LEDs (IDs 12–15 are invalid and abort the whole call, which
/// is why an earlier revision left the D-pad dark). The color argument is packed
/// <c>0x00RRGGBB</c>. Colors are <b>solid</b> — the MCU exposes a set-color call, not a
/// per-frame animation surface, so this API sets colors rather than animating them (contrast
/// <see cref="LedDriver"/>, which animates a sysfs LED).
/// </para>
/// </remarks>
public sealed class IfmKeyboardLeds
{
    private const string Service = "com.ifm.Keyboard";
    private const string ObjectPath = "/com/ifm/Keyboard";
    private const string Interface = "com.ifm.Keyboard";

    /// <summary>The CR1102 keypad's 12 backlight LED IDs (keyboard 0): function keys F1–F8 =
    /// 0–7, navigation (D-pad) cluster = 8–11 (live CR1102, 2026-09-21; the MCU's valid
    /// LED-ID range is <c>[0,11]</c>). These are <b>LED IDs</b>, not evdev input key IDs — the
    /// nav <i>input</i> keys are 11–15, but the nav <i>LEDs</i> are 8–11.</summary>
    private static readonly uint[] LedIds = { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };

    private readonly uint _keyboard;
    private readonly object _gate = new();
    private bool _probed;
    private bool _available;

    /// <summary>Create a controller for the given keyboard id (0 = the operator keypad).</summary>
    /// <param name="keyboard">The <c>com.ifm.Keyboard</c> keyboard id; 0 on the CR1102 keypad.</param>
    public IfmKeyboardLeds(uint keyboard = 0) => _keyboard = keyboard;

    /// <summary>Whether the keyboard D-Bus service answered a hardware-config probe (false off-device).</summary>
    public bool Available
    {
        get { EnsureProbed(); return _available; }
    }

    /// <summary>The backlight LED IDs the color methods address: function keys 0–7 + nav (D-pad) 8–11.</summary>
    public IReadOnlyList<uint> LedIdList => LedIds;

    /// <summary>
    /// Set every key backlight (function keys + D-pad) to one solid RGB color. Fire-and-forget:
    /// the D-Bus call runs on a background thread and any failure is swallowed (off-device no-op).
    /// </summary>
    public void SetAll(byte r, byte g, byte b)
    {
        uint packed = (uint)((r << 16) | (g << 8) | b);
        Post(() =>
        {
            var idList = "[" + string.Join(",", LedIds) + "]";
            Invoke("SetLedColor",
                _keyboard.ToString(CultureInfo.InvariantCulture),
                idList,
                packed.ToString(CultureInfo.InvariantCulture));
        });
    }

    /// <summary>Turn every key backlight off (the daemon's <c>ResetLeds</c>). Fire-and-forget.</summary>
    public void Reset() => Post(() => Invoke("ResetLeds"));

    private static void Post(Action work) =>
        Task.Run(() => { try { work(); } catch { /* best-effort hardware write */ } });

    private void EnsureProbed()
    {
        lock (_gate)
        {
            if (_probed)
                return;
            _probed = true;

            // GetHardwareConfig is used purely as a "service reachable" probe. Its result is
            // the evdev *input* key IDs (0–7, 11–15), which are NOT the *LED* IDs this class
            // drives (0–11), so the returned array is intentionally discarded.
            var (ok, _) = Run("GetHardwareConfig", _keyboard.ToString(CultureInfo.InvariantCulture));
            _available = ok;
        }
    }

    private void Invoke(string method, params string[] args)
    {
        var argv = new string[args.Length + 1];
        argv[0] = method;
        Array.Copy(args, 0, argv, 1, args.Length);
        Run(argv);
    }

    private static (bool ok, string output) Run(params string[] methodAndArgs)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "gdbus",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };
            psi.ArgumentList.Add("call");
            psi.ArgumentList.Add("--system");
            psi.ArgumentList.Add("--dest");
            psi.ArgumentList.Add(Service);
            psi.ArgumentList.Add("--object-path");
            psi.ArgumentList.Add(ObjectPath);
            psi.ArgumentList.Add("--method");
            psi.ArgumentList.Add(Interface + "." + methodAndArgs[0]);
            for (int i = 1; i < methodAndArgs.Length; i++)
                psi.ArgumentList.Add(methodAndArgs[i]);

            using var p = Process.Start(psi);
            if (p is null)
                return (false, string.Empty);

            string stdout = p.StandardOutput.ReadToEnd();
            _ = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(3000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                return (false, string.Empty);
            }
            return (p.ExitCode == 0, stdout);
        }
        catch
        {
            // gdbus missing / not permitted / no bus — treat as off-device no-op.
            return (false, string.Empty);
        }
    }
}
