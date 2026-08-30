using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CodexQuota.Domain;

namespace CodexQuota.UI.Avalonia;

public sealed class TrendChartControl : Control
{
    private IReadOnlyList<QuotaHistoryPoint> _points = [];
    private DateTimeOffset? _resetAt;
    private int _windowMinutes;
    private Point? _hover;
    private HoverSample? _lastHoverSample;
    private Rect? _lastHoverLabelBounds;
    private Rect _lastPlotBounds;
    private bool _isDark = true;

    public IReadOnlyList<QuotaHistoryPoint> Points
    {
        get => _points;
        set { _points = value ?? []; InvalidateVisual(); }
    }

    public DateTimeOffset? ResetAt
    {
        get => _resetAt;
        set { _resetAt = value; InvalidateVisual(); }
    }

    public int WindowMinutes
    {
        get => _windowMinutes;
        set { _windowMinutes = Math.Max(0, value); InvalidateVisual(); }
    }

    public bool IsDark
    {
        get => _isDark;
        set { _isDark = value; InvalidateVisual(); }
    }

    public TrendChartControl()
    {
        MinHeight = 72;
        PointerMoved += (_, e) => { _hover = e.GetPosition(this); InvalidateVisual(); };
        PointerExited += (_, _) => { _hover = null; InvalidateVisual(); };
    }

    internal bool HasHoverSample => _lastHoverSample is not null;
    internal DateTimeOffset? HoverTime => _lastHoverSample?.Time;
    internal double? HoverActualPercent => _lastHoverSample?.ActualPercent;
    internal Rect? HoverLabelBounds => _lastHoverLabelBounds;
    internal Rect PlotBounds => _lastPlotBounds;

    internal void SetHoverPositionForTest(Point? position)
    {
        _hover = position;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        _lastHoverSample = null;
        _lastHoverLabelBounds = null;
        // A transparent fill makes the complete chart bounds participate in
        // hit testing. Without it, pointer movement can be delivered only over
        // the thin painted strokes on some Avalonia backends.
        context.DrawRectangle(Brushes.Transparent, null, new Rect(Bounds.Size));
        // Keep a dedicated information band above the plot. The hover text is
        // fixed here so it can never cover either the actual or even-use line.
        const double informationBandHeight = 25d;
        var plot = new Rect(5, informationBandHeight + 2, Math.Max(1, Bounds.Width - 10),
            Math.Max(1, Bounds.Height - informationBandHeight - 10));
        _lastPlotBounds = plot;
        var grid = UiPalette.B(_isDark ? "#26342E" : "#D7E1DC");
        var actual = UiPalette.B(_isDark ? "#62DCAF" : "#168A67");
        var guide = UiPalette.B(_isDark ? "#69847A" : "#789389");
        context.DrawLine(new Pen(grid, 1), new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));

        if (_points.Count < 2)
        {
            var empty = Text("—", 17, UiPalette.B(_isDark ? "#8FA198" : "#667A70"));
            context.DrawText(empty, new Point(plot.Center.X - empty.Width / 2, plot.Center.Y - empty.Height / 2));
            return;
        }

        var ordered = _points.OrderBy(point => point.ObservedAt).ToArray();
        var end = ordered[^1].ObservedAt;
        var start = new[] { ordered[0].ObservedAt, end.AddHours(-24) }.Max();
        if (end <= start) end = start.AddMinutes(1);
        var cycleStart = _resetAt?.AddMinutes(-_windowMinutes);
        if (_resetAt is { } reset && cycleStart is { } cycle && reset > cycle)
        {
            var guideStart = UniformUsageGuide.RemainingPercentAt(cycle, reset, start);
            var guideEnd = UniformUsageGuide.RemainingPercentAt(cycle, reset, end);
            if (guideStart is not null && guideEnd is not null)
            {
                var guideGeometry = new StreamGeometry();
                using (var stream = guideGeometry.Open())
                {
                    stream.BeginFigure(ToPoint(start, guideStart.Value, start, end, plot), false);
                    stream.LineTo(ToPoint(end, guideEnd.Value, start, end, plot));
                }
                context.DrawGeometry(null, new Pen(guide, 1.2, dashStyle: new DashStyle([4, 4], 0)), guideGeometry);
            }
        }

        var actualGeometry = new StreamGeometry();
        using (var stream = actualGeometry.Open())
        {
            stream.BeginFigure(ToPoint(ordered[0].ObservedAt, ordered[0].RemainingPercent, start, end, plot), false);
            foreach (var point in ordered.Skip(1))
                stream.LineTo(ToPoint(point.ObservedAt, point.RemainingPercent, start, end, plot));
        }
        context.DrawGeometry(null, new Pen(actual, 1.8, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), actualGeometry);

        if (_hover is not { } hover ||
            hover.X < plot.Left || hover.X > plot.Right ||
            hover.Y < 0 || hover.Y > Bounds.Height) return;
        var ratio = Math.Clamp((hover.X - plot.Left) / plot.Width, 0, 1);
        var time = start + TimeSpan.FromTicks((long)((end - start).Ticks * ratio));
        var actualPercent = InterpolateActualPercent(ordered, time);
        var guidePercent = _resetAt is { } hoverReset && cycleStart is { } hoverCycle
            ? UniformUsageGuide.RemainingPercentAt(hoverCycle, hoverReset, time)
            : null;
        var x = plot.Left + plot.Width * ratio;
        context.DrawLine(new Pen(grid, 1), new Point(x, plot.Top), new Point(x, plot.Bottom));
        var label = guidePercent is null
            ? $"{time.ToLocalTime():MM-dd HH:mm}  actual {actualPercent:0.#}%"
            : $"{time.ToLocalTime():MM-dd HH:mm}  actual {actualPercent:0.#}%  even {guidePercent:0.#}%";
        _lastHoverSample = new HoverSample(time, actualPercent, guidePercent);
        var formatted = Text(label, 10.5, UiPalette.B(_isDark ? "#F2F4EF" : "#15211B"));
        var boxWidth = Math.Min(plot.Width, formatted.Width + 16);
        var left = Math.Clamp(plot.Right - boxWidth, plot.Left, plot.Right - boxWidth);
        var box = new Rect(left, 1, boxWidth, Math.Min(informationBandHeight - 2, formatted.Height + 8));
        _lastHoverLabelBounds = box;
        context.DrawRectangle(UiPalette.B(_isDark ? "#E6151C19" : "#F2FFFFFF"),
            new Pen(grid, 1), box, 6, 6);
        context.DrawText(formatted, new Point(box.Left + 8, box.Top + 4));
    }

    private static double InterpolateActualPercent(IReadOnlyList<QuotaHistoryPoint> ordered, DateTimeOffset time)
    {
        if (time <= ordered[0].ObservedAt) return ordered[0].RemainingPercent;
        if (time >= ordered[^1].ObservedAt) return ordered[^1].RemainingPercent;
        for (var index = 1; index < ordered.Count; index++)
        {
            var right = ordered[index];
            if (right.ObservedAt < time) continue;
            var left = ordered[index - 1];
            var duration = (right.ObservedAt - left.ObservedAt).TotalMilliseconds;
            if (duration <= 0) return right.RemainingPercent;
            var ratio = Math.Clamp((time - left.ObservedAt).TotalMilliseconds / duration, 0, 1);
            return left.RemainingPercent + (right.RemainingPercent - left.RemainingPercent) * ratio;
        }
        return ordered[^1].RemainingPercent;
    }

    private static Point ToPoint(DateTimeOffset at, double percent, DateTimeOffset start, DateTimeOffset end, Rect plot)
    {
        var ratio = Math.Clamp((at - start).TotalMilliseconds / Math.Max(1, (end - start).TotalMilliseconds), 0, 1);
        return new Point(plot.Left + ratio * plot.Width, plot.Bottom - Math.Clamp(percent, 0, 100) / 100d * plot.Height);
    }

    private static FormattedText Text(string value, double size, IBrush brush) => new(
        value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
        new Typeface(UiElements.AppFont), size, brush);

    private sealed record HoverSample(DateTimeOffset Time, double ActualPercent, double? EvenPercent);
}
