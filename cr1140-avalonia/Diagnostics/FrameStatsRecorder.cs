// SPDX-License-Identifier: GPL-3.0-only

using System.Diagnostics;
using Avalonia;
using Cr1140.Avalonia.Output;

namespace Cr1140.Avalonia.Diagnostics;

/// <summary>
/// Records frame timing statistics across render and present phases.
/// Thread-safe for concurrent Begin/End calls and snapshot retrieval.
/// </summary>
public sealed class FrameStatsRecorder
{
    private readonly FrameStats _stats;
    private readonly object _lock = new();

    private long _renderStartTicks;
    private long _presentStartTicks;
    private long _previousPresentEndTicks;
    private bool _hasBaseline;

    private string _backendName = string.Empty;
    private DisplayRotation _rotation;
    private PixelSize _viewport;

    /// <summary>
    /// Initializes a new frame statistics recorder.
    /// </summary>
    /// <param name="capacity">
    /// Maximum number of frame samples to retain in the rolling window.
    /// Defaults to <see cref="FrameStats.DefaultCapacity"/>.
    /// </param>
    public FrameStatsRecorder(int capacity = FrameStats.DefaultCapacity)
    {
        _stats = new FrameStats(capacity);
    }

    /// <summary>
    /// Marks the start of the render phase (before Skia rasterization under lock).
    /// </summary>
    public void BeginRender()
    {
        lock (_lock)
        {
            _renderStartTicks = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>
    /// Marks the start of the present phase (render complete, before blit and vsync).
    /// </summary>
    public void BeginPresent()
    {
        lock (_lock)
        {
            _presentStartTicks = Stopwatch.GetTimestamp();
        }
    }

    /// <summary>
    /// Marks the end of the frame and records timing statistics.
    /// </summary>
    /// <param name="vSync">
    /// True if VSync was used (DRM page-flip or fbdev FBIO_WAITFORVSYNC succeeded).
    /// </param>
    /// <remarks>
    /// The first call establishes a baseline and does not record a sample.
    /// Subsequent calls compute render, present, and total frame times relative
    /// to the previous frame's present completion.
    /// </remarks>
    public void EndFrame(bool vSync)
    {
        lock (_lock)
        {
            long presentEndTicks = Stopwatch.GetTimestamp();

            double renderMs = TicksToMs(_presentStartTicks - _renderStartTicks);
            double presentMs = TicksToMs(presentEndTicks - _presentStartTicks);

            if (_hasBaseline)
            {
                double totalMs = TicksToMs(presentEndTicks - _previousPresentEndTicks);
                var sample = new FrameSample(totalMs, renderMs, presentMs, vSync);
                _stats.Record(in sample);
            }

            _previousPresentEndTicks = presentEndTicks;
            _hasBaseline = true;
        }
    }

    /// <summary>
    /// Updates presentation environment metadata (backend name, rotation, viewport).
    /// </summary>
    /// <param name="backendName">Output backend identifier (e.g., "Drm", "Fbdev").</param>
    /// <param name="rotation">Display rotation applied by the backend.</param>
    /// <param name="viewport">Final viewport size after rotation.</param>
    public void SetPresentInfo(string backendName, DisplayRotation rotation, PixelSize viewport)
    {
        lock (_lock)
        {
            _backendName = backendName ?? string.Empty;
            _rotation = rotation;
            _viewport = viewport;
        }
    }

    /// <summary>
    /// Gets the name of the output backend (e.g., "Drm", "Fbdev").
    /// </summary>
    public string BackendName
    {
        get
        {
            lock (_lock)
            {
                return _backendName;
            }
        }
    }

    /// <summary>
    /// Gets the display rotation applied by the output backend.
    /// </summary>
    public DisplayRotation Rotation
    {
        get
        {
            lock (_lock)
            {
                return _rotation;
            }
        }
    }

    /// <summary>
    /// Gets the viewport size after rotation.
    /// </summary>
    public PixelSize Viewport
    {
        get
        {
            lock (_lock)
            {
                return _viewport;
            }
        }
    }

    /// <summary>
    /// Captures an immutable snapshot of current frame statistics.
    /// </summary>
    /// <returns>A snapshot containing aggregated metrics and metadata.</returns>
    public FrameStatsSnapshot Snapshot()
    {
        lock (_lock)
        {
            return new FrameStatsSnapshot(
                fps: _stats.Fps,
                mspf: _stats.Mspf,
                frameCount: _stats.FrameCount,
                vSync: _stats.VSync,
                total: _stats.Metric(FrameMetricKind.Total),
                render: _stats.Metric(FrameMetricKind.Render),
                present: _stats.Metric(FrameMetricKind.Present)
            );
        }
    }

    /// <summary>
    /// Copies historical samples for a specific metric kind into the destination span.
    /// </summary>
    /// <param name="kind">The metric kind to retrieve (Total, Render, or Present).</param>
    /// <param name="destination">
    /// Destination span to fill. Samples are right-aligned (oldest to newest),
    /// with leading slots zeroed if fewer samples exist than the span length.
    /// </param>
    /// <returns>The number of real samples written (may be less than destination.Length).</returns>
    public int CopyHistory(FrameMetricKind kind, Span<double> destination)
    {
        lock (_lock)
        {
            return _stats.CopyHistory(kind, destination);
        }
    }

    /// <summary>
    /// Resets all recorded statistics and timing baselines.
    /// </summary>
    public void Reset()
    {
        lock (_lock)
        {
            _stats.Reset();
            _hasBaseline = false;
            _renderStartTicks = 0;
            _presentStartTicks = 0;
            _previousPresentEndTicks = 0;
        }
    }

    private static double TicksToMs(long ticks)
    {
        double ms = 1000.0 * ticks / Stopwatch.Frequency;
        return Math.Max(0.0, ms);
    }
}
