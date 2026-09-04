using System.Buffers.Binary;
using System.Runtime.InteropServices;
using Cr1140.Avalonia.Diagnostics;
using Avalonia;
using Avalonia.Controls.Platform.Surfaces;
using Avalonia.LinuxFramebuffer.Output;
using Avalonia.Platform;

namespace Cr1140.Avalonia.Output;

/// <summary>
/// A DRM/KMS <see cref="IOutputBackend"/> that renders Avalonia (software Skia) into an
/// off-screen buffer and presents it tear-free through double-buffered <em>DUMB</em> buffers
/// and a KMS page-flip, optionally rotating each frame so a CR1140/CR1141 panel can be
/// mounted in any of the four orientations (see <see cref="DisplayRotation"/>).
/// </summary>
/// <remarks>
/// This is the DRM sibling of <see cref="RotatingFbdevOutput"/>. Where the fbdev backend
/// <c>mmap</c>s a single <c>/dev/fb0</c> surface (which can tear during large redraws), this
/// backend opens the DRM primary node (<c>/dev/dri/card0</c>), modesets the connected panel's
/// current mode, allocates <b>two</b> DUMB scanout buffers, and — after Skia renders a full
/// frame into a private logical back buffer — rotate-blits into the buffer that is <em>not</em>
/// being scanned out via <see cref="FramebufferRotator"/> and issues a
/// <c>DRM_IOCTL_MODE_PAGE_FLIP</c>, waiting for the flip-complete event. The result is a
/// vsync-throttled, tear-free present that keeps CPU (Skia) rendering — the i.MX 8M Nano has
/// no working GL driver, so Avalonia's GL-based <c>DrmOutput</c> is not usable here.
///
/// <para>Reports a <em>logical</em> (rotated) size to Avalonia — swapping width/height for the
/// 90°/270° cases — so layout, hit-testing and DPI stay correct for the orientation. Pass an
/// instance to <c>StartLinuxDirect</c> (or use <c>StartLinuxDrmRotated</c>).</para>
///
/// <para><b>Input is not transformed.</b> Pointer/touch coordinates are delivered in physical
/// space; this backend targets the keypad-only SKU (see <c>EvdevKeypadInput</c>), whose keys
/// carry no screen coordinates, so rotation and input are independent.</para>
///
/// <para><b>32 bpp only.</b> The DUMB scanout buffers are created as XRGB8888
/// (<see cref="PixelFormat.Bgra8888"/>), matching the CR1140/CR1141 panel. Unlike the fbdev
/// backend, no 16 bpp path is provided.</para>
/// </remarks>
public sealed class RotatingDrmOutput : IOutputBackend, IFramebufferPlatformSurface, IDisposable
{
    private const int O_RDWR = 2;
    private const int O_CLOEXEC = 0x80000; // octal 02000000 on aarch64/linux
    private const int PROT_READ = 1;
    private const int PROT_WRITE = 2;
    private const int MAP_SHARED = 1;

    // ioctl encoding: DRM commands are _IOWR('d' /*0x64*/, nr, struct).
    private const uint DrmIoctlBase = 0x64;
    private const uint DrmIoctlSetMaster = (DrmIoctlBase << 8) | 0x1E;   // _IO('d', 0x1E)
    private const uint DrmIoctlDropMaster = (DrmIoctlBase << 8) | 0x1F;  // _IO('d', 0x1F)

    private const uint DrmModeConnected = 1;          // DRM_MODE_CONNECTED
    private const uint DrmModeTypePreferred = 1 << 3; // DRM_MODE_TYPE_PREFERRED
    private const uint DrmModePageFlipEvent = 0x01;   // DRM_MODE_PAGE_FLIP_EVENT
    private const uint DrmEventFlipComplete = 0x02;   // DRM_EVENT_FLIP_COMPLETE
    private const int PollIn = 0x001;                 // POLLIN
    private const int EIntr = 4;                      // EINTR

    // Bounded ceiling (ms) for observing a page-flip completion event. The normal path
    // returns in ~one refresh — poll() wakes the instant the flip event is ready — so this
    // only bites when the driver drops a vblank/flip event: the render loop then proceeds
    // instead of blocking on read() forever (which froze the panel until the next keypress).
    private const int FlipCompleteTimeoutMs = 250;

    private static uint Iowr(uint nr, uint size) => (3u << 30) | ((size & 0x3FFF) << 16) | (DrmIoctlBase << 8) | nr;

    private readonly DisplayRotation _rotation;
    private readonly FrameStatsRecorder? _stats;
    private readonly object _lock = new();

    private int _fd;

    private uint _connectorId;
    private uint _crtcId;
    private DrmModeModeInfo _mode;
    private uint _reqPageFlip;

    private int _physWidth;
    private int _physHeight;
    private int _bytesPerPixel;
    private PixelFormat _format;

    private DumbBuffer[] _dumb = System.Array.Empty<DumbBuffer>();
    private int _frontIndex;
    private bool _supportsFlip = true;

    private IntPtr _backBuffer;
    private int _logicalWidth;
    private int _logicalHeight;
    private int _logicalStrideBytes;

    /// <inheritdoc />
    public double Scaling { get; set; } = 1.0;

    /// <summary>The rotation applied when presenting to the panel.</summary>
    public DisplayRotation Rotation => _rotation;

    /// <summary>The logical (rotated) surface size Avalonia lays out against.</summary>
    public PixelSize PixelSize => new(_logicalWidth, _logicalHeight);

    /// <summary>Open a DRM device and prepare it for rotated, double-buffered output.</summary>
    /// <param name="card">DRM primary node, or null for <c>/dev/dri/card0</c>.</param>
    /// <param name="rotation">Clockwise rotation to apply to every frame.</param>
    /// <param name="scaling">Initial layout scale factor.</param>
    /// <param name="stats">Optional recorder fed one <c>FrameSample</c> per presented frame; <c>null</c> disables instrumentation.</param>
    public RotatingDrmOutput(string? card = null, DisplayRotation rotation = DisplayRotation.None, double scaling = 1.0, FrameStatsRecorder? stats = null)
    {
        _rotation = rotation;
        _stats = stats;
        Scaling = scaling;

        var path = card ?? "/dev/dri/card0";
        _fd = open(path, O_RDWR | O_CLOEXEC, 0);
        if (_fd <= 0)
            throw new InvalidOperationException($"Unable to open DRM device '{path}': errno {Marshal.GetLastWin32Error()}");

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
        // Become DRM master so we may modeset. Best-effort: the first/only opener of the
        // primary node is usually granted master implicitly, and the call is a no-op then.
        ioctl(_fd, DrmIoctlSetMaster, null);

        var (crtcs, connectors) = GetResources();
        SelectConnectorAndMode(crtcs, connectors);

        _bytesPerPixel = 4;
        _format = PixelFormat.Bgra8888; // XRGB8888 DUMB buffer, little-endian => Bgra8888

        _dumb = new[]
        {
            CreateDumb(_physWidth, _physHeight, 32),
            CreateDumb(_physWidth, _physHeight, 32),
        };

        _reqPageFlip = Iowr(0xB0, (uint)sizeof(DrmModeCrtcPageFlip));

        // Initial modeset onto the first buffer establishes the mode; page-flips follow.
        SetCrtc(_dumb[0].FbId);
        _frontIndex = 0;

        // 90°/270° turn the landscape panel into a portrait logical surface.
        var swap = _rotation is DisplayRotation.Clockwise90 or DisplayRotation.Clockwise270;
        _logicalWidth = swap ? _physHeight : _physWidth;
        _logicalHeight = swap ? _physWidth : _physHeight;
        _logicalStrideBytes = _logicalWidth * _bytesPerPixel;

        var backBufferLength = _logicalStrideBytes * _logicalHeight;
        _backBuffer = Marshal.AllocHGlobal(backBufferLength);
        new Span<byte>((void*)_backBuffer, backBufferLength).Clear();
        _stats?.SetPresentInfo("DRM (tear-free)", _rotation, new PixelSize(_logicalWidth, _logicalHeight));
    }

    /// <inheritdoc />
    public IFramebufferRenderTarget CreateFramebufferRenderTarget() => new FuncFramebufferRenderTarget(Lock);

    private ILockedFramebuffer Lock()
    {
        if (_fd <= 0)
            throw new ObjectDisposedException(nameof(RotatingDrmOutput));

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
                () =>
                {
                    _stats?.BeginPresent();
                    try
                    {
                        var vsync = Present();
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

    private unsafe bool Present()
    {
        int back = _frontIndex ^ 1;
        var target = _dumb[back];

        var src = new ReadOnlySpan<byte>((void*)_backBuffer, _logicalStrideBytes * _logicalHeight);
        var dst = new Span<byte>((void*)target.Map, checked((int)target.Size));
        FramebufferRotator.Rotate(
            src, _logicalStrideBytes,
            dst, target.Pitch,
            _physWidth, _physHeight,
            _bytesPerPixel, _rotation);

        if (_supportsFlip && PageFlip(target.FbId))
        {
            _frontIndex = back;
            return true;
        }

        // Driver rejected the page-flip: fall back to a (tearing, but working) modeset present.
        _supportsFlip = false;
        SetCrtc(target.FbId);
        _frontIndex = back;
        return false;
    }
    // --- KMS setup ---------------------------------------------------------------------

    private unsafe (uint[] crtcs, uint[] connectors) GetResources()
    {
        var res = default(DrmModeCardRes);
        var req = Iowr(0xA0, (uint)sizeof(DrmModeCardRes));
        IoctlOrThrow(req, &res, "DRM_IOCTL_MODE_GETRESOURCES");

        var crtcs = new uint[res.CountCrtcs];
        var connectors = new uint[res.CountConnectors];
        if (crtcs.Length == 0 || connectors.Length == 0)
            throw new InvalidOperationException("DRM device exposes no CRTCs or connectors");

        fixed (uint* pCrtcs = crtcs)
        fixed (uint* pConns = connectors)
        {
            res.CrtcIdPtr = (ulong)pCrtcs;
            res.ConnectorIdPtr = (ulong)pConns;
            res.FbIdPtr = 0;
            res.EncoderIdPtr = 0;
            res.CountFbs = 0;
            res.CountEncoders = 0;
            IoctlOrThrow(req, &res, "DRM_IOCTL_MODE_GETRESOURCES");
        }

        return (crtcs, connectors);
    }

    private unsafe void SelectConnectorAndMode(uint[] crtcs, uint[] connectors)
    {
        foreach (var connId in connectors)
        {
            var (connection, encoderId, modes, encoders) = GetConnector(connId);
            if (connection != DrmModeConnected || modes.Length == 0)
                continue;

            var mode = modes[0];
            foreach (var m in modes)
            {
                if ((m.Type & DrmModeTypePreferred) != 0)
                {
                    mode = m;
                    break;
                }
            }

            var crtcId = ResolveCrtc(encoderId, encoders, crtcs);
            if (crtcId == 0)
                continue;

            _connectorId = connId;
            _crtcId = crtcId;
            _mode = mode;
            _physWidth = mode.HDisplay;
            _physHeight = mode.VDisplay;
            if (_physWidth <= 0 || _physHeight <= 0)
                throw new InvalidOperationException("Connected DRM mode has an empty resolution");
            return;
        }

        throw new InvalidOperationException("No connected DRM connector with a usable mode was found");
    }

    private unsafe uint ResolveCrtc(uint currentEncoderId, uint[] encoders, uint[] crtcs)
    {
        // Prefer the connector's active encoder + CRTC if it already has one.
        if (currentEncoderId != 0)
        {
            var enc = GetEncoder(currentEncoderId);
            if (enc.CrtcId != 0)
                return enc.CrtcId;
        }

        // Otherwise pick the first CRTC any candidate encoder can drive.
        foreach (var encId in encoders)
        {
            var enc = GetEncoder(encId);
            for (int i = 0; i < crtcs.Length; i++)
            {
                if ((enc.PossibleCrtcs & (1u << i)) != 0)
                    return crtcs[i];
            }
        }

        return 0;
    }

    private unsafe (uint connection, uint encoderId, DrmModeModeInfo[] modes, uint[] encoders) GetConnector(uint connId)
    {
        var c = default(DrmModeGetConnector);
        c.ConnectorId = connId;
        var req = Iowr(0xA7, (uint)sizeof(DrmModeGetConnector));
        IoctlOrThrow(req, &c, "DRM_IOCTL_MODE_GETCONNECTOR");

        var modes = new DrmModeModeInfo[c.CountModes];
        var encoders = new uint[c.CountEncoders];
        if (modes.Length > 0 || encoders.Length > 0)
        {
            fixed (DrmModeModeInfo* pModes = modes)
            fixed (uint* pEncoders = encoders)
            {
                c.ModesPtr = modes.Length > 0 ? (ulong)pModes : 0;
                c.EncodersPtr = encoders.Length > 0 ? (ulong)pEncoders : 0;
                c.PropsPtr = 0;
                c.PropValuesPtr = 0;
                c.CountProps = 0;
                IoctlOrThrow(req, &c, "DRM_IOCTL_MODE_GETCONNECTOR");
            }
        }

        return (c.Connection, c.EncoderId, modes, encoders);
    }

    private unsafe DrmModeGetEncoder GetEncoder(uint encoderId)
    {
        var e = default(DrmModeGetEncoder);
        e.EncoderId = encoderId;
        IoctlOrThrow(Iowr(0xA6, (uint)sizeof(DrmModeGetEncoder)), &e, "DRM_IOCTL_MODE_GETENCODER");
        return e;
    }

    private unsafe DumbBuffer CreateDumb(int width, int height, int bpp)
    {
        var create = new DrmModeCreateDumb { Width = (uint)width, Height = (uint)height, Bpp = (uint)bpp };
        IoctlOrThrow(Iowr(0xB2, (uint)sizeof(DrmModeCreateDumb)), &create, "DRM_IOCTL_MODE_CREATE_DUMB");

        var fb = new DrmModeFbCmd
        {
            Width = (uint)width,
            Height = (uint)height,
            Pitch = create.Pitch,
            Bpp = (uint)bpp,
            Depth = bpp == 32 ? 24u : 16u,
            Handle = create.Handle,
        };
        IoctlOrThrow(Iowr(0xAE, (uint)sizeof(DrmModeFbCmd)), &fb, "DRM_IOCTL_MODE_ADDFB");

        var map = new DrmModeMapDumb { Handle = create.Handle };
        IoctlOrThrow(Iowr(0xB3, (uint)sizeof(DrmModeMapDumb)), &map, "DRM_IOCTL_MODE_MAP_DUMB");

        var addr = mmap(IntPtr.Zero, (IntPtr)create.Size, PROT_READ | PROT_WRITE, MAP_SHARED, _fd, (IntPtr)map.Offset);
        if (addr == new IntPtr(-1))
            throw new InvalidOperationException($"mmap of DUMB buffer failed: errno {Marshal.GetLastWin32Error()}");

        new Span<byte>((void*)addr, checked((int)create.Size)).Clear();

        return new DumbBuffer
        {
            Handle = create.Handle,
            FbId = fb.FbId,
            Pitch = (int)create.Pitch,
            Size = (long)create.Size,
            Map = addr,
        };
    }

    private unsafe void SetCrtc(uint fbId)
    {
        uint conn = _connectorId;
        var crtc = new DrmModeCrtc
        {
            SetConnectorsPtr = (ulong)&conn,
            CountConnectors = 1,
            CrtcId = _crtcId,
            FbId = fbId,
            X = 0,
            Y = 0,
            GammaSize = 0,
            ModeValid = 1,
            Mode = _mode,
        };
        IoctlOrThrow(Iowr(0xA2, (uint)sizeof(DrmModeCrtc)), &crtc, "DRM_IOCTL_MODE_SETCRTC");
    }

    private unsafe bool PageFlip(uint fbId)
    {
        if (!QueueFlip(fbId))
        {
            // Most likely EBUSY: a previous flip's completion event was never observed
            // (the driver dropped a vblank), so a flip is still queued. Drain whatever is
            // pending and retry once — a single missed event must not permanently drop us
            // to tearing modeset presents, nor block read() forever.
            DrainPendingEvents();
            if (!QueueFlip(fbId))
                return false;
        }

        // Wait for THIS flip to report completion, but never block the render loop
        // indefinitely: poll() with a bounded ceiling so a dropped flip-complete event
        // can't deadlock the present thread (which previously froze the panel until the
        // next keypress woke a fresh flip). The normal path still returns at vblank, so
        // the loop stays vsync-throttled to the panel refresh.
        WaitForFlipComplete();
        return true;
    }

    private unsafe bool QueueFlip(uint fbId)
    {
        var flip = new DrmModeCrtcPageFlip
        {
            CrtcId = _crtcId,
            FbId = fbId,
            Flags = DrmModePageFlipEvent,
        };
        return ioctl(_fd, _reqPageFlip, &flip) != -1;
    }

    // Block until the pending page-flip reports completion or the bounded ceiling elapses.
    // Returns true if a DRM_EVENT_FLIP_COMPLETE was drained, false on timeout/error — the
    // caller proceeds regardless; an unconfirmed flip re-syncs on the next frame.
    private unsafe bool WaitForFlipComplete()
    {
        long deadline = Environment.TickCount64 + FlipCompleteTimeoutMs;
        byte* buf = stackalloc byte[256];

        while (true)
        {
            int remaining = (int)(deadline - Environment.TickCount64);
            if (remaining <= 0)
                return false;

            var pfd = new PollFd { Fd = _fd, Events = (short)PollIn, Revents = 0 };
            int pr = poll(&pfd, 1, remaining);
            if (pr < 0)
            {
                if (Marshal.GetLastWin32Error() == EIntr)
                    continue;
                return false;
            }
            if (pr == 0)
                return false; // timed out waiting for the flip event

            long n = read(_fd, buf, (UIntPtr)256);
            if (n <= 0)
                return false;

            if (ContainsFlipComplete(new ReadOnlySpan<byte>(buf, (int)n)))
                return true;
            // A non-flip event (e.g. a bare vblank): keep waiting within the budget.
        }
    }

    // Best-effort, non-blocking drain of any queued DRM events — clears a stale
    // flip-complete before re-queuing after an EBUSY. Bounded so it can never spin.
    private unsafe void DrainPendingEvents()
    {
        byte* buf = stackalloc byte[256];
        for (int i = 0; i < 8; i++)
        {
            var pfd = new PollFd { Fd = _fd, Events = (short)PollIn, Revents = 0 };
            if (poll(&pfd, 1, 0) <= 0)
                return;
            if (read(_fd, buf, (UIntPtr)256) <= 0)
                return;
        }
    }

    // Parse a DRM event stream — a packed sequence of { u32 type, u32 length } records —
    // and report whether any record is a page-flip completion. Pure and host-testable.
    internal static bool ContainsFlipComplete(ReadOnlySpan<byte> events)
    {
        int off = 0;
        while (off + 8 <= events.Length)
        {
            uint type = BinaryPrimitives.ReadUInt32LittleEndian(events.Slice(off, 4));
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(events.Slice(off + 4, 4));
            if (length < 8 || off + (int)length > events.Length)
                break; // malformed or truncated tail
            if (type == DrmEventFlipComplete)
                return true;
            off += (int)length;
        }
        return false;
    }

    private unsafe void IoctlOrThrow(uint request, void* arg, string what)
    {
        if (ioctl(_fd, request, arg) == -1)
            throw new InvalidOperationException($"{what} failed: errno {Marshal.GetLastWin32Error()}");
    }

    // --- teardown ----------------------------------------------------------------------

    /// <inheritdoc />
    public void Dispose()
    {
        ReleaseUnmanaged();
        GC.SuppressFinalize(this);
    }

    private unsafe void ReleaseUnmanaged()
    {
        if (_backBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_backBuffer);
            _backBuffer = IntPtr.Zero;
        }

        if (_fd > 0)
        {
            foreach (var db in _dumb)
            {
                if (db.Map != IntPtr.Zero)
                    munmap(db.Map, (IntPtr)db.Size);

                if (db.FbId != 0)
                {
                    uint fbId = db.FbId;
                    ioctl(_fd, Iowr(0xAF, sizeof(uint)), &fbId); // DRM_IOCTL_MODE_RMFB
                }

                if (db.Handle != 0)
                {
                    var destroy = new DrmModeDestroyDumb { Handle = db.Handle };
                    ioctl(_fd, Iowr(0xB4, (uint)sizeof(DrmModeDestroyDumb)), &destroy);
                }
            }
            _dumb = System.Array.Empty<DumbBuffer>();

            ioctl(_fd, DrmIoctlDropMaster, null);
            close(_fd);
            _fd = 0;
        }
    }

    /// <summary>Releases the DRM buffers, mappings and file descriptor if not disposed.</summary>
    ~RotatingDrmOutput() => ReleaseUnmanaged();

    private struct DumbBuffer
    {
        public uint Handle;
        public uint FbId;
        public int Pitch;
        public long Size;
        public IntPtr Map;
    }

    // --- libc / DRM interop ------------------------------------------------------------

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

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe long read(int fd, void* buf, UIntPtr count);

    [DllImport("libc", SetLastError = true)]
    private static extern unsafe int poll(PollFd* fds, uint nfds, int timeout);

    [StructLayout(LayoutKind.Sequential)]
    private struct PollFd
    {
        public int Fd;
        public short Events;
        public short Revents;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrmModeCardRes
    {
        public ulong FbIdPtr;
        public ulong CrtcIdPtr;
        public ulong ConnectorIdPtr;
        public ulong EncoderIdPtr;
        public uint CountFbs;
        public uint CountCrtcs;
        public uint CountConnectors;
        public uint CountEncoders;
        public uint MinWidth;
        public uint MaxWidth;
        public uint MinHeight;
        public uint MaxHeight;
    }

    [StructLayout(LayoutKind.Sequential)]
    private unsafe struct DrmModeModeInfo
    {
        public uint Clock;
        public ushort HDisplay;
        public ushort HSyncStart;
        public ushort HSyncEnd;
        public ushort HTotal;
        public ushort HSkew;
        public ushort VDisplay;
        public ushort VSyncStart;
        public ushort VSyncEnd;
        public ushort VTotal;
        public ushort VScan;
        public uint VRefresh;
        public uint Flags;
        public uint Type;
        public fixed byte Name[32];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrmModeGetConnector
    {
        public ulong EncodersPtr;
        public ulong ModesPtr;
        public ulong PropsPtr;
        public ulong PropValuesPtr;
        public uint CountModes;
        public uint CountProps;
        public uint CountEncoders;
        public uint EncoderId;
        public uint ConnectorId;
        public uint ConnectorType;
        public uint ConnectorTypeId;
        public uint Connection;
        public uint MmWidth;
        public uint MmHeight;
        public uint Subpixel;
        public uint Pad;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrmModeGetEncoder
    {
        public uint EncoderId;
        public uint EncoderType;
        public uint CrtcId;
        public uint PossibleCrtcs;
        public uint PossibleClones;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrmModeCreateDumb
    {
        public uint Height;
        public uint Width;
        public uint Bpp;
        public uint Flags;
        public uint Handle;
        public uint Pitch;
        public ulong Size;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrmModeMapDumb
    {
        public uint Handle;
        public uint Pad;
        public ulong Offset;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrmModeDestroyDumb
    {
        public uint Handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrmModeFbCmd
    {
        public uint FbId;
        public uint Width;
        public uint Height;
        public uint Pitch;
        public uint Bpp;
        public uint Depth;
        public uint Handle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrmModeCrtc
    {
        public ulong SetConnectorsPtr;
        public uint CountConnectors;
        public uint CrtcId;
        public uint FbId;
        public uint X;
        public uint Y;
        public uint GammaSize;
        public uint ModeValid;
        public DrmModeModeInfo Mode;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DrmModeCrtcPageFlip
    {
        public uint CrtcId;
        public uint FbId;
        public uint Flags;
        public uint Reserved;
        public ulong UserData;
    }
}
