using System.Runtime.InteropServices;
using Cr1140.Avalonia.Diagnostics;
using Avalonia;
using Avalonia.Platform.Surfaces;
using Avalonia.LinuxFramebuffer.Output;
using Avalonia.Platform;

namespace Cr1140.Avalonia.Output;

/// <summary>
/// A Linux framebuffer <see cref="IOutputBackend"/> that renders Avalonia into an off-screen
/// buffer and rotate-blits it onto <c>/dev/fb0</c>, so a CR1140/CR1141 panel can be mounted
/// in any of the four orientations (see <see cref="DisplayRotation"/>).
/// </summary>
/// <remarks>
/// Avalonia's stock <c>FbdevOutput</c> reports the framebuffer's native resolution and
/// draws straight into it, so it cannot rotate. This backend reports a <em>logical</em>
/// (rotated) size to the layout system — swapping width/height for the 90°/270° cases — has
/// Skia render into a private buffer at that size, and copies the pixels into the physical
/// framebuffer through <see cref="FramebufferRotator"/> on each frame. Pass an instance to
/// <c>StartLinuxDirect</c> (or use <c>StartLinuxFbDevRotated</c>).
///
/// <para><b>Input is not transformed.</b> Pointer/touch coordinates are delivered in physical
/// space; this backend targets the keypad-only SKU (see <c>EvdevKeypadInput</c>), whose keys
/// carry no screen coordinates, so rotation and input are independent.</para>
/// </remarks>
public sealed class RotatingFbdevOutput : IOutputBackend, IFramebufferPlatformSurface, IDisposable
{
    private const int O_RDWR = 2;
    private const int PROT_READ = 1;
    private const int PROT_WRITE = 2;
    private const int MAP_SHARED = 1;

    private const uint FBIOGET_VSCREENINFO = 0x4600;
    private const uint FBIOGET_FSCREENINFO = 0x4602;
    private const uint FBIO_WAITFORVSYNC = 0x40044620;

    private readonly DisplayRotation _rotation;
    private readonly FrameStatsRecorder? _stats;
    private readonly object _lock = new();

    private int _fd;
    private IntPtr _mappedAddress;
    private IntPtr _mappedLength;

    private int _physWidth;
    private int _physHeight;
    private int _physStrideBytes;
    private int _bytesPerPixel;
    private PixelFormat _format;

    private IntPtr _backBuffer;
    private int _logicalWidth;
    private int _logicalHeight;
    private int _logicalStrideBytes;

    /// <inheritdoc />
    public double Scaling { get; set; } = 1.0;

    /// <summary>The rotation applied when blitting to the framebuffer.</summary>
    public DisplayRotation Rotation => _rotation;

    /// <summary>The logical (rotated) surface size Avalonia lays out against.</summary>
    public PixelSize PixelSize => new(_logicalWidth, _logicalHeight);

    /// <summary>Open a framebuffer device and prepare it for rotated output.</summary>
    /// <param name="fileName">Framebuffer node, or null for <c>$FRAMEBUFFER</c> / <c>/dev/fb0</c>.</param>
    /// <param name="rotation">Clockwise rotation to apply to every frame.</param>
    /// <param name="scaling">Initial layout scale factor.</param>
    /// <param name="stats">Optional recorder fed one <c>FrameSample</c> per presented frame; <c>null</c> disables instrumentation.</param>
    public RotatingFbdevOutput(string? fileName = null, DisplayRotation rotation = DisplayRotation.None, double scaling = 1.0, FrameStatsRecorder? stats = null)
    {
        _rotation = rotation;
        _stats = stats;
        Scaling = scaling;

        var path = fileName ?? Environment.GetEnvironmentVariable("FRAMEBUFFER") ?? "/dev/fb0";
        _fd = open(path, O_RDWR, 0);
        if (_fd <= 0)
            throw new InvalidOperationException($"Unable to open framebuffer '{path}': errno {Marshal.GetLastWin32Error()}");

        try
        {
            Init();
        }
        catch
        {
            Dispose();
            throw;
        }
    }

    private unsafe void Init()
    {
        FbVarScreenInfo varInfo;
        if (ioctl(_fd, FBIOGET_VSCREENINFO, &varInfo) == -1)
            throw new InvalidOperationException($"FBIOGET_VSCREENINFO failed: errno {Marshal.GetLastWin32Error()}");

        FbFixScreenInfo fixInfo;
        if (ioctl(_fd, FBIOGET_FSCREENINFO, &fixInfo) == -1)
            throw new InvalidOperationException($"FBIOGET_FSCREENINFO failed: errno {Marshal.GetLastWin32Error()}");

        _physWidth = (int)varInfo.Xres;
        _physHeight = (int)varInfo.Yres;
        _physStrideBytes = (int)fixInfo.LineLength;
        _bytesPerPixel = (int)(varInfo.BitsPerPixel / 8);
        if (_bytesPerPixel != 4 && _bytesPerPixel != 2)
            throw new NotSupportedException($"Unsupported framebuffer depth: {varInfo.BitsPerPixel} bpp");

        // Same format selection as the stock fbdev backend (FbDevBackBuffer.LockFb).
        _format = varInfo.BitsPerPixel == 16
            ? PixelFormat.Rgb565
            : (varInfo.Blue.Offset == 16 ? PixelFormat.Rgba8888 : PixelFormat.Bgra8888);

        _mappedLength = (IntPtr)((long)_physStrideBytes * _physHeight);
        _mappedAddress = mmap(IntPtr.Zero, _mappedLength, PROT_READ | PROT_WRITE, MAP_SHARED, _fd, IntPtr.Zero);
        if (_mappedAddress == new IntPtr(-1))
        {
            _mappedAddress = IntPtr.Zero;
            throw new InvalidOperationException($"mmap failed: errno {Marshal.GetLastWin32Error()}");
        }

        // 90°/270° turn the landscape framebuffer into a portrait logical surface.
        var swap = _rotation is DisplayRotation.Clockwise90 or DisplayRotation.Clockwise270;
        _logicalWidth = swap ? _physHeight : _physWidth;
        _logicalHeight = swap ? _physWidth : _physHeight;
        _logicalStrideBytes = _logicalWidth * _bytesPerPixel;

        var backBufferLength = _logicalStrideBytes * _logicalHeight;
        _backBuffer = Marshal.AllocHGlobal(backBufferLength);
        new Span<byte>((void*)_backBuffer, backBufferLength).Clear();
        _stats?.SetPresentInfo("fbdev", _rotation, new PixelSize(_logicalWidth, _logicalHeight));
    }

    /// <inheritdoc />
    public IFramebufferRenderTarget CreateFramebufferRenderTarget() => new FuncFramebufferRenderTarget(Lock);

    private ILockedFramebuffer Lock()
    {
        if (_fd <= 0)
            throw new ObjectDisposedException(nameof(RotatingFbdevOutput));

        Monitor.Enter(_lock);
        try
        {
            var dpi = new Vector(96, 96) * Scaling;
            _stats?.BeginRender();
            return new LockedFramebuffer(
                _backBuffer,
                new PixelSize(_logicalWidth, _logicalHeight),
                _logicalStrideBytes,
                dpi,
                _format,
                _format == PixelFormat.Rgb565 ? AlphaFormat.Opaque : AlphaFormat.Premul,
                () =>
                {
                    _stats?.BeginPresent();
                    try
                    {
                        var vsync = BlitToDevice();
                        _stats?.EndFrame(vsync);
                    }
                    finally
                    {
                        Monitor.Exit(_lock);
                    }
                });
        }
        catch
        {
            Monitor.Exit(_lock);
            throw;
        }
    }

    private unsafe bool BlitToDevice()
    {
        // Best-effort vsync wait (ignored if the driver doesn't support it), mirroring the
        // stock FbDevBackBuffer blit.
        var rc = ioctl(_fd, FBIO_WAITFORVSYNC, null);

        var src = new ReadOnlySpan<byte>((void*)_backBuffer, _logicalStrideBytes * _logicalHeight);
        var dst = new Span<byte>((void*)_mappedAddress, checked((int)_mappedLength));
        FramebufferRotator.Rotate(
            src, _logicalStrideBytes,
            dst, _physStrideBytes,
            _physWidth, _physHeight,
            _bytesPerPixel, _rotation);
        return rc == 0;
    }
    /// <inheritdoc />
    public void Dispose()
    {
        ReleaseUnmanaged();
        GC.SuppressFinalize(this);
    }

    private void ReleaseUnmanaged()
    {
        if (_backBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_backBuffer);
            _backBuffer = IntPtr.Zero;
        }

        if (_mappedAddress != IntPtr.Zero)
        {
            munmap(_mappedAddress, _mappedLength);
            _mappedAddress = IntPtr.Zero;
        }

        if (_fd > 0)
        {
            close(_fd);
            _fd = 0;
        }
    }

    /// <summary>Releases the framebuffer mapping and file descriptor if not disposed.</summary>
    ~RotatingFbdevOutput() => ReleaseUnmanaged();

    [DllImport("libc", SetLastError = true)]
    private static extern int open(string pathname, int flags, int mode);

    [DllImport("libc", SetLastError = true)]
    private static extern int close(int fd);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int ioctl(int fd, uint request, void* arg);

    [DllImport("libc", SetLastError = true)]
    private static extern IntPtr mmap(IntPtr addr, IntPtr length, int prot, int flags, int fd, IntPtr offset);

    [DllImport("libc", SetLastError = true)]
    private static extern int munmap(IntPtr addr, IntPtr length);

    [StructLayout(LayoutKind.Sequential)]
    private struct FbBitfield
    {
        public uint Offset;
        public uint Length;
        public uint MsbRight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct FbVarScreenInfo
    {
        public uint Xres;
        public uint Yres;
        public uint XresVirtual;
        public uint YresVirtual;
        public uint Xoffset;
        public uint Yoffset;
        public uint BitsPerPixel;
        public uint Grayscale;
        public FbBitfield Red;
        public FbBitfield Green;
        public FbBitfield Blue;
        public FbBitfield Transp;
        public uint Nonstd;
        public uint Activate;
        public uint Height;
        public uint Width;
        public uint AccelFlags;
        public uint Pixclock;
        public uint LeftMargin;
        public uint RightMargin;
        public uint UpperMargin;
        public uint LowerMargin;
        public uint HsyncLen;
        public uint VsyncLen;
        public uint Sync;
        public uint Vmode;
        public fixed uint Reserved[6];
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct FbFixScreenInfo
    {
        public fixed byte Id[16];
        public nint SmemStart;
        public uint SmemLen;
        public uint Type;
        public uint TypeAux;
        public uint Visual;
        public ushort Xpanstep;
        public ushort Ypanstep;
        public ushort Ywrapstep;
        public uint LineLength;
        public nint MmioStart;
        public uint MmioLen;
        public uint Accel;
        public fixed ushort Reserved[3];
    }
}
