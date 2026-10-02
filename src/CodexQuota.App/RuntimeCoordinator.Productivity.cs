using Avalonia.Threading;
using CodexQuota.Domain;
using CodexQuota.Infrastructure;
using CodexQuota.UI.Avalonia;

namespace CodexQuota.App;

internal sealed partial class RuntimeCoordinator
{
    private readonly HttpClient _pricingClient = new(new HttpClientHandler { AllowAutoRedirect = false })
        { Timeout = TimeSpan.FromSeconds(8) };
    private PricingCatalogUpdater _pricingUpdater = null!;
    private Task? _productivityLoop;
    private OfficialQuotaSnapshot? _previousLiveSnapshot;
    private ResetToastWindow? _resetToast;

    private async Task InitializeProductivityAsync()
    {
        _pricingUpdater = new PricingCatalogUpdater(Path.Combine(_dataRoot, "pricing-v1.json"), _pricingClient);
        await _pricingUpdater.LoadAsync(_lifetime.Token);
    }

    private async Task ProductivityLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        try
        {
            do
            {
                if (_settings.AutoUpdatePricing && DateTimeOffset.UtcNow - _pricingUpdater.LastAttempt >= TimeSpan.FromHours(24))
                    await UpdatePricingAsync(true);
            } while (await timer.WaitForNextTickAsync(token));
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    private async Task UpdatePricingAsync(bool silent)
    {
        if (_shuttingDown) return;
        if (!silent) _settingsWindow?.SetPricingStatus(T("正在同步…", "Syncing…"), true);
        try
        {
            var result = await _pricingUpdater.UpdateAsync(_lifetime.Token);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (_shuttingDown) return;
                if (!silent)
                {
                    var message = result switch
                    {
                        PricingUpdateStatus.Updated => T("已同步", "Synced"),
                        PricingUpdateStatus.Current => T("已是最新费率", "Rates are up to date"),
                        PricingUpdateStatus.NotPublished => T("GitHub 费率表尚未发布", "GitHub price table is not published"),
                        PricingUpdateStatus.Outdated => T("远程表较旧，已保留本地费率", "Remote table is older; local rates retained"),
                        _ => T("同步失败，已保留本地费率", "Sync failed; local rates retained")
                    };
                    if (result is PricingUpdateStatus.Updated or PricingUpdateStatus.Current)
                        message += " · " + ApiCostEstimator.CurrentBasisDate;
                    _settingsWindow?.SetPricingStatus(message);
                }
                if (result == PricingUpdateStatus.Updated) ApplyPresentation();
            });
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested) { }
    }

    private void ShowQuotaRecovery(IReadOnlyList<QuotaWindow> windows)
    {
        if (_shuttingDown || windows.Count == 0 || IsQuietHours()) return;
        if (_settings.ResetCelebrationEnabled) _orb?.CelebrateReset();
        if (!_settings.ResetNotificationEnabled) return;
        _resetToast?.Close();
        _resetToast = new ResetToastWindow(_settings,
            string.Join(" · ", windows.Select(w => $"{UiElements.WindowLabel(w, _settings.Language)} {w.ClampedRemainingPercent:0}%")));
        PrepareNativeWindowTheme(_resetToast);
        _resetToast.Show();
    }
}
