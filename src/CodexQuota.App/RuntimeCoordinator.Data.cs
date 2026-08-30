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
        // A crash can leave the cursor file present but empty or truncated.
        // Treat that state exactly like a first launch so the finite worker,
        // rather than the resident UI process, repairs the historical index.
        var rebuilt = false;
        if (!JsonlUsageEventSource.HasUsableCursorState(cursorState))
            rebuilt = await UsageIndexWorker.RunChildAsync(_dataRoot, cancellationToken).ConfigureAwait(false);
        if (rebuilt)
            await RefreshAsync().ConfigureAwait(false);
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
            var retained = _lastTrustedQuotaSnapshot ?? _presentation.Snapshot;
            var freshSnapshot = false;
            string? error = null;
            var connectionState = QuotaConnectionState.Connecting;
            string? connectionDetail = null;
            try
            {
                var live = await _liveQuota.ReadAsync(_lifetime.Token).ConfigureAwait(false);
                // Once this process has displayed a trustworthy snapshot, never
                // replace it with a different fallback source during a transient
                // App Server outage. Alternating live and JSONL snapshots caused
                // visible quota jumps and vertical spikes in the trend chart.
                var local = live is null && retained is null
                    ? await _localQuota.ReadAsync(_lifetime.Token).ConfigureAwait(false)
                    : null;
                var selection = QuotaSnapshotContinuity.Select(live, retained, local);
                snapshot = selection.Snapshot;
                freshSnapshot = selection.IsFresh;
                if (selection.Kind == QuotaSnapshotSelectionKind.Live)
                {
                    _lastTrustedQuotaSnapshot = snapshot;
                    try
                    {
                        await _trustedQuotaStore.WriteAsync(snapshot!, _lifetime.Token).ConfigureAwait(false);
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        // A cache write failure must never downgrade a valid live reading.
                    }
                    connectionState = QuotaConnectionState.Live;
                    connectionDetail = T("已连接 Codex 实时额度服务", "Connected to Codex live quota service");
                }
                else if (selection.Kind == QuotaSnapshotSelectionKind.Retained)
                {
                    connectionState = QuotaConnectionState.Stale;
                    connectionDetail = DisconnectedDetail(snapshot!);
                }
                else if (selection.Kind == QuotaSnapshotSelectionKind.Local)
                {
                    _lastTrustedQuotaSnapshot = snapshot;
                    var age = DateTimeOffset.UtcNow - snapshot!.ObservedAt;
                    connectionState = snapshot.IsStale || age > TimeSpan.FromMinutes(3)
                        ? QuotaConnectionState.Stale
                        : QuotaConnectionState.LocalFallback;
                    connectionDetail = connectionState == QuotaConnectionState.Stale
                        ? T($"实时服务暂不可用；首次启动使用 {Math.Max(1, (int)age.TotalMinutes)} 分钟前的本地快照",
                            $"Live service unavailable; first-start local snapshot is {Math.Max(1, (int)age.TotalMinutes)} minutes old")
                        : T("实时服务暂不可用，首次启动使用本地 Codex 会话快照",
                            "Live service unavailable; using a local Codex session snapshot on first start");
                }
                else
                {
                    connectionState = QuotaConnectionState.Offline;
                    connectionDetail = T("尚未发现可用的 Codex 实时服务或本地额度快照",
                        "No Codex live service or local quota snapshot is available");
                }
                if (freshSnapshot && snapshot is not null && _settings.TrendRecordingEnabled)
                    await _history.AppendQuotaAsync(snapshot, _lifetime.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                if (retained is not null)
                {
                    snapshot = retained;
                    connectionState = QuotaConnectionState.Stale;
                    connectionDetail = DisconnectedDetail(retained);
                }
                else
                {
                    error = T("暂时无法读取 Codex 额度数据", "Codex quota data is temporarily unavailable");
                    connectionState = QuotaConnectionState.Offline;
                    connectionDetail = error;
                }
            }

            var rawHistory = await _history.ReadQuotaAsync(DateTimeOffset.UtcNow.AddHours(-24), _lifetime.Token).ConfigureAwait(false);
            var history = QuotaHistoryContinuity.RemoveTransientSourceSpikes(rawHistory);
            var usageSince = snapshot?.VisibleWindows.OrderByDescending(window => window.WindowMinutes).FirstOrDefault() is { } longest
                ? longest.ResetsAt?.AddMinutes(-longest.WindowMinutes) ?? DateTimeOffset.UtcNow.AddDays(-7)
                : DateTimeOffset.UtcNow.AddDays(-7);
            var usage = await _history.ReadUsageAsync(usageSince, _lifetime.Token).ConfigureAwait(false);
            var forecast = snapshot is null ? null : QuotaRunwayForecaster.Evaluate(snapshot, history);
            _presentation = new QuotaPresentation(snapshot, history, usage, forecast, false, error, DateTimeOffset.Now)
            {
                ConnectionState = connectionState,
                ConnectionDetail = connectionDetail
            };
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ApplyPresentation();
                CheckAlerts();
            });
        }
        finally { _refreshGate.Release(); }
    }

    private string DisconnectedDetail(OfficialQuotaSnapshot snapshot)
    {
        var age = DateTimeOffset.UtcNow - snapshot.ObservedAt;
        return age < TimeSpan.FromMinutes(1)
            ? T("实时服务断联，继续显示最近一次可信额度（不到 1 分钟前）",
                "Live service disconnected; showing the last trusted quota from less than a minute ago")
            : T($"实时服务断联，继续显示 {Math.Max(1, (int)age.TotalMinutes)} 分钟前的可信额度",
                $"Live service disconnected; showing trusted quota from {Math.Max(1, (int)age.TotalMinutes)} minutes ago");
    }

    private void SetRefreshing(bool refreshing)
    {
        _presentation = _presentation with
        {
            IsRefreshing = refreshing,
            ConnectionState = refreshing && _presentation.Snapshot is null
                ? QuotaConnectionState.Connecting
                : _presentation.ConnectionState,
            ConnectionDetail = refreshing && _presentation.Snapshot is null
                ? T("正在连接 Codex 数据源", "Connecting to Codex data source")
                : _presentation.ConnectionDetail
        };
        ApplyPresentation();
    }

    private void ApplyPresentation()
    {
        _orb?.ApplyPresentation(_presentation);
        _dashboard?.ApplyPresentation(_presentation);
        if (_usageWindow?.IsVisible == true) ShowUsageDetails();
        if (_tray is not null)
        {
            var windows = _presentation.Snapshot?.VisibleWindows ?? [];
            var minimum = windows.Count == 0 ? (double?)null : windows.Min(window => window.ClampedRemainingPercent);
            var state = _presentation.ConnectionState switch
            {
                QuotaConnectionState.Live => T("实时", "live"),
                QuotaConnectionState.LocalFallback => T("本地回退", "local fallback"),
                QuotaConnectionState.Stale => T("断联", "offline"),
                QuotaConnectionState.Offline => T("未连接", "offline"),
                _ => T("连接中", "connecting")
            };
            var quota = windows.Count == 0
                ? T("等待额度", "waiting for quota")
                : string.Join(" · ", windows.Select(window =>
                    $"{(window.WindowMinutes == 300 ? "5H" : window.WindowMinutes == 10_080 ? "7D" : $"{window.WindowMinutes}m")} " +
                    $"{window.ClampedRemainingPercent:0}%"));
            _tray.ToolTipText = $"CodexQuota · {quota} · {state}";
            using var icon = TrayIconFactory.Create(minimum ?? 0, _presentation.ConnectionState);
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
