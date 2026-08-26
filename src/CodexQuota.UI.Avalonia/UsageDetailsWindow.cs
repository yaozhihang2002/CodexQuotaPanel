using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CodexQuota.Application;
using CodexQuota.Domain;

namespace CodexQuota.UI.Avalonia;

public sealed class UsageDetailsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly UiPalette _palette;
    private readonly StackPanel _summary;
    private readonly StackPanel _daily;
    private readonly DailyUsageChartControl _dailyChart;

    internal int DailyChartDayCount => _dailyChart.RenderedDayCount;

    public UsageDetailsWindow(AppSettings settings, bool systemDark = true)
    {
        _settings = settings.Normalize();
        _palette = UiPalette.For(_settings.Theme, systemDark);
        Title = T("Codex 使用明细", "Codex usage details");
        var scale = _settings.InterfaceScalePercent / 100d;
        Width = Math.Clamp(760 * (.72 + .28 * scale), 650, 920);
        Height = Math.Clamp(650 * (.78 + .22 * scale), 540, 760);
        MinWidth = 650;
        MinHeight = 520;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Background = _palette.Canvas;
        _summary = new StackPanel { Spacing = 8 };
        _daily = new StackPanel { Spacing = 10 };
        _dailyChart = new DailyUsageChartControl
        {
            Height = 205,
            Language = _settings.Language,
            IsDark = _settings.Theme != AppTheme.Light
        };
        Content = BuildContent();
    }

    public void ApplyUsage(IReadOnlyList<ObservedUsage> usage, DateTimeOffset? cycleStart = null, DateTimeOffset? cycleEnd = null)
    {
        _summary.Children.Clear();
        _daily.Children.Clear();
        var filtered = usage.Where(item => cycleStart is null || item.ObservedAt >= cycleStart)
            .Where(item => cycleEnd is null || item.ObservedAt <= cycleEnd).ToArray();
        var days = UsageSummaryCalculator.SummarizeByDay(filtered, TimeZoneInfo.Local);
        _dailyChart.Days = days;
        _dailyChart.CycleStart = cycleStart;
        _dailyChart.CycleEnd = cycleEnd;
        var total = filtered.Aggregate(TokenUsageBreakdown.Zero, (sum, item) => sum.Add(item.Usage));
        var slices = days.SelectMany(day => day.Slices)
            .GroupBy(slice => (slice.Model, slice.ServiceTier))
            .Select(group => new
            {
                group.Key.Model,
                group.Key.ServiceTier,
                Tokens = group.Sum(item => item.Usage.TotalTokens),
                Cost = group.Sum(item => item.EstimatedApiUsd),
                Unpriced = group.Sum(item => item.UnpricedEventCount)
            }).OrderByDescending(item => item.Tokens).ToArray();
        var priced = days.Sum(day => day.EstimatedApiUsd);
        _summary.Children.Add(UiElements.Text($"{T("本周期 API 估算", "Cycle API estimate")}   ${priced:0.00}", 21,
            FontWeight.Bold, _palette.Mint));
        _summary.Children.Add(UiElements.Text($"Raw tokens  {total.TotalTokens:N0}  ·  Input {total.InputTokens:N0}  ·  " +
            $"Cached {total.CachedInputTokens:N0}  ·  Output {total.OutputTokens:N0}  ·  Reasoning {total.ReasoningOutputTokens:N0}",
            11, FontWeight.Normal, _palette.TextSecondary));
        _summary.Children.Add(UiElements.Text(T("按公开 API 价格估算，不代表订阅账单或官方额度换算。", "Estimated from public API prices; not a subscription bill or official quota conversion."),
            10.5, FontWeight.Normal, _palette.TextMuted));
        foreach (var slice in slices)
        {
            var value = slice.Unpriced > 0 ? "Unpriced" : $"${slice.Cost:0.000}";
            _summary.Children.Add(SliceRow($"{ApiCostEstimator.DisplayModel(slice.Model)} · {DisplayTier(slice.ServiceTier)}", $"{slice.Tokens:N0} raw", value));
        }

        var maxTokens = Math.Max(1L, days.Count == 0 ? 1 : days.Max(day => day.Usage.TotalTokens));
        foreach (var day in days.OrderByDescending(day => day.Day))
        {
            var row = new StackPanel { Spacing = 6 };
            var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
            header.Children.Add(UiElements.Text(_settings.Language == AppLanguage.SimplifiedChinese
                ? day.Day.ToString("M月d日")
                : day.Day.ToString("MMM d"), 14, FontWeight.Bold, _palette.TextPrimary));
            var cost = UiElements.Text(day.UnpricedEventCount > 0 ? $"${day.EstimatedApiUsd:0.00} + Unpriced" : $"${day.EstimatedApiUsd:0.00}",
                11.5, FontWeight.Bold, _palette.Mint);
            Grid.SetColumn(cost, 1);
            header.Children.Add(cost);
            row.Children.Add(header);
            var bar = new ProgressBar { Minimum = 0, Maximum = maxTokens, Value = day.Usage.TotalTokens,
                Height = 7, Foreground = _palette.Mint, Background = _palette.Border };
            ToolTip.SetTip(bar, $"{day.Day:yyyy-MM-dd}\n{day.Usage.TotalTokens:N0} raw tokens\n${day.EstimatedApiUsd:0.0000}");
            row.Children.Add(bar);
            foreach (var slice in day.Slices)
                row.Children.Add(SliceRow($"{ApiCostEstimator.DisplayModel(slice.Model)} · {DisplayTier(slice.ServiceTier)}",
                    $"{slice.Usage.TotalTokens:N0} raw", slice.UnpricedEventCount > 0 ? "Unpriced" : $"${slice.EstimatedApiUsd:0.000}"));
            _daily.Children.Add(UiElements.Card(row, _palette));
        }
        if (days.Count == 0)
            _daily.Children.Add(UiElements.Text(T("当前周期尚未发现 Token 记录", "No token records found in this cycle"),
                12, FontWeight.Normal, _palette.TextMuted));
    }

    private Control BuildContent()
    {
        var panel = new StackPanel { Spacing = 16, Margin = new Thickness(24, 18, 24, 24), Children =
        {
            UiElements.Text(T("使用明细", "Usage details"), 25, FontWeight.Bold, _palette.TextPrimary),
            UiElements.Text(T("模型与速率保持英文，避免 Fast/Default 等术语混淆", "Model and service-tier names remain in English"),
                11, FontWeight.Normal, _palette.TextMuted),
            UiElements.Card(_summary, _palette),
            UiElements.Text(T("每日使用", "Daily usage"), 17, FontWeight.Bold, _palette.TextPrimary),
            UiElements.Card(new StackPanel { Spacing = 7, Children =
            {
                UiElements.Text(T("当前重置周期", "Current reset cycle"), 12.5, FontWeight.SemiBold, _palette.TextPrimary),
                _dailyChart,
                UiElements.Text(T("悬停柱形可查看日期、Token 与 API 估算；无使用的日期仍会保留。",
                        "Hover a bar for date, tokens and API estimate; zero-usage days remain visible."),
                    10, FontWeight.Normal, _palette.TextMuted)
            }}, _palette),
            _daily
        }};
        return new ScrollViewer { Content = panel,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
    }

    private Control SliceRow(string name, string tokens, string cost)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 14 };
        grid.Children.Add(UiElements.Text(name, 11.5, FontWeight.SemiBold, _palette.TextPrimary));
        var token = UiElements.Text(tokens, 10.5, FontWeight.Normal, _palette.TextMuted);
        Grid.SetColumn(token, 1);
        grid.Children.Add(token);
        var price = UiElements.Text(cost, 10.5, FontWeight.SemiBold, cost == "Unpriced" ? _palette.Amber : _palette.Mint);
        Grid.SetColumn(price, 2);
        grid.Children.Add(price);
        return grid;
    }

    private static string DisplayTier(string tier) => string.IsNullOrWhiteSpace(tier) || tier.Equals("unknown", StringComparison.OrdinalIgnoreCase)
        ? "Default" : ApiCostEstimator.DisplayTier(tier);
    private string T(string zh, string en) => _settings.Language == AppLanguage.SimplifiedChinese ? zh : en;
}
