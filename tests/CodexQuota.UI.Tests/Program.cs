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
Dispatcher.UIThread.RunJobs();
AvaloniaHeadlessPlatform.ForceRenderTimerTick();
var frame = window.CaptureRenderedFrame()
            ?? throw new InvalidOperationException("The preview did not produce a rendered frame.");

if (frame.PixelSize.Width < 760 || frame.PixelSize.Height < 500)
    throw new InvalidOperationException($"Unexpected preview size: {frame.PixelSize}.");

Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
using (var output = File.Create(outputPath))
    frame.Save(output, PngBitmapEncoderOptions.Default);
window.Close();

Console.WriteLine($"UI render check passed: {outputPath}");

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApplication>()
            .UseSkia()
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
