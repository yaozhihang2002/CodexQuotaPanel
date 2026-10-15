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
if (args.Length > 1 && args[1] == "edge-containment")
{
    ProductivityChecks.RunEdgeContainmentMotion();
    return;
}
if (args.Length > 1 && args[1] == "placement")
{
    ProductivityChecks.RunPlacement();
    return;
}
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
    using (var output = File.Create(outputPath))
        frame.Save(output, PngBitmapEncoderOptions.Default);
    window.Close();
    Console.WriteLine($"Rendered {name}");
}

if (string.IsNullOrWhiteSpace(requestedScenario) || formalOnly)
{
    // Use local noon so now - 3h and now - 1d always land on distinct
    // calendar days. A live UTC clock before 03:00 merged both fixtures
    // into yesterday on UTC CI hosts, invalidating the two-bar assertion.
    var now = new DateTimeOffset(new DateTime(2026, 9, 7, 12, 0, 0, DateTimeKind.Local));
    var snapshot = new OfficialQuotaSnapshot(now,
        [new QuotaWindow("5h", 300, 71, now.AddHours(3)),
         new QuotaWindow("7d", 10_080, 44, now.AddDays(4))],
        Source: "App Server", PlanType: "pro",
        ResetCredits: [new ResetCredit("r1", "available", now.AddDays(2), "Full reset")],
        Credits: new CreditBalance(62309m));

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
            using var output = File.Create(Path.Combine(outputRoot, $"button-states-{themeName}.png"));
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
        ("light-A-white", LightPreview("#FFFFFF", "#F7F8FA")),
        ("light-B-mist", LightPreview("#F2F4F7", "#FFFFFF")),
        ("light-C-warm", LightPreview("#FAF8F3", "#FFFFFF")),
        ("light-D-sage", LightPreview("#F0F6F3", "#FFFFFF")),
        ("close-zh-light", new PanelCloseWindow(new AppSettings { Theme = AppTheme.Light }, false)),
        ("close-zh-dark", new PanelCloseWindow(new AppSettings { Theme = AppTheme.Dark }, true)),
        ("close-en-light", new PanelCloseWindow(new AppSettings { Theme = AppTheme.Light, Language = AppLanguage.English }, false)),
        ("dashboard-zh-light-B", new DashboardWindow(new AppSettings { Theme = AppTheme.Light })),
        ("dashboard-system-light", new DashboardWindow(new AppSettings { Theme = AppTheme.System }, false)),
        ("dashboard-system-dark", new DashboardWindow(new AppSettings { Theme = AppTheme.System }, true)),
        ("usage-system-light", new UsageDetailsWindow(new AppSettings { Theme = AppTheme.System }, false)),
        ("usage-system-dark", new UsageDetailsWindow(new AppSettings { Theme = AppTheme.System }, true)),
        ("dashboard-en-light-narrow", new DashboardWindow(new AppSettings { Theme = AppTheme.Light, Language = AppLanguage.English }) { Width = 420 }),
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
    static DashboardWindow LightPreview(string canvas, string surface) => new(
        new AppSettings { Theme = AppTheme.Light }, false,
        UiPalette.For(AppTheme.Light) with { Canvas = UiPalette.B(canvas), Surface = UiPalette.B(surface) });
    foreach (var (name, window) in formal)
    {
        Console.WriteLine($"Formal rendering {name}...");
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
            var bars = laidOutDashboard.GetVisualDescendants().OfType<ProgressBar>().ToArray();
            var chart = laidOutDashboard.GetVisualDescendants().OfType<DailyUsageChartControl>().Single();
            var trend = laidOutDashboard.GetVisualDescendants().OfType<TrendChartControl>().Single();
            var color = ((ISolidColorBrush)chart.AccentBrush).Color;
            Check.True(bars.All(b => ((ISolidColorBrush)b.Foreground!).Color == color) &&
                       ((ISolidColorBrush)trend.AccentBrush).Color == color,
                "quota bars, daily columns and actual trend use the same theme accent");
            if (name == "dashboard-system-light") Check.True(!chart.IsDark && !trend.IsDark, "system light resolves to light charts");
            if (name == "dashboard-system-dark") Check.True(chart.IsDark && trend.IsDark, "system dark resolves to dark charts");
            var icons = laidOutDashboard.GetVisualDescendants().OfType<Button>()
                .Where(button => button.Content is global::Avalonia.Controls.Shapes.Path).ToArray();
            Check.True(icons.Length == 2, "two header icons");
            foreach (var button in icons)
            {
                var icon = (Control)button.Content!;
                var origin = icon.TranslatePoint(default, button)!.Value;
                Check.True(Math.Abs(origin.X + icon.Bounds.Width / 2 - button.Bounds.Width / 2) <= 1 &&
                           Math.Abs(origin.Y + icon.Bounds.Height / 2 - button.Bounds.Height / 2) <= 1,
                    "header icon horizontally and vertically centered within one DIP");
            }
            var firstIcon = icons[0].TranslatePoint(default, laidOutDashboard)!.Value;
            var lastIcon = icons[1].TranslatePoint(default, laidOutDashboard)!.Value;
            Check.True(Math.Abs(firstIcon.Y - lastIcon.Y) <= 1, "header icons share top edge");
            Check.True(Math.Abs(lastIcon.X + icons[1].Bounds.Width - (laidOutDashboard.Bounds.Width - 19)) <= 1,
                "header and cards share right inset");
            Check.True(laidOutDashboard.CreditBalanceVisible && laidOutDashboard.CreditBalanceText == "62,309",
                "positive live credits displayed without dollar conversion");
            Check.True(!laidOutDashboard.SourceDetailVisible && laidOutDashboard.ConnectionTipText.Contains("App Server"),
                "live source details move to connection tooltip");
            Check.True(laidOutDashboard.ForecastDisplayText.Split('\n').Length == 2 &&
                       !string.IsNullOrWhiteSpace(laidOutDashboard.ForecastTipText),
                "forecast has two lines and retains confidence in tooltip");
            foreach (var example in new[] { (62308.47879m, "62,308"), (12.50m, "12"), (1m, "1"), (.50m, "0.5"), (.001m, "0.001") })
            {
                laidOutDashboard.ApplyPresentation(presentation with { Snapshot = snapshot with { Credits = new CreditBalance(example.Item1) } });
                Check.True(laidOutDashboard.CreditBalanceText == example.Item2, "credits display whole units above one and actual decimals below one");
            }
            foreach (var balance in new decimal?[] { 0m, null })
            {
                laidOutDashboard.ApplyPresentation(presentation with { Snapshot = snapshot with { Credits = new CreditBalance(balance) } });
                Check.True(!laidOutDashboard.CreditBalanceVisible, "zero/missing credits hidden");
            }
            laidOutDashboard.ApplyPresentation(presentation with { ConnectionState = QuotaConnectionState.Stale });
            Check.True(laidOutDashboard.SourceDetailVisible, "stale source details remain visible");
            Check.True(!laidOutDashboard.CreditBalanceVisible, "stale balance hidden");
            laidOutDashboard.ApplyPresentation(name.EndsWith("single", StringComparison.Ordinal)
                ? presentation with { Snapshot = snapshot with { Windows = [snapshot.VisibleWindows[1]] } }
                : presentation);
            laidOutDashboard.UpdateLayout();
            var compactScroll = laidOutDashboard.GetVisualDescendants().OfType<ScrollViewer>().First();
            foreach (var bar in laidOutDashboard.GetVisualDescendants().OfType<ProgressBar>())
            {
                var parent = (Control)bar.Parent!;
                Check.True(bar.Bounds.Left >= -1 && bar.Bounds.Right <= parent.Bounds.Width + 1,
                    "quota progress bar stays inside padded content at narrow widths");
            }
            Check.True(compactScroll.Extent.Height <= compactScroll.Viewport.Height + 1,
                $"{name}: default dashboard with credits fits without scrolling");
            var orbBounds = laidOutDashboard.SummaryOrbBoundsInWindow;
            Check.True(orbBounds.Width >= 109 && orbBounds.Height >= 109,
                $"{name}: summary orb keeps its fixed layout slot");
            Check.True(orbBounds.Left >= 17 && orbBounds.Right <= laidOutDashboard.Bounds.Width - 17,
                $"{name}: summary orb stays fully inside the dashboard client area");
        }
        if (name.StartsWith("usage-system-"))
        {
            var chart = window.GetVisualDescendants().OfType<DailyUsageChartControl>().Single();
            Check.True(chart.IsDark == name.EndsWith("dark"), "usage details resolve the system theme");
        }
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException($"{name}: no rendered frame");
        Check.True(frame.PixelSize.Width >= 100, $"{name}: width");
        Check.True(frame.PixelSize.Height >= 100, $"{name}: height");
        // The headless harness has no continuously running dispatcher loop.
        // DisposeAsync may yield while flushing and strand its continuation on macOS.
        using var output = File.Create(Path.Combine(outputRoot, name + ".png"));
        frame.Save(output, PngBitmapEncoderOptions.Default);
        if (window is DashboardWindow finishedDashboard) finishedDashboard.ClosePermanently();
        else if (window is SettingsWindow finishedSettings) finishedSettings.ClosePermanently();
        else window.Close();
        Console.WriteLine($"Formal rendered {name}");
    }

    Console.WriteLine("Formal settings interaction checks...");
    var rememberedSize = AppSettings.Default with { DashboardWidth = 760, DashboardHeight = 820 };
    var sizeJson = System.Text.Json.JsonSerializer.Serialize(rememberedSize);
    var restoredSize = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(sizeJson)!;
    var rebuiltPanel = new DashboardWindow(restoredSize);
    Check.True(rebuiltPanel.Width == 760 && rebuiltPanel.Height == 820, "recreated panel restores persisted width and height");
    var sizeRecovery = new WindowDisplayRecovery(rebuiltPanel);
    sizeRecovery.Fit(new PixelRect(0, 0, 600, 700), 1);
    Check.True(rebuiltPanel.Width <= 600 && rebuiltPanel.Height <= 700, "small screen temporarily constrains remembered size");
    sizeRecovery.Fit(new PixelRect(0, 0, 1920, 1080), 1);
    Check.True(rebuiltPanel.Width == 760 && rebuiltPanel.Height == 820, "large screen restores preferred dimensions");
    rebuiltPanel.ClosePermanently();
    Check.True((AppSettings.Default with { DashboardWidth = double.NaN, DashboardHeight = -1 }).Normalize()
        is { DashboardWidth: null, DashboardHeight: null }, "invalid saved dimensions fall back to defaults");
    var closeOwner = new DashboardWindow(AppSettings.Default);
    Check.True(closeOwner.CanResize && double.IsPositiveInfinity(closeOwner.MaxWidth), "dashboard width is no longer capped at 580");
    var resizeSize = new Size(452, 640);
    foreach (var hit in new[]
    {
        (new Point(4, 300), WindowEdge.West), (new Point(448, 300), WindowEdge.East),
        (new Point(200, 4), WindowEdge.North), (new Point(200, 636), WindowEdge.South),
        (new Point(8, 8), WindowEdge.NorthWest), (new Point(444, 8), WindowEdge.NorthEast),
        (new Point(8, 632), WindowEdge.SouthWest), (new Point(444, 632), WindowEdge.SouthEast)
    }) Check.True(DashboardWindow.ResizeEdgeAt(hit.Item1, resizeSize) == hit.Item2, "all eight resize directions are reachable");
    Check.True(DashboardWindow.ResizeEdgeAt(new Point(410, 32), resizeSize) is null &&
               DashboardWindow.ResizeEdgeAt(new Point(200, 300), resizeSize) is null,
        "header buttons and content do not trigger resizing");
    closeOwner.Show();
    var closeRequests = 0;
    closeOwner.CloseRequested += (_, _) => closeRequests++;
    closeOwner.Close();
    Check.True(closeRequests == 1 && closeOwner.IsVisible, "native close requests choice without hiding");
    foreach (var choice in new[] { ("隐藏到托盘", PanelCloseAction.Tray), ("退出应用", PanelCloseAction.Exit), ("×", PanelCloseAction.Ask) })
    {
        var prompt = new PanelCloseWindow(AppSettings.Default, true);
        var result = prompt.ShowDialog<PanelCloseAction>(closeOwner);
        prompt.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Check.True(!prompt.RememberChoice, "remember is unchecked by default");
        Check.True(prompt.GetVisualDescendants().OfType<Button>().All(b => !Equals(b.Content, "取消")), "cancel is available through close icon only");
        prompt.GetVisualDescendants().OfType<CheckBox>().Single().IsChecked = true;
        Check.True(prompt.RememberChoice, "remember can be checked");
        prompt.GetVisualDescendants().OfType<Button>().Single(b => Equals(b.Content, choice.Item1))
            .RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        Check.True(await result == choice.Item2, "close dialog returns selected action");
    }
    closeOwner.ClosePermanently();
    Console.WriteLine("PASS: close request, three dialog choices and remember checkbox.");
    if (args.Length > 2 && args[2] == "close-preview") return;
    ProductivityChecks.Run(outputRoot);
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
    var productVersion = "v" + System.Reflection.Assembly.GetEntryAssembly()!
        .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
        .Cast<System.Reflection.AssemblyInformationalVersionAttribute>().Single().InformationalVersion.Split('+')[0];
    Check.True(settingsInteraction.Title!.Contains(productVersion), "version visible in settings title");
    Check.True(settingsInteraction.GetVisualDescendants().OfType<TextBlock>().Any(t => t.Text == productVersion),
        "version visible in persistent settings header");
    var removedOptions = new[] { "吸附屏幕边缘", "显示穿透提醒", "悬浮球背景", "外环额度窗口", "内环额度窗口", "反馈风格" };
    Check.True(!settingsInteraction.GetVisualDescendants().OfType<TextBlock>().Any(t => removedOptions.Contains(t.Text)),
        "redundant and low-frequency switches removed");
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
    Check.True(pricingCopy.Any(text => text.Contains("API 等价估算", StringComparison.Ordinal)) &&
               pricingCopy.Any(text => text.Contains(ApiCostEstimator.BasisDate, StringComparison.Ordinal)) &&
               pricingCopy.Any(text => text.Contains("查看官方价格", StringComparison.Ordinal)) &&
               pricingCopy.Any(text => text.Contains("Auto-review", StringComparison.Ordinal)) &&
               !pricingCopy.Any(text => text.Contains("每百万 Token", StringComparison.Ordinal)),
        "settings gives a concise dated API-equivalent estimate with official link");
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
    Console.WriteLine("Formal settings interaction checks passed");

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
        using var output = File.Create(Path.Combine(outputRoot, "settings-zh-dark-font150.png"));
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
        using var output = File.Create(Path.Combine(outputRoot, "dashboard-en-light-font150.png"));
        frame.Save(output, PngBitmapEncoderOptions.Default);
    }
    largeDashboard.ClosePermanently();
    Console.WriteLine("Formal large-layout checks passed");
    UiElements.ScaleFactor = 1d;

    var lifecycleOrb = new OrbWindow { Position = new PixelPoint(240, 180) };
    lifecycleOrb.ApplySettings(new AppSettings { OrbSize = 96, ReducedMotion = false });
    lifecycleOrb.Show();
    var positionBeforeTransition = lifecycleOrb.Position;
    PumpAnimation(lifecycleOrb.AnimateOutAsync(), "orb animate out");
    Check.True(!lifecycleOrb.IsVisible, "orb is fully hidden before its replacement surface appears");
    PumpAnimation(lifecycleOrb.AnimateInAsync(), "orb animate in");
    Check.True(lifecycleOrb.Position == positionBeforeTransition, "orb transition preserves position");
    Check.True(lifecycleOrb.Opacity > .99, "orb transition restores opacity");
    if (lifecycleOrb.Screens.Primary is { } orbScreen)
    {
        var target = new PixelPoint(orbScreen.WorkingArea.X + 34, orbScreen.WorkingArea.Y + 29);
        lifecycleOrb.RestorePosition(target.X, target.Y,
            $"{orbScreen.Bounds.X},{orbScreen.Bounds.Y},{orbScreen.Bounds.Width},{orbScreen.Bounds.Height}");
        Check.True(lifecycleOrb.Position == target,
            "orb restores the exact saved position on the matching display");
    }
    lifecycleOrb.Close();

    var clickableOrb = new OrbWindow();
    var openRequests = 0;
    clickableOrb.OpenDetailsRequested += (_, _) => openRequests++;
    clickableOrb.ApplySettings(new AppSettings { OrbSize = 96, ClickThrough = false, PositionLocked = false });
    Check.True(clickableOrb.HasInteractiveCursor,
        "orb shows an interactive cursor when click-through is disabled");
    Check.True(clickableOrb.UsesClickCursor && !clickableOrb.UsesMoveCursor,
        "clickable orb uses the hand cursor outside explicit move mode");
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
    Check.True(clickableOrb.UsesClickCursor,
        "locked clickable orb keeps the hand cursor");
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
    Check.True(adaptiveOrb.UsesMoveCursor && !adaptiveOrb.UsesClickCursor,
        "explicit move mode uses the four-way move cursor");
    adaptiveOrb.SetMoveMode(false);
    Check.True(adaptiveOrb.UsesClickCursor && !adaptiveOrb.UsesMoveCursor,
        "leaving move mode restores the hand cursor");

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
        var frameAllowance = new WindowDisplayRecovery(adaptiveDashboard).FrameInsets;
        var orbSize = (int)Math.Ceiling(88 * scale);
        var centeredOrb = new PixelPoint(work.X + work.Width / 2 - orbSize / 2,
            work.Y + work.Height / 2 - orbSize / 2);
        adaptiveDashboard.PlaceNear(centeredOrb, 88);
        var panelWidth = (int)Math.Ceiling((adaptiveDashboard.Width + frameAllowance.Width) * scale);
        var panelHeight = (int)Math.Ceiling((adaptiveDashboard.Height + frameAllowance.Height) * scale);
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
        adaptiveDashboard.InvisibleFrameInsetsProvider = () => new Thickness(8, 0, 8, 8);
        adaptiveDashboard.PlaceNear(edgeOrb, 88);
        Check.True(adaptiveDashboard.Position.X + panelWidth - 8 == work.Right &&
                   adaptiveDashboard.Position.Y + panelHeight - 8 == work.Bottom,
            "dashboard aligns visible right and bottom edges, not invisible resize borders");
        adaptiveDashboard.PlaceNear(new PixelPoint(work.X, work.Y), 88);
        Check.True(adaptiveDashboard.Position.X + 8 == work.X && adaptiveDashboard.Position.Y == work.Y,
            "dashboard aligns visible left edge without hiding title bar");
        adaptiveDashboard.InvisibleFrameInsetsProvider = null;
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
    PumpAnimation(adaptiveDashboard.AnimateOutAsync(), "dashboard animate out");
    Check.True(!adaptiveDashboard.IsVisible && adaptiveDashboard.Opacity <= .001,
        "dashboard retires its transparent final frame before the orb is shown");
    PumpAnimation(adaptiveDashboard.AnimateInAsync(), "dashboard animate in");
    adaptiveDashboard.ClosePermanently();

    var dpiDashboard = new DashboardWindow(new AppSettings { Theme = AppTheme.Dark });
    dpiDashboard.ApplyPresentation(presentation);
    dpiDashboard.Show();
    var recovery = new WindowDisplayRecovery(dpiDashboard);
    var preferred = new Size(dpiDashboard.Width, dpiDashboard.Height);
    foreach (var dpi in new[] { 1d, 1.25d, 1.5d, 2d })
    {
        var border = (int)(8 * dpi);
        recovery.InvisibleFrameInsetsProvider = () => new Thickness(border, 0, border, border);
        var area = new PixelRect(-1920, -240, 1920, 1080);
        var outer = new PixelSize((int)(480 * dpi), (int)(710 * dpi));
        // Sizing is covered below; position checks use a fitting height.
        outer = new PixelSize(outer.Width, Math.Min(outer.Height, area.Height));
        var right = recovery.ConstrainPosition(new PixelPoint(10000, 10000), area, outer);
        var left = recovery.ConstrainPosition(new PixelPoint(-10000, -10000), area, outer);
        Check.True(right.X + outer.Width - border == area.Right &&
                   right.Y + outer.Height - border == area.Bottom &&
                   left.X + border == area.X && left.Y == area.Y,
            $"physical visible edge compensation at {dpi} on a negative-origin monitor");
    }
    recovery.InvisibleFrameInsetsProvider = null;
    foreach (var dpi in new[] { 1d, 1.5d, 2d, 1d })
    {
        dpiDashboard.SetRenderScaling(dpi);
        // Simulate a changed remote desktop work area while retaining this
        // exact window and its controls, then returning to the local desktop.
        var work = dpi == 1 ? new PixelRect(0, 0, 1920, 1080) : new PixelRect(-1280, 0, 1280, 960);
        dpiDashboard.Position = new PixelPoint(work.Right - 8, work.Bottom - 8);
        var outer = recovery.Fit(work, dpi);
        dpiDashboard.InvalidateMeasure();
        dpiDashboard.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        Check.True(dpiDashboard.Position.X >= work.X && dpiDashboard.Position.Y >= work.Y &&
                   dpiDashboard.Position.X + outer.Width <= work.Right &&
                   dpiDashboard.Position.Y + outer.Height <= work.Bottom,
            $"retained dashboard complete frame fits changed work area at {dpi}");
        var footerButton = dpiDashboard.GetVisualDescendants().OfType<Button>()
            .Single(button => button.Content is string text && text == "收起为悬浮球");
        var footerOrigin = footerButton.TranslatePoint(default, dpiDashboard) ?? default;
        Check.True(footerOrigin.Y >= 0 && footerOrigin.Y + footerButton.Bounds.Height <= dpiDashboard.ClientSize.Height + 1,
            $"retained dashboard footer remains visible at {dpi}");
        Check.True(dpiDashboard.WindowCardCount == 2 && dpiDashboard.DailyChartDayCount > 0,
            $"retained dashboard data survives DPI change at {dpi}");
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
    }
    Check.True(Math.Abs(dpiDashboard.Width - preferred.Width) < 1 &&
               Math.Abs(dpiDashboard.Height - preferred.Height) < 1,
        "return to larger local desktop restores preferred dashboard size");
    using (var dpiFrame = dpiDashboard.CaptureRenderedFrame())
    {
        if (dpiFrame is null) throw new InvalidOperationException("DPI recovery produced no rendered frame");
        using var dpiOutput = File.Create(Path.Combine(outputRoot, "dashboard-dpi-roundtrip.png"));
        dpiFrame.Save(dpiOutput, PngBitmapEncoderOptions.Default);
    }
    dpiDashboard.ClosePermanently();

    var persistentDashboard = new DashboardWindow(new AppSettings
    {
        Theme = AppTheme.Dark,
        Language = AppLanguage.SimplifiedChinese
    }) { UseClientOpacityAnimation = false };
    var nativeOpacity = 1d;
    var opacityAtOpened = 1d;
    var activatedAtOpened = true;
    persistentDashboard.TransitionOpacityChanged += value => nativeOpacity = value;
    persistentDashboard.Opened += (_, _) =>
    {
        opacityAtOpened = nativeOpacity;
        activatedAtOpened = persistentDashboard.ShowActivated;
    };
    PumpAnimation(persistentDashboard.PrepareNativeSurfaceAsync(), "dashboard native surface prewarm");
    Check.True(opacityAtOpened == 0 && nativeOpacity == 0 && !activatedAtOpened,
        "native first Show is already transparent and does not activate the preparing window");
    Check.True(persistentDashboard.ShowActivated, "preparation restores normal activation policy");
    PumpAnimation(persistentDashboard.AnimateInAsync(), "dashboard persistent animate in");
    Check.True(persistentDashboard.IsVisible && persistentDashboard.IsPresented,
        "persistent dashboard presents its prewarmed native surface");
    PumpAnimation(persistentDashboard.AnimateOutAsync(), "dashboard persistent animate out");
    Check.True(persistentDashboard.IsVisible && !persistentDashboard.IsPresented,
        "persistent dashboard parks instead of hiding its native surface");
    PumpAnimation(persistentDashboard.AnimateInAsync(animate: false), "dashboard recovery without entrance animation");
    Check.True(persistentDashboard.IsPresented &&
               persistentDashboard.Content is Control { RenderTransform: ScaleTransform { ScaleX: 1, ScaleY: 1 } },
        "recovery presents the full-size surface without an extra zoom transition");
    persistentDashboard.ClosePermanently();
    Console.WriteLine("Formal window lifecycle checks passed");

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
        using var output = File.Create(Path.Combine(outputRoot, "usage-zh-dark-hover.png"));
        frame.Save(output, PngBitmapEncoderOptions.Default);
    }
    usageCycle.Close();

    var costSortedUsage = new UsageDetailsWindow(new AppSettings
        { Theme = AppTheme.Dark, Language = AppLanguage.SimplifiedChinese });
    costSortedUsage.ApplyUsage(
    [
        new ObservedUsage(now, "codex-auto-review", "default",
            new TokenUsageBreakdown(2_000_000, 2_000_000, 0, 0, 0), "auto", true),
        new ObservedUsage(now, "gpt-5.6-sol", "default",
            new TokenUsageBreakdown(1_000_000, 0, 0, 1_000_000, 0), "sol", true)
    ], now.Date, now.Date.AddDays(1));
    costSortedUsage.Show();
    costSortedUsage.UpdateLayout();
    Dispatcher.UIThread.RunJobs();
    var usageLabels = costSortedUsage.GetVisualDescendants().OfType<TextBlock>()
        .Select(text => text.Text ?? string.Empty).ToArray();
    var solIndex = Array.IndexOf(usageLabels, "GPT-5.6 Sol · Default");
    var autoReviewIndex = Array.IndexOf(usageLabels, "Auto-review · Default");
    Check.True(solIndex >= 0 && autoReviewIndex >= 0 && solIndex < autoReviewIndex,
        "usage details sort higher estimated USD above higher raw-token rows");
    costSortedUsage.Close();

    var chartNow = new DateTimeOffset(2026, 8, 30, 12, 0, 0, TimeSpan.Zero);
    var hoverChart = new TrendChartControl
    {
        Width = 420,
        Height = 90,
        Points =
        [
            new QuotaHistoryPoint(chartNow.AddHours(-24), "weekly", 10_080, 80),
            new QuotaHistoryPoint(chartNow, "weekly", 10_080, 60)
        ],
        ResetAt = chartNow.AddDays(5),
        WindowMinutes = 10_080,
        IsDark = true
    };
    var hoverHost = new Window { Width = 440, Height = 110, Content = hoverChart };
    hoverHost.Show();
    hoverHost.UpdateLayout();
    hoverChart.SetHoverPositionForTest(new Point(hoverChart.Bounds.Width / 2, hoverChart.Bounds.Height - 2));
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    Dispatcher.UIThread.RunJobs();
    Check.True(hoverChart.HasHoverSample,
        "trend chart accepts hover across the full plot height, not only over the line");
    Check.True(hoverChart.HoverTime is not null &&
               Math.Abs((hoverChart.HoverTime.Value - chartNow.AddHours(-12)).TotalMinutes) < 1,
        "trend chart maps hover by horizontal time position");
    Check.True(hoverChart.HoverActualPercent is not null &&
               Math.Abs(hoverChart.HoverActualPercent.Value - 70) < .1,
        "trend chart interpolates actual quota at the hovered time");
    Check.True(hoverChart.HoverLabelBounds is { } labelBounds &&
               labelBounds.Bottom <= hoverChart.PlotBounds.Top,
        "trend hover label stays in the fixed information band outside the plot");
    Check.True(Math.Abs(hoverChart.PlotBounds.Top - 17) < .01 &&
               Math.Abs(hoverChart.PlotBounds.Bottom - (hoverChart.Bounds.Height - 8)) < .01,
        "trend top moves up five DIP while the zero-percent baseline stays fixed");
    hoverHost.Close();

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
        using var output = File.Create(Path.Combine(outputRoot, "orb-dual-minimum-legibility.png"));
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
        using var output = File.Create(Path.Combine(outputRoot, "feedback-matrix.png"));
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
        using var output = File.Create(Path.Combine(outputRoot, "feedback-matrix-56px.png"));
        frame.Save(output, PngBitmapEncoderOptions.Default);
    }
    compactFeedbackWindow.Close();
}

Console.WriteLine($"UI render matrix passed: {scenarios.Length + (string.IsNullOrWhiteSpace(requestedScenario) || formalOnly ? 32 : 0)} scenarios -> {outputRoot}");
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
