using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Threading;
using CodexQuota.Application;
using CodexQuota.Domain;

namespace CodexQuota.UI.Avalonia;

public sealed class DashboardWindow : Window
{
    public event Action<Size>? UserSizeChanged;
    private static readonly Dictionary<WindowEdge, Cursor> ResizeCursors = new()
    {
        [WindowEdge.North] = new(StandardCursorType.SizeNorthSouth),
        [WindowEdge.South] = new(StandardCursorType.SizeNorthSouth),
        [WindowEdge.East] = new(StandardCursorType.SizeWestEast),
        [WindowEdge.West] = new(StandardCursorType.SizeWestEast),
        [WindowEdge.NorthWest] = new(StandardCursorType.TopLeftCorner),
        [WindowEdge.NorthEast] = new(StandardCursorType.TopRightCorner),
        [WindowEdge.SouthWest] = new(StandardCursorType.BottomLeftCorner),
        [WindowEdge.SouthEast] = new(StandardCursorType.BottomRightCorner)
    };

    internal static WindowEdge? ResizeEdgeAt(Point p, Size size)
    {
        if (p.X < 0 || p.Y < 0 || p.X > size.Width || p.Y > size.Height) return null;
        if (p.X <= 12 && p.Y <= 12) return WindowEdge.NorthWest;
        if (p.X >= size.Width - 12 && p.Y <= 12) return WindowEdge.NorthEast;
        if (p.X <= 12 && p.Y >= size.Height - 12) return WindowEdge.SouthWest;
        if (p.X >= size.Width - 12 && p.Y >= size.Height - 12) return WindowEdge.SouthEast;
        if (p.X <= 6) return WindowEdge.West;
        if (p.X >= size.Width - 6) return WindowEdge.East;
        if (p.Y <= 6) return WindowEdge.North;
        if (p.Y >= size.Height - 6) return WindowEdge.South;
        return null;
    }

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
    private readonly Border _balanceCard;
    private readonly TextBlock _balance;
    private readonly TextBlock _tokenTotal;
    private readonly Control _transitionSurface;
    private readonly DispatcherTimer _placementTimer;
    private bool _allowClose;
    private bool _trackPlacement;
    private bool _placementDirty;
    private bool _presented;
    private bool _nativeSurfacePrepared;
    private readonly WindowDisplayRecovery _displayRecovery;
    private PixelPoint? _openingOrbPosition;
    private double _openingOrbSize;
    public event Action? DisplayRecovered;

    public event EventHandler? CollapseRequested;
    public event EventHandler? CloseRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? UsageDetailsRequested;
    public event Action<PixelPoint, string>? PlacementCommitted;
    public event Action<double>? TransitionOpacityChanged;

    public bool UseClientOpacityAnimation { get; set; } = true;
    public Func<Thickness>? InvisibleFrameInsetsProvider
    {
        get => _displayRecovery.InvisibleFrameInsetsProvider;
        set => _displayRecovery.InvisibleFrameInsetsProvider = value;
    }
    public bool IsPresented => UseClientOpacityAnimation ? IsVisible : _presented;

    internal int WindowCardCount => _windowCards.Children.Count;
    internal int DailyChartDayCount => _dailyUsage.RenderedDayCount;
    internal string ConnectionBadgeText => _connectionText.Text ?? string.Empty;
    internal string ForecastDisplayText => _forecast.Text ?? string.Empty;
    internal bool SourceDetailVisible => _source.IsVisible;
    internal string ConnectionTipText => ToolTip.GetTip(_connectionBadge)?.ToString() ?? string.Empty;
    internal string ForecastTipText => ToolTip.GetTip(_forecast)?.ToString() ?? string.Empty;
    internal string ResetCreditDisplayText => _credit.Text ?? string.Empty;
    internal string ResetCreditMetaText => _creditMeta.Text ?? string.Empty;
    internal bool CreditBalanceVisible => _balanceCard.IsVisible;
    internal string CreditBalanceText => _balance.Text ?? string.Empty;
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

    public DashboardWindow(AppSettings settings, bool systemDark = true, UiPalette? previewPalette = null)
    {
        _settings = settings.Normalize();
        _palette = previewPalette ?? UiPalette.For(_settings.Theme, systemDark);
        Title = _settings.Language == AppLanguage.SimplifiedChinese ? "Codex 额度详情" : "Codex quota details";
        var scale = _settings.InterfaceScalePercent / 100d;
        var englishWidthAllowance = _settings.Language == AppLanguage.English
            ? Math.Clamp(8 + (_settings.InterfaceScalePercent - 100) * .44, 8, 30)
            : 0;
        Width = Math.Clamp(452 * (.65 + .35 * scale) + englishWidthAllowance, 420, 580);
        Height = Math.Clamp(640 * (.72 + .28 * scale), 600, 780);
        MinWidth = 420;
        MinHeight = 570;
        var savedSize = settings.Normalize();
        Width = savedSize.DashboardWidth ?? Width;
        Height = savedSize.DashboardHeight ?? Height;
        WindowDecorations = WindowDecorations.None;
        TransparencyLevelHint = [WindowTransparencyLevel.Transparent];
        Background = Brushes.Transparent;
        CanResize = true;
        AddHandler(PointerPressedEvent, (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed &&
                ResizeEdgeAt(e.GetPosition(this), Bounds.Size) is { } edge)
            {
                e.Handled = true;
                BeginResizeDrag(edge, e);
            }
        }, RoutingStrategies.Tunnel);
        PointerMoved += (_, e) => Cursor = ResizeEdgeAt(e.GetPosition(this), Bounds.Size) is { } edge
            ? ResizeCursors[edge] : null;
        PointerExited += (_, _) => Cursor = null;
        // This is a tray utility surface. Set the Avalonia hint before the
        // native HWND is created; changing it during first-show prewarm is too
        // late for Explorer on some Windows configurations.
        ShowInTaskbar = false;
        Topmost = _settings.AlwaysOnTop;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _summaryOrb = new OrbControl
        {
            Width = 110,
            Height = 110,
            MinWidth = 110,
            MinHeight = 110,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
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
        _trend = new TrendChartControl { Height = 40, IsDark = UiPalette.IsDarkTheme(_settings.Theme, systemDark) };
        ToolTip.SetTip(_trend, T("在绘图区移动鼠标可对照实际与均匀额度", "Move across the plot to compare actual and even-use quota"));
        _dailyUsage = new DailyUsageChartControl
        {
            Height = 90,
            Language = _settings.Language,
            IsDark = UiPalette.IsDarkTheme(_settings.Theme, systemDark)
        };
        _creditBadge = UiElements.Text(T("重置卡", "RESET"), 9.2, FontWeight.Bold, _palette.TextMuted,
            TextWrapping.NoWrap);
        _credit = UiElements.Text(T("重置卡信息暂不可用", "Reset credit information unavailable"), 11.5,
            FontWeight.Bold, _palette.TextSecondary);
        _creditMeta = UiElements.Text(T("等待 Codex 返回可用重置卡", "Waiting for available reset credits from Codex"),
            9, FontWeight.Normal, _palette.TextMuted);
        _balance = UiElements.Text(string.Empty, 13, FontWeight.Bold, _palette.Mint);
        _balanceCard = new Border { BorderThickness = new Thickness(1, 0, 0, 0), BorderBrush = _palette.Border,
            Margin = new Thickness(10, 0, 0, 0), Padding = new Thickness(12, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center, Child = new StackPanel { Spacing = 2, Children =
            {
                UiElements.Text("Credits", 10, FontWeight.SemiBold, _palette.TextSecondary), _balance
            }} };
        _balanceCard.IsVisible = false;
        // Construct after the balance controls: both belong to one compact row.
        _creditCard = BuildResetCreditCard();
        _tokenTotal = UiElements.Text(T("API 估算：—", "API estimate: —"), 11.5, FontWeight.SemiBold, _palette.TextSecondary);
        ToolTip.SetTip(_tokenTotal, T("当前周期按公开 API 价格估算的等价美元；不是订阅账单或实际扣费。柱顶显示每日估算金额。",
            "Current-cycle API-equivalent USD at public prices, not subscription billing or actual charges. Bar labels show daily estimates."));
        _placementTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(320) };
        _placementTimer.Tick += (_, _) => FlushPlacement();
        _transitionSurface = BuildContent();
        _transitionSurface.RenderTransformOrigin = RelativePoint.Center;
        _transitionSurface.RenderTransform = new ScaleTransform(1, 1);
        Content = _transitionSurface;
        _displayRecovery = new WindowDisplayRecovery(this, () => IsPresented,
            () => DisplayRecovered?.Invoke());
        _displayRecovery.UserSizeChanged += size => UserSizeChanged?.Invoke(size);
        Closing += (_, e) =>
        {
            // OS/application shutdown must close the window, not start an
            // asynchronous collapse that may try to reopen an already closed orb.
            if (_allowClose || e.CloseReason != WindowCloseReason.WindowClosing) return;
            e.Cancel = true;
            CloseRequested?.Invoke(this, EventArgs.Empty);
        };
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
        _source.IsVisible = presentation.ConnectionState != QuotaConnectionState.Live ||
                            presentation.Snapshot?.IsStale == true || presentation.Error is not null;

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
        var cycleUsage = UsageCycleSelector.Select(presentation.Usage, cycleStart, selected?.ResetsAt);
        var days = UsageSummaryCalculator.SummarizeByDay(cycleUsage, TimeZoneInfo.Local);
        _dailyUsage.Days = days;
        _dailyUsage.CycleStart = cycleStart;
        _dailyUsage.CycleEnd = selected?.ResetsAt;
        var reset = presentation.Snapshot?.SoonestAvailableResetCredit;
        var credits = presentation.Snapshot?.Credits;
        // Do not display old balances as current, unknown as zero, or mix them
        // with reset-card counts and estimated API dollars.
        _balanceCard.IsVisible = presentation.ConnectionState == QuotaConnectionState.Live &&
                                 presentation.Snapshot?.IsStale == false && credits is { Balance: > 0, Unlimited: false };
        _balance.Text = _balanceCard.IsVisible
            ? credits!.Balance!.Value < 1m
                ? credits.Balance.Value.ToString("0.############################", CultureInfo.InvariantCulture)
                : decimal.Truncate(credits.Balance.Value).ToString("#,##0", CultureInfo.InvariantCulture)
            : string.Empty;
        if (reset?.ExpiresAt is { } expiry)
        {
            var reference = presentation.UpdatedAt == DateTimeOffset.MinValue
                ? DateTimeOffset.Now
                : presentation.UpdatedAt;
            var remaining = expiry - reference;
            var availableCount = presentation.Snapshot?.ResetCredits?.Count(credit =>
                credit.Status.Equals("available", StringComparison.OrdinalIgnoreCase) &&
                credit.ExpiresAt > reference) ?? 0;
            _creditBadge.Text = T($"重置卡 · {availableCount} 张可用", $"RESET · {availableCount} AVAILABLE");
            _creditBadge.Foreground = _palette.Amber;
            _credit.Text = remaining > TimeSpan.Zero
                ? T($"最早 {FormatDuration(remaining)} 后到期",
                    $"Earliest expires in {FormatDuration(remaining)}")
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
        _tokenTotal.Text = $"{T("API 估算", "API estimate")}：${estimatedCost:0.00}" +
                           (unpriced > 0 ? T(" + 未定价", " + unpriced") : string.Empty);
    }

    public void PlaceNear(PixelPoint orbPosition, double orbLogicalSize)
    {
        _openingOrbPosition = orbPosition;
        _openingOrbSize = orbLogicalSize;
        var orbScreen = Screens.ScreenFromPoint(orbPosition) ?? Screens.Primary;
        if (orbScreen is null) return;
        var scale = orbScreen.Scaling;
        var work = orbScreen.WorkingArea;
        var outer = _displayRecovery.Fit(orbScreen);
        var panelWidth = outer.Width;
        var panelHeight = outer.Height;
        var orbSize = (int)Math.Ceiling(orbLogicalSize * scale);
        var x = (int)Math.Round(orbPosition.X + orbSize / 2d - panelWidth / 2d);
        var y = (int)Math.Round(orbPosition.Y + orbSize / 2d - panelHeight / 2d);
        SetAutomaticPosition(_displayRecovery.ConstrainPosition(new PixelPoint(x, y), work, outer));
    }

    public bool RestorePosition(double? x, double? y, string? displayId = null)
    {
        if (x is null || y is null) return false;
        var desired = new PixelPoint((int)Math.Round(x.Value), (int)Math.Round(y.Value));
        var screen = Screens.All.FirstOrDefault(item => DisplayId(item) == displayId) ??
                     Screens.ScreenFromPoint(desired) ?? Screens.Primary;
        if (screen is null) return false;
        var work = screen.WorkingArea;
        var outer = _displayRecovery.Fit(screen);
        var restored = _displayRecovery.ConstrainPosition(desired, work, outer);
        SetAutomaticPosition(restored);
        return true;
    }

    public async Task PrepareNativeSurfaceAsync()
    {
        _trackPlacement = false;
        // Create and paint the first native surface offscreen. A brand-new HWND
        // can otherwise expose its title bar and an unpainted white client area
        // before either native alpha or Avalonia composition is ready. Keep the
        // surface alive afterwards; Windows clears a hidden HWND again on Show.
        // Avalonia may allocate an HWND before Show, so a nonzero handle does
        // not prove the native frame has been laid out or painted yet.
        var needsNativePrerender = !UseClientOpacityAnimation && !_nativeSurfacePrepared;
        if (!needsNativePrerender) return;
        var intendedPosition = Position;
        WindowStartupLocation = WindowStartupLocation.Manual;
        Position = new PixelPoint(30000, 30000);
        var activateOnShow = ShowActivated;
        ShowActivated = false;
        try
        {
            // RDP/WM_DPICHANGED may relocate an offscreen window during Show.
            // Position alone is not a visibility guard: set native alpha and
            // theme before Show, then repeat alpha before yielding to rendering.
            SetTransitionOpacity(0);
            Show();
            SetTransitionOpacity(0);
            await Task.Delay(90);
            Position = intendedPosition;
            await Task.Delay(34);
            _nativeSurfacePrepared = true;
        }
        finally { ShowActivated = activateOnShow; }
    }

    public async Task AnimateInAsync(bool animate = true)
    {
        _trackPlacement = false;
        await PrepareNativeSurfaceAsync();

        SetTransitionOpacity(0);
        if (UseClientOpacityAnimation) Opacity = 0;
        if (_transitionSurface.RenderTransform is ScaleTransform start) start.ScaleX = .98;
        if (_transitionSurface.RenderTransform is ScaleTransform startY) startY.ScaleY = .975;
        if (!IsVisible) Show();
        // Show/moving across displays can change DPI and native frame size.
        // Re-anchor using the new dimensions before the first visible frame.
        if (_openingOrbPosition is { } openingOrb) PlaceNear(openingOrb, _openingOrbSize);
        if (!UseClientOpacityAnimation) Opacity = 1;
        // The native handle is guaranteed to exist after Show. Repeat the
        // zero-alpha assignment before activation so the whole HWND begins at 0.
        SetTransitionOpacity(0);
        Activate();
        if (_settings.ReducedMotion || !animate)
        {
            SetTransitionOpacity(1);
            if (_transitionSurface.RenderTransform is ScaleTransform reduced)
            {
                reduced.ScaleX = 1;
                reduced.ScaleY = 1;
            }
            _presented = true;
            _trackPlacement = true;
            return;
        }
        // The surface is already fully painted while transparent, so a short
        // native fade blends one complete frame into the desktop instead of
        // exposing a white/unpainted client frame. Keep the scale subtle: the
        // transition should read as a continuous handoff, not a zoom.
        await AnimateAsync(.04, 1, .98, 1, .975, 1, 155);
        _presented = true;
        _trackPlacement = true;
    }

    public async Task AnimateOutAsync()
    {
        FlushPlacement();
        _trackPlacement = false;
        if (_settings.ReducedMotion)
        {
            SetTransitionOpacity(0);
            _presented = false;
            if (UseClientOpacityAnimation) Hide();
            else Position = new PixelPoint(30000, 30000);
            return;
        }
        await AnimateAsync(1, .04, 1, .98, 1, .975, 125);
        SetTransitionOpacity(0);
        _presented = false;
        if (UseClientOpacityAnimation) Hide();
        else Position = new PixelPoint(30000, 30000);
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
        var root = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        var header = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto,Auto"), ColumnSpacing = 10,
            Margin = new Thickness(18, 12, 18, 8) };
        header.Children.Add(UiElements.Text("CODEX · " + T("额度", "QUOTA"), 19, FontWeight.Bold, _palette.TextPrimary));
        Grid.SetColumn(_connectionBadge, 1);
        header.Children.Add(_connectionBadge);
        var settings = UiElements.Button("⚙", _palette);
        settings.Width = 34;
        settings.Height = 38;
        settings.VerticalAlignment = VerticalAlignment.Center;
        settings.Padding = new Thickness(0);
        settings.FontSize = 21;
        settings.Content = new global::Avalonia.Controls.Shapes.Path
        {
            Width = 20, Height = 20, Stretch = Stretch.Uniform,
            Stroke = _palette.TextPrimary, StrokeThickness = 1.6,
            Data = Geometry.Parse("M9 2 L15 2 L16 5 L19 6 L22 9 L20 12 L22 15 L19 18 L16 19 L15 22 L9 22 L8 19 L5 18 L2 15 L4 12 L2 9 L5 6 L8 5 Z M16 12 A4 4 0 1 1 8 12 A4 4 0 1 1 16 12")
        };
        ToolTip.SetTip(settings, T("设置", "Settings"));
        global::Avalonia.Automation.AutomationProperties.SetName(settings, T("设置", "Settings"));
        settings.Click += (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty);
        Grid.SetColumn(settings, 2);
        header.Children.Add(settings);
        var close = UiElements.Button("×", _palette);
        close.Width = 34;
        close.Height = 38;
        close.VerticalAlignment = VerticalAlignment.Center;
        close.Padding = new Thickness(0);
        close.FontSize = 23;
        close.Content = new global::Avalonia.Controls.Shapes.Path
        {
            Width = 12, Height = 12, Stretch = Stretch.Uniform,
            Stroke = _palette.TextPrimary, StrokeThickness = 1.6,
            Data = Geometry.Parse("M0 0 L12 12 M12 0 L0 12")
        };
        ToolTip.SetTip(close, T("关闭", "Close"));
        global::Avalonia.Automation.AutomationProperties.SetName(close, T("关闭", "Close"));
        close.Click += (_, _) => CloseRequested?.Invoke(this, EventArgs.Empty);
        Grid.SetColumn(close, 3);
        header.Children.Add(close);
        header.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) BeginMoveDrag(e);
        };
        root.Children.Add(header);

        var scrollContent = new StackPanel { Spacing = 5, Margin = new Thickness(18, 0, 18, 5),
            HorizontalAlignment = HorizontalAlignment.Stretch };
        var hero = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("110,*"),
            ColumnSpacing = 14,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            ClipToBounds = false
        };
        hero.Children.Add(_summaryOrb);
        var copy = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center,
            Children = { _plan, _headline, _forecast, _source } };
        Grid.SetColumn(copy, 1);
        hero.Children.Add(copy);
        scrollContent.Children.Add(hero);
        scrollContent.Children.Add(_creditCard);
        scrollContent.Children.Add(UiElements.Card(new StackPanel { Spacing = 5, Children =
        {
            UiElements.Text(T("额度与趋势", "QUOTA & TREND"), 12.5,
                FontWeight.SemiBold, _palette.TextPrimary),
            _windowCards,
            new Border { Height = 1, Background = _palette.Border, Margin = new Thickness(0, 1) },
            _trend
        }}, _palette, new Thickness(12, 9)));
        var dailyHeader = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto"), ColumnSpacing = 6 };
        dailyHeader.Children.Add(UiElements.Text(T("每日消耗", "DAILY USAGE"), 12.5,
            FontWeight.SemiBold, _palette.TextPrimary));
        _tokenTotal.FontSize = 10.5 * UiElements.ScaleFactor;
        _tokenTotal.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_tokenTotal, 3);
        dailyHeader.Children.Add(_tokenTotal);
        var usage = UiElements.Button(T("查看明细", "Details"), _palette);
        usage.MinHeight = 26;
        usage.FontSize = 10.5 * UiElements.ScaleFactor;
        usage.Padding = new Thickness(8, 4);
        usage.VerticalAlignment = VerticalAlignment.Center;
        ToolTip.SetTip(usage, T("查看使用明细", "View usage details"));
        usage.Click += (_, _) => UsageDetailsRequested?.Invoke(this, EventArgs.Empty);
        Grid.SetColumn(usage, 1);
        dailyHeader.Children.Add(usage);
        scrollContent.Children.Add(UiElements.Card(new StackPanel { Spacing = 5, Children =
        {
            dailyHeader,
            _dailyUsage
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
        return new Border { Background = _palette.Canvas, CornerRadius = new CornerRadius(16),
            BorderBrush = _palette.Border, BorderThickness = new Thickness(1), ClipToBounds = true,
            Child = root };
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
        var copy = new StackPanel { Spacing = 1, Children = { _creditBadge, _credit } };
        ToolTip.SetTip(copy, _creditMeta);
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), Children = { accent, copy, _balanceCard } };
        Grid.SetColumn(copy, 1);
        Grid.SetColumn(_balanceCard, 2);
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
            MinWidth = 0, HorizontalAlignment = HorizontalAlignment.Stretch,
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
        {
            ToolTip.SetTip(_forecast, null);
            return T("续航预测：等待更多样本", "Forecast: collecting more samples");
        }
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
        ToolTip.SetTip(_forecast, $"{state} · {confidence}");
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
            availability = T($"预计可用 {FormatDuration(exhaustsAt - reference)} · {exhaustsAt.ToLocalTime():MM-dd HH:mm} 耗尽",
                $"~{FormatDuration(exhaustsAt - reference)} available · empty {exhaustsAt.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.InvariantCulture)}");
        }
        else
        {
            availability = T("预计可用时长：尚无法判断", "Estimated availability: not enough data");
        }
        return $"{availability}\n{T("当前", "Pace")} {forecast.PercentPerHour:0.##}%/h · {T("可持续", "sustainable")} {forecast.SustainablePercentPerHour:0.##}%/h";
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
            QuotaConnectionState.Stale => (T("断联", "OFFLINE"), _palette.Amber, _palette.SurfaceRaised),
            QuotaConnectionState.Offline => (T("未连接", "OFFLINE"), _palette.Red, _palette.SurfaceRaised),
            _ => (T("连接中", "CONNECTING"), _palette.Amber, _palette.SurfaceRaised)
        };
        _connectionText.Text = label;
        _connectionText.Foreground = color;
        _connectionBadge.Background = background;
        ToolTip.SetTip(_connectionBadge, string.IsNullOrWhiteSpace(presentation.ConnectionDetail)
            ? _source.Text : $"{presentation.ConnectionDetail}\n{_source.Text}");
    }

    private async Task AnimateAsync(double fromOpacity, double toOpacity, double sx0, double sx1, double sy0, double sy1, int ms)
    {
        var frames = Math.Clamp(ms / 12, 10, 16);
        var animateOpacity = Math.Abs(toOpacity - fromOpacity) > .0001;
        if (!animateOpacity) SetTransitionOpacity(toOpacity);
        for (var i = 0; i <= frames; i++)
        {
            var t = i / (double)frames;
            var eased = 1 - Math.Pow(1 - t, 3);
            if (animateOpacity) SetTransitionOpacity(fromOpacity + (toOpacity - fromOpacity) * eased);
            if (_transitionSurface.RenderTransform is ScaleTransform transform)
            {
                transform.ScaleX = sx0 + (sx1 - sx0) * eased;
                transform.ScaleY = sy0 + (sy1 - sy0) * eased;
            }
            await Task.Delay(Math.Max(1, ms / frames));
        }
    }

    private void SetTransitionOpacity(double opacity)
    {
        Opacity = UseClientOpacityAnimation ? opacity : 1d;
        TransitionOpacityChanged?.Invoke(opacity);
    }

    private string T(string zh, string en) => _settings.Language == AppLanguage.SimplifiedChinese ? zh : en;
}
