// SPDX-License-Identifier: GPL-3.0-only
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Cr1140.Avalonia.Input;

namespace Cr1140.Avalonia.Diagnostics;

/// <summary>
/// Extension methods for attaching a <see cref="PerfOverlay"/> to an Avalonia
/// <see cref="TopLevel"/> control.
/// </summary>
public static class PerfOverlayExtensions
{
    /// <summary>
    /// Create a <see cref="PerfOverlay"/> and attach it to this <see cref="TopLevel"/>'s
    /// overlay layer. If <paramref name="options"/> specifies a
    /// <see cref="PerfOverlayOptions.ToggleKeypad"/>, the chosen gesture on the chosen
    /// key will toggle overlay visibility.
    /// </summary>
    /// <param name="topLevel">
    /// The window or top-level control to attach the overlay to (e.g. the main window).
    /// </param>
    /// <param name="recorder">
    /// The <see cref="FrameStatsRecorder"/> sampling frame timing from the output backend.
    /// </param>
    /// <param name="options">
    /// Display and toggle options, or <c>null</c> for defaults.
    /// </param>
    /// <returns>The created overlay control, now attached to the overlay layer.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when <paramref name="topLevel"/> has no <see cref="OverlayLayer"/>.
    /// </exception>
    public static PerfOverlay AttachPerfOverlay(
        this TopLevel topLevel,
        FrameStatsRecorder recorder,
        PerfOverlayOptions? options = null)
    {
        var overlay = new PerfOverlay(recorder, options);
        var layer = OverlayLayer.GetOverlayLayer(topLevel);
        if (layer is null)
        {
            throw new InvalidOperationException("No OverlayLayer on this TopLevel.");
        }

        layer.Children.Add(overlay);

        // Wire up keypad toggle if configured
        if (options?.ToggleKeypad is IKeypadInput keypad)
        {
            var toggleKey = options.ToggleKey;
            Action<KeypadKey> toggleHandler = key =>
            {
                if (key == toggleKey)
                {
                    Dispatcher.UIThread.Post(() => overlay.IsVisible = !overlay.IsVisible);
                }
            };

            switch (options.ToggleGesture)
            {
                case PerfOverlayToggleGesture.Tapped:
                    keypad.KeyTapped += toggleHandler;
                    break;
                case PerfOverlayToggleGesture.DoubleTapped:
                    keypad.KeyDoubleTapped += toggleHandler;
                    break;
                case PerfOverlayToggleGesture.Held:
                    keypad.KeyHeld += toggleHandler;
                    break;
            }
        }

        return overlay;
    }
}
