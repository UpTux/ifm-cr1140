namespace Cr1140.Avalonia.Input;

/// <summary>
/// A physical key on the ifm CR1140/CR1141 keypad, decoded from evdev
/// <c>KEY_*</c> codes by <see cref="EvdevKeypadInput"/>.
/// </summary>
public enum KeypadKey
{
    /// <summary>Soft-key F1 (evdev <c>KEY_F1</c>, code 59).</summary>
    F1,

    /// <summary>Soft-key F2 (evdev <c>KEY_F2</c>, code 60).</summary>
    F2,

    /// <summary>Soft-key F3 (evdev <c>KEY_F3</c>, code 61).</summary>
    F3,

    /// <summary>Soft-key F4 (evdev <c>KEY_F4</c>, code 62).</summary>
    F4,

    /// <summary>Soft-key F5 (evdev <c>KEY_F5</c>, code 63).</summary>
    F5,

    /// <summary>Soft-key F6 (evdev <c>KEY_F6</c>, code 64).</summary>
    F6,

    /// <summary>Up arrow (evdev <c>KEY_UP</c>, code 103).</summary>
    Up,

    /// <summary>Down arrow (evdev <c>KEY_DOWN</c>, code 108).</summary>
    Down,

    /// <summary>Left arrow (evdev <c>KEY_LEFT</c>, code 105).</summary>
    Left,

    /// <summary>Right arrow (evdev <c>KEY_RIGHT</c>, code 106).</summary>
    Right,

    /// <summary>Enter / OK (evdev <c>KEY_ENTER</c>, code 28).</summary>
    Enter
}
