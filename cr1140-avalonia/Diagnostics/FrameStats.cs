// SPDX-License-Identifier: GPL-3.0-only

namespace Cr1140.Avalonia.Diagnostics;

/// <summary>
/// Maintains a sliding window of frame timing samples and computes aggregate statistics.
/// </summary>
/// <remarks>
/// This class uses a fixed-capacity ring buffer to track recent frame samples. All timing
/// statistics are computed on-demand from the current window. The class is thread-safe
/// for single-writer, multiple-reader scenarios, but external synchronization is required
/// for multi-writer use.
/// </remarks>
public sealed class FrameStats
{
    /// <summary>
    /// The default capacity for the frame sample ring buffer.
    /// </summary>
    public const int DefaultCapacity = 240;

    private readonly FrameSample[] _samples;
    private int _head;
    private int _count;
    private long _frameCount;

    /// <summary>
    /// Initializes a new instance of the <see cref="FrameStats"/> class.
    /// </summary>
    /// <param name="capacity">The maximum number of samples to retain. Minimum value is 1.</param>
    public FrameStats(int capacity = DefaultCapacity)
    {
        Capacity = Math.Max(1, capacity);
        _samples = new FrameSample[Capacity];
        _head = 0;
        _count = 0;
        _frameCount = 0;
    }

    /// <summary>
    /// Gets the maximum number of samples retained in the sliding window.
    /// </summary>
    public int Capacity { get; }

    /// <summary>
    /// Gets the current number of valid samples in the window.
    /// </summary>
    /// <remarks>
    /// This value ranges from 0 to <see cref="Capacity"/>. After <see cref="Capacity"/>
    /// frames have been recorded, this remains constant as new samples overwrite the oldest.
    /// </remarks>
    public int Count => _count;

    /// <summary>
    /// Gets the total number of frames recorded since construction or the last <see cref="Reset"/>.
    /// </summary>
    public long FrameCount => _frameCount;

    /// <summary>
    /// Gets the current frames-per-second estimate based on the sliding window mean.
    /// </summary>
    /// <remarks>
    /// Computed as 1000.0 divided by the mean total frame time in milliseconds.
    /// Returns 0 if the window is empty or the mean is non-positive.
    /// </remarks>
    public double Fps
    {
        get
        {
            if (_count == 0)
                return 0.0;

            double sum = 0.0;
            int oldest = _count < Capacity ? 0 : _head;
            for (int i = 0; i < _count; i++)
            {
                int index = (oldest + i) % Capacity;
                sum += _samples[index].TotalMs;
            }

            double mean = sum / _count;
            return mean > 0.0 ? 1000.0 / mean : 0.0;
        }
    }

    /// <summary>
    /// Gets the mean milliseconds-per-frame over the sliding window.
    /// </summary>
    /// <remarks>
    /// Returns 0 if the window is empty.
    /// </remarks>
    public double Mspf
    {
        get
        {
            if (_count == 0)
                return 0.0;

            double sum = 0.0;
            int oldest = _count < Capacity ? 0 : _head;
            for (int i = 0; i < _count; i++)
            {
                int index = (oldest + i) % Capacity;
                sum += _samples[index].TotalMs;
            }

            return sum / _count;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the most recent frame was synchronized to vertical refresh.
    /// </summary>
    /// <remarks>
    /// Returns false if no frames have been recorded yet.
    /// </remarks>
    public bool VSync
    {
        get
        {
            if (_count == 0)
                return false;

            int lastIndex = (_head - 1 + Capacity) % Capacity;
            return _samples[lastIndex].VSync;
        }
    }

    /// <summary>
    /// Records a new frame sample, overwriting the oldest sample if the window is full.
    /// </summary>
    /// <param name="sample">The frame timing sample to record.</param>
    public void Record(in FrameSample sample)
    {
        _samples[_head] = sample;
        _frameCount++;

        if (_count < Capacity)
            _count++;

        _head = (_head + 1) % Capacity;
    }

    /// <summary>
    /// Resets all statistics, clearing the sample window and frame count.
    /// </summary>
    public void Reset()
    {
        _head = 0;
        _count = 0;
        _frameCount = 0;
    }

    /// <summary>
    /// Computes aggregate statistics for the specified metric over the current window.
    /// </summary>
    /// <param name="kind">The metric to aggregate (total, render, or present time).</param>
    /// <returns>
    /// A <see cref="FrameMetric"/> containing the mean, minimum, maximum, and most recent value.
    /// Returns all zeros if the window is empty.
    /// </returns>
    public FrameMetric Metric(FrameMetricKind kind)
    {
        if (_count == 0)
            return new FrameMetric(0.0, 0.0, 0.0, 0.0);

        double sum = 0.0;
        double min = double.MaxValue;
        double max = double.MinValue;
        double last = 0.0;

        int oldest = _count < Capacity ? 0 : _head;
        for (int i = 0; i < _count; i++)
        {
            int index = (oldest + i) % Capacity;
            double value = GetMetricValue(_samples[index], kind);

            sum += value;
            if (value < min)
                min = value;
            if (value > max)
                max = value;

            if (i == _count - 1)
                last = value;
        }

        double average = sum / _count;
        return new FrameMetric(average, min, max, last);
    }

    /// <summary>
    /// Copies the history of a specific metric into the provided span, right-aligned.
    /// </summary>
    /// <param name="kind">The metric to copy (total, render, or present time).</param>
    /// <param name="destination">
    /// The destination span. Samples are written oldest to newest, right-aligned.
    /// If fewer samples exist than the span length, leading slots are zero-filled.
    /// </param>
    /// <returns>The number of real samples written (excluding zero-fill).</returns>
    public int CopyHistory(FrameMetricKind kind, Span<double> destination)
    {
        if (_count == 0 || destination.Length == 0)
        {
            destination.Clear();
            return 0;
        }

        int toCopy = Math.Min(_count, destination.Length);
        int offset = destination.Length - toCopy;

        // Zero-fill leading slots
        if (offset > 0)
            destination.Slice(0, offset).Clear();

        // Copy samples oldest to newest into the right-aligned portion
        int oldest = _count < Capacity ? 0 : _head;
        for (int i = 0; i < toCopy; i++)
        {
            int sourceIndex = (oldest + (_count - toCopy) + i) % Capacity;
            double value = GetMetricValue(_samples[sourceIndex], kind);
            destination[offset + i] = value;
        }

        return toCopy;
    }

    private static double GetMetricValue(in FrameSample sample, FrameMetricKind kind)
    {
        return kind switch
        {
            FrameMetricKind.Total => sample.TotalMs,
            FrameMetricKind.Render => sample.RenderMs,
            FrameMetricKind.Present => sample.PresentMs,
            _ => 0.0
        };
    }
}
