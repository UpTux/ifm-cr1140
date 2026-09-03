// SPDX-License-Identifier: GPL-3.0-only
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Cr1140.Avalonia.Output;
using Cr1140.Avalonia.Telemetry;

namespace Cr1140.Avalonia.Diagnostics;

/// <summary>
/// A non-interactive HUD overlay that displays real-time frame timing (FPS, mspf,
/// render/present breakdown), sparkline graphs, and optional system telemetry (CPU,
/// memory, temperatures). Self-driving via <see cref="DispatcherTimer"/>s; attach to
/// a <see cref="TopLevel"/> with <see cref="PerfOverlayExtensions.AttachPerfOverlay"/>.
/// </summary>
/// <remarks>
/// The overlay is non-interactive (<c>IsHitTestVisible</c> = <c>false</c>,
/// <c>Focusable</c> = <c>false</c>) and positioned in a corner via
/// <see cref="PerfOverlayOptions.Corner"/>. Two timers drive updates: a fast redraw
/// timer (default 16ms) invalidates the graphs, and a slower numeric timer (default
/// 500ms) refreshes the snapshot and system-info strings to avoid per-frame allocation.
/// </remarks>
public class PerfOverlay : Control
{
    private const int PanelWidth = 300;
    private const int GraphWidth = 120;
    private const int GraphHeight = 30;

    // Content size measured during Render; MeasureOverride returns it so the control
    // shrink-wraps and the chosen Corner alignment can position it (converges in one
    // frame via InvalidateMeasure, imperceptible under continuous redraw).
    private Size _measuredSize = new(PanelWidth, 460);

    /// <summary>Foreground color for primary text. Default <c>#E0E0E0</c>.</summary>
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<PerfOverlay, IBrush?>(
            nameof(Foreground),
            new SolidColorBrush(Color.Parse("#E0E0E0")));

    /// <summary>Accent color for the FPS headline and graph lines. Default <c>#7FFF7F</c>.</summary>
    public static readonly StyledProperty<IBrush?> AccentProperty =
        AvaloniaProperty.Register<PerfOverlay, IBrush?>(
            nameof(Accent),
            new SolidColorBrush(Color.Parse("#7FFF7F")));

    /// <summary>Warning color for the "Worst" metric column. Default <c>#FF6B4A</c>.</summary>
    public static readonly StyledProperty<IBrush?> WarnBrushProperty =
        AvaloniaProperty.Register<PerfOverlay, IBrush?>(
            nameof(WarnBrush),
            new SolidColorBrush(Color.Parse("#FF6B4A")));

    /// <summary>Semi-transparent background fill. Default <c>#C0101010</c> (~75% opaque dark gray).</summary>
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<PerfOverlay, IBrush?>(
            nameof(Background),
            new SolidColorBrush(Color.Parse("#C0101010")));

    /// <summary>Corner radius of the background panel. Default <c>6</c>.</summary>
    public static readonly StyledProperty<double> CornerRadiusProperty =
        AvaloniaProperty.Register<PerfOverlay, double>(nameof(CornerRadius), 6.0);

    /// <summary>Font size multiplier. Default <c>1.0</c>.</summary>
    public static readonly StyledProperty<double> FontScaleProperty =
        AvaloniaProperty.Register<PerfOverlay, double>(nameof(FontScale), 1.0);

    private readonly FrameStatsRecorder _recorder;
    private readonly PerfOverlayOptions _options;
    private readonly SystemTelemetry? _telemetry;
    private readonly Typeface _typeface;
    private readonly Pen _graphPen;
    private readonly double[] _historyBuffer = new double[GraphWidth];

    private DispatcherTimer? _redrawTimer;
    private DispatcherTimer? _numericTimer;

    // Cached snapshot state, refreshed by _numericTimer
    private FrameStatsSnapshot _snapshot;
    private TelemetrySnapshot? _telemetrySnapshot;
    private string? _cpuModel;
    private int? _cpuCount;
    private string? _osName;

    static PerfOverlay()
    {
        AffectsRender<PerfOverlay>(
            ForegroundProperty,
            AccentProperty,
            WarnBrushProperty,
            BackgroundProperty,
            CornerRadiusProperty,
            FontScaleProperty);
    }

    /// <summary>
    /// Create a performance overlay that displays frame stats from the given recorder.
    /// </summary>
    /// <param name="recorder">The recorder sampling frame timing from the output backend.</param>
    /// <param name="options">Display options, or <c>null</c> for defaults.</param>
    public PerfOverlay(FrameStatsRecorder recorder, PerfOverlayOptions? options = null)
    {
        _recorder = recorder ?? throw new ArgumentNullException(nameof(recorder));
        _options = options ?? new PerfOverlayOptions();

        IsHitTestVisible = false;
        Focusable = false;

        // OverlayLayer is a Canvas: it ignores HorizontalAlignment/Margin and positions
        // children by the Canvas.Left/Top/Right/Bottom attached properties. MeasureOverride
        // reports the content size so the Right/Bottom anchors resolve correctly.
        const double pad = 8;
        switch (_options.Corner)
        {
            case PerfOverlayCorner.TopLeft:
                Canvas.SetLeft(this, pad); Canvas.SetTop(this, pad); break;
            case PerfOverlayCorner.BottomLeft:
                Canvas.SetLeft(this, pad); Canvas.SetBottom(this, pad); break;
            case PerfOverlayCorner.BottomRight:
                Canvas.SetRight(this, pad); Canvas.SetBottom(this, pad); break;
            case PerfOverlayCorner.TopRight:
            default:
                Canvas.SetRight(this, pad); Canvas.SetTop(this, pad); break;
        }

        IsVisible = _options.StartVisible;

        // System telemetry (optional)
        if (_options.ShowSystemInfo)
        {
            _telemetry = _options.Telemetry ?? new SystemTelemetry();
        }

        _typeface = new Typeface("monospace");
        _graphPen = new Pen(Accent, 1.0);

        // Numeric update timer: refresh snapshot and system-info strings
        if (_options.NumericUpdateInterval > TimeSpan.Zero)
        {
            _numericTimer = new DispatcherTimer
            {
                Interval = _options.NumericUpdateInterval
            };
            _numericTimer.Tick += (_, _) => RefreshSnapshot();
        }

        // Redraw timer: invalidate graphs
        if (_options.RedrawInterval > TimeSpan.Zero)
        {
            _redrawTimer = new DispatcherTimer
            {
                Interval = _options.RedrawInterval
            };
            _redrawTimer.Tick += (_, _) => InvalidateVisual();
        }

        // Prime the snapshot now
        RefreshSnapshot();
    }

    /// <inheritdoc cref="ForegroundProperty"/>
    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    /// <inheritdoc cref="AccentProperty"/>
    public IBrush? Accent
    {
        get => GetValue(AccentProperty);
        set => SetValue(AccentProperty, value);
    }

    /// <inheritdoc cref="WarnBrushProperty"/>
    public IBrush? WarnBrush
    {
        get => GetValue(WarnBrushProperty);
        set => SetValue(WarnBrushProperty, value);
    }

    /// <inheritdoc cref="BackgroundProperty"/>
    public IBrush? Background
    {
        get => GetValue(BackgroundProperty);
        set => SetValue(BackgroundProperty, value);
    }

    /// <inheritdoc cref="CornerRadiusProperty"/>
    public double CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    /// <inheritdoc cref="FontScaleProperty"/>
    public double FontScale
    {
        get => GetValue(FontScaleProperty);
        set => SetValue(FontScaleProperty, value);
    }

    /// <inheritdoc/>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        if (IsVisible)
        {
            _redrawTimer?.Start();
            _numericTimer?.Start();
        }
    }

    /// <inheritdoc/>
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        _redrawTimer?.Stop();
        _numericTimer?.Stop();
    }

    /// <inheritdoc/>
    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == IsVisibleProperty)
        {
            if (IsVisible)
            {
                _redrawTimer?.Start();
                _numericTimer?.Start();
            }
            else
            {
                _redrawTimer?.Stop();
                _numericTimer?.Stop();
            }
        }
    }

    private void RefreshSnapshot()
    {
        _snapshot = _recorder.Snapshot();
        if (_telemetry is not null)
        {
            _telemetrySnapshot = _telemetry.Sample();
        }

        // System info strings (fetched once, rarely change)
        if (_cpuModel is null || _cpuCount is null || _osName is null)
        {
            _cpuModel ??= DeviceInfo.CpuModel();
            _cpuCount ??= DeviceInfo.CpuCount();
            _osName ??= DeviceInfo.OsRelease("PRETTY_NAME");
        }
    }

    /// <inheritdoc/>
    protected override Size MeasureOverride(Size availableSize) => _measuredSize;

    /// <inheritdoc/>
    public override void Render(DrawingContext ctx)
    {
        base.Render(ctx);

        var scale = FontScale;
        var panelW = PanelWidth * scale;
        var y = 0.0;

        // Background sized to the previously-measured content height.
        if (Background is not null)
        {
            var rect = new Rect(0, 0, panelW, _measuredSize.Height);
            ctx.DrawRectangle(Background, null, rect, CornerRadius, CornerRadius);
        }

        y += 8; // top padding

        // Header: FPS
        var fpsText = new FormattedText(
            $"{_snapshot.Fps:F1} FPS",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _typeface,
            26 * scale,
            Accent);
        ctx.DrawText(fpsText, new Point(8, y));
        y += fpsText.Height;

        // mspf + vsync
        var vsyncSuffix = _snapshot.VSync ? " (V-Sync)" : "";
        var mspfText = new FormattedText(
            $"{_snapshot.Mspf:F2} mspf{vsyncSuffix}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _typeface,
            12 * scale,
            Foreground);
        ctx.DrawText(mspfText, new Point(8, y));
        y += mspfText.Height + 2;

        // Frame count
        var frameText = new FormattedText(
            $"Frame: {_snapshot.FrameCount}",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _typeface,
            12 * scale,
            Foreground);
        ctx.DrawText(frameText, new Point(8, y));
        y += frameText.Height + 6;

        // Timing table header
        var headerText = new FormattedText(
            "         Average   Best  Worst   Last",
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _typeface,
            10 * scale,
            Foreground);
        ctx.DrawText(headerText, new Point(8, y));
        y += headerText.Height + 2;

        // Table rows: Total, CPU (Render), GPU (Present)
        DrawMetricRow(ctx, ref y, "Total", _snapshot.Total, scale, Foreground);
        DrawMetricRow(ctx, ref y, "CPU", _snapshot.Render, scale, Foreground);
        DrawMetricRow(ctx, ref y, "GPU", _snapshot.Present, scale, Foreground);
        y += 6;

        // Sparklines: FPS, Total, CPU, GPU
        DrawSparkline(ctx, ref y, "FPS", FrameMetricKind.Total, scale, deriveFps: true);
        DrawSparkline(ctx, ref y, "Total", FrameMetricKind.Total, scale, deriveFps: false);
        DrawSparkline(ctx, ref y, "CPU", FrameMetricKind.Render, scale, deriveFps: false);
        DrawSparkline(ctx, ref y, "GPU", FrameMetricKind.Present, scale, deriveFps: false);
        y += 6;

        // System info block
        if (_options.ShowSystemInfo)
        {
            var cpuLine = _cpuModel is not null && _cpuCount is not null
                ? $"{_cpuModel} ({_cpuCount} threads)"
                : _cpuModel ?? "?";
            DrawInfoLine(ctx, ref y, cpuLine, scale);

            var osLine = _osName ?? "?";
            DrawInfoLine(ctx, ref y, osLine, scale);

            DrawInfoLine(ctx, ref y, "Rendering: Software Skia (CPU)", scale);
            DrawInfoLine(ctx, ref y, $"Backend: {_recorder.BackendName}", scale);
            DrawInfoLine(ctx, ref y, $"Rotation: {_recorder.Rotation}", scale);
            var vp = _recorder.Viewport;
            DrawInfoLine(ctx, ref y, $"Viewport: {vp.Width}×{vp.Height}", scale);

            if (_telemetrySnapshot is TelemetrySnapshot telem)
            {
                var socTemp = telem.SocTempC.HasValue ? $"{telem.SocTempC.Value:F0}°C" : "—";
                DrawInfoLine(ctx, ref y, $"SoC {socTemp}", scale);

                var boardTemp = telem.BoardTempC.HasValue ? $"{telem.BoardTempC.Value:F0}°C" : "—";
                DrawInfoLine(ctx, ref y, $"Board {boardTemp}", scale);

                var cpuPct = telem.CpuPercent.HasValue ? $"{telem.CpuPercent.Value:F0}%" : "?";
                DrawInfoLine(ctx, ref y, $"CPU {cpuPct}", scale);

                var memPct = telem.Memory.HasValue ? $"{telem.Memory.Value.UsedPercent:F0}%" : "?";
                DrawInfoLine(ctx, ref y, $"Mem {memPct}", scale);

                var load = telem.Load1.HasValue ? $"{telem.Load1.Value:F2}" : "?";
                DrawInfoLine(ctx, ref y, $"Load {load}", scale);

                var uptime = telem.UptimeSeconds.HasValue ? ProcFs.FormatUptime(telem.UptimeSeconds.Value) : "?";
                DrawInfoLine(ctx, ref y, $"Up {uptime}", scale);
            }
        }

        // Converge the measured size to the actual content so Corner alignment + the
        // background both track the real extent.
        var desiredH = y + 8;
        if (Math.Abs(desiredH - _measuredSize.Height) > 0.5 || Math.Abs(panelW - _measuredSize.Width) > 0.5)
        {
            _measuredSize = new Size(panelW, desiredH);
            Dispatcher.UIThread.Post(InvalidateMeasure, DispatcherPriority.Render);
        }
    }

    private void DrawMetricRow(DrawingContext ctx, ref double y, string label, FrameMetric metric, double scale, IBrush? fg)
    {
        var line = string.Format(
            CultureInfo.InvariantCulture,
            "{0,-8} {1,7:F2}  {2,5:F2}  {3,5:F2}  {4,5:F2}",
            label,
            metric.Average,
            metric.Best,
            metric.Worst,
            metric.Last);

        var text = new FormattedText(
            line,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _typeface,
            10 * scale,
            fg);

        // Recolor just the "Worst" column in the warning brush (no overdraw).
        var worstPrefix = string.Format(
            CultureInfo.InvariantCulture,
            "{0,-8} {1,7:F2}  {2,5:F2}  ",
            label, metric.Average, metric.Best);
        var worstField = string.Format(CultureInfo.InvariantCulture, "{0,5:F2}", metric.Worst);
        if (WarnBrush is not null)
            text.SetForegroundBrush(WarnBrush, worstPrefix.Length, worstField.Length);

        ctx.DrawText(text, new Point(8, y));

        y += text.Height + 2;
    }

    private void DrawSparkline(DrawingContext ctx, ref double y, string label, FrameMetricKind kind, double scale, bool deriveFps)
    {
        var labelText = new FormattedText(
            label,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _typeface,
            10 * scale,
            Foreground);
        ctx.DrawText(labelText, new Point(8, y));

        var graphX = 8 + 40 * scale;
        var graphW = GraphWidth * scale;
        var graphH = GraphHeight * scale;

        // Fetch history
        var count = _recorder.CopyHistory(kind, _historyBuffer);
        if (count > 0)
        {
            // Derive FPS series if requested
            if (deriveFps)
            {
                for (int i = 0; i < count; i++)
                {
                    _historyBuffer[i] = _historyBuffer[i] > 0 ? 1000.0 / _historyBuffer[i] : 0;
                }
            }

            // Find min/max for scaling
            var min = double.MaxValue;
            var max = double.MinValue;
            for (int i = 0; i < count; i++)
            {
                if (_historyBuffer[i] < min) min = _historyBuffer[i];
                if (_historyBuffer[i] > max) max = _historyBuffer[i];
            }

            var range = max - min;
            if (range < 0.01) range = 1.0; // prevent div/zero

            // Build polyline geometry (right-aligned, oldest to newest)
            var geo = new StreamGeometry();
            using (var gctx = geo.Open())
            {
                var startIdx = Math.Max(0, count - GraphWidth);
                var firstSample = _historyBuffer[startIdx];
                var firstY = y + graphH - ((firstSample - min) / range * graphH);
                gctx.BeginFigure(new Point(graphX, firstY), false);

                for (int i = startIdx + 1; i < count; i++)
                {
                    var sample = _historyBuffer[i];
                    var px = graphX + (i - startIdx) * (graphW / GraphWidth);
                    var py = y + graphH - ((sample - min) / range * graphH);
                    gctx.LineTo(new Point(px, py));
                }
            }

            ctx.DrawGeometry(null, _graphPen, geo);
        }

        y += graphH + 4;
    }

    private void DrawInfoLine(DrawingContext ctx, ref double y, string text, double scale)
    {
        var ft = new FormattedText(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            _typeface,
            9 * scale,
            Foreground);
        ctx.DrawText(ft, new Point(8, y));
        y += ft.Height + 1;
    }
}
