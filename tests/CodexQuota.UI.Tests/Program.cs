using Avalonia;
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
var scenarios = string.IsNullOrWhiteSpace(requestedScenario)
    ? allScenarios
    : allScenarios.Where(item => item.Item1.Equals(requestedScenario, StringComparison.Ordinal)).ToArray();
if (scenarios.Length == 0)
    throw new ArgumentException($"Unknown render scenario: {requestedScenario}");

foreach (var (name, scenario, scale) in scenarios)
{
    Console.WriteLine($"Rendering {name}...");
    var window = new PreviewWindow(scenario);
    window.Width = 980;
    window.Height = 620;
    // Establish the logical window size before the platform creates its first
    // native frame. Changing render scaling after Show() leaves macOS headless
    // with a physical-size layout and causes a clipped intermediate frame.
    window.SetRenderScaling(scale);
    window.Show();
    window.UpdateLayout();
    window.ApplyQuota(new OfficialQuotaSnapshot(DateTimeOffset.UtcNow,
        scenario.DualRing
            ? [new QuotaWindow("5h", 300, 71, DateTimeOffset.UtcNow.AddHours(3)),
               new QuotaWindow("7d", 10_080, 44, DateTimeOffset.UtcNow.AddDays(4))]
            : [new QuotaWindow("7d", 10_080, 44, DateTimeOffset.UtcNow.AddDays(4))]));
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    Dispatcher.UIThread.RunJobs();
    AvaloniaHeadlessPlatform.ForceRenderTimerTick();
    var frame = window.CaptureRenderedFrame()
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

Console.WriteLine($"UI render matrix passed: {scenarios.Length} scenarios -> {outputRoot}");
Environment.Exit(0);

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
