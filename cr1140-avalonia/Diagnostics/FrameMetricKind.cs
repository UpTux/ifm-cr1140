// SPDX-License-Identifier: GPL-3.0-only

namespace Cr1140.Avalonia.Diagnostics;

/// <summary>
/// Specifies the frame timing metric to query or export.
/// </summary>
public enum FrameMetricKind
{
    /// <summary>
    /// Total frame time (present-to-present cadence).
    /// </summary>
    Total,

    /// <summary>
    /// Render phase duration.
    /// </summary>
    Render,

    /// <summary>
    /// Present phase duration (blit + vsync wait).
    /// </summary>
    Present
}
