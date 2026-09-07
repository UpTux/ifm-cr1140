// SPDX-License-Identifier: GPL-3.0-only

using Avalonia.Controls;

namespace Cr1140.Avalonia.Emulator;

/// <summary>
/// Entry point for hosting a CR1140/CR1141 Avalonia app in the desktop emulator.
/// </summary>
public static class Cr1140Emulator
{
    /// <summary>
    /// Creates an emulator window that hosts the given app root control inside a device bezel,
    /// with an emulated keypad and live hardware state indicators.
    /// Call this from a consuming app's classic desktop lifetime to run the app on a dev machine.
    /// </summary>
    /// <param name="appRoot">The application's root view control.</param>
    /// <param name="keypad">The keypad input handler.</param>
    /// <param name="device">The emulated device hardware state.</param>
    /// <param name="options">Optional configuration; defaults to standard CR1140 settings.</param>
    /// <returns>A configured emulator window ready to show.</returns>
    public static EmulatorWindow BuildWindow(Control appRoot, WindowKeypadInput keypad, EmulatedDevice device, EmulatorOptions? options = null)
        => new EmulatorWindow(appRoot, keypad, device, options ?? new EmulatorOptions());
}
