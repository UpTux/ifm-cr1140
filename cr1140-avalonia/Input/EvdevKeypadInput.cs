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
/// <see cref="KeyPressed"/> for each key-press.
/// </summary>
/// <remarks>
/// Avalonia's stock LinuxFramebuffer input (LibInput / EvDev) delivers only
/// touch/pointer events, so a keypad-only panel needs this. Pass an instance as the
/// <c>inputBackend</c> argument of <c>StartLinuxFbDev</c>/<c>StartLinuxDrm</c> and
/// drive your UI from <see cref="KeyPressed"/> (marshal to the UI thread with
/// <c>Dispatcher.UIThread.Post</c>).
/// </remarks>
public sealed class EvdevKeypadInput : IInputBackend, IDisposable
{
    private const int EventSize = 24;
    private const ushort EvKey = 1;

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

    private Thread? _readerThread;
    private FileStream? _stream;
    private IInputRoot? _inputRoot;
    private Action<RawInputEventArgs>? _onInput; // Future text-entry could dispatch RawKeyEventArgs via this

    /// <summary>Raised on the reader thread when a mapped key is pressed (evdev value 1).</summary>
    public event Action<KeypadKey>? KeyPressed;

    /// <summary>Creates a backend bound to an evdev device node.</summary>
    /// <param name="devicePath">The evdev node to read, e.g. <c>/dev/input/event1</c>.</param>
    public EvdevKeypadInput(string devicePath)
    {
        _devicePath = devicePath;
    }

    /// <summary>Called by the Avalonia LinuxFramebuffer platform; starts the evdev reader thread.</summary>
    public void Initialize(IScreenInfoProvider info, Action<RawInputEventArgs> onInput)
    {
        _onInput = onInput;

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

                // Only process key-down events (EV_KEY type=1, value=1)
                if (type == EvKey && value == 1 && KeycodeMap.TryGetValue(code, out var key))
                {
                    KeyPressed?.Invoke(key);
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

    /// <summary>Stops the reader thread and closes the device node.</summary>
    public void Dispose()
    {
        _cts.Cancel();
        _stream?.Close();
        _readerThread?.Join();
        _cts.Dispose();
    }
}
