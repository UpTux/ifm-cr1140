// SPDX-License-Identifier: GPL-3.0-only

namespace Cr1140.Avalonia.Input;

/// <summary>
/// The managed keypad event surface shared by every CR1140/CR1141 keypad source:
/// the on-device <see cref="EvdevKeypadInput"/> (evdev) and the desktop
/// <c>Cr1140.Avalonia.Emulator.WindowKeypadInput</c> (keyboard + on-screen buttons).
/// </summary>
/// <remarks>
/// An application drives its navigation from these events (marshalling to the UI thread
/// as needed) rather than from Avalonia's routed key events. Depending on this interface
/// — instead of a concrete backend — lets the same view-model run unchanged on the
/// device and in the desktop emulator. The raw down/up pair (<see cref="KeyPressed"/> /
/// <see cref="KeyReleased"/>) plus the four derived gestures mirror the
/// <see cref="KeyGestureDetector"/> output.
/// </remarks>
public interface IKeypadInput
{
    /// <summary>Raised when a mapped key is pressed (key-down).</summary>
    event Action<KeypadKey>? KeyPressed;

    /// <summary>Raised when a mapped key is released (key-up).</summary>
    event Action<KeypadKey>? KeyReleased;

    /// <summary>Raised for a completed short press with no second tap inside the double-tap window.</summary>
    event Action<KeypadKey>? KeyTapped;

    /// <summary>Raised when two taps of the same key complete within the double-tap window.</summary>
    event Action<KeypadKey>? KeyDoubleTapped;

    /// <summary>Raised once when a key has stayed down past the hold threshold.</summary>
    event Action<KeypadKey>? KeyHeld;

    /// <summary>Raised repeatedly (press-and-hold auto-repeat) while a key stays down after <see cref="KeyHeld"/>.</summary>
    event Action<KeypadKey>? KeyHolding;
}
