// SPDX-License-Identifier: GPL-3.0-only

namespace Cr1140.Avalonia.Diagnostics;

/// <summary>
/// Immutable snapshot of frame statistics at a point in time.
/// </summary>
/// <param name="fps">Frames per second (1000 / mean total ms).</param>
/// <param name="mspf">Milliseconds per frame (mean total ms).</param>
/// <param name="frameCount">Total number of frames recorded since creation or reset.</param>
/// <param name="vSync">VSync status of the most recent frame.</param>
/// <param name="total">Aggregate metrics for total frame time (present-to-present cadence).</param>
/// <param name="render">Aggregate metrics for render time (Skia rasterize under lock).</param>
/// <param name="present">Aggregate metrics for present time (blit + vsync/page-flip wait).</param>
public readonly struct FrameStatsSnapshot(
    double fps,
    double mspf,
    long frameCount,
    bool vSync,
    FrameMetric total,
    FrameMetric render,
    FrameMetric present)
{
    /// <summary>
    /// Frames per second (1000 / mean total ms over the sample window).
    /// </summary>
    public double Fps { get; } = fps;

    /// <summary>
    /// Milliseconds per frame (mean total ms over the sample window).
    /// </summary>
    public double Mspf { get; } = mspf;

    /// <summary>
    /// Total number of frames recorded since creation or last reset.
    /// </summary>
    public long FrameCount { get; } = frameCount;

    /// <summary>
    /// VSync status of the most recent frame.
    /// </summary>
    public bool VSync { get; } = vSync;

    /// <summary>
    /// Aggregate metrics for total frame time (present-to-present cadence).
    /// </summary>
    public FrameMetric Total { get; } = total;

    /// <summary>
    /// Aggregate metrics for render time (Skia rasterize under lock).
    /// </summary>
    public FrameMetric Render { get; } = render;

    /// <summary>
    /// Aggregate metrics for present time (blit + vsync/page-flip wait).
    /// </summary>
    public FrameMetric Present { get; } = present;
}
