// SPDX-License-Identifier: GPL-3.0-only

namespace Cr1140.Avalonia.Diagnostics;

/// <summary>
/// Represents a single frame timing sample capturing render duration, present duration,
/// total frame time, and vsync status.
/// </summary>
/// <remarks>
/// All timing values are in milliseconds. TotalMs typically reflects the frame-to-frame
/// cadence (present-to-present), while RenderMs and PresentMs break down the two major
/// phases of frame production.
/// </remarks>
public readonly struct FrameSample
{
    /// <summary>
    /// Initializes a new frame timing sample.
    /// </summary>
    /// <param name="totalMs">Total frame time in milliseconds (typically present-to-present cadence).</param>
    /// <param name="renderMs">Render phase duration in milliseconds.</param>
    /// <param name="presentMs">Present phase duration in milliseconds (blit + vsync wait).</param>
    /// <param name="vSync">True if this frame was synchronized to vertical refresh; otherwise false.</param>
    public FrameSample(double totalMs, double renderMs, double presentMs, bool vSync)
    {
        TotalMs = totalMs;
        RenderMs = renderMs;
        PresentMs = presentMs;
        VSync = vSync;
    }

    /// <summary>
    /// Gets the total frame time in milliseconds.
    /// </summary>
    public double TotalMs { get; }

    /// <summary>
    /// Gets the render phase duration in milliseconds.
    /// </summary>
    public double RenderMs { get; }

    /// <summary>
    /// Gets the present phase duration in milliseconds.
    /// </summary>
    public double PresentMs { get; }

    /// <summary>
    /// Gets a value indicating whether this frame was synchronized to vertical refresh.
    /// </summary>
    public bool VSync { get; }
}
