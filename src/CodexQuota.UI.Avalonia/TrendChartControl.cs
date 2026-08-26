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

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var plot = new Rect(5, 8, Math.Max(1, Bounds.Width - 10), Math.Max(1, Bounds.Height - 20));
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

        if (_hover is not { } hover || !plot.Contains(hover)) return;
        var ratio = Math.Clamp((hover.X - plot.Left) / plot.Width, 0, 1);
        var time = start + TimeSpan.FromTicks((long)((end - start).Ticks * ratio));
        var nearest = ordered.MinBy(item => Math.Abs((item.ObservedAt - time).Ticks))!;
        var guidePercent = _resetAt is { } hoverReset && cycleStart is { } hoverCycle
            ? UniformUsageGuide.RemainingPercentAt(hoverCycle, hoverReset, time)
            : null;
        var x = plot.Left + plot.Width * ratio;
        context.DrawLine(new Pen(grid, 1), new Point(x, plot.Top), new Point(x, plot.Bottom));
        var label = guidePercent is null
            ? $"{time.ToLocalTime():MM-dd HH:mm}  actual {nearest.RemainingPercent:0.#}%"
            : $"{time.ToLocalTime():MM-dd HH:mm}  actual {nearest.RemainingPercent:0.#}%  even {guidePercent:0.#}%";
        var formatted = Text(label, 10.5, UiPalette.B(_isDark ? "#F2F4EF" : "#15211B"));
        var boxWidth = Math.Min(plot.Width, formatted.Width + 16);
        var left = Math.Clamp(x - boxWidth / 2, plot.Left, plot.Right - boxWidth);
        var box = new Rect(left, plot.Top, boxWidth, formatted.Height + 10);
        context.DrawRectangle(UiPalette.B(_isDark ? "#E6151C19" : "#F2FFFFFF"),
            new Pen(grid, 1), box, 6, 6);
        context.DrawText(formatted, new Point(box.Left + 8, box.Top + 5));
    }

    private static Point ToPoint(DateTimeOffset at, double percent, DateTimeOffset start, DateTimeOffset end, Rect plot)
    {
        var ratio = Math.Clamp((at - start).TotalMilliseconds / Math.Max(1, (end - start).TotalMilliseconds), 0, 1);
        return new Point(plot.Left + ratio * plot.Width, plot.Bottom - Math.Clamp(percent, 0, 100) / 100d * plot.Height);
    }

    private static FormattedText Text(string value, double size, IBrush brush) => new(
        value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
        new Typeface(UiElements.AppFont), size, brush);
}
