using Avalonia;
using Avalonia.LinuxFramebuffer;
using Cr1140.Avalonia.Diagnostics;
using Avalonia.LinuxFramebuffer.Input;

namespace Cr1140.Avalonia.Output;

/// <summary>
/// <see cref="AppBuilder"/> startup helpers that render to a Linux framebuffer through a
/// rotating <see cref="RotatingFbdevOutput"/>, so the panel can be mounted in any orientation.
/// </summary>
public static class RotatingFramebufferPlatformExtensions
{
    /// <summary>
    /// Start Avalonia on a Linux framebuffer, rotating every frame by <paramref name="rotation"/>.
    /// The rotated counterpart of the stock <c>StartLinuxFbDev</c>.
    /// </summary>
    /// <param name="builder">The configured app builder.</param>
    /// <param name="args">Process arguments forwarded to the lifetime.</param>
    /// <param name="rotation">Clockwise rotation to apply to the display.</param>
    /// <param name="fbdev">Framebuffer node, or null for <c>$FRAMEBUFFER</c> / <c>/dev/fb0</c>.</param>
    /// <param name="scaling">Layout scale factor.</param>
    /// <param name="stats">Optional performance recorder.</param>
    /// <param name="inputBackend">Optional input backend (e.g. <c>EvdevKeypadInput</c>).</param>
    /// <param name="fps">
    /// Maximum frames per second the renderer is allowed to run at (the Avalonia
    /// <see cref="LinuxFramebufferPlatformOptions.Fps"/> render-timer cap). Rendering is
    /// <b>software Skia</b> (no GPU on the i.MX 8M Nano), so every produced frame is CPU work
    /// (Skia rasterize + rotate-blit); lowering this from the default 60 to, say, 24 caps how
    /// often that work runs and frees CPU for the rest of the app. Values &lt;= 0 leave the
    /// Avalonia platform default (60) untouched.
    /// </param>
    /// <returns>The application exit code.</returns>
    public static int StartLinuxFbDevRotated(
        this AppBuilder builder,
        string[] args,
        DisplayRotation rotation,
        string? fbdev = null,
        double scaling = 1.0,
        FrameStatsRecorder? stats = null,
        IInputBackend? inputBackend = null,
        int fps = 60)
    {
        if (fps > 0)
            builder.With(new LinuxFramebufferPlatformOptions { Fps = fps });
        return builder.StartLinuxDirect(args, new RotatingFbdevOutput(fbdev, rotation, scaling, stats), inputBackend);
    }
}
