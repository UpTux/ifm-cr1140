using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.LinuxFramebuffer.Input;

namespace Cr1140.AvaloniaDemo.Input;

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

    public event Action<KeypadKey>? KeyPressed;

    public EvdevKeypadInput(string devicePath)
    {
        _devicePath = devicePath;
    }

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

    public void Dispose()
    {
        _cts.Cancel();
        _stream?.Close();
        _readerThread?.Join();
        _cts.Dispose();
    }
}
