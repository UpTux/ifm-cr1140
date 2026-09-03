// SPDX-License-Identifier: GPL-3.0-only
using Cr1140.Avalonia.Input;
using Cr1140.Avalonia.Telemetry;

namespace Cr1140.Avalonia.Diagnostics;

/// <summary>
/// Placement corner for the <see cref="PerfOverlay"/> on the screen.
/// </summary>
public enum PerfOverlayCorner
{
    /// <summary>Top-left corner.</summary>
    TopLeft,

    /// <summary>Top-right corner.</summary>
    TopRight,

    /// <summary>Bottom-left corner.</summary>
    BottomLeft,

    /// <summary>Bottom-right corner.</summary>
    BottomRight
}

/// <summary>
/// The keypad gesture that toggles <see cref="PerfOverlay"/> visibility when
/// <see cref="PerfOverlayOptions.ToggleKeypad"/> is configured.
/// </summary>
public enum PerfOverlayToggleGesture
{
    /// <summary>Single tap (short press).</summary>
    Tapped,

    /// <summary>Double tap.</summary>
    DoubleTapped,

    /// <summary>Long press (hold).</summary>
    Held
}

/// <summary>
/// Configuration options for the <see cref="PerfOverlay"/> HUD control.
/// </summary>
/// <remarks>
/// Create an instance, set desired properties, and pass it to the
/// <see cref="PerfOverlay"/> constructor or
/// <see cref="PerfOverlayExtensions.AttachPerfOverlay"/> extension.
/// Every field is optional and defaults to a sensible value for an embedded
/// operator panel.
/// </remarks>
public sealed class PerfOverlayOptions
{
    /// <summary>
    /// Whether to show the system information block (CPU model, OS, memory, temperatures).
    /// Default <c>true</c>.
    /// </summary>
    public bool ShowSystemInfo { get; set; } = true;

    /// <summary>
    /// The <see cref="SystemTelemetry"/> instance to sample for live system metrics
    /// (CPU %, memory %, temperatures, load, uptime). If <c>null</c> and
    /// <see cref="ShowSystemInfo"/> is <c>true</c>, a new instance is created
    /// automatically. Default <c>null</c>.
    /// </summary>
    public SystemTelemetry? Telemetry { get; set; }

    /// <summary>
    /// Screen corner where the overlay is positioned. Default
    /// <see cref="PerfOverlayCorner.TopRight"/>.
    /// </summary>
    public PerfOverlayCorner Corner { get; set; } = PerfOverlayCorner.TopRight;

    /// <summary>
    /// Font size multiplier applied to all text in the overlay. Use values like
    /// <c>0.8</c> (smaller) or <c>1.2</c> (larger) to scale the HUD for different
    /// panel resolutions. Default <c>1.0</c>.
    /// </summary>
    public double FontScale { get; set; } = 1.0;

    /// <summary>
    /// How often to refresh the numeric snapshot and system-info text. This throttles
    /// expensive sampling and string formatting independently of the graph redraw rate.
    /// Default <c>500ms</c>.
    /// </summary>
    public TimeSpan NumericUpdateInterval { get; set; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// How often to invalidate and redraw the overlay. Drives the graph update rate.
    /// Set to <see cref="TimeSpan.Zero"/> or negative to disable the redraw timer
    /// (manual invalidation only). Default <c>16ms</c> (~60 Hz).
    /// </summary>
    public TimeSpan RedrawInterval { get; set; } = TimeSpan.FromMilliseconds(16);

    /// <summary>
    /// Whether the overlay is visible on first attach. When <c>false</c> (default),
    /// the HUD starts hidden and must be toggled via keypad or programmatically.
    /// Default <c>false</c>.
    /// </summary>
    public bool StartVisible { get; set; }

    /// <summary>
    /// The optional <see cref="EvdevKeypadInput"/> instance whose gesture events drive
    /// visibility toggling. If <c>null</c>, no keypad toggle is wired. Default <c>null</c>.
    /// </summary>
    public EvdevKeypadInput? ToggleKeypad { get; set; }

    /// <summary>
    /// The physical key on the keypad that toggles overlay visibility. Only used when
    /// <see cref="ToggleKeypad"/> is non-<c>null</c>. Default
    /// <see cref="KeypadKey.F5"/>.
    /// </summary>
    public KeypadKey ToggleKey { get; set; } = KeypadKey.F5;

    /// <summary>
    /// The gesture on <see cref="ToggleKey"/> that triggers the visibility toggle.
    /// Only used when <see cref="ToggleKeypad"/> is non-<c>null</c>. Default
    /// <see cref="PerfOverlayToggleGesture.DoubleTapped"/>.
    /// </summary>
    public PerfOverlayToggleGesture ToggleGesture { get; set; } = PerfOverlayToggleGesture.DoubleTapped;
}
