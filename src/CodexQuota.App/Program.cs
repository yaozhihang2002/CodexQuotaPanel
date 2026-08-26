using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using CodexQuota.UI.Avalonia;

namespace CodexQuota.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        if (args.Contains("--index-usage", StringComparer.OrdinalIgnoreCase))
        {
            Environment.ExitCode = UsageIndexWorker.RunAsync().GetAwaiter().GetResult();
            return;
        }
        WaitForPriorProcess(args);
        var isolatedSmoke = args.Contains("--isolated-smoke", StringComparer.OrdinalIgnoreCase) &&
                            string.Equals(Environment.GetEnvironmentVariable("CODEXQUOTA_ALLOW_ISOLATED_SMOKE"), "1",
                                StringComparison.Ordinal);
        using var gate = SingleInstanceGate.TryCreate(isolatedSmoke
            ? OperatingSystem.IsWindows() ? @"Local\CodexQuotaPanel.Smoke.v1" : "CodexQuotaPanel.Smoke.v1"
            : OperatingSystem.IsWindows() ? @"Local\CodexQuotaPanel.Singleton.v1" : "CodexQuotaPanel.Singleton.v1");
        if (!gate.IsPrimary)
        {
            gate.NotifyPrimary();
            return;
        }
        gate.ActivationRequested += () => App.CurrentCoordinator?.ActivatePrimarySurface();
        using var exitSignal = LegacyExitSignal.Create();
        exitSignal.ExitRequested += () => App.CurrentCoordinator?.RequestExitFromInstaller();
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();

    private static void WaitForPriorProcess(string[] args)
    {
        var marker = Array.IndexOf(args, "--restart-after");
        if (marker < 0 || marker + 1 >= args.Length || !int.TryParse(args[marker + 1], out var pid)) return;
        try { System.Diagnostics.Process.GetProcessById(pid).WaitForExit(8_000); } catch { }
    }
}

internal sealed class App : Avalonia.Application
{
    internal static RuntimeCoordinator? CurrentCoordinator { get; private set; }

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            try
            {
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexQuotaPanel");
                Directory.CreateDirectory(root);
                File.AppendAllText(Path.Combine(root, "crash.log"), $"{DateTimeOffset.Now:O} {e.Exception}\n");
            }
            catch { }
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            CurrentCoordinator = new RuntimeCoordinator(desktop);
            desktop.Exit += async (_, _) =>
            {
                if (CurrentCoordinator is not null) await CurrentCoordinator.DisposeAsync();
                CurrentCoordinator = null;
            };
            _ = StartSafelyAsync(CurrentCoordinator, desktop);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static async Task StartSafelyAsync(RuntimeCoordinator coordinator, IClassicDesktopStyleApplicationLifetime desktop)
    {
        try { await coordinator.StartAsync(); }
        catch (Exception ex)
        {
            try
            {
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "CodexQuotaPanel");
                Directory.CreateDirectory(root);
                File.AppendAllText(Path.Combine(root, "crash.log"), $"{DateTimeOffset.Now:O} startup {ex}\n");
            }
            catch { }
            var palette = UiPalette.For(CodexQuota.Application.AppTheme.Dark);
            var window = new Window
            {
                Title = "CodexQuota",
                Width = 430,
                Height = 220,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                Background = palette.Canvas,
                Content = new StackPanel { Margin = new Thickness(24), Spacing = 12, Children =
                {
                    UiElements.Text("CodexQuota 无法启动", 19, FontWeight.Bold, palette.Red),
                    UiElements.Text("配置会自动从备份恢复。若问题持续，请复制 LocalAppData/CodexQuotaPanel/crash.log。\n" + ex.GetType().Name,
                        12, FontWeight.Normal, palette.TextSecondary)
                }}
            };
            desktop.MainWindow = window;
            window.Show();
        }
    }
}
