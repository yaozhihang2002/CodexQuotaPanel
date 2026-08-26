using System.Reflection;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Input.Platform;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using Avalonia.Threading;
using CodexQuota.Application;
using CodexQuota.Domain;
using CodexQuota.Infrastructure;
using CodexQuota.Platform.macOS;
using CodexQuota.Platform.Windows;
using CodexQuota.UI.Avalonia;

namespace CodexQuota.App;

internal sealed partial class RuntimeCoordinator
{
    private async Task RefreshLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
            {
                await RefreshAsync().ConfigureAwait(false);
                Dispatcher.UIThread.Post(ApplyNativeOrbSettings);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task RunUsagePipelineAsync(CancellationToken cancellationToken)
    {
        var cursorState = Path.Combine(_dataRoot, "usage-file-state.jsonl");
        if (!File.Exists(cursorState))
            await UsageIndexWorker.RunChildAsync(_dataRoot, cancellationToken).ConfigureAwait(false);
        var source = new JsonlUsageEventSource(cursorStatePath: cursorState);
        await WatchUsageAsync(source, cancellationToken).ConfigureAwait(false);
    }

    private async Task WatchUsageAsync(JsonlUsageEventSource source, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var batch in source.WatchBatchesAsync(cancellationToken).ConfigureAwait(false))
                await _history.AppendUsageBatchAsync(batch, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { }
    }

    private async Task TopmostLoopAsync(CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                if (_settings.AlwaysOnTop) Dispatcher.UIThread.Post(ApplyNativeOrbSettings);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task RefreshAsync()
    {
        if (!await _refreshGate.WaitAsync(0, _lifetime.Token).ConfigureAwait(false)) return;
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => SetRefreshing(true));
            OfficialQuotaSnapshot? snapshot = null;
            string? error = null;
            try
            {
                var live = await _liveQuota.ReadAsync(_lifetime.Token).ConfigureAwait(false);
                snapshot = live ?? await _localQuota.ReadAsync(_lifetime.Token).ConfigureAwait(false);
                if (snapshot is not null && _settings.TrendRecordingEnabled)
                    await _history.AppendQuotaAsync(snapshot, _lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                error = T("暂时无法读取 Codex 额度数据", "Codex quota data is temporarily unavailable");
            }

            var history = await _history.ReadQuotaAsync(DateTimeOffset.UtcNow.AddHours(-24), _lifetime.Token).ConfigureAwait(false);
            var usageSince = snapshot?.VisibleWindows.OrderByDescending(window => window.WindowMinutes).FirstOrDefault() is { } longest
                ? longest.ResetsAt?.AddMinutes(-longest.WindowMinutes) ?? DateTimeOffset.UtcNow.AddDays(-7)
                : DateTimeOffset.UtcNow.AddDays(-7);
            var usage = await _history.ReadUsageAsync(usageSince, _lifetime.Token).ConfigureAwait(false);
            var forecast = snapshot is null ? null : QuotaRunwayForecaster.Evaluate(snapshot, history);
            _presentation = new QuotaPresentation(snapshot, history, usage, forecast, false, error, DateTimeOffset.Now);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ApplyPresentation();
                CheckAlerts();
            });
        }
        finally { _refreshGate.Release(); }
    }

    private void SetRefreshing(bool refreshing)
    {
        _presentation = _presentation with { IsRefreshing = refreshing };
        ApplyPresentation();
    }

    private void ApplyPresentation()
    {
        _orb?.ApplyPresentation(_presentation);
        _dashboard?.ApplyPresentation(_presentation);
        if (_usageWindow?.IsVisible == true) ShowUsageDetails();
        if (_tray is not null)
        {
            var minimum = _presentation.Snapshot?.VisibleWindows.Min(window => window.ClampedRemainingPercent);
            _tray.ToolTipText = minimum is null ? "CodexQuota · waiting" : $"CodexQuota · {minimum:0}%";
            using var icon = TrayIconFactory.Create(minimum ?? 0);
            _tray.Icon = new WindowIcon(icon);
        }
    }

    private void CheckAlerts()
    {
        if (!_settings.AlertsEnabled || IsQuietHours()) return;
        var window = _presentation.Snapshot?.VisibleWindows.OrderBy(window => window.ClampedRemainingPercent).FirstOrDefault();
        if (window is null || window.ResetsAt is null) return;
        var cycle = $"{window.Id}:{window.ResetsAt.Value.ToUnixTimeSeconds()}";
        if (_cycleAlertDismissal == cycle || _settings.DismissedAlertCycleKey == cycle) return;
        var critical = window.ClampedRemainingPercent <= _settings.CriticalThreshold;
        var warning = window.ClampedRemainingPercent <= _settings.WarningThreshold;
        if (!critical && !warning) return;
        if (critical && _settings.LastCriticalCycleKey == cycle || !critical && _settings.LastWarningCycleKey == cycle) return;
        var alert = new AlertWindow(_settings, critical ? T("额度严重不足", "Quota critically low") : T("额度提醒", "Quota alert"),
            $"{UiElements.WindowLabel(window, _settings.Language)} · {window.ClampedRemainingPercent:0}% {T("剩余", "remaining")}", critical);
        PrepareNativeWindowTheme(alert);
        alert.DismissForCycleRequested += async (_, _) =>
        {
            _cycleAlertDismissal = cycle;
            _settings = _settings with { DismissedAlertCycleKey = cycle };
            await _settingsStore.WriteAsync(_settings, _lifetime.Token).ConfigureAwait(false);
        };
        alert.Show();
        if (_settings.AlertSoundEnabled) _platform.PlayAlertSound();
        _settings = critical ? _settings with { LastCriticalCycleKey = cycle } : _settings with { LastWarningCycleKey = cycle };
        _ = _settingsStore.WriteAsync(_settings, _lifetime.Token);
    }

    private bool IsQuietHours()
    {
        if (!_settings.QuietHoursEnabled) return false;
        var minute = DateTime.Now.Hour * 60 + DateTime.Now.Minute;
        return _settings.QuietStartMinutes <= _settings.QuietEndMinutes
            ? minute >= _settings.QuietStartMinutes && minute < _settings.QuietEndMinutes
            : minute >= _settings.QuietStartMinutes || minute < _settings.QuietEndMinutes;
    }

}
