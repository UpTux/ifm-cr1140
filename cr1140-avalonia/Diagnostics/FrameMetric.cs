// SPDX-License-Identifier: GPL-3.0-only

namespace Cr1140.Avalonia.Diagnostics;

/// <summary>
/// Represents aggregated statistics for a single frame metric (total, render, or present)
/// over a sliding window of samples.
/// </summary>
/// <remarks>
/// All values are in milliseconds. The window size is determined by the FrameStats capacity.
/// </remarks>
public readonly struct FrameMetric
{
    /// <summary>
    /// Initializes a new frame metric with computed statistics.
    /// </summary>
    /// <param name="average">The mean value over the window.</param>
    /// <param name="best">The minimum (best) value observed in the window.</param>
    /// <param name="worst">The maximum (worst) value observed in the window.</param>
    /// <param name="last">The most recent value in the window.</param>
    public FrameMetric(double average, double best, double worst, double last)
    {
        Average = average;
        Best = best;
        Worst = worst;
        Last = last;
    }

    /// <summary>
    /// Gets the mean value over the window in milliseconds.
    /// </summary>
    public double Average { get; }

    /// <summary>
    /// Gets the minimum (best) value observed in the window in milliseconds.
    /// </summary>
    public double Best { get; }

    /// <summary>
    /// Gets the maximum (worst) value observed in the window in milliseconds.
    /// </summary>
    public double Worst { get; }

    /// <summary>
    /// Gets the most recent value in the window in milliseconds.
    /// </summary>
    public double Last { get; }
}
