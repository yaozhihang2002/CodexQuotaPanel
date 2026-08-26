using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using CodexQuota.Application;
using CodexQuota.Domain;

namespace CodexQuota.UI.Avalonia;

public sealed class DailyUsageChartControl : Control
{
    private IReadOnlyList<DailyUsageSummary> _days = [];
    private DateTimeOffset? _cycleStart;
    private DateTimeOffset? _cycleEnd;
    private int _hoverIndex = -1;

    public IReadOnlyList<DailyUsageSummary> Days
    {
        get => _days;
        set { _days = value ?? []; _hoverIndex = -1; InvalidateVisual(); }
    }

    public DateTimeOffset? CycleStart
    {
        get => _cycleStart;
        set { _cycleStart = value; _hoverIndex = -1; InvalidateVisual(); }
    }

    public DateTimeOffset? CycleEnd
    {
        get => _cycleEnd;
        set { _cycleEnd = value; _hoverIndex = -1; InvalidateVisual(); }
    }

    public AppLanguage Language { get; set; } = AppLanguage.SimplifiedChinese;
    public bool IsDark { get; set; } = true;
    internal int RenderedDayCount => BuildSeries().Count;
    internal decimal RenderedMaximumCost => BuildSeries().Select(item => item.Cost).DefaultIfEmpty(0).Max();
    internal int RenderedValueLabelCount => BuildSeries().Count(item => item.Cost > 0);
    internal int HoveredDayIndex => _hoverIndex;

    internal void SetHoverIndexForTest(int index)
    {
        var count = BuildSeries().Count;
        _hoverIndex = count == 0 ? -1 : Math.Clamp(index, 0, count - 1);
        InvalidateVisual();
    }

    public DailyUsageChartControl()
    {
        ClipToBounds = true;
        PointerMoved += OnPointerMoved;
        PointerExited += (_, _) => { _hoverIndex = -1; InvalidateVisual(); };
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var series = BuildSeries();
        var muted = B(IsDark ? "#8FA198" : "#667A70");
        var grid = B(IsDark ? "#27342F" : "#D7E1DB");
        var mint = B(IsDark ? "#57D9AA" : "#168A67");
        var mintSoft = B(IsDark ? "#2E775F" : "#74B9A2");
        var text = B(IsDark ? "#F2F4EF" : "#15211B");
        var left = 8d;
        var right = Math.Max(left + 1, Bounds.Width - 8d);
        var top = 22d;
        var bottom = Math.Max(top + 1, Bounds.Height - 25d);
        var plot = new Rect(left, top, right - left, bottom - top);

        context.DrawLine(new Pen(grid, 1), new Point(plot.Left, plot.Bottom), new Point(plot.Right, plot.Bottom));
        if (series.Count == 0)
        {
            DrawText(context, Language == AppLanguage.SimplifiedChinese ? "当前周期暂无本地使用记录" : "No local usage in this cycle",
                new Point(plot.Left, plot.Center.Y - 8), 11, muted, FontWeight.Normal);
            return;
        }

        var max = Math.Max(.0001m, series.Max(item => item.Cost));
        var average = series.Average(item => item.Cost);
        var slot = plot.Width / series.Count;
        var barWidth = Math.Clamp(slot * .52, 5d, 22d);
        var averageY = plot.Bottom - plot.Height * (double)(average / max);
        context.DrawLine(new Pen(mintSoft, 1, dashStyle: new DashStyle([3, 4], 0)),
            new Point(plot.Left, averageY), new Point(plot.Right, averageY));

        for (var index = 0; index < series.Count; index++)
        {
            var item = series[index];
            var centerX = plot.Left + slot * (index + .5);
            var height = item.Cost <= 0 ? 2d : Math.Max(5d, plot.Height * (double)(item.Cost / max));
            var rect = new Rect(centerX - barWidth / 2, plot.Bottom - height, barWidth, height);
            var brush = index == _hoverIndex ? text : mint;
            context.DrawRectangle(brush, null, rect);
            if (item.Cost > 0)
            {
                var valueLabel = FormatText(CompactUsd(item.Cost), 8.3, index == _hoverIndex ? text : muted,
                    FontWeight.SemiBold);
                var labelX = Math.Clamp(centerX - valueLabel.Width / 2, plot.Left,
                    Math.Max(plot.Left, plot.Right - valueLabel.Width));
                var labelY = Math.Max(2, rect.Top - valueLabel.Height - 3);
                context.DrawText(valueLabel, new Point(labelX, labelY));
            }
        }

        DrawText(context, DateLabel(series[0].Day), new Point(plot.Left, plot.Bottom + 6), 9.5, muted, FontWeight.Normal);
        var lastLabel = FormatText(DateLabel(series[^1].Day), 9.5, muted, FontWeight.Normal);
        context.DrawText(lastLabel, new Point(plot.Right - lastLabel.Width, plot.Bottom + 6));

        if (_hoverIndex >= 0 && _hoverIndex < series.Count)
            DrawHoverCard(context, plot, slot, series[_hoverIndex], _hoverIndex, text, muted);
    }

    private void DrawHoverCard(DrawingContext context, Rect plot, double slot, DailyBar item, int index,
        IBrush text, IBrush muted)
    {
        var title = FormatText(item.Day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), 10.5, text, FontWeight.SemiBold);
        var usage = item.Usage;
        var detail = FormatText($"Total {usage.TotalTokens:N0}  ·  Input {usage.InputTokens:N0}",
            9.2, muted, FontWeight.Normal);
        var output = FormatText($"Cached {usage.CachedInputTokens:N0}  ·  Output {usage.OutputTokens:N0}",
            9.2, muted, FontWeight.Normal);
        var cost = FormatText($"Reasoning {usage.ReasoningOutputTokens:N0}  ·  ${item.Cost:0.0000}" +
                                (item.Unpriced > 0 ? " + Unpriced" : string.Empty), 9.2, muted, FontWeight.Normal);
        var width = Math.Max(Math.Max(title.Width, detail.Width), Math.Max(output.Width, cost.Width)) + 18;
        width = Math.Min(width, Math.Max(180, Bounds.Width - 4));
        var height = title.Height + detail.Height + output.Height + cost.Height + 14;
        var anchor = plot.Left + slot * (index + .5);
        var x = Math.Clamp(anchor - width / 2, 2, Math.Max(2, Bounds.Width - width - 2));
        var y = Math.Max(2, plot.Top - 8);
        context.DrawRectangle(B(IsDark ? "#E618201C" : "#F2FFFFFF"), new Pen(B(IsDark ? "#4A665A" : "#9CB7AA"), 1),
            new Rect(x, y, width, height));
        context.DrawText(title, new Point(x + 9, y + 6));
        context.DrawText(detail, new Point(x + 9, y + 6 + title.Height));
        context.DrawText(output, new Point(x + 9, y + 6 + title.Height + detail.Height));
        context.DrawText(cost, new Point(x + 9, y + 6 + title.Height + detail.Height + output.Height));
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var series = BuildSeries();
        if (series.Count == 0) return;
        var x = e.GetPosition(this).X;
        var left = 8d;
        var width = Math.Max(1, Bounds.Width - 16d);
        var next = x < left || x > left + width ? -1 : Math.Clamp((int)((x - left) / (width / series.Count)), 0, series.Count - 1);
        if (next == _hoverIndex) return;
        _hoverIndex = next;
        InvalidateVisual();
    }

    private IReadOnlyList<DailyBar> BuildSeries()
    {
        if (_days.Count == 0 && _cycleStart is null) return [];
        var lookup = _days.ToDictionary(day => day.Day, day => day);
        var start = _cycleStart?.ToLocalTime().Date ?? _days.Min(day => day.Day.ToDateTime(TimeOnly.MinValue));
        var requestedEnd = _cycleEnd?.ToLocalTime().Date ?? _days.Max(day => day.Day.ToDateTime(TimeOnly.MinValue));
        var end = requestedEnd > DateTime.Today ? DateTime.Today : requestedEnd;
        if (end < start) end = start;
        var result = new List<DailyBar>();
        for (var date = start; date <= end; date = date.AddDays(1))
        {
            var key = DateOnly.FromDateTime(date);
            if (lookup.TryGetValue(key, out var day))
                result.Add(new DailyBar(key, day.Usage, day.EstimatedApiUsd, day.UnpricedEventCount));
            else result.Add(new DailyBar(key, TokenUsageBreakdown.Zero, 0, 0));
        }
        return result;
    }

    private string DateLabel(DateOnly day) => Language == AppLanguage.SimplifiedChinese
        ? day.ToString("M/d", CultureInfo.InvariantCulture)
        : day.ToString("MMM d", CultureInfo.InvariantCulture);

    private static string CompactUsd(decimal value) => value switch
    {
        >= 1_000 => $"${value / 1_000m:0.#}K",
        >= 100 => $"${value:0}",
        >= 10 => $"${value:0.0}",
        >= 1 => $"${value:0.00}",
        >= .01m => $"${value:0.00}",
        > 0 => $"${value:0.0000}",
        _ => "$0"
    };

    private static void DrawText(DrawingContext context, string value, Point point, double size, IBrush brush, FontWeight weight) =>
        context.DrawText(FormatText(value, size, brush, weight), point);

    private static FormattedText FormatText(string value, double size, IBrush brush, FontWeight weight) => new(
        value, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
        new Typeface(UiElements.AppFont, FontStyle.Normal, weight), size, brush);

    private static IBrush B(string value) => UiPalette.B(value);
    private sealed record DailyBar(DateOnly Day, TokenUsageBreakdown Usage, decimal Cost, int Unpriced)
    {
        public long Tokens => Usage.TotalTokens;
    }
}
