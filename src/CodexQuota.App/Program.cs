using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;
using Avalonia.Threading;
using System.Data.Common;
using System.Diagnostics;
using CodexQuota.Application;
using CodexQuota.Infrastructure;
using CodexQuota.UI.Avalonia;

namespace CodexQuota.App;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args) => BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}

internal sealed class App : Avalonia.Application
{
    private readonly CancellationTokenSource _lifetime = new();

    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Dark;
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new PreviewWindow(new PreviewScenario(
                AppLanguage.SimplifiedChinese, AppTheme.Dark, false));
            desktop.MainWindow = window;
            desktop.Exit += (_, _) => _lifetime.Cancel();
            _ = LoadDataAsync(window, _lifetime.Token);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static async Task LoadDataAsync(PreviewWindow window, CancellationToken cancellationToken)
    {
        try
        {
            var dataRoot = LocalDataPathResolver.ResolveApplicationData();
            var history = new SqliteUsageHistoryStore(Path.Combine(dataRoot, "history-vnext.db"));
            await history.InitializeAsync(cancellationToken).ConfigureAwait(false);

            var localQuota = new JsonlQuotaSource();
            var snapshot = await localQuota.ReadAsync(cancellationToken).ConfigureAwait(false);
            if (snapshot is not null)
            {
                await history.AppendQuotaAsync(snapshot, cancellationToken).ConfigureAwait(false);
                Dispatcher.UIThread.Post(() => window.ApplyQuota(snapshot));
            }

            var liveQuota = await new CodexAppServerQuotaSource().ReadAsync(cancellationToken).ConfigureAwait(false);
            if (liveQuota is not null)
            {
                await history.AppendQuotaAsync(liveQuota, cancellationToken).ConfigureAwait(false);
                Dispatcher.UIThread.Post(() => window.ApplyQuota(liveQuota));
            }

            var usageSource = new JsonlUsageEventSource();
            await foreach (var usage in usageSource.WatchAsync(cancellationToken).ConfigureAwait(false))
                await history.AppendUsageAsync(usage, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   InvalidOperationException or DbException)
        {
            Trace.WriteLine($"vNext data pipeline unavailable: {ex.GetType().Name}");
        }
    }
}
