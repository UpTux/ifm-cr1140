// SPDX-License-Identifier: GPL-3.0-only

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
}
