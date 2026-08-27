using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
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
    private readonly Grid _windowCards;
    private readonly TrendChartControl _trend;
    private readonly DailyUsageChartControl _dailyUsage;
    private readonly Border _creditCard;
    private readonly TextBlock _creditBadge;
    private readonly TextBlock _credit;
    private readonly TextBlock _creditMeta;
    private readonly TextBlock _tokenTotal;
    private readonly DispatcherTimer _placementTimer;
    private bool _allowClose;
    private bool _trackPlacement;
    private bool _placementDirty;

    public event EventHandler? CollapseRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? UsageDetailsRequested;
    public event Action<PixelPoint, string>? PlacementCommitted;

    internal int WindowCardCount => _windowCards.Children.Count;
    internal int DailyChartDayCount => _dailyUsage.RenderedDayCount;
    internal string ConnectionBadgeText => _connectionText.Text ?? string.Empty;
    internal string ForecastDisplayText => _forecast.Text ?? string.Empty;
    internal string ResetCreditDisplayText => _credit.Text ?? string.Empty;
    internal string ResetCreditMetaText => _creditMeta.Text ?? string.Empty;
    internal bool ResetCreditIsProminent => ReferenceEquals(_creditCard.BorderBrush, _palette.Amber);
    internal int SummaryRingCount => double.IsFinite(_summaryOrb.SecondaryRemainingPercent) ? 2 :
        string.IsNullOrWhiteSpace(_summaryOrb.PrimaryLabel) ? 0 : 1;
    internal Rect SummaryOrbBoundsInWindow
    {
        get
        {
            var origin = _summaryOrb.TranslatePoint(default, this) ?? default;
            return new Rect(origin, _summaryOrb.Bounds.Size);
        }
    }
    internal void EnablePlacementTrackingForTest() => _trackPlacement = true;
    internal void CommitPlacementForTest() { _placementDirty = true; FlushPlacement(); }

    public DashboardWindow(AppSettings settings, bool systemDark = true)
    {
        _settings = settings.Normalize();
        _palette = UiPalette.For(_settings.Theme, systemDark);
        Title = _settings.Language == AppLanguage.SimplifiedChinese ? "Codex 额度详情" : "Codex quota details";
        var scale = _settings.InterfaceScalePercent / 100d;
        var englishWidthAllowance = _settings.Language == AppLanguage.English
            ? Math.Clamp(8 + (_settings.InterfaceScalePercent - 100) * .44, 8, 30)
            : 0;
        Width = Math.Clamp(452 * (.65 + .35 * scale) + englishWidthAllowance, 420, 580);
        Height = Math.Clamp(690 * (.72 + .28 * scale), 620, 780);
        MinWidth = 420;
        MinHeight = 570;
        MaxWidth = 580;
        Background = _palette.Canvas;
        CanResize = true;
        Topmost = _settings.AlwaysOnTop;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        RenderTransformOrigin = RelativePoint.Center;
        RenderTransform = new ScaleTransform(1, 1);

        _summaryOrb = new OrbControl
        {
            Width = 110,
            Height = 110,
            MinWidth = 110,
            MinHeight = 110,
            HorizontalAlignment = HorizontalAlignment.Left,
            ClipToBounds = false
        };
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
        _windowCards = new Grid
        {
            ColumnSpacing = 8,
            RowSpacing = 8,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        _trend = new TrendChartControl { Height = 46, IsDark = _settings.Theme != AppTheme.Light };
        ToolTip.SetTip(_trend, T("悬停曲线可对照实际与均匀额度", "Hover to compare actual and even-use quota"));
        _dailyUsage = new DailyUsageChartControl
        {
            Height = 70,
            Language = _settings.Language,
            IsDark = _settings.Theme != AppTheme.Light
        };
        _creditBadge = UiElements.Text(T("重置卡", "RESET"), 9.2, FontWeight.Bold, _palette.TextMuted,
            TextWrapping.NoWrap);
        _credit = UiElements.Text(T("重置卡信息暂不可用", "Reset credit information unavailable"), 11.5,
            FontWeight.Bold, _palette.TextSecondary);
        _creditMeta = UiElements.Text(T("等待 Codex 返回可用重置卡", "Waiting for available reset credits from Codex"),
            9, FontWeight.Normal, _palette.TextMuted);
        _creditCard = BuildResetCreditCard();
        _tokenTotal = UiElements.Text(T("本周期 API 估算：—", "Cycle API estimate: —"), 11.5, FontWeight.SemiBold, _palette.TextSecondary);
        _placementTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        _placementTimer.Tick += (_, _) => FlushPlacement();
        Content = BuildContent();
        Closing += (_, e) => { if (!_allowClose) { e.Cancel = true; CollapseRequested?.Invoke(this, EventArgs.Empty); } };
        PositionChanged += (_, _) =>
        {
            if (!_trackPlacement || !IsVisible) return;
            _placementDirty = true;
            _placementTimer.Stop();
            _placementTimer.Start();
        };
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
        _summaryOrb.FeedbackIntensity = ConsumptionFeedbackIntensity.From(presentation.Forecast);
        _summaryOrb.ConnectionState = presentation.ConnectionState;
        _forecast.Text = ForecastText(presentation.Forecast, windows, presentation.UpdatedAt);
        _source.Text = presentation.Error is not null
            ? presentation.Error
            : $"{presentation.Snapshot?.Source ?? T("尚未连接", "Not connected")} · {T("更新于", "Updated")} " +
              (presentation.UpdatedAt == DateTimeOffset.MinValue ? "—" : presentation.UpdatedAt.ToLocalTime().ToString("HH:mm:ss"));
        ApplyConnectionState(presentation);

        _windowCards.Children.Clear();
        _windowCards.ColumnDefinitions.Clear();
        _windowCards.RowDefinitions.Clear();
        var orderedWindows = windows.OrderByDescending(window => window.WindowMinutes).ToArray();
        var columnCount = Math.Min(2, Math.Max(1, orderedWindows.Length));
        for (var column = 0; column < columnCount; column++)
            _windowCards.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        var rowCount = Math.Max(1, (int)Math.Ceiling(orderedWindows.Length / (double)columnCount));
        for (var row = 0; row < rowCount; row++)
            _windowCards.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        for (var index = 0; index < orderedWindows.Length; index++)
        {
            var card = BuildWindowCard(orderedWindows[index]);
            Grid.SetColumn(card, index % columnCount);
            Grid.SetRow(card, index / columnCount);
            _windowCards.Children.Add(card);
        }
        if (windows.Count == 0)
            _windowCards.Children.Add(UiElements.Card(UiElements.Text(T("等待额度快照", "Waiting for quota snapshot"), 12,
                FontWeight.Normal, _palette.TextMuted), _palette));

        var selected = windows.OrderByDescending(window => window.WindowMinutes).FirstOrDefault();
        _trend.Points = selected is null ? [] : presentation.History.Where(point =>
            point.WindowId == selected.Id && point.WindowMinutes == selected.WindowMinutes).ToArray();
        _trend.ResetAt = selected?.ResetsAt;
        _trend.WindowMinutes = selected?.WindowMinutes ?? 0;
        var cycleStart = selected?.ResetsAt?.AddMinutes(-selected.WindowMinutes);
        var cycleUsage = presentation.Usage.Where(item =>
                (cycleStart is null || item.ObservedAt >= cycleStart) &&
                (selected?.ResetsAt is null || item.ObservedAt <= selected.ResetsAt))
            .ToArray();
        var days = UsageSummaryCalculator.SummarizeByDay(cycleUsage, TimeZoneInfo.Local);
        _dailyUsage.Days = days;
        _dailyUsage.CycleStart = cycleStart;
        _dailyUsage.CycleEnd = selected?.ResetsAt;
        var reset = presentation.Snapshot?.SoonestAvailableResetCredit;
        if (reset?.ExpiresAt is { } expiry)
        {
            var reference = presentation.UpdatedAt == DateTimeOffset.MinValue
                ? DateTimeOffset.Now
                : presentation.UpdatedAt;
            var remaining = expiry - reference;
            var availableCount = presentation.Snapshot?.ResetCredits?.Count(credit =>
                credit.Status.Equals("available", StringComparison.OrdinalIgnoreCase) &&
                credit.ExpiresAt > reference) ?? 0;
            _creditBadge.Text = T("重置卡 · 可用", "RESET · AVAILABLE");
            _creditBadge.Foreground = _palette.Amber;
            _credit.Text = remaining > TimeSpan.Zero
                ? T($"最早一张将在 {FormatDuration(remaining)} 后到期",
                    $"Earliest card expires in {FormatDuration(remaining)}")
                : T("最早一张重置卡即将到期", "The earliest reset credit is expiring now");
            _credit.Foreground = _palette.TextPrimary;
            _creditMeta.Text = $"{T($"{availableCount} 张可用", $"{availableCount} available")} · " +
                               $"{T("有效至", "valid until")} {expiry.ToLocalTime():yyyy-MM-dd HH:mm}";
            _creditCard.BorderBrush = _palette.Amber;
        }
        else
        {
            _creditBadge.Text = T("重置卡", "RESET CREDIT");
            _creditBadge.Foreground = _palette.TextMuted;
            _credit.Text = T("重置卡信息暂不可用", "Reset credit information unavailable");
            _credit.Foreground = _palette.TextSecondary;
            _creditMeta.Text = T("等待 Codex 返回可用重置卡", "Waiting for available reset credits from Codex");
            _creditCard.BorderBrush = _palette.Border;
        }
        var estimatedCost = days.Sum(day => day.EstimatedApiUsd);
        var unpriced = days.Sum(day => day.UnpricedEventCount);
        _tokenTotal.Text = $"{T("本周期 API 估算", "Cycle API estimate")}：${estimatedCost:0.00}" +
                           (unpriced > 0 ? T(" + 未定价", " + unpriced") : string.Empty);
    }

    public void PlaceNear(PixelPoint orbPosition, double orbLogicalSize)
    {
        var orbScreen = Screens.ScreenFromPoint(orbPosition) ?? Screens.Primary;
        if (orbScreen is null) return;
        var scale = orbScreen.Scaling;
        var work = orbScreen.WorkingArea;
        var panelWidth = (int)Math.Ceiling(Width * scale);
        var panelHeight = (int)Math.Ceiling(Height * scale);
        var orbSize = (int)Math.Ceiling(orbLogicalSize * scale);
        var x = (int)Math.Round(orbPosition.X + orbSize / 2d - panelWidth / 2d);
        var y = (int)Math.Round(orbPosition.Y + orbSize / 2d - panelHeight / 2d);
        x = Math.Clamp(x, work.X, Math.Max(work.X, work.Right - panelWidth));
        y = Math.Clamp(y, work.Y, Math.Max(work.Y, work.Bottom - panelHeight));
        SetAutomaticPosition(new PixelPoint(x, y));
    }

    public bool RestorePosition(double? x, double? y, string? displayId = null)
    {
        if (x is null || y is null) return false;
        var desired = new PixelPoint((int)Math.Round(x.Value), (int)Math.Round(y.Value));
        var screen = Screens.All.FirstOrDefault(item => DisplayId(item) == displayId) ??
                     Screens.ScreenFromPoint(desired) ?? Screens.Primary;
        if (screen is null) return false;
        var work = screen.WorkingArea;
        var panelWidth = (int)Math.Ceiling(Width * screen.Scaling);
        var panelHeight = (int)Math.Ceiling(Height * screen.Scaling);
        var restored = new PixelPoint(
            Math.Clamp(desired.X, work.X, Math.Max(work.X, work.Right - panelWidth)),
            Math.Clamp(desired.Y, work.Y, Math.Max(work.Y, work.Bottom - panelHeight)));
        SetAutomaticPosition(restored);
        return true;
    }

    public async Task AnimateInAsync()
    {
        _trackPlacement = false;
        if (_settings.ReducedMotion) { Opacity = 1; Show(); Activate(); _trackPlacement = true; return; }
        Opacity = 0;
        if (RenderTransform is ScaleTransform start) start.ScaleX = .94;
        if (RenderTransform is ScaleTransform startY) startY.ScaleY = .88;
        Show();
        Activate();
        await AnimateAsync(0, 1, .94, 1, .88, 1, 125);
        _trackPlacement = true;
    }

    public async Task AnimateOutAsync()
    {
        FlushPlacement();
        _trackPlacement = false;
        if (_settings.ReducedMotion) { Hide(); return; }
        await AnimateAsync(1, 0, 1, .94, 1, .88, 105);
        Hide();
    }

    public void ClosePermanently()
    {
        FlushPlacement();
        _placementTimer.Stop();
        _allowClose = true;
        Close();
    }

    private void SetAutomaticPosition(PixelPoint position)
    {
        _trackPlacement = false;
        _placementDirty = false;
        _placementTimer.Stop();
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = position;
    }

    private void FlushPlacement()
    {
        _placementTimer.Stop();
        if (!_placementDirty) return;
        _placementDirty = false;
        var screen = Screens.ScreenFromPoint(Position) ?? Screens.Primary;
        PlacementCommitted?.Invoke(Position, screen is null ? string.Empty : DisplayId(screen));
    }

    private static string DisplayId(Screen screen) =>
        $"{screen.Bounds.X},{screen.Bounds.Y},{screen.Bounds.Width},{screen.Bounds.Height}";

    private Control BuildContent()
    {
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto"), Background = _palette.Canvas };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto"), ColumnSpacing = 8,
            Margin = new Thickness(18, 12, 14, 8) };
        header.Children.Add(new StackPanel { Spacing = 1, Children =
        {
            UiElements.Text("CODEX · " + T("额度", "QUOTA"), 19, FontWeight.Bold, _palette.TextPrimary),
            UiElements.Text(T("实时额度限制", "Live quota limits"), 10.5, FontWeight.Normal, _palette.TextMuted)
        }});
        Grid.SetColumn(_connectionBadge, 1);
        header.Children.Add(_connectionBadge);
        var settings = UiElements.Button("⋯", _palette);
        settings.Width = 42;
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        Grid.SetColumn(settings, 2);
        header.Children.Add(settings);
        root.Children.Add(header);

        var scrollContent = new StackPanel { Spacing = 7, Margin = new Thickness(18, 0, 18, 8),
            HorizontalAlignment = HorizontalAlignment.Stretch };
        var hero = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("110,*"),
            ColumnSpacing = 10,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ClipToBounds = false
        };
        hero.Children.Add(_summaryOrb);
        var copy = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center,
            Children = { _plan, _headline, _forecast, _source } };
        Grid.SetColumn(copy, 1);
        hero.Children.Add(copy);
        scrollContent.Children.Add(hero);
        scrollContent.Children.Add(_creditCard);
        scrollContent.Children.Add(UiElements.Card(new StackPanel { Spacing = 5, Children =
        {
            UiElements.Text(T("额度窗口与 24 小时节奏", "QUOTA WINDOWS · 24-HOUR PACE"), 12.5,
                FontWeight.SemiBold, _palette.TextPrimary),
            _windowCards,
            new Border { Height = 1, Background = _palette.Border, Margin = new Thickness(0, 1) },
            _trend
        }}, _palette, new Thickness(12, 9)));
        var dailyHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        dailyHeader.Children.Add(UiElements.Text(T("本周期每日消耗", "DAILY USAGE · CURRENT CYCLE"), 12.5,
            FontWeight.SemiBold, _palette.TextPrimary));
        _tokenTotal.FontSize = 10.5 * UiElements.ScaleFactor;
        _tokenTotal.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_tokenTotal, 1);
        dailyHeader.Children.Add(_tokenTotal);
        var usage = UiElements.Button(T("查看使用明细", "Usage details"), _palette);
        usage.MinHeight = 34;
        usage.Padding = new Thickness(12, 6);
        usage.Click += (_, _) => UsageDetailsRequested?.Invoke(this, EventArgs.Empty);
        var dailyFooter = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 8 };
        dailyFooter.Children.Add(UiElements.Text(T("柱顶为 API 等价美元", "Bar labels show API-equivalent USD"),
            9.5, FontWeight.Normal, _palette.TextMuted));
        Grid.SetColumn(usage, 1);
        dailyFooter.Children.Add(usage);
        scrollContent.Children.Add(UiElements.Card(new StackPanel { Spacing = 5, Children =
        {
            dailyHeader,
            _dailyUsage,
            dailyFooter
        }}, _palette, new Thickness(12, 9)));
        var scroll = new ScrollViewer { Content = scrollContent,
            VerticalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        var footer = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("*,*"),
            ColumnSpacing = 10,
            Margin = new Thickness(18, 7, 18, 11)
        };
        var refresh = UiElements.Button(T("刷新", "Refresh"), _palette);
        refresh.HorizontalAlignment = HorizontalAlignment.Stretch;
        refresh.HorizontalContentAlignment = HorizontalAlignment.Center;
        refresh.VerticalContentAlignment = VerticalAlignment.Center;
        refresh.Click += (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty);
        footer.Children.Add(refresh);
        var collapse = UiElements.Button(T("收起为悬浮球", "Collapse to orb"), _palette, true);
        collapse.HorizontalAlignment = HorizontalAlignment.Stretch;
        collapse.HorizontalContentAlignment = HorizontalAlignment.Center;
        collapse.VerticalContentAlignment = VerticalAlignment.Center;
        collapse.Click += (_, _) => CollapseRequested?.Invoke(this, EventArgs.Empty);
        Grid.SetColumn(collapse, 1);
        footer.Children.Add(collapse);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private Border BuildResetCreditCard()
    {
        var accent = new Border
        {
            Width = 3,
            Background = _palette.Amber,
            CornerRadius = new CornerRadius(999),
            Margin = new Thickness(0, 1, 9, 1)
        };
        var summary = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 9 };
        summary.Children.Add(_creditBadge);
        Grid.SetColumn(_credit, 1);
        summary.Children.Add(_credit);
        var copy = new StackPanel { Spacing = 1, Children = { summary, _creditMeta } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { accent, copy } };
        Grid.SetColumn(copy, 1);
        return new Border
        {
            Background = _palette.Surface,
            BorderBrush = _palette.Border,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(11, 7),
            Child = grid
        };
    }

    private Control BuildWindowCard(QuotaWindow window)
    {
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        header.Children.Add(UiElements.Text(DashboardWindowLabel(window), 15, FontWeight.Bold, _palette.TextPrimary));
        var percent = UiElements.Text($"{window.ClampedRemainingPercent:0}% {T("剩余", "left")}", 13, FontWeight.Bold, _palette.Mint);
        Grid.SetColumn(percent, 1);
        header.Children.Add(percent);
        var bar = new ProgressBar { Minimum = 0, Maximum = 100, Value = window.ClampedRemainingPercent, Height = 4,
            Foreground = _palette.Mint, Background = _palette.Border };
        return new Border
        {
            Background = _palette.SurfaceRaised,
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 6),
            Child = new StackPanel { Spacing = 3, Children =
            {
                header, bar,
                UiElements.Text($"{UiElements.RemainingTime(window.ResetsAt, _settings.Language)} · {window.ResetsAt?.ToLocalTime():MM-dd HH:mm}",
                    9.5, FontWeight.Normal, _palette.TextMuted)
            }}
        };
    }

    private string ForecastText(UsageForecast? forecast, IReadOnlyList<QuotaWindow> windows,
        DateTimeOffset updatedAt)
    {
        if (forecast is null)
            return T("续航预测：等待更多样本\n预计可用时长：尚无法判断",
                "Forecast: collecting more samples\nEstimated availability: not enough data");
        var state = forecast.State switch
        {
            ForecastState.Sustainable => T("可持续到重置", "On track to reset"),
            ForecastState.AtRisk => T("按当前速度可能提前耗尽", "At risk before reset"),
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
        var reference = updatedAt == DateTimeOffset.MinValue ? DateTimeOffset.Now : updatedAt;
        var resetAt = windows.FirstOrDefault(window => window.Id == forecast.WindowId)?.ResetsAt;
        string availability;
        if (forecast.State == ForecastState.Exhausted)
        {
            availability = T("预计可用 0 分钟", "Estimated availability: 0 minutes");
        }
        else if (forecast.State == ForecastState.Sustainable && resetAt is { } reset && reset > reference)
        {
            availability = T($"预计至少可用 {FormatDuration(reset - reference)}（至本轮重置）",
                $"At least {FormatDuration(reset - reference)} available · until reset");
        }
        else if (forecast.ExhaustsAt is { } exhaustsAt && exhaustsAt > reference)
        {
            availability = T($"预计可用 {FormatDuration(exhaustsAt - reference)} · {exhaustsAt.ToLocalTime():MM-dd HH:mm} 见底",
                $"~{FormatDuration(exhaustsAt - reference)} available · empty {exhaustsAt.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.InvariantCulture)}");
        }
        else
        {
            availability = T("预计可用时长：尚无法判断", "Estimated availability: not enough data");
        }
        return $"{state} · {confidence}\n{availability}\n{T("当前", "Pace")} {forecast.PercentPerHour:0.##}%/h · {T("安全", "sustainable")} {forecast.SustainablePercentPerHour:0.##}%/h";
    }

    private string DashboardWindowLabel(QuotaWindow window)
    {
        if (_settings.Language != AppLanguage.English)
            return UiElements.WindowLabel(window, _settings.Language);
        if (window.WindowMinutes == 300) return "5-hour";
        if (window.WindowMinutes == 10_080) return "7-day";
        var span = TimeSpan.FromMinutes(window.WindowMinutes);
        return span.TotalDays >= 1 ? $"{span.TotalDays:0.#}-day" : $"{span.TotalHours:0.#}-hour";
    }

    private string FormatDuration(TimeSpan span)
    {
        if (span <= TimeSpan.Zero) return T("0 分钟", "0 min");
        var days = Math.Max(0, (int)span.TotalDays);
        if (days > 0) return T($"{days} 天 {span.Hours} 小时", $"{days}d {span.Hours}h");
        if (span.Hours > 0) return T($"{span.Hours} 小时 {span.Minutes} 分", $"{span.Hours}h {span.Minutes}m");
        return T($"{Math.Max(1, span.Minutes)} 分钟", $"{Math.Max(1, span.Minutes)}m");
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
