// SPDX-License-Identifier: GPL-3.0-only
using System.Net;
using System.Net.Sockets;
using System.Text;
using Avalonia.Threading;

namespace Cr1140.Avalonia.Systemd;

/// <summary>
/// Minimal <c>sd_notify(3)</c> client for the systemd service watchdog, mirroring the
/// liveness mechanism the stock CODESYS runtime uses (<c>Type=notify</c> +
/// <c>WatchdogSec=</c>).
/// </summary>
/// <remarks>
/// <para>
/// When the unit is <c>Type=notify</c>, systemd exports <c>NOTIFY_SOCKET</c>; when the unit
/// additionally sets <c>WatchdogSec=</c>, systemd exports <c>WATCHDOG_USEC</c>. This class
/// sends <c>READY=1</c> once and then <c>WATCHDOG=1</c> keep-alives on a
/// <see cref="DispatcherTimer"/> that runs on the Avalonia UI thread — so if the UI/render
/// thread wedges, the pings stop and systemd kills + restarts (or, if the unit is configured
/// that way, reboots) the app. The keep-alive cadence is half of <c>WATCHDOG_USEC</c>, the
/// conventional value.
/// </para>
/// <para>
/// It no-ops when <c>NOTIFY_SOCKET</c> is absent (running outside systemd, e.g. on the
/// developer desktop), so it is safe to construct and <see cref="Start"/> unconditionally.
/// It needs no <c>libsystemd</c> dependency: it speaks the AF_UNIX datagram protocol directly.
/// </para>
/// </remarks>
public sealed class SystemdWatchdog : IDisposable
{
    private readonly Socket? _socket;
    private readonly EndPoint? _endpoint;
    private DispatcherTimer? _timer;
    private bool _disposed;

    /// <summary>
    /// Keep-alive cadence (half of <c>WATCHDOG_USEC</c>), or <see cref="TimeSpan.Zero"/> when
    /// the unit did not set <c>WatchdogSec=</c>.
    /// </summary>
    public TimeSpan HeartbeatInterval { get; }

    /// <summary>True when systemd exported <c>NOTIFY_SOCKET</c> (the unit is <c>Type=notify</c>).</summary>
    public bool IsNotifyEnabled => _socket is not null;

    /// <summary>True when systemd exported <c>WATCHDOG_USEC</c> (the unit set <c>WatchdogSec=</c>).</summary>
    public bool IsWatchdogEnabled => IsNotifyEnabled && HeartbeatInterval > TimeSpan.Zero;

    /// <summary>
    /// Reads <c>NOTIFY_SOCKET</c> / <c>WATCHDOG_USEC</c> from the environment and prepares the
    /// notify socket. Stays a no-op when <c>NOTIFY_SOCKET</c> is unset or the socket cannot be
    /// opened.
    /// </summary>
    public SystemdWatchdog()
    {
        var notifySocket = Environment.GetEnvironmentVariable("NOTIFY_SOCKET");
        if (string.IsNullOrEmpty(notifySocket))
            return; // not launched under a Type=notify systemd unit — stay a no-op

        // NOTIFY_SOCKET is either a filesystem path (e.g. /run/systemd/notify) or an
        // abstract-namespace socket whose name starts with '@' (map '@' -> NUL for .NET).
        var path = notifySocket[0] == '@'
            ? '\0' + notifySocket.Substring(1)
            : notifySocket;

        try
        {
            _socket = new Socket(AddressFamily.Unix, SocketType.Dgram, ProtocolType.Unspecified);
            _endpoint = new UnixDomainSocketEndPoint(path);
        }
        catch
        {
            _socket?.Dispose();
            _socket = null;
            _endpoint = null;
            return;
        }

        // Half the configured timeout is the conventional keep-alive cadence.
        var usec = Environment.GetEnvironmentVariable("WATCHDOG_USEC");
        if (long.TryParse(usec, out var micros) && micros > 0)
            HeartbeatInterval = TimeSpan.FromMilliseconds(micros / 1000.0 / 2.0);
    }

    /// <summary>
    /// Sends <c>READY=1</c> and, when the watchdog is enabled, starts the UI-thread heartbeat.
    /// Call once, on the Avalonia UI thread, after the app surface is up. Safe no-op when the
    /// watchdog is disabled or after <see cref="Dispose"/>.
    /// </summary>
    public void Start()
    {
        if (_socket is null || _disposed)
            return;

        Notify("READY=1");

        if (HeartbeatInterval <= TimeSpan.Zero)
            return; // Type=notify but no WatchdogSec — READY is all systemd expects

        // DispatcherPriority.Background: the ping only lands when the UI thread's queue
        // drains, so a UI thread wedged in a long/blocking operation stops pinging and
        // systemd trips the watchdog — exactly the liveness signal we want for a GUI app.
        _timer = new DispatcherTimer(HeartbeatInterval, DispatcherPriority.Background, (_, _) => Notify("WATCHDOG=1"));
        _timer.Start();
    }

    /// <summary>
    /// Sends a raw <c>sd_notify</c> assignment list (newline-separated, e.g. <c>"WATCHDOG=1"</c>).
    /// No-op when the watchdog is disabled; never throws — a failed notify must not take down the
    /// app (systemd will act on the missing keep-alive instead).
    /// </summary>
    /// <param name="state">The state assignment(s) to send.</param>
    public void Notify(string state)
    {
        if (_socket is null || _endpoint is null || _disposed)
            return;
        try
        {
            _socket.SendTo(Encoding.UTF8.GetBytes(state), _endpoint);
        }
        catch
        {
            // Intentionally swallowed: the watchdog is best-effort supervision.
        }
    }

    /// <summary>Stops the heartbeat, sends <c>STOPPING=1</c>, and closes the notify socket.</summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _timer?.Stop();
        _timer = null;
        Notify("STOPPING=1");
        _socket?.Dispose();
    }
}
