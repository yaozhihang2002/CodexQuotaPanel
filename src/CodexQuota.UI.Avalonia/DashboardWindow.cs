using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CodexQuota.Application;
using CodexQuota.Domain;

namespace CodexQuota.UI.Avalonia;

public sealed class DashboardWindow : Window
{
    private readonly UiPalette _palette;
    private readonly AppSettings _settings;
    private readonly OrbControl _summaryOrb;
    private readonly TextBlock _plan;
    private readonly TextBlock _headline;
    private readonly TextBlock _forecast;
    private readonly TextBlock _source;
    private readonly Border _connectionBadge;
    private readonly TextBlock _connectionText;
    private readonly StackPanel _windowCards;
    private readonly TrendChartControl _trend;
    private readonly DailyUsageChartControl _dailyUsage;
    private readonly TextBlock _credit;
    private readonly TextBlock _tokenTotal;
    private bool _allowClose;

    public event EventHandler? CollapseRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? UsageDetailsRequested;

    internal int WindowCardCount => _windowCards.Children.Count;
    internal int DailyChartDayCount => _dailyUsage.RenderedDayCount;
    internal string ConnectionBadgeText => _connectionText.Text ?? string.Empty;
    internal int SummaryRingCount => double.IsFinite(_summaryOrb.SecondaryRemainingPercent) ? 2 :
        string.IsNullOrWhiteSpace(_summaryOrb.PrimaryLabel) ? 0 : 1;

    public DashboardWindow(AppSettings settings, bool systemDark = true)
    {
        _settings = settings.Normalize();
        _palette = UiPalette.For(_settings.Theme, systemDark);
        Title = _settings.Language == AppLanguage.SimplifiedChinese ? "Codex 额度详情" : "Codex quota details";
        var scale = _settings.InterfaceScalePercent / 100d;
        Width = Math.Clamp(452 * (.65 + .35 * scale), 420, 560);
        Height = Math.Clamp(700 * (.72 + .28 * scale), 620, 800);
        MinWidth = 420;
        MinHeight = 590;
        MaxWidth = 560;
        Background = _palette.Canvas;
        CanResize = true;
        Topmost = _settings.AlwaysOnTop;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RenderTransformOrigin = RelativePoint.Center;
        RenderTransform = new ScaleTransform(1, 1);

        _summaryOrb = new OrbControl { Width = 138, Height = 138, HorizontalAlignment = HorizontalAlignment.Left };
        _plan = UiElements.Text("—", 11, FontWeight.Bold, _palette.Mint);
        _headline = UiElements.Text(T("等待数据", "Waiting for data"), 28, FontWeight.Bold, _palette.TextPrimary);
        _forecast = UiElements.Text(T("正在连接 Codex 数据源", "Connecting to Codex data"), 12, FontWeight.Normal, _palette.TextSecondary);
        _source = UiElements.Text("—", 10.5, FontWeight.Normal, _palette.TextMuted);
        _connectionText = UiElements.Text(T("连接中", "CONNECTING"), 10, FontWeight.Bold, _palette.Amber,
            TextWrapping.NoWrap);
        _connectionBadge = new Border
        {
            Background = _palette.SurfaceRaised,
            BorderBrush = _palette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(999),
            Padding = new Thickness(11, 5),
            VerticalAlignment = VerticalAlignment.Center,
            Child = _connectionText
        };
        _windowCards = new StackPanel { Spacing = 10, HorizontalAlignment = HorizontalAlignment.Stretch };
        _trend = new TrendChartControl { Height = 82, IsDark = _settings.Theme != AppTheme.Light };
        _dailyUsage = new DailyUsageChartControl
        {
            Height = 132,
            Language = _settings.Language,
            IsDark = _settings.Theme != AppTheme.Light
        };
        _credit = UiElements.Text(T("重置卡信息暂不可用", "Reset credit information unavailable"), 11.5, FontWeight.Normal, _palette.TextSecondary);
        _tokenTotal = UiElements.Text(T("本周期 Token：—", "Cycle tokens: —"), 11.5, FontWeight.SemiBold, _palette.TextSecondary);
        Content = BuildContent();
        Closing += (_, e) => { if (!_allowClose) { e.Cancel = true; CollapseRequested?.Invoke(this, EventArgs.Empty); } };
    }

    public void ApplyPresentation(QuotaPresentation presentation)
    {
        var windows = presentation.Snapshot?.VisibleWindows ?? [];
        if (windows.Count > 0)
        {
            var ringSelection = UiElements.SelectRingWindows(windows, _settings);
            var outerWindow = ringSelection.Outer!;
            var innerWindow = ringSelection.Inner;
            _summaryOrb.RemainingPercent = outerWindow.ClampedRemainingPercent;
            _summaryOrb.PrimaryLabel = UiElements.ShortWindowLabel(outerWindow.WindowMinutes);
            _summaryOrb.SecondaryRemainingPercent = innerWindow?.ClampedRemainingPercent ?? double.NaN;
            _summaryOrb.SecondaryLabel = innerWindow is null ? string.Empty : UiElements.ShortWindowLabel(innerWindow.WindowMinutes);
            _summaryOrb.Caption = T("剩余", "REMAINING");
            _plan.Text = string.IsNullOrWhiteSpace(presentation.Snapshot?.PlanType)
                ? T("额度方案", "QUOTA PLAN")
                : $"{presentation.Snapshot.PlanType!.ToUpperInvariant()}  {T("方案", "PLAN")}";
            var minimum = windows.Min(window => window.ClampedRemainingPercent);
            _headline.Text = minimum switch
            {
                <= 0 => T("额度已耗尽", "Quota exhausted"),
                <= 10 => T("额度紧张", "Quota critical"),
                <= 20 => T("注意用量", "Watch usage"),
                _ => T("额度充足", "Quota healthy")
            };
        }
        else
        {
            _summaryOrb.RemainingPercent = 0;
            _summaryOrb.SecondaryRemainingPercent = double.NaN;
            _summaryOrb.PrimaryLabel = string.Empty;
            _summaryOrb.SecondaryLabel = string.Empty;
            _headline.Text = T("等待数据", "Waiting for data");
        }

        _summaryOrb.OrbBackground = Color.Parse(_settings.OrbBackground);
        _summaryOrb.OuterRingColor = Color.Parse(_settings.OuterRingColor);
        _summaryOrb.InnerRingColor = Color.Parse(_settings.InnerRingColor);
        _summaryOrb.FeedbackEnabled = _settings.ConsumptionFeedbackEnabled;
        _summaryOrb.FeedbackStyle = _settings.ConsumptionFeedbackStyle;
        _summaryOrb.AnimateFeedback = !_settings.ReducedMotion;
        _summaryOrb.FeedbackIntensity = Math.Clamp((presentation.Forecast?.PercentPerHour ?? 0) / 8d, 0, 1);
        _summaryOrb.ConnectionState = presentation.ConnectionState;
        _forecast.Text = ForecastText(presentation.Forecast);
        _source.Text = presentation.Error is not null
            ? presentation.Error
            : $"{presentation.Snapshot?.Source ?? T("尚未连接", "Not connected")} · {T("更新于", "Updated")} " +
              (presentation.UpdatedAt == DateTimeOffset.MinValue ? "—" : presentation.UpdatedAt.ToLocalTime().ToString("HH:mm:ss"));
        ApplyConnectionState(presentation);

        _windowCards.Children.Clear();
        foreach (var window in windows.OrderByDescending(window => window.WindowMinutes))
            _windowCards.Children.Add(BuildWindowCard(window, presentation.History));
        if (windows.Count == 0)
            _windowCards.Children.Add(UiElements.Card(UiElements.Text(T("等待额度快照", "Waiting for quota snapshot"), 12,
                FontWeight.Normal, _palette.TextMuted), _palette));

        var selected = windows.OrderByDescending(window => window.WindowMinutes).FirstOrDefault();
        _trend.Points = selected is null ? [] : presentation.History.Where(point =>
            point.WindowId == selected.Id && point.WindowMinutes == selected.WindowMinutes).ToArray();
        _trend.ResetAt = selected?.ResetsAt;
        _trend.WindowMinutes = selected?.WindowMinutes ?? 0;
        var cycleStart = selected?.ResetsAt?.AddMinutes(-selected.WindowMinutes);
        var days = UsageSummaryCalculator.SummarizeByDay(presentation.Usage.Where(item =>
            cycleStart is null || item.ObservedAt >= cycleStart), TimeZoneInfo.Local);
        _dailyUsage.Days = days;
        _dailyUsage.CycleStart = cycleStart;
        _dailyUsage.CycleEnd = selected?.ResetsAt;
        var reset = presentation.Snapshot?.SoonestAvailableResetCredit;
        _credit.Text = reset?.ExpiresAt is { } expiry
            ? $"{T("最早到期重置卡", "Next reset credit")} · {expiry.ToLocalTime():yyyy-MM-dd HH:mm}"
            : T("重置卡信息暂不可用", "Reset credit information unavailable");
        _tokenTotal.Text = $"{T("本周期 Token", "Cycle tokens")}：{presentation.Usage.Sum(item => item.TotalTokens):N0}";
    }

    public async Task AnimateInAsync()
    {
        if (_settings.ReducedMotion) { Opacity = 1; Show(); Activate(); return; }
        Opacity = 0;
        if (RenderTransform is ScaleTransform start) start.ScaleX = .94;
        if (RenderTransform is ScaleTransform startY) startY.ScaleY = .88;
        Show();
        Activate();
        await AnimateAsync(0, 1, .94, 1, .88, 1, 125);
    }

    public async Task AnimateOutAsync()
    {
        if (_settings.ReducedMotion) { Hide(); return; }
        await AnimateAsync(1, 0, 1, .94, 1, .88, 105);
        Hide();
    }

    public void ClosePermanently() { _allowClose = true; Close(); }

    private Control BuildContent()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Background = _palette.Canvas };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8,
            Margin = new Thickness(20, 15, 16, 12) };
        header.Children.Add(new StackPanel { Spacing = 1, Children =
        {
            UiElements.Text("CODEX · " + T("额度", "QUOTA"), 19, FontWeight.Bold, _palette.TextPrimary),
            UiElements.Text(T("实时额度限制", "Live quota limits"), 10.5, FontWeight.Normal, _palette.TextMuted)
        }});
        Grid.SetColumn(_connectionBadge, 1);
        header.Children.Add(_connectionBadge);
        var settings = UiElements.Button("···", _palette);
        settings.Width = 42;
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        Grid.SetColumn(settings, 2);
        header.Children.Add(settings);
        root.Children.Add(header);

        var scrollContent = new StackPanel { Spacing = 12, Margin = new Thickness(18, 4, 18, 16),
            HorizontalAlignment = HorizontalAlignment.Stretch };
        var hero = new Grid { ColumnDefinitions = new ColumnDefinitions("140,*") };
        hero.Children.Add(_summaryOrb);
        var copy = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
            Children = { _plan, _headline, _forecast, _source } };
        Grid.SetColumn(copy, 1);
        hero.Children.Add(copy);
        scrollContent.Children.Add(hero);
        scrollContent.Children.Add(UiElements.Text(T("额度窗口", "QUOTA WINDOWS"), 11, FontWeight.Bold, _palette.TextMuted));
        scrollContent.Children.Add(_windowCards);
        scrollContent.Children.Add(UiElements.Card(new StackPanel { Spacing = 7, Children =
        {
            UiElements.Text(T("24 小时趋势与均匀使用线", "24-hour trend and even-use guide"), 12.5, FontWeight.SemiBold, _palette.TextPrimary),
            _trend,
            UiElements.Text(T("将鼠标放在曲线上可查看实际与均匀额度", "Hover the chart for actual and even-use values"), 10, FontWeight.Normal, _palette.TextMuted)
        }}, _palette));
        scrollContent.Children.Add(UiElements.Card(new StackPanel { Spacing = 7, Children =
        {
            UiElements.Text(T("本周期每日消耗", "DAILY USAGE · CURRENT CYCLE"), 12.5, FontWeight.SemiBold, _palette.TextPrimary),
            _dailyUsage,
            UiElements.Text(T("悬停柱形查看具体日期、Token 和 API 估算", "Hover a bar for date, tokens and API estimate"),
                10, FontWeight.Normal, _palette.TextMuted)
        }}, _palette));
        scrollContent.Children.Add(UiElements.Card(new StackPanel { Spacing = 6, Children = { _credit, _tokenTotal } }, _palette));
        var usage = UiElements.Button(T("查看使用明细", "Usage details"), _palette);
        usage.Click += (_, _) => UsageDetailsRequested?.Invoke(this, EventArgs.Empty);
        scrollContent.Children.Add(usage);
        var scroll = new ScrollViewer { Content = scrollContent,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var footer = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(18, 10, 18, 16) };
        var refresh = UiElements.Button(T("刷新", "Refresh"), _palette);
        refresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        footer.Children.Add(refresh);
        var collapse = UiElements.Button(T("收起为悬浮球", "Collapse to orb"), _palette, true);
        collapse.HorizontalAlignment = HorizontalAlignment.Stretch;
        collapse.Margin = new Thickness(10, 0, 0, 0);
        collapse.Click += (_, _) => CollapseRequested?.Invoke(this, EventArgs.Empty);
        Grid.SetColumn(collapse, 1);
        footer.Children.Add(collapse);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private Control BuildWindowCard(QuotaWindow window, IReadOnlyList<QuotaHistoryPoint> history)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(UiElements.Text(UiElements.WindowLabel(window, _settings.Language), 15, FontWeight.Bold, _palette.TextPrimary));
        var percent = UiElements.Text($"{window.ClampedRemainingPercent:0}% {T("剩余", "left")}", 13, FontWeight.Bold, _palette.Mint);
        Grid.SetColumn(percent, 1);
        header.Children.Add(percent);
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = window.ClampedRemainingPercent, Height = 5,
            Foreground = _palette.Mint, Background = _palette.Border };
        var points = history.Where(point => point.WindowId == window.Id && point.WindowMinutes == window.WindowMinutes).ToArray();
        var chart = new TrendChartControl { Height = 54, Points = points, ResetAt = window.ResetsAt,
            WindowMinutes = window.WindowMinutes, IsDark = _settings.Theme != AppTheme.Light };
        return UiElements.Card(new StackPanel { Spacing = 7, Children =
        {
            header, bar, chart,
            UiElements.Text($"{UiElements.RemainingTime(window.ResetsAt, _settings.Language)} · {window.ResetsAt?.ToLocalTime():MM-dd HH:mm}",
                10.5, FontWeight.Normal, _palette.TextMuted)
        }}, _palette);
    }

    private string ForecastText(UsageForecast? forecast)
    {
        if (forecast is null) return T("续航预测：等待更多样本", "Forecast: collecting more samples");
        var state = forecast.State switch
        {
            ForecastState.Sustainable => T("可持续到重置", "Sustainable until reset"),
            ForecastState.AtRisk => T("按当前速度可能提前耗尽", "May exhaust before reset"),
            ForecastState.Exhausted => T("额度已耗尽", "Quota exhausted"),
            _ => T("等待更多样本", "Collecting more samples")
        };
        var confidence = forecast.Confidence switch
        {
            ForecastConfidence.High => T("高置信度", "high confidence"),
            ForecastConfidence.Medium => T("中等置信度", "medium confidence"),
            ForecastConfidence.Low => T("低置信度", "low confidence"),
            _ => T("置信度不足", "confidence unavailable")
        };
        return $"{state} · {confidence}\n{T("当前", "Current")} {forecast.PercentPerHour:0.##}%/h · {T("安全", "safe")} {forecast.SustainablePercentPerHour:0.##}%/h";
    }

    private void ApplyConnectionState(QuotaPresentation presentation)
    {
        var (label, color, background) = presentation.ConnectionState switch
        {
            QuotaConnectionState.Live => (T("实时", "LIVE"), _palette.Mint, _palette.Active),
            QuotaConnectionState.LocalFallback => (T("本地回退", "LOCAL"), _palette.Blue, _palette.SurfaceRaised),
            QuotaConnectionState.Stale => (T("数据陈旧", "STALE"), _palette.Amber, _palette.SurfaceRaised),
            QuotaConnectionState.Offline => (T("未连接", "OFFLINE"), _palette.Red, _palette.SurfaceRaised),
            _ => (T("连接中", "CONNECTING"), _palette.Amber, _palette.SurfaceRaised)
        };
        _connectionText.Text = label;
        _connectionText.Foreground = color;
        _connectionBadge.Background = background;
        ToolTip.SetTip(_connectionBadge, presentation.ConnectionDetail ?? _source.Text);
    }

    private async Task AnimateAsync(double fromOpacity, double toOpacity, double sx0, double sx1, double sy0, double sy1, int ms)
    {
        const int frames = 10;
        for (var i = 0; i <= frames; i++)
        {
            var t = i / (double)frames;
            var eased = 1 - Math.Pow(1 - t, 3);
            Opacity = fromOpacity + (toOpacity - fromOpacity) * eased;
            if (RenderTransform is ScaleTransform transform)
            {
                transform.ScaleX = sx0 + (sx1 - sx0) * eased;
                transform.ScaleY = sy0 + (sy1 - sy0) * eased;
            }
            await Task.Delay(Math.Max(1, ms / frames));
        }
    }

    private string T(string zh, string en) => _settings.Language == AppLanguage.SimplifiedChinese ? zh : en;
}
