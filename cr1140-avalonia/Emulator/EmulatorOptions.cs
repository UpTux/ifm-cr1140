// SPDX-License-Identifier: GPL-3.0-only

using Cr1140.Avalonia.Devices;
using Cr1140.Avalonia.Output;

namespace Cr1140.Avalonia.Emulator;

/// <summary>
/// Configuration options for the CR1140 emulator window.
/// </summary>
public sealed class EmulatorOptions
{
    /// <summary>
    /// Gets or sets the panel width in logical pixels. Default is 800.
    /// </summary>
    public int PanelWidth { get; set; } = 800;

    /// <summary>
    /// Gets or sets the panel height in logical pixels. Default is 480.
    /// </summary>
    public int PanelHeight { get; set; } = 480;

    /// <summary>
    /// Gets or sets the display rotation. Default is None.
    /// </summary>
    public DisplayRotation Rotation { get; set; } = DisplayRotation.None;

    /// <summary>
    /// Gets or sets the window title. Default is "CR1140 Emulator".
    /// </summary>
    public string Title { get; set; } = "CR1140 Emulator";

    /// <summary>
    /// Gets or sets whether to show the on-screen keypad. Default is true.
    /// </summary>
    public bool ShowKeypad { get; set; } = true;

    /// <summary>
    /// Gets or sets whether to show keyboard mapping hints. Default is true.
    /// </summary>
    public bool ShowKeyboardHints { get; set; } = true;

    /// <summary>
    /// Optional provider of a live caption for each keypad key, shown under the fixed
    /// hardware label on the on-screen keypad button. Return the current action label the
    /// key triggers (or <see langword="null"/> for no caption). Polled ~30&#160;Hz, so it
    /// tracks app state such as the current screen and soft-key footer layout. Default
    /// <see langword="null"/> (no captions). <see cref="Input.KeypadKey"/> values that are
    /// not function keys typically return <see langword="null"/>.
    /// </summary>
    public Func<Input.KeypadKey, string?>? KeyCaptions { get; set; }

    /// <summary>
    /// Gets or sets the number of physical function keys the on-screen keypad renders
    /// (F1..F<em>N</em>). Default is 6 (CR1140/CR1141); the CR1102 is 8.
    /// </summary>
    public int FunctionKeyCount { get; set; } = 6;

    /// <summary>
    /// Gets or sets the panel edge the on-screen keypad is drawn along, mirroring the
    /// physical device. <see cref="SoftKeyEdge.Bottom"/> (default) draws a horizontal key
    /// row below the screen (CR1140/CR1141); <see cref="SoftKeyEdge.Right"/> draws a
    /// vertical key column to the right of the screen (CR1102).
    /// </summary>
    public SoftKeyEdge SoftKeyEdge { get; set; } = SoftKeyEdge.Bottom;

    /// <summary>
    /// Creates emulator options configured for a specific device profile. Panel dimensions,
    /// title, and keypad visibility are set from the profile; other options use their defaults.
    /// Rotation can be set afterward via <see cref="Rotation"/>.
    /// </summary>
    /// <param name="profile">The device profile to configure for.</param>
    /// <returns>A new <see cref="EmulatorOptions"/> instance.</returns>
    public static EmulatorOptions ForDevice(DeviceProfile profile)
    {
        return new EmulatorOptions
        {
            PanelWidth = profile.PanelWidth,
            PanelHeight = profile.PanelHeight,
            Title = $"{profile.Name} Emulator",
            ShowKeypad = profile.HasKeypad,
            FunctionKeyCount = profile.FunctionKeyCount,
            SoftKeyEdge = profile.SoftKeyEdge,
        };
    }
}
