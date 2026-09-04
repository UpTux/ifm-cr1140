// SPDX-License-Identifier: GPL-3.0-only
using Cr1140.Avalonia.Display;

namespace Cr1140.AvaloniaDemo.ViewModels;

/// <summary>
/// Live demonstration of the <c>Cr1140.Avalonia.Display</c> API adjusting the panel's
/// real display backlight (<c>/sys/class/backlight/backlight/brightness</c>). Up / F2
/// brighten and Down / F1 dim in fixed steps; the readout reflects the live hardware.
/// </summary>
/// <remarks>
/// A safety floor (<see cref="MinPercent"/>) keeps the panel visible: on a keypad-only
/// SKU there is no touch to recover from a fully dark screen, so brightness never drops
/// to 0. Off-device (or without write access) the writes are no-ops and the VM simply
/// previews the intended value.
/// </remarks>
public sealed class BrightnessViewModel : ViewModelBase
{
    /// <summary>Never dim below this — a keypad-only panel can't be recovered from full black.</summary>
    private const int MinPercent = 10;

    /// <summary>Adjustment granularity per keypress, in percentage points.</summary>
    private const int StepPercent = 10;

    private readonly string _node = Backlight.Default;
    private uint _max = Backlight.MaxHint;

    private int _percent = 100;
    private string _rawText = "";
    private string _hardwareState = "";

    public BrightnessViewModel()
    {
        ReadFromHardware();
    }

    /// <summary>Current backlight level, 0–100 (bound to the big readout and the bar).</summary>
    public int Percent { get => _percent; private set => SetField(ref _percent, value); }

    /// <summary>Raw count readout, e.g. <c>"320 / 400"</c>.</summary>
    public string RawText { get => _rawText; private set => SetField(ref _rawText, value); }

    /// <summary>Whether the sysfs backlight node is writable (hint shown off-device / without permission).</summary>
    public string HardwareState { get => _hardwareState; private set => SetField(ref _hardwareState, value); }

    /// <summary>Screen entered: re-read the live backlight so the bar matches the real hardware.</summary>
    public void Activate() => ReadFromHardware();

    /// <summary>Up / F2 — brighten by one step.</summary>
    public void Increase() => Apply(_percent + StepPercent);

    /// <summary>Down / F1 — dim by one step (never below the visibility floor).</summary>
    public void Decrease() => Apply(_percent - StepPercent);

    private void Apply(int targetPercent)
    {
        int clamped = Math.Clamp(targetPercent, MinPercent, 100);
        bool ok = Backlight.SetPercent(_node, clamped);
        Percent = clamped;
        RefreshReadout(ok);
    }

    private void ReadFromHardware()
    {
        var max = Backlight.Max(_node);
        if (max is > 0)
            _max = max.Value;

        var pct = Backlight.ReadPercent(_node);
        if (pct is not null)
        {
            Percent = Math.Clamp((int)Math.Round(pct.Value), MinPercent, 100);
            RefreshReadout(true);
        }
        else
        {
            // Off-device / unreadable: keep the current preview value and flag no access.
            RefreshReadout(false);
        }
    }

    private void RefreshReadout(bool ok)
    {
        uint raw = (uint)Math.Round(_percent / 100.0 * _max, MidpointRounding.AwayFromZero);
        RawText = $"{raw} / {_max} counts";
        HardwareState = ok
            ? $"/sys/class/backlight/{_node} — OK"
            : $"/sys/class/backlight/{_node} — no access (preview)";
    }
}
