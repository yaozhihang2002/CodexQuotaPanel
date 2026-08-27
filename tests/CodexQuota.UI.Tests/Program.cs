using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CodexQuota.Application;
using CodexQuota.UI.Avalonia;
using CodexQuota.Domain;

AppDomain.CurrentDomain.UnhandledException += (_, eventArgs) =>
{
    Console.Error.WriteLine("UI render matrix failed.");
    Console.Error.WriteLine(eventArgs.ExceptionObject);
    Environment.Exit(1);
};

if (args.Length > 0 && args[0].StartsWith('-'))
    throw new ArgumentException("The first argument must be an output directory, not an option. Example: CodexQuota.UI.Tests.exe C:\\Temp\\quota-ui formal");

var outputRoot = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine("artifacts", "vnext-preview"));
Directory.CreateDirectory(outputRoot);

TestAppBuilder.BuildAvaloniaApp().SetupWithoutStarting();
var allScenarios = new[]
{
    ("zh-dark-single-100", new PreviewScenario(AppLanguage.SimplifiedChinese, AppTheme.Dark, false), 1d),
    ("zh-dark-dual-150", new PreviewScenario(AppLanguage.SimplifiedChinese, AppTheme.Dark, true), 1.5d),
    ("zh-light-single-100", new PreviewScenario(AppLanguage.SimplifiedChinese, AppTheme.Light, false), 1d),
    ("zh-light-dual-200", new PreviewScenario(AppLanguage.SimplifiedChinese, AppTheme.Light, true), 2d),
    ("en-dark-single-100", new PreviewScenario(AppLanguage.English, AppTheme.Dark, false), 1d),
    ("en-dark-dual-150", new PreviewScenario(AppLanguage.English, AppTheme.Dark, true), 1.5d),
    ("en-light-single-100", new PreviewScenario(AppLanguage.English, AppTheme.Light, false), 1d),
    ("en-light-dual-200", new PreviewScenario(AppLanguage.English, AppTheme.Light, true), 2d)
};
var requestedScenario = args.Length > 1 ? args[1] : null;
var formalOnly = string.Equals(requestedScenario, "formal", StringComparison.OrdinalIgnoreCase);
var scenarios = string.IsNullOrWhiteSpace(requestedScenario)
    ? allScenarios
    : formalOnly ? []
    : allScenarios.Where(item => item.Item1.Equals(requestedScenario, StringComparison.Ordinal)).ToArray();
if (scenarios.Length == 0 && !formalOnly)
    throw new ArgumentException($"Unknown render scenario: {requestedScenario}");

foreach (var (name, scenario, scale) in scenarios)
{
    Console.WriteLine($"Rendering {name}...");
    var window = new PreviewWindow(scenario);
    window.Show();
    window.SetRenderScaling(scale);
    window.Width = 980;
    window.Height = 620;
    // The macOS headless host needs a resize after a 2x DPI notification to
    // commit a frame. Force layout after that resize so capture cannot observe
    // the intermediate physical-size layout.
    window.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
    window.ApplyQuota(new OfficialQuotaSnapshot(DateTimeOffset.UtcNow,
        scenario.DualRing
            ? [new QuotaWindow("5h", 300, 71, DateTimeOffset.UtcNow.AddHours(3)),
               new QuotaWindow("7d", 10_080, 44, DateTimeOffset.UtcNow.AddDays(4))]
            : [new QuotaWindow("7d", 10_080, 44, DateTimeOffset.UtcNow.AddDays(4))]));
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using var frame = window.CaptureRenderedFrame()
                      ?? throw new InvalidOperationException($"{name}: no rendered frame.");

    Check.True(frame.PixelSize.Width >= 760 * scale, $"{name}: pixel width");
    Check.True(frame.PixelSize.Height >= 500 * scale, $"{name}: pixel height");
    Check.True(window.ContentRegion.Bounds.Width >= 700, $"{name}: content width");
    Check.True(window.SummaryCards.Bounds is { Width: >= 380, Height: >= 280 }, $"{name}: cards");
    Check.True(window.OrbPreviewPanel.Bounds is { Width: >= 240, Height: >= 400 }, $"{name}: orb");
    Check.True(window.OrbPreviewControl.Bounds.Width >= 185 && window.OrbPreviewControl.Bounds.Height >= 185,
        $"{name}: orb control size");
    Check.True(scenario.DualRing == double.IsFinite(window.OrbPreviewControl.SecondaryRemainingPercent),
        $"{name}: adaptive ring count");

    var outputPath = Path.Combine(outputRoot, name + ".png");
    await using (var output = File.Create(outputPath))
        frame.Save(output, PngBitmapEncoderOptions.Default);
    window.Close();
    Console.WriteLine($"Rendered {name}");
}

if (string.IsNullOrWhiteSpace(requestedScenario) || formalOnly)
{
    var now = DateTimeOffset.UtcNow;
    var snapshot = new OfficialQuotaSnapshot(now,
        [new QuotaWindow("5h", 300, 71, now.AddHours(3)),
         new QuotaWindow("7d", 10_080, 44, now.AddDays(4))],
        Source: "App Server", PlanType: "pro",
        ResetCredits: [new ResetCredit("r1", "available", now.AddDays(2), "Full reset")]);

    foreach (var (themeName, palette) in new[]
             {
                 ("dark", UiPalette.For(AppTheme.Dark)),
                 ("light", UiPalette.For(AppTheme.Light, false))
             })
    {
        var normalButton = UiElements.Button("收起为悬浮球", palette, true);
        var hoverButton = UiElements.Button("收起为悬浮球", palette, true);
        Check.True(hoverButton.Resources["ButtonBackgroundPointerOver"] is IBrush,
            $"{themeName}: primary pointer-over brush exists");
        hoverButton.Background = (IBrush)hoverButton.Resources["ButtonBackgroundPointerOver"]!;
        hoverButton.BorderBrush = (IBrush)hoverButton.Resources["ButtonBorderBrushPointerOver"]!;
        var stateRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Margin = new Thickness(22),
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new StackPanel { Spacing = 6, Children = { UiElements.Text("默认", 11, FontWeight.SemiBold, palette.TextSecondary), normalButton } },
                new StackPanel { Spacing = 6, Children = { UiElements.Text("悬停", 11, FontWeight.SemiBold, palette.TextSecondary), hoverButton } }
            }
        };
        var stateWindow = new Window
        {
            Width = 390, Height = 130, Background = palette.Canvas, Content = stateRow
        };
        stateWindow.Show();
        stateWindow.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        using (var frame = stateWindow.CaptureRenderedFrame() ??
                           throw new InvalidOperationException($"{themeName} button states: no rendered frame"))
        {
            await using var output = File.Create(Path.Combine(outputRoot, $"button-states-{themeName}.png"));
            frame.Save(output, PngBitmapEncoderOptions.Default);
        }
        stateWindow.Close();
    }
    var history = Enumerable.Range(0, 49).Select(index => new QuotaHistoryPoint(
        now.AddMinutes((index - 48) * 30), "7d", 10_080, 68 - index * .5)).ToArray();
    var usage = new[]
    {
        new ObservedUsage(now.AddHours(-3), "gpt-5.6-sol", "Default",
            new TokenUsageBreakdown(12_000, 7_000, 2_000, 3_000, 1_000), "a", true),
        new ObservedUsage(now.AddDays(-1), "gpt-5.6-terra", "Fast",
            new TokenUsageBreakdown(8_000, 5_000, 1_000, 2_000, 500), "b", true)
    };
    var presentation = new QuotaPresentation(snapshot, history, usage,
        QuotaRunwayForecaster.Evaluate(snapshot, history), false, null, now)
    {
        ConnectionState = QuotaConnectionState.Live,
        ConnectionDetail = "Connected to Codex live quota service"
    };
    var formal = new (string Name, Window Window)[]
    {
        ("dashboard-zh-dark", new DashboardWindow(new AppSettings { Theme = AppTheme.Dark, Language = AppLanguage.SimplifiedChinese })),
        ("dashboard-zh-dark-single", new DashboardWindow(new AppSettings { Theme = AppTheme.Dark, Language = AppLanguage.SimplifiedChinese })),
        ("dashboard-en-light", new DashboardWindow(new AppSettings { Theme = AppTheme.Light, Language = AppLanguage.English })),
        ("settings-zh-dark-general", CreateSettings(AppLanguage.SimplifiedChinese, AppTheme.Dark, 0)),
        ("settings-zh-dark-appearance", CreateSettings(AppLanguage.SimplifiedChinese, AppTheme.Dark, 1)),
        ("settings-zh-dark-interaction", CreateSettings(AppLanguage.SimplifiedChinese, AppTheme.Dark, 2)),
        ("settings-zh-dark-notifications", CreateSettings(AppLanguage.SimplifiedChinese, AppTheme.Dark, 3)),
        ("settings-zh-dark-data", CreateSettings(AppLanguage.SimplifiedChinese, AppTheme.Dark, 4)),
        ("settings-en-light-general", CreateSettings(AppLanguage.English, AppTheme.Light, 0)),
        ("settings-en-light-appearance", CreateSettings(AppLanguage.English, AppTheme.Light, 1)),
        ("settings-en-light-interaction", CreateSettings(AppLanguage.English, AppTheme.Light, 2)),
        ("settings-en-light-notifications", CreateSettings(AppLanguage.English, AppTheme.Light, 3)),
        ("settings-en-light-data", CreateSettings(AppLanguage.English, AppTheme.Light, 4)),
        ("usage-zh-dark", new UsageDetailsWindow(new AppSettings { Theme = AppTheme.Dark, Language = AppLanguage.SimplifiedChinese })),
        ("usage-en-light", new UsageDetailsWindow(new AppSettings { Theme = AppTheme.Light, Language = AppLanguage.English })),
        ("orb-single", new OrbWindow()),
        ("orb-dual", new OrbWindow()),
        ("orb-offline", new OrbWindow()),
        ("alert-zh-dark", new AlertWindow(new AppSettings { Theme = AppTheme.Dark, Language = AppLanguage.SimplifiedChinese },
            "额度提醒", "7 天窗口 · 18% 剩余", false)),
        ("clickthrough-en-light", new ClickThroughReminderWindow(new AppSettings
            { Theme = AppTheme.Light, Language = AppLanguage.English }))
    };
    foreach (var (name, window) in formal)
    {
        if (window is DashboardWindow dashboard)
            dashboard.ApplyPresentation(name.EndsWith("single", StringComparison.Ordinal)
                ? presentation with { Snapshot = snapshot with { Windows = [snapshot.VisibleWindows[1]] } }
                : presentation);
        if (window is UsageDetailsWindow details) details.ApplyUsage(usage, now.AddDays(-7), now.AddDays(1));
        if (window is OrbWindow orb)
        {
            orb.ApplySettings(new AppSettings { OrbSize = 120, Theme = AppTheme.Dark });
            orb.ApplyPresentation(name.EndsWith("dual", StringComparison.Ordinal)
                ? presentation
                : name.EndsWith("offline", StringComparison.Ordinal)
                    ? QuotaPresentation.Empty with
                    {
                        ConnectionState = QuotaConnectionState.Offline,
                        ConnectionDetail = "No quota source available"
                    }
                    : presentation with { Snapshot = snapshot with { Windows = [snapshot.VisibleWindows[1]] } });
        }
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        if (window is DashboardWindow laidOutDashboard)
        {
            var orbBounds = laidOutDashboard.SummaryOrbBoundsInWindow;
            Check.True(orbBounds.Width >= 109 && orbBounds.Height >= 109,
                $"{name}: summary orb keeps its fixed layout slot");
            Check.True(orbBounds.Left >= 17 && orbBounds.Right <= laidOutDashboard.Bounds.Width - 17,
                $"{name}: summary orb stays fully inside the dashboard client area");
        }
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"{name}: no rendered frame");
        Check.True(frame.PixelSize.Width >= 100, $"{name}: width");
        Check.True(frame.PixelSize.Height >= 100, $"{name}: height");
        await using var output = File.Create(Path.Combine(outputRoot, name + ".png"));
        frame.Save(output, PngBitmapEncoderOptions.Default);
        window.Close();
    }

    var settingsInteraction = new SettingsWindow(new AppSettings
    {
        Theme = AppTheme.Dark,
        Language = AppLanguage.SimplifiedChinese,
        OrbSize = 88
    });
    var previewCount = 0;
    AppSettings? savedDraft = null;
    var cancelCount = 0;
    settingsInteraction.PreviewChanged += _ => previewCount++;
    settingsInteraction.SaveRequested += draft => savedDraft = draft;
    settingsInteraction.CancelRequested += (_, _) => cancelCount++;
    settingsInteraction.Show();
    settingsInteraction.NavigateToPage(1);
    settingsInteraction.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
    var numericEditors = settingsInteraction.GetVisualDescendants().OfType<NumericUpDown>().ToArray();
    Check.True(numericEditors.Length >= 3 && numericEditors.All(editor => editor.Bounds.Width >= 128),
        "settings numeric editors preserve three-digit width");
    var numericTextBoxes = numericEditors
        .Select(editor => editor.GetVisualDescendants().OfType<TextBox>().FirstOrDefault())
        .Where(box => box is not null).Cast<TextBox>().ToArray();
    Check.True(numericTextBoxes.Length == numericEditors.Length && numericTextBoxes.All(box => box.Bounds.Width >= 45),
        "settings numeric editor text area remains readable");
    Check.True(settingsInteraction.GetVisualDescendants().OfType<ValueSliderControl>().Count() == 3 &&
               !settingsInteraction.GetVisualDescendants().OfType<Slider>().Any(),
        "settings uses themed sliders instead of system accent sliders");
    settingsInteraction.NavigateToPage(0);
    var localizedChoices = settingsInteraction.GetVisualDescendants().OfType<ComboBox>()
        .SelectMany(combo => (combo.ItemsSource as System.Collections.IEnumerable)?.Cast<object>() ?? [])
        .Select(item => item.ToString()).Where(text => text is not null).Cast<string>().ToArray();
    Check.True(localizedChoices.Contains("恢复上次状态") && localizedChoices.Contains("跟随系统") &&
               localizedChoices.Contains("简体中文"), "settings enum choices are localized");
    settingsInteraction.NavigateToPage(4);
    settingsInteraction.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
    Check.True(settingsInteraction.CachedPageCount == 5, "settings caches five pages");
    Check.True(settingsInteraction.SelectedPageIndex == 4 && settingsInteraction.VisiblePageCount == 1,
        "settings atomic tab switch");
    var pricingCopy = settingsInteraction.GetVisualDescendants().OfType<TextBlock>()
        .Select(text => text.Text ?? string.Empty).ToArray();
    Check.True(pricingCopy.Any(text => text.Contains("API 等价计价标准", StringComparison.Ordinal)) &&
               pricingCopy.Any(text => text.Contains(ApiCostEstimator.BasisDate, StringComparison.Ordinal)) &&
               pricingCopy.Any(text => text.Contains("Fast", StringComparison.Ordinal)) &&
               pricingCopy.Any(text => text.Contains("Auto-review", StringComparison.Ordinal)),
        "settings explains the dated API-equivalent pricing standard");
    settingsInteraction.PreviewSettings(settings => settings with
    {
        Theme = AppTheme.Light,
        Language = AppLanguage.English,
        OrbSize = 146,
        InterfaceScalePercent = 125
    });
    Dispatcher.UIThread.RunJobs();
    Check.True(settingsInteraction.DraftSettings.Theme == AppTheme.Light &&
               settingsInteraction.DraftSettings.Language == AppLanguage.English &&
               settingsInteraction.DraftSettings.OrbSize == 146, "settings live draft");
    Check.True(settingsInteraction.CachedPageCount == 5 && settingsInteraction.VisiblePageCount == 1,
        "settings rebuild stays atomic");
    Check.True(previewCount == 1, "settings preview event");
    settingsInteraction.RequestSave();
    Check.True(savedDraft?.OrbSize == 146, "settings save draft");
    settingsInteraction.MarkSaved(savedDraft!);
    Check.True(settingsInteraction.PersistedSettings.OrbSize == 146, "settings save keeps window state");
    settingsInteraction.PreviewSettings(settings => settings with { OrbSize = 172 });
    settingsInteraction.CancelEdits();
    Check.True(cancelCount == 1 && settingsInteraction.DraftSettings.OrbSize == 146,
        "settings cancel rollback");
    settingsInteraction.ClosePermanently();

    UiElements.ScaleFactor = 1.5;
    var largeSettings = new SettingsWindow(new AppSettings
    {
        Theme = AppTheme.Dark, Language = AppLanguage.SimplifiedChinese, InterfaceScalePercent = 150
    });
    largeSettings.Show();
    largeSettings.NavigateToPage(1);
    largeSettings.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using (var frame = largeSettings.CaptureRenderedFrame() ??
                       throw new InvalidOperationException("settings-zh-dark-font150: no rendered frame"))
    {
        Check.True(largeSettings.Bounds.Width >= 1000 && largeSettings.VisiblePageCount == 1,
            "large-font settings layout");
        await using var output = File.Create(Path.Combine(outputRoot, "settings-zh-dark-font150.png"));
        frame.Save(output, PngBitmapEncoderOptions.Default);
    }
    largeSettings.ClosePermanently();

    var largeDashboard = new DashboardWindow(new AppSettings
    {
        Theme = AppTheme.Light, Language = AppLanguage.English, InterfaceScalePercent = 150
    });
    largeDashboard.ApplyPresentation(presentation);
    largeDashboard.Show();
    largeDashboard.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using (var frame = largeDashboard.CaptureRenderedFrame() ??
                       throw new InvalidOperationException("dashboard-en-light-font150: no rendered frame"))
    {
        Check.True(largeDashboard.Bounds.Width >= 555, "large-font English dashboard adds localization width");
        Check.True(!largeDashboard.ForecastDisplayText.Contains("Estimated availability", StringComparison.Ordinal),
            "large-font English forecast uses compact copy");
        await using var output = File.Create(Path.Combine(outputRoot, "dashboard-en-light-font150.png"));
        frame.Save(output, PngBitmapEncoderOptions.Default);
    }
    largeDashboard.ClosePermanently();
    UiElements.ScaleFactor = 1d;

    var lifecycleOrb = new OrbWindow { Position = new PixelPoint(240, 180) };
    lifecycleOrb.ApplySettings(new AppSettings { OrbSize = 96, ReducedMotion = false });
    lifecycleOrb.Show();
    var positionBeforeTransition = lifecycleOrb.Position;
    PumpAnimation(lifecycleOrb.AnimateOutAsync(), "orb animate out");
    PumpAnimation(lifecycleOrb.AnimateInAsync(), "orb animate in");
    Check.True(lifecycleOrb.Position == positionBeforeTransition, "orb transition preserves position");
    Check.True(lifecycleOrb.Opacity > .99, "orb transition restores opacity");
    lifecycleOrb.Close();

    var clickableOrb = new OrbWindow();
    var openRequests = 0;
    clickableOrb.OpenDetailsRequested += (_, _) => openRequests++;
    clickableOrb.ApplySettings(new AppSettings { OrbSize = 96, ClickThrough = false, PositionLocked = false });
    Check.True(clickableOrb.HasInteractiveCursor,
        "orb shows an interactive cursor when click-through is disabled");
    clickableOrb.Show();
    clickableOrb.MouseDown(new Point(48, 48), MouseButton.Left);
    clickableOrb.MouseUp(new Point(48, 48), MouseButton.Left);
    Check.True(openRequests == 1, "orb click opens details when click-through is disabled");
    clickableOrb.ApplySettings(new AppSettings { OrbSize = 96, ClickThrough = true, PositionLocked = false });
    Check.True(!clickableOrb.HasInteractiveCursor,
        "orb keeps the default cursor when click-through is enabled");
    clickableOrb.MouseDown(new Point(48, 48), MouseButton.Left);
    clickableOrb.MouseUp(new Point(48, 48), MouseButton.Left);
    Check.True(openRequests == 1, "orb ignores clicks when click-through is enabled");
    clickableOrb.ApplySettings(new AppSettings { OrbSize = 96, ClickThrough = false, PositionLocked = true });
    Check.True(clickableOrb.HasInteractiveCursor,
        "locked clickable orb still shows an interactive cursor");
    clickableOrb.MouseDown(new Point(48, 48), MouseButton.Left);
    clickableOrb.MouseUp(new Point(48, 48), MouseButton.Left);
    Check.True(openRequests == 2, "position lock keeps click-to-open available");
    clickableOrb.Close();

    var adaptiveOrb = new OrbWindow();
    adaptiveOrb.ApplySettings(new AppSettings { OrbSize = 120, Theme = AppTheme.Dark });
    adaptiveOrb.ApplyPresentation(presentation with
    {
        Snapshot = new OfficialQuotaSnapshot(now, [new QuotaWindow("7d", 10_080, 62, now.AddDays(4))])
    });
    Check.True(adaptiveOrb.RenderedRingCount == 1 && adaptiveOrb.RenderedPrimaryLabel == "7D",
        "adaptive orb detects 7D-only single ring");
    adaptiveOrb.ApplyPresentation(presentation);
    Check.True(adaptiveOrb.RenderedRingCount == 2 && adaptiveOrb.RenderedPrimaryLabel == "5H" &&
               adaptiveOrb.RenderedSecondaryLabel == "7D", "adaptive orb restores dual ring");
    adaptiveOrb.ApplyPresentation(presentation with
    {
        Snapshot = new OfficialQuotaSnapshot(now, [new QuotaWindow("5h", 300, 81, now.AddHours(4))]),
        ConnectionState = QuotaConnectionState.LocalFallback
    });
    Check.True(adaptiveOrb.RenderedRingCount == 1 && adaptiveOrb.RenderedPrimaryLabel == "5H" &&
               adaptiveOrb.RenderedConnectionState == QuotaConnectionState.LocalFallback,
        "adaptive orb detects 5H-only and connection transition");
    adaptiveOrb.SetMoveMode(true);
    Check.True(adaptiveOrb.IsMoveMode, "orb temporary move mode overrides interaction block");
    adaptiveOrb.SetMoveMode(false);

    var adaptiveDashboard = new DashboardWindow(new AppSettings { Theme = AppTheme.Dark, Language = AppLanguage.SimplifiedChinese });
    adaptiveDashboard.ApplyPresentation(presentation with
    {
        Snapshot = new OfficialQuotaSnapshot(now, [new QuotaWindow("7d", 10_080, 62, now.AddDays(4))])
    });
    Check.True(adaptiveDashboard.WindowCardCount == 1 && adaptiveDashboard.SummaryRingCount == 1 &&
               adaptiveDashboard.ConnectionBadgeText == "实时", "dashboard 7D-only adaptive layout");
    adaptiveDashboard.ApplyPresentation(presentation);
    Check.True(adaptiveDashboard.WindowCardCount == 2 && adaptiveDashboard.SummaryRingCount == 2,
        "dashboard dynamic dual-window restore");
    Check.True(adaptiveDashboard.ForecastDisplayText.Contains("预计可用", StringComparison.Ordinal) &&
               adaptiveDashboard.ForecastDisplayText.Contains("当前", StringComparison.Ordinal),
        "dashboard forecast shows both availability duration and pace");
    Check.True(adaptiveDashboard.ResetCreditDisplayText.Contains("后到期", StringComparison.Ordinal) &&
               adaptiveDashboard.ResetCreditMetaText.Contains("1 张可用", StringComparison.Ordinal) &&
               adaptiveDashboard.ResetCreditIsProminent,
        "dashboard promotes the earliest reset credit expiry");
    adaptiveDashboard.Show();
    adaptiveDashboard.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
    var dashboardScroll = adaptiveDashboard.GetVisualDescendants().OfType<ScrollViewer>().Single();
    Check.True(dashboardScroll.Extent.Height <= dashboardScroll.Viewport.Height + 2,
        "default dashboard keeps all information visible without scrolling");
    if (adaptiveDashboard.Screens.Primary is { } placementScreen)
    {
        var work = placementScreen.WorkingArea;
        var scale = placementScreen.Scaling;
        var panelWidth = (int)Math.Ceiling(adaptiveDashboard.Width * scale);
        var panelHeight = (int)Math.Ceiling(adaptiveDashboard.Height * scale);
        var orbSize = (int)Math.Ceiling(88 * scale);
        var centeredOrb = new PixelPoint(work.X + work.Width / 2 - orbSize / 2,
            work.Y + work.Height / 2 - orbSize / 2);
        adaptiveDashboard.PlaceNear(centeredOrb, 88);
        Check.True(Math.Abs(adaptiveDashboard.Position.X -
                            (centeredOrb.X + orbSize / 2d - panelWidth / 2d)) <= 1 &&
                   Math.Abs(adaptiveDashboard.Position.Y -
                            (centeredOrb.Y + orbSize / 2d - panelHeight / 2d)) <= 1,
            "dashboard initially centers on the orb");
        var edgeOrb = new PixelPoint(work.Right - orbSize, work.Bottom - orbSize);
        adaptiveDashboard.PlaceNear(edgeOrb, 88);
        Check.True(adaptiveDashboard.Position.X == work.Right - panelWidth &&
                   adaptiveDashboard.Position.Y == work.Bottom - panelHeight,
            "dashboard clamps flush to the work-area edge");
        Check.True(adaptiveDashboard.RestorePosition(work.X + 42, work.Y + 38,
                       $"{placementScreen.Bounds.X},{placementScreen.Bounds.Y},{placementScreen.Bounds.Width},{placementScreen.Bounds.Height}") &&
                   adaptiveDashboard.Position == new PixelPoint(work.X + 42, work.Y + 38),
            "dashboard restores a user-fixed position");
        PixelPoint? committedPosition = null;
        adaptiveDashboard.PlacementCommitted += (position, _) => committedPosition = position;
        adaptiveDashboard.EnablePlacementTrackingForTest();
        var moved = new PixelPoint(work.X + 68, work.Y + 61);
        adaptiveDashboard.Position = moved;
        adaptiveDashboard.CommitPlacementForTest();
        Check.True(committedPosition == moved, "dashboard commits a manually adjusted position");
    }
    adaptiveDashboard.ClosePermanently();

    var usageCycle = new UsageDetailsWindow(new AppSettings { Theme = AppTheme.Dark, Language = AppLanguage.SimplifiedChinese });
    usageCycle.ApplyUsage(usage, now.Date.AddDays(-6), now.Date.AddDays(1));
    Check.True(usageCycle.DailyChartDayCount == 7, "daily chart retains full seven-day cycle including zero days");
    usageCycle.Show();
    usageCycle.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
    var dailyChart = usageCycle.GetVisualDescendants().OfType<DailyUsageChartControl>().Single();
    Check.True(dailyChart.RenderedMaximumCost > 0, "daily chart scales the bars by API estimate");
    Check.True(dailyChart.RenderedValueLabelCount == 2,
        "daily chart draws a USD value label for every non-zero bar");
    dailyChart.SetHoverIndexForTest(6);
    Check.True(dailyChart.HoveredDayIndex == 6, "daily chart hover selects the exact day");
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    Dispatcher.UIThread.RunJobs();
    using (var frame = usageCycle.CaptureRenderedFrame() ?? throw new InvalidOperationException("usage hover: no rendered frame"))
    {
        await using var output = File.Create(Path.Combine(outputRoot, "usage-zh-dark-hover.png"));
        frame.Save(output, PngBitmapEncoderOptions.Default);
    }
    usageCycle.Close();

    var compactDualRow = new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 34,
        Margin = new Thickness(24),
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center
    };
    foreach (var orbSize in new[] { 56d, 88d })
    {
        var compactOrb = new OrbControl
        {
            Width = orbSize,
            Height = orbSize,
            RemainingPercent = 100,
            SecondaryRemainingPercent = 100,
            PrimaryLabel = "7D",
            SecondaryLabel = "5H",
            FeedbackStyle = ConsumptionFeedbackStyle.Fluid,
            FeedbackIntensity = .94,
            AnimateFeedback = false,
            ConnectionState = QuotaConnectionState.Live
        };
        compactDualRow.Children.Add(new StackPanel
        {
            Spacing = 8,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                UiElements.Text($"{orbSize:0} px · 7D 100 / 5H 100", 10, FontWeight.SemiBold,
                    UiPalette.B("#D7E3DD"), TextWrapping.NoWrap),
                compactOrb
            }
        });
    }
    var compactDualWindow = new Window
    {
        Width = 390,
        Height = 170,
        Background = UiPalette.B("#0D1210"),
        Content = compactDualRow
    };
    compactDualWindow.Show();
    compactDualWindow.SetRenderScaling(2d);
    compactDualWindow.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using (var frame = compactDualWindow.CaptureRenderedFrame() ??
                       throw new InvalidOperationException("compact dual orb: no rendered frame"))
    {
        await using var output = File.Create(Path.Combine(outputRoot, "orb-dual-minimum-legibility.png"));
        frame.Save(output, PngBitmapEncoderOptions.Default);
    }
    compactDualWindow.Close();

    UsageForecast FeedbackForecast(double rate, double sustainable, string windowId = "7d") =>
        new(windowId, now.AddHours(12), rate, rate, rate, sustainable,
            ForecastConfidence.High, ForecastState.Sustainable, 8, 180);
    var sevenDayMean = ConsumptionFeedbackIntensity.From(FeedbackForecast(100d / 168d, 100d / 168d));
    var fiveHourMean = ConsumptionFeedbackIntensity.From(FeedbackForecast(20d, 20d, "5h"));
    Check.True(sevenDayMean > .25 && sevenDayMean <= .52 &&
               Math.Abs(sevenDayMean - fiveHourMean) < .0001,
        "feedback maps both window means to the same warm stage");
    Check.True(ConsumptionFeedbackIntensity.FromPressure(.4) is > .03 and <= .25,
        "feedback maps a low pressure pace to cool flame");
    Check.True(ConsumptionFeedbackIntensity.FromPressure(1.5) is > .52 and <= .78,
        "feedback maps a pace above the sustainable range to hot flame");
    Check.True(ConsumptionFeedbackIntensity.FromPressure(2.5) > .78,
        "feedback maps severe quota pressure to intense flame");
    Check.True(ConsumptionFeedbackIntensity.MotionStep(.9) >
               ConsumptionFeedbackIntensity.MotionStep(.4) * 2d,
        "feedback motion accelerates with quota pressure");
    var motionSteps = new[] { 0d, .14d, .38d, .66d, .9d }
        .Select(ConsumptionFeedbackIntensity.MotionStep)
        .ToArray();
    Check.True(motionSteps.Zip(motionSteps.Skip(1), (left, right) => right > left).All(value => value),
        "feedback motion increases monotonically from ice to intense fire");

    var feedbackGrid = new Grid
    {
        RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
        ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto"),
        RowSpacing = 10,
        ColumnSpacing = 10,
        Margin = new Thickness(18)
    };
    var styles = Enum.GetValues<ConsumptionFeedbackStyle>();
    var intensities = new[] { 0d, .17d, .42d, .68d, .94d };
    var styleNames = new[] { "简约余烬", "流体火焰", "像素火焰" };
    var stateNames = new[] { "冰晶", "冷焰", "温焰", "旺火", "烈焰" };
    for (var row = 0; row < styles.Length; row++)
    for (var column = 0; column < intensities.Length; column++)
    {
        var control = new OrbControl
        {
            Width = 104, Height = 104, RemainingPercent = 68, SecondaryRemainingPercent = 29,
            FeedbackStyle = styles[row], FeedbackIntensity = intensities[column], AnimateFeedback = false,
            ConnectionState = QuotaConnectionState.Live
        };
        var cell = new StackPanel
        {
            Spacing = 3,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children =
            {
                UiElements.Text($"{styleNames[row]} · {stateNames[column]}", 10, FontWeight.SemiBold,
                    UiPalette.B("#D7E3DD"), TextWrapping.NoWrap),
                control
            }
        };
        Grid.SetRow(cell, row);
        Grid.SetColumn(cell, column);
        feedbackGrid.Children.Add(cell);
    }
    var feedbackWindow = new Window { Width = 760, Height = 430, Background = UiPalette.B("#0D1210"), Content = feedbackGrid };
    feedbackWindow.Show();
    feedbackWindow.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using (var frame = feedbackWindow.CaptureRenderedFrame() ?? throw new InvalidOperationException("feedback matrix: no rendered frame"))
    {
        await using var output = File.Create(Path.Combine(outputRoot, "feedback-matrix.png"));
        frame.Save(output, PngBitmapEncoderOptions.Default);
    }
    feedbackWindow.Close();

    var compactFeedbackGrid = new Grid
    {
        RowDefinitions = new RowDefinitions("Auto,Auto,Auto"),
        ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto"),
        RowSpacing = 12,
        ColumnSpacing = 18,
        Margin = new Thickness(22)
    };
    for (var row = 0; row < styles.Length; row++)
    for (var column = 0; column < intensities.Length; column++)
    {
        var control = new OrbControl
        {
            Width = 56, Height = 56, RemainingPercent = 68, SecondaryRemainingPercent = 29,
            FeedbackStyle = styles[row], FeedbackIntensity = intensities[column], AnimateFeedback = false,
            ConnectionState = QuotaConnectionState.Live
        };
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        compactFeedbackGrid.Children.Add(control);
    }
    var compactFeedbackWindow = new Window
    {
        Width = 470, Height = 250, Background = UiPalette.B("#0D1210"), Content = compactFeedbackGrid
    };
    compactFeedbackWindow.Show();
    compactFeedbackWindow.SetRenderScaling(2d);
    compactFeedbackWindow.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    using (var frame = compactFeedbackWindow.CaptureRenderedFrame() ??
                       throw new InvalidOperationException("compact feedback matrix: no rendered frame"))
    {
        await using var output = File.Create(Path.Combine(outputRoot, "feedback-matrix-56px.png"));
        frame.Save(output, PngBitmapEncoderOptions.Default);
    }
    compactFeedbackWindow.Close();
}

Console.WriteLine($"UI render matrix passed: {scenarios.Length + (string.IsNullOrWhiteSpace(requestedScenario) || formalOnly ? 31 : 0)} scenarios -> {outputRoot}");
Environment.Exit(0);

static SettingsWindow CreateSettings(AppLanguage language, AppTheme theme, int page)
{
    var window = new SettingsWindow(new AppSettings { Theme = theme, Language = language });
    window.NavigateToPage(page);
    return window;
}

static void PumpAnimation(Task animation, string name)
{
    var deadline = DateTime.UtcNow.AddSeconds(3);
    while (!animation.IsCompleted && DateTime.UtcNow < deadline)
    {
        Thread.Sleep(10);
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    }
    if (!animation.IsCompleted) throw new TimeoutException($"{name}: timed out");
    animation.GetAwaiter().GetResult();
}

static class Check
{
    public static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException($"{name}: expected true");
    }
}

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApplication>()
            .UseSkia()
            .UseHarfBuzz()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

public sealed class TestApplication : Avalonia.Application
{
    public override void Initialize() => Styles.Add(new FluentTheme());
}
