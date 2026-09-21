// SPDX-License-Identifier: GPL-3.0-only

using System;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Cr1140.Avalonia.Input;

namespace Cr1140.Avalonia.Emulator;

/// <summary>
/// Desktop window keypad input implementation that translates physical keyboard and programmatic button presses into keypad events.
/// </summary>
public sealed class WindowKeypadInput : IKeypadInput, IDisposable
{
    private const int TickIntervalMs = 25;
    
    private readonly KeyGestureDetector _gestures;
    private readonly bool[] _keysDown;
    private DispatcherTimer? _tickTimer;
    private EventHandler<KeyEventArgs>? _keyDownHandler;
    private EventHandler<KeyEventArgs>? _keyUpHandler;

    /// <summary>
    /// Initializes a new instance of the <see cref="WindowKeypadInput"/> class.
    /// </summary>
    /// <param name="gestureOptions">Optional gesture detection options.</param>
    public WindowKeypadInput(KeyGestureOptions? gestureOptions = null)
    {
        _gestures = new KeyGestureDetector(gestureOptions);
        _keysDown = new bool[Enum.GetValues<KeypadKey>().Length];
        
        _gestures.Tapped += k => KeyTapped?.Invoke(k);
        _gestures.DoubleTapped += k => KeyDoubleTapped?.Invoke(k);
        _gestures.Held += k => KeyHeld?.Invoke(k);
        _gestures.Holding += k => KeyHolding?.Invoke(k);
    }

    /// <inheritdoc/>
    public event Action<KeypadKey>? KeyPressed;
    
    /// <inheritdoc/>
    public event Action<KeypadKey>? KeyReleased;
    
    /// <inheritdoc/>
    public event Action<KeypadKey>? KeyTapped;
    
    /// <inheritdoc/>
    public event Action<KeypadKey>? KeyDoubleTapped;
    
    /// <inheritdoc/>
    public event Action<KeypadKey>? KeyHeld;
    
    /// <inheritdoc/>
    public event Action<KeypadKey>? KeyHolding;

    /// <summary>
    /// Programmatically presses a keypad key.
    /// </summary>
    /// <param name="key">The key to press.</param>
    public void Press(KeypadKey key)
    {
        int idx = (int)key;
        if (_keysDown[idx])
            return;
        
        _keysDown[idx] = true;
        long now = Environment.TickCount64;
        KeyPressed?.Invoke(key);
        _gestures.Down(key, now);
        EnsureTicking();
    }

    /// <summary>
    /// Programmatically releases a keypad key.
    /// </summary>
    /// <param name="key">The key to release.</param>
    public void Release(KeypadKey key)
    {
        int idx = (int)key;
        if (!_keysDown[idx])
            return;
        
        _keysDown[idx] = false;
        long now = Environment.TickCount64;
        KeyReleased?.Invoke(key);
        _gestures.Up(key, now);
    }

    /// <summary>
    /// Attaches keyboard event handlers to the specified input element.
    /// </summary>
    /// <param name="element">The input element to attach to.</param>
    public void Attach(InputElement element)
    {
        _keyDownHandler = OnKeyDown;
        _keyUpHandler = OnKeyUp;
        
        element.AddHandler(
            InputElement.KeyDownEvent,
            _keyDownHandler,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
        
        element.AddHandler(
            InputElement.KeyUpEvent,
            _keyUpHandler,
            RoutingStrategies.Tunnel | RoutingStrategies.Bubble,
            handledEventsToo: true);
    }

    /// <summary>
    /// Maps an Avalonia keyboard key to a keypad key.
    /// </summary>
    /// <param name="key">The Avalonia key.</param>
    /// <returns>The corresponding keypad key, or null if not mapped.</returns>
    public static KeypadKey? Map(Key key)
    {
        return key switch
        {
            Key.F1 => KeypadKey.F1,
            Key.F2 => KeypadKey.F2,
            Key.F3 => KeypadKey.F3,
            Key.F4 => KeypadKey.F4,
            Key.F5 => KeypadKey.F5,
            Key.F6 => KeypadKey.F6,
            Key.F7 => KeypadKey.F7,
            Key.F8 => KeypadKey.F8,
            Key.Up => KeypadKey.Up,
            Key.Down => KeypadKey.Down,
            Key.Left => KeypadKey.Left,
            Key.Right => KeypadKey.Right,
            Key.Return => KeypadKey.Enter,
            _ => null
        };
    }

    /// <summary>
    /// Releases resources used by this instance.
    /// </summary>
    public void Dispose()
    {
        _tickTimer?.Stop();
        _tickTimer = null;
    }

    private void EnsureTicking()
    {
        if (_tickTimer == null)
        {
            _tickTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(TickIntervalMs)
            };
            _tickTimer.Tick += OnTick;
            _tickTimer.Start();
        }
        else if (!_tickTimer.IsEnabled)
        {
            _tickTimer.Start();
        }
    }

    private void OnTick(object? sender, EventArgs e)
    {
        _gestures.Tick(Environment.TickCount64);
        if (_gestures.IsIdle)
        {
            _tickTimer!.Stop();
        }
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var mapped = Map(e.Key);
        if (mapped is { } k)
        {
            Press(k);
            e.Handled = true;
        }
    }

    private void OnKeyUp(object? sender, KeyEventArgs e)
    {
        var mapped = Map(e.Key);
        if (mapped is { } k)
        {
            Release(k);
            e.Handled = true;
        }
    }
}
