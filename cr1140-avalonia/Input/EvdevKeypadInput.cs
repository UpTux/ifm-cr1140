using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LinuxFramebuffer.Input;
using Cr1140.Avalonia.Devices;

namespace Cr1140.Avalonia.Input;

/// <summary>
/// An Avalonia <see cref="IInputBackend"/> that reads the CR1140/CR1141/CR1102 keypad
/// from one or more evdev device nodes and raises managed key events: <see cref="KeyPressed"/>
/// and <see cref="KeyReleased"/> for raw down/up, plus the derived gestures <see cref="KeyTapped"/>,
/// <see cref="KeyDoubleTapped"/>, <see cref="KeyHeld"/> (long-press), and <see cref="KeyHolding"/>
/// (press-and-hold repeat).
/// </summary>
/// <remarks>
/// Avalonia's stock LinuxFramebuffer input (LibInput / EvDev) delivers only
/// touch/pointer events, so a keypad-only panel needs this. Pass an instance as the
/// <c>inputBackend</c> argument of <c>StartLinuxFbDev</c>/<c>StartLinuxDrm</c> and
/// drive your UI from the events (marshal to the UI thread with
/// <c>Dispatcher.UIThread.Post</c>). Gesture timing is configurable via
/// <see cref="KeyGestureOptions"/>; every event fires on a background thread.
/// <para>
/// This class can read from multiple evdev nodes concurrently (used for CR1102's
/// "PDM3 virtual keyboard" uinput devices). <see cref="ForDevice"/> auto-discovers
/// nodes by <see cref="DeviceProfile.KeypadDeviceName"/> when present.
/// </para>
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
        // F7/F8 exist on 8-key SKUs (e.g. CR1102). Standard Linux KEY_F7/KEY_F8; the CR1102
        // codes 65/66 were confirmed live 2026-09-21. Harmless on 6-key SKUs that never emit them.
        [65] = KeypadKey.F7,
        [66] = KeypadKey.F8,
        [103] = KeypadKey.Up,
        [108] = KeypadKey.Down,
        [105] = KeypadKey.Left,
        [106] = KeypadKey.Right,
        [28] = KeypadKey.Enter
    };

    private readonly IReadOnlyList<string> _devicePaths;
    private readonly CancellationTokenSource _cts = new();
    private readonly KeyGestureDetector _gestures;
    private readonly object _gate = new();
    private readonly List<Thread> _readerThreads = new();
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
    public EvdevKeypadInput(string devicePath) : this(new[] { devicePath }, null)
    {
    }

    /// <summary>Creates a backend bound to an evdev device node with custom gesture timing.</summary>
    /// <param name="devicePath">The evdev node to read, e.g. <c>/dev/input/event1</c>.</param>
    /// <param name="gestureOptions">Tap/double-tap/hold thresholds, or null for defaults.</param>
    public EvdevKeypadInput(string devicePath, KeyGestureOptions? gestureOptions)
        : this(new[] { devicePath }, gestureOptions)
    {
    }

    /// <summary>
    /// Creates a backend bound to multiple evdev device nodes with custom gesture timing.
    /// Each node is read by a dedicated thread; all threads share the same gesture detector.
    /// </summary>
    /// <param name="devicePaths">The evdev nodes to read, e.g. <c>/dev/input/event1</c>.</param>
    /// <param name="gestureOptions">Tap/double-tap/hold thresholds, or null for defaults.</param>
    public EvdevKeypadInput(IReadOnlyList<string> devicePaths, KeyGestureOptions? gestureOptions = null)
    {
        _devicePaths = devicePaths;
        _gestures = new KeyGestureDetector(gestureOptions);
        _gestures.Tapped += k => KeyTapped?.Invoke(k);
        _gestures.DoubleTapped += k => KeyDoubleTapped?.Invoke(k);
        _gestures.Held += k => KeyHeld?.Invoke(k);
        _gestures.Holding += k => KeyHolding?.Invoke(k);
    }

    /// <summary>
    /// Creates a backend for a specific device profile. If <see cref="DeviceProfile.KeypadDeviceName"/>
    /// is non-null and non-empty, the backend auto-discovers matching evdev nodes by device name
    /// (e.g. "PDM3 virtual keyboard" for CR1102); otherwise it uses
    /// <see cref="DeviceProfile.KeypadDevicePath"/>. Gesture timing can be customized via
    /// <paramref name="gestureOptions"/>.
    /// </summary>
    /// <param name="profile">The device profile to configure for.</param>
    /// <param name="gestureOptions">Tap/double-tap/hold thresholds, or null for defaults.</param>
    /// <returns>A new <see cref="EvdevKeypadInput"/> instance.</returns>
    public static EvdevKeypadInput ForDevice(DeviceProfile profile, KeyGestureOptions? gestureOptions = null)
    {
        if (!string.IsNullOrEmpty(profile.KeypadDeviceName))
        {
            var discovered = DiscoverByName(profile.KeypadDeviceName);
            if (discovered.Count > 0)
                return new EvdevKeypadInput(discovered, gestureOptions);
        }
        return new EvdevKeypadInput(profile.KeypadDevicePath, gestureOptions);
    }

    /// <summary>
    /// Discovers evdev device nodes by device name. Scans <c>/dev/input/event*</c> and
    /// returns the sorted list of paths whose <c>/sys/class/input/eventN/device/name</c>
    /// matches <paramref name="deviceName"/> (case-sensitive, ordinal).
    /// </summary>
    /// <param name="deviceName">The device name to match, e.g. "PDM3 virtual keyboard".</param>
    /// <returns>Sorted list of matching <c>/dev/input/eventN</c> paths, or empty on error/no match.</returns>
    public static IReadOnlyList<string> DiscoverByName(string deviceName)
    {
        var matches = new List<string>();
        try
        {
            var inputDir = "/dev/input";
            if (!Directory.Exists(inputDir))
                return matches;

            var entries = Directory.GetFiles(inputDir, "event*");
            foreach (var eventPath in entries)
            {
                try
                {
                    var eventNode = Path.GetFileName(eventPath);
                    var sysPath = $"/sys/class/input/{eventNode}/device/name";
                    if (File.Exists(sysPath))
                    {
                        var name = File.ReadAllText(sysPath).TrimEnd('\n', '\r');
                        if (name == deviceName)
                        {
                            matches.Add(eventPath);
                        }
                    }
                }
                catch (IOException)
                {
                    // Skip this device on read error
                }
                catch (UnauthorizedAccessException)
                {
                    // Skip devices we can't read
                }
            }
        }
        catch (IOException)
        {
            // Return empty list on directory enumeration error
        }
        catch (UnauthorizedAccessException)
        {
            // Return empty list on permission error
        }

        matches.Sort(StringComparer.Ordinal);
        return matches;
    }

    /// <summary>Called by the Avalonia LinuxFramebuffer platform; starts the evdev reader threads.</summary>
    public void Initialize(IScreenInfoProvider info, Action<RawInputEventArgs> onInput)
    {
        _onInput = onInput;

        // Stopped until a key press; a down-event arms it, OnTick disarms it once idle.
        _tickTimer = new Timer(OnTick, null, Timeout.Infinite, Timeout.Infinite);

        // Start one reader thread per device path
        foreach (var devicePath in _devicePaths)
        {
            var thread = new Thread(() => ReaderLoop(devicePath))
            {
                IsBackground = true,
                Name = $"EvdevKeypadReader:{Path.GetFileName(devicePath)}"
            };
            lock (_gate)
            {
                _readerThreads.Add(thread);
            }
            thread.Start();
        }
    }

    /// <summary>Called by the Avalonia LinuxFramebuffer platform to supply the input root.</summary>
    public void SetInputRoot(IInputRoot root)
    {
        _inputRoot = root;
    }

    private void ReaderLoop(string devicePath)
    {
        var token = _cts.Token;
        var buffer = new byte[EventSize];
        FileStream? stream = null;

        try
        {
            stream = new FileStream(devicePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

            while (!token.IsCancellationRequested)
            {
                // Read full 24-byte event, handling partial reads
                int totalRead = 0;
                while (totalRead < EventSize)
                {
                    int bytesRead = stream.Read(buffer, totalRead, EventSize - totalRead);
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
                        lock (_gate)
                        {
                            KeyPressed?.Invoke(key);
                            _gestures.Down(key, Environment.TickCount64);
                            _tickTimer?.Change(TickIntervalMs, TickIntervalMs);
                        }
                    }
                    else if (value == 0)
                    {
                        lock (_gate)
                        {
                            KeyReleased?.Invoke(key);
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
        finally
        {
            stream?.Close();
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

    /// <summary>Stops all reader threads, the gesture timer, and closes all device nodes.</summary>
    public void Dispose()
    {
        _cts.Cancel();
        _tickTimer?.Dispose();
        
        // Join all reader threads
        Thread[] threads;
        lock (_gate)
        {
            threads = _readerThreads.ToArray();
        }
        foreach (var thread in threads)
        {
            thread.Join();
        }
        
        _cts.Dispose();
    }
}
