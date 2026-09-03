using Avalonia;
using Cr1140.Avalonia.Diagnostics;
using Avalonia.LinuxFramebuffer.Input;

namespace Cr1140.Avalonia.Output;

/// <summary>
/// <see cref="AppBuilder"/> startup helpers that present through a DRM/KMS
/// <see cref="RotatingDrmOutput"/> — double-buffered DUMB buffers with a page-flip for
/// tear-free output — so the panel can be mounted in any orientation.
/// </summary>
public static class RotatingDrmPlatformExtensions
{
    /// <summary>
    /// Start Avalonia on a DRM/KMS device (software Skia into DUMB buffers, tear-free
    /// page-flip present), rotating every frame by <paramref name="rotation"/>. The DRM
    /// counterpart of <see cref="RotatingFramebufferPlatformExtensions.StartLinuxFbDevRotated"/>.
    /// </summary>
    /// <param name="builder">The configured app builder.</param>
    /// <param name="args">Process arguments forwarded to the lifetime.</param>
    /// <param name="rotation">Clockwise rotation to apply to the display.</param>
    /// <param name="card">DRM primary node, or null for <c>/dev/dri/card0</c>.</param>
    /// <param name="scaling">Layout scale factor.</param>
    /// <param name="stats">Optional performance recorder.</param>
    /// <param name="inputBackend">Optional input backend (e.g. <c>EvdevKeypadInput</c>).</param>
    /// <returns>The application exit code.</returns>
    public static int StartLinuxDrmRotated(
        this AppBuilder builder,
        string[] args,
        DisplayRotation rotation = DisplayRotation.None,
        string? card = null,
        double scaling = 1.0,
        FrameStatsRecorder? stats = null,
        IInputBackend? inputBackend = null)
        => builder.StartLinuxDirect(args, new RotatingDrmOutput(card, rotation, scaling, stats), inputBackend);
}
