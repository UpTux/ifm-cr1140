using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LinuxFramebuffer.Input;

namespace Cr1140.Avalonia.Input;

/// <summary>
/// An Avalonia <see cref="IInputBackend"/> that reads the CR1140/CR1141 gpio-keys
/// keypad from an evdev device node (default <c>/dev/input/event1</c>) and raises
/// managed key events: <see cref="KeyPressed"/> and <see cref="KeyReleased"/> for raw
/// down/up, plus the derived gestures <see cref="KeyTapped"/>, <see cref="KeyDoubleTapped"/>,
/// <see cref="KeyHeld"/> (long-press), and <see cref="KeyHolding"/> (press-and-hold repeat).
/// </summary>
/// <remarks>
/// Avalonia's stock LinuxFramebuffer input (LibInput / EvDev) delivers only
/// touch/pointer events, so a keypad-only panel needs this. Pass an instance as the
/// <c>inputBackend</c> argument of <c>StartLinuxFbDev</c>/<c>StartLinuxDrm</c> and
/// drive your UI from the events (marshal to the UI thread with
/// <c>Dispatcher.UIThread.Post</c>). Gesture timing is configurable via
/// <see cref="KeyGestureOptions"/>; every event fires on a background thread.
/// </remarks>
public sealed class EvdevKeypadInput : IInputBackend, IKeypadInput, IDisposable
{
    private const int EventSize = 24;
    private const ushort EvKey = 1;
    // Cadence of the gesture clock while a key is active; drives hold-repeat and tap resolution.
    private const int TickIntervalMs = 25;

    private static readonly IReadOnlyDictionary<ushort, KeypadKey> KeycodeMap = new Dictionary<ushort, KeypadKey>
    {
        [59] = KeypadKey.F1,
        [60] = KeypadKey.F2,
        [61] = KeypadKey.F3,
        [62] = KeypadKey.F4,
        [63] = KeypadKey.F5,
        [64] = KeypadKey.F6,
        [103] = KeypadKey.Up,
        [108] = KeypadKey.Down,
        [105] = KeypadKey.Left,
        [106] = KeypadKey.Right,
        [28] = KeypadKey.Enter
    };

    private readonly string _devicePath;
    private readonly CancellationTokenSource _cts = new();
    private readonly KeyGestureDetector _gestures;
    private readonly object _gate = new();

    private Thread? _readerThread;
    private FileStream? _stream;
    private Timer? _tickTimer;
    private IInputRoot? _inputRoot;
    private Action<RawInputEventArgs>? _onInput; // Future text-entry could dispatch RawKeyEventArgs via this

    /// <summary>Raised on the reader thread when a mapped key is pressed (evdev value 1).</summary>
    public event Action<KeypadKey>? KeyPressed;

    /// <summary>Raised on the reader thread when a mapped key is released (evdev value 0).</summary>
    public event Action<KeypadKey>? KeyReleased;

    /// <summary>Raised for a completed short press with no second tap inside the double-tap window.</summary>
    public event Action<KeypadKey>? KeyTapped;

    /// <summary>Raised when two taps of the same key complete within the double-tap window.</summary>
    public event Action<KeypadKey>? KeyDoubleTapped;

    /// <summary>Raised once when a key has stayed down past the hold threshold.</summary>
    public event Action<KeypadKey>? KeyHeld;

    /// <summary>Raised repeatedly (press-and-hold auto-repeat) while a key stays down after <see cref="KeyHeld"/>.</summary>
    public event Action<KeypadKey>? KeyHolding;

    /// <summary>Creates a backend bound to an evdev device node with default gesture timing.</summary>
    /// <param name="devicePath">The evdev node to read, e.g. <c>/dev/input/event1</c>.</param>
    public EvdevKeypadInput(string devicePath) : this(devicePath, null)
    {
    }

    /// <summary>Creates a backend bound to an evdev device node with custom gesture timing.</summary>
    /// <param name="devicePath">The evdev node to read, e.g. <c>/dev/input/event1</c>.</param>
    /// <param name="gestureOptions">Tap/double-tap/hold thresholds, or null for defaults.</param>
    public EvdevKeypadInput(string devicePath, KeyGestureOptions? gestureOptions)
    {
        _devicePath = devicePath;
        _gestures = new KeyGestureDetector(gestureOptions);
        _gestures.Tapped += k => KeyTapped?.Invoke(k);
        _gestures.DoubleTapped += k => KeyDoubleTapped?.Invoke(k);
        _gestures.Held += k => KeyHeld?.Invoke(k);
        _gestures.Holding += k => KeyHolding?.Invoke(k);
    }

    /// <summary>Called by the Avalonia LinuxFramebuffer platform; starts the evdev reader thread.</summary>
    public void Initialize(IScreenInfoProvider info, Action<RawInputEventArgs> onInput)
    {
        _onInput = onInput;

        // Stopped until a key press; a down-event arms it, OnTick disarms it once idle.
        _tickTimer = new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);

        _readerThread = new Thread(ReaderLoop)
        {
            IsBackground = true,
            Name = "EvdevKeypadReader"
        };
        _readerThread.Start();
    }

    /// <summary>Called by the Avalonia LinuxFramebuffer platform to supply the input root.</summary>
    public void SetInputRoot(IInputRoot root)
    {
        _inputRoot = root;
    }

    private void ReaderLoop()
    {
        var token = _cts.Token;
        var buffer = new byte[EventSize];

        try
        {
            _stream = new FileStream(_devicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            while (!token.IsCancellationRequested)
            {
                // Read full 24-byte event, handling partial reads
                int totalRead = 0;
                while (totalRead < EventSize)
                {
                    int bytesRead = _stream.Read(buffer, totalRead, EventSize - totalRead);
                    if (bytesRead == 0)
                    {
                        // End of stream
                        return;
                    }
                    totalRead += bytesRead;
                }

                // Parse input_event struct (24 bytes on 64-bit):
                // [16..18) = type (u16 LE)
                // [18..20) = code (u16 LE)
                // [20..24) = value (s32 LE)
                ushort type = BitConverter.ToUInt16(buffer, 16);
                ushort code = BitConverter.ToUInt16(buffer, 18);
                int value = BitConverter.ToInt32(buffer, 20);

                // EV_KEY value 1 = down, 0 = up, 2 = auto-repeat (ignored; holding is timer-driven).
                if (type == EvKey && KeycodeMap.TryGetValue(code, out var key))
                {
                    if (value == 1)
                    {
                        KeyPressed?.Invoke(key);
                        lock (_gate)
                        {
                            _gestures.Down(key, Environment.TickCount64);
                            _tickTimer?.Change(TickIntervalMs, TickIntervalMs);
                        }
                    }
                    else if (value == 0)
                    {
                        KeyReleased?.Invoke(key);
                        lock (_gate)
                        {
                            _gestures.Up(key, Environment.TickCount64);
                        }
                    }
                }
            }
        }
        catch (IOException)
        {
            // Expected during shutdown
        }
        catch (ObjectDisposedException)
        {
            // Expected during shutdown
        }
    }

    // Gesture clock: fires while a key is active, disarms itself once the detector is idle.
    private void OnTick(object? state)
    {
        lock (_gate)
        {
            _gestures.Tick(Environment.TickCount64);
            if (_gestures.IsIdle)
                _tickTimer?.Change(Timeout.Infinite, Timeout.Infinite);
        }
    }

    /// <summary>Stops the reader thread, the gesture timer, and closes the device node.</summary>
    public void Dispose()
    {
        _cts.Cancel();
        _tickTimer?.Dispose();
        _stream?.Close();
        _readerThread?.Join();
        _cts.Dispose();
    }
}
