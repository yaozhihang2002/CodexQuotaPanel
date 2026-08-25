using Avalonia;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CodexQuota.UI.Avalonia;

var outputPath = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine("artifacts", "vnext-preview.png"));

TestAppBuilder.BuildAvaloniaApp().SetupWithoutStarting();

var window = new PreviewWindow();
window.Show();
window.SetRenderScaling(1d);
window.Width = 980;
window.Height = 620;
Dispatcher.UIThread.RunJobs();
AvaloniaHeadlessPlatform.ForceRenderTimerTick();
Dispatcher.UIThread.RunJobs();
AvaloniaHeadlessPlatform.ForceRenderTimerTick();
var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("The preview did not produce a rendered frame.");

if (frame.PixelSize.Width < 760 || frame.PixelSize.Height < 500)
    throw new InvalidOperationException($"Unexpected preview size: {frame.PixelSize}.");
if (window.ClientSize.Width < 900 || window.ClientSize.Height < 560)
    throw new InvalidOperationException($"Unexpected client size: {window.ClientSize}.");
if (window.ContentRegion.Bounds.Width < 700 || window.ContentRegion.Bounds.Height < 470)
    throw new InvalidOperationException($"Content region collapsed: {window.ContentRegion.Bounds}.");
if (window.SummaryCards.Bounds.Width < 380 || window.SummaryCards.Bounds.Height < 280)
    throw new InvalidOperationException($"Summary cards collapsed: {window.SummaryCards.Bounds}.");
if (window.OrbPreviewPanel.Bounds.Width < 240 || window.OrbPreviewPanel.Bounds.Height < 400)
    throw new InvalidOperationException($"Orb preview collapsed: {window.OrbPreviewPanel.Bounds}.");

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
using (var output = File.Create(outputPath))
    frame.Save(output, PngBitmapEncoderOptions.Default);
window.Close();

Console.WriteLine($"UI render check passed: {outputPath}");
Console.WriteLine(
    $"Layout: client={window.ClientSize}; content={window.ContentRegion.Bounds}; " +
    $"cards={window.SummaryCards.Bounds}; orb={window.OrbPreviewPanel.Bounds}");

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApplication>()
            .UseSkia()
            .UseHarfBuzz()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions
            {
                UseHeadlessDrawing = false
            });
}

public sealed class TestApplication : Avalonia.Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = Avalonia.Styling.ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }
}
