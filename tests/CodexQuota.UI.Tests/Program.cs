using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CodexQuota.Application;
using CodexQuota.UI.Avalonia;
using CodexQuota.Domain;

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
    var history = Enumerable.Range(0, 25).Select(index => new QuotaHistoryPoint(
        now.AddHours(index - 24), "7d", 10_080, 68 - index)).ToArray();
    var usage = new[]
    {
        new ObservedUsage(now.AddHours(-3), "GPT-5.6 Sol", "Default",
            new TokenUsageBreakdown(12_000, 7_000, 2_000, 3_000, 1_000), "a", true),
        new ObservedUsage(now.AddDays(-1), "GPT-5.6 Terra", "Fast",
            new TokenUsageBreakdown(8_000, 5_000, 1_000, 2_000, 500), "b", true)
    };
    var presentation = new QuotaPresentation(snapshot, history, usage,
        QuotaRunwayForecaster.Evaluate(snapshot, history), false, null, now);
    var formal = new (string Name, Window Window)[]
    {
        ("dashboard-zh-dark", new DashboardWindow(new AppSettings { Theme = AppTheme.Dark, Language = AppLanguage.SimplifiedChinese })),
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
        ("alert-zh-dark", new AlertWindow(new AppSettings { Theme = AppTheme.Dark, Language = AppLanguage.SimplifiedChinese },
            "额度提醒", "7 天窗口 · 18% 剩余", false)),
        ("clickthrough-en-light", new ClickThroughReminderWindow(new AppSettings
            { Theme = AppTheme.Light, Language = AppLanguage.English }))
    };
    foreach (var (name, window) in formal)
    {
        if (window is DashboardWindow dashboard) dashboard.ApplyPresentation(presentation);
        if (window is UsageDetailsWindow details) details.ApplyUsage(usage, now.AddDays(-7), now.AddDays(1));
        if (window is OrbWindow orb)
        {
            orb.ApplySettings(new AppSettings { OrbSize = 120, Theme = AppTheme.Dark });
            orb.ApplyPresentation(name.EndsWith("dual", StringComparison.Ordinal)
                ? presentation
                : presentation with { Snapshot = new OfficialQuotaSnapshot(now, [snapshot.VisibleWindows[1]]) });
        }
        window.Show();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
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
    settingsInteraction.NavigateToPage(4);
    Check.True(settingsInteraction.CachedPageCount == 5, "settings caches five pages");
    Check.True(settingsInteraction.SelectedPageIndex == 4 && settingsInteraction.VisiblePageCount == 1,
        "settings atomic tab switch");
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
        Check.True(largeDashboard.Bounds.Width >= 520, "large-font dashboard width");
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
}

Console.WriteLine($"UI render matrix passed: {scenarios.Length + (string.IsNullOrWhiteSpace(requestedScenario) || formalOnly ? 20 : 0)} scenarios -> {outputRoot}");
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
