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
    private void ShowSettings()
    {
        if (_settingsWindow?.IsVisible == true) { _settingsWindow.Activate(); return; }
        _settingsWindow = new SettingsWindow(_settings, IsSystemDark(),
            _presentation.Snapshot?.VisibleWindows.Select(window => window.WindowMinutes).ToArray());
        PrepareNativeWindowTheme(_settingsWindow);
        _settingsWindow.PreviewChanged += draft => ApplyPreview(draft);
        _settingsWindow.SaveRequested += draft => _ = SaveSettingsAsync(draft);
        _settingsWindow.CancelRequested += (_, _) => CancelSettings();
        _settingsWindow.ImportRequested += async (_, _) => await ImportSettingsAsync();
        _settingsWindow.ExportRequested += async (_, _) => await ExportSettingsAsync();
        _settingsWindow.UpdateCheckRequested += async (_, _) => await CheckForUpdatesAsync(false);
        _settingsWindow.ClearHistoryRequested += async (_, _) => await ClearHistoryAsync();
        _settingsWindow.CopyDiagnosticsRequested += async (_, _) => await CopyDiagnosticsAsync();
        _settingsWindow.RestoreDefaultsRequested += async (_, _) => await ResetDefaultsAsync();
        _settingsWindow.OpenProjectRequested += (_, _) =>
            _platform.OpenUri(new Uri("https://github.com/yaozhihang2002/CodexQuotaPanel"));
        _settingsWindow.OpenPricingRequested += (_, _) =>
            _platform.OpenUri(new Uri(ApiCostEstimator.SourceUrl));
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    private void ApplyPreview(AppSettings draft)
    {
        ApplyApplicationTheme(draft);
        _orb?.ApplySettings(draft with { ClickThrough = _settings.ClickThrough });
        ApplyNativeWindowTheme(_settingsWindow, draft);
    }

    private async Task SaveSettingsAsync(AppSettings draft)
    {
        draft = draft.Normalize();
        if (!_settings.ClickThrough && draft.ClickThrough && draft.ShowClickThroughReminder)
        {
            var reminder = new ClickThroughReminderWindow(draft);
            PrepareNativeWindowTheme(reminder, draft);
            var enabled = await reminder.ShowDialog<bool>(_settingsWindow!);
            if (!enabled) return;
            if (reminder.DoNotShowAgain) draft = draft with { ShowClickThroughReminder = false };
        }
        _platform.SetStartWithSystem(draft.StartWithSystem);
        var visualShellChanged = draft.Theme != _settings.Theme || draft.Language != _settings.Language ||
                                 draft.InterfaceScalePercent != _settings.InterfaceScalePercent;
        _settings = draft;
        await _settingsStore.WriteAsync(_settings, _lifetime.Token).ConfigureAwait(true);
        ApplyApplicationTheme(_settings);
        _orb?.ApplySettings(_settings);
        ApplyNativeOrbSettings();
        ConfigureRecoveryShortcut();
        _settingsWindow?.MarkSaved(_settings);
        UpdateTrayState();
        if (visualShellChanged)
        {
            RecreateTray();
            RecreateSecondaryWindows();
        }
    }

    private void CancelSettings()
    {
        ApplyApplicationTheme(_settings);
        _orb?.ApplySettings(_settings);
        ApplyNativeOrbSettings();
        var old = _settingsWindow;
        _settingsWindow = null;
        old?.ClosePermanently();
    }

    private void RecreateSecondaryWindows()
    {
        var dashboardVisible = _dashboard?.IsVisible == true;
        _dashboard?.ClosePermanently();
        _dashboard = null;
        _usageWindow?.Close();
        _usageWindow = null;
        if (dashboardVisible)
        {
            EnsureDashboard();
            _dashboard!.ApplyPresentation(_presentation);
            if (!_dashboard.RestorePosition(_settings.DashboardX, _settings.DashboardY, _settings.DashboardDisplayId) &&
                _orb is not null)
                _dashboard.PlaceNear(_orb.Position, _settings.OrbSize);
            _ = _dashboard.AnimateInAsync();
        }
    }

    private void RecreateTray()
    {
        _tray?.Dispose();
        _tray = null;
        _clickThroughTrayItem = null;
        CreateTray();
    }

    private async Task ToggleClickThroughAsync()
    {
        var desired = !_settings.ClickThrough;
        if (desired && _settings.ShowClickThroughReminder)
        {
            Window? owner = _dashboard?.IsVisible == true ? _dashboard : _settingsWindow?.IsVisible == true ? _settingsWindow : null;
            if (owner is null && _orb is not null)
            {
                if (!_orb.IsVisible) _orb.Show();
                owner = _orb;
            }
            var reminder = new ClickThroughReminderWindow(_settings);
            PrepareNativeWindowTheme(reminder);
            if (owner is null) return;
            var enabled = await reminder.ShowDialog<bool>(owner);
            if (!enabled) return;
            if (reminder.DoNotShowAgain) _settings = _settings with { ShowClickThroughReminder = false };
        }
        _settings = _settings with { ClickThrough = desired };
        await _settingsStore.WriteAsync(_settings, _lifetime.Token).ConfigureAwait(true);
        _orb?.ApplySettings(_settings);
        ApplyNativeOrbSettings();
        UpdateTrayState();
    }

    private async Task ExportSettingsAsync()
    {
        if (_settingsWindow is null) return;
        var file = await _settingsWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = T("导出设置", "Export settings"), SuggestedFileName = "CodexQuotaPanel-settings.json",
            DefaultExtension = "json", FileTypeChoices = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        });
        if (file is null) return;
        var portable = _settings with { OrbX = null, OrbY = null, OrbDisplayId = null,
            DashboardX = null, DashboardY = null, DashboardDisplayId = null, StartWithSystem = false,
            DismissedAlertCycleKey = null, LastWarningCycleKey = null, LastCriticalCycleKey = null };
        await using var stream = await file.OpenWriteAsync();
        await JsonSerializer.SerializeAsync(stream, portable, new JsonSerializerOptions { WriteIndented = true }, _lifetime.Token);
    }

    private async Task ImportSettingsAsync()
    {
        if (_settingsWindow is null) return;
        var files = await _settingsWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = T("导入设置", "Import settings"), AllowMultiple = false,
            FileTypeFilter = [new FilePickerFileType("JSON") { Patterns = ["*.json"] }]
        });
        var file = files.FirstOrDefault();
        if (file is null) return;
        try
        {
            await using var stream = await file.OpenReadAsync();
            var imported = await JsonSerializer.DeserializeAsync<AppSettings>(stream, cancellationToken: _lifetime.Token);
            if (imported is null) throw new JsonException();
            await SaveSettingsAsync(imported with { OrbX = _settings.OrbX, OrbY = _settings.OrbY,
                OrbDisplayId = _settings.OrbDisplayId, DashboardX = _settings.DashboardX,
                DashboardY = _settings.DashboardY, DashboardDisplayId = _settings.DashboardDisplayId,
                StartWithSystem = _settings.StartWithSystem });
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            await ShowMessageAsync(T("导入失败", "Import failed"), T("文件不是有效的 CodexQuota 设置。", "The file is not valid CodexQuota settings."));
        }
    }

    private async Task ClearHistoryAsync()
    {
        await _history.ClearAsync(_lifetime.Token);
        await RefreshAsync();
        await ShowMessageAsync(T("已清除", "Cleared"), T("本地趋势和 Token 历史已清除。", "Local trend and token history were cleared."));
    }

    private async Task ResetDefaultsAsync()
    {
        var defaults = AppSettings.Default with
        {
            OrbX = _settings.OrbX,
            OrbY = _settings.OrbY,
            OrbDisplayId = _settings.OrbDisplayId,
            DashboardX = _settings.DashboardX,
            DashboardY = _settings.DashboardY,
            DashboardDisplayId = _settings.DashboardDisplayId,
            StartWithSystem = _settings.StartWithSystem
        };
        await SaveSettingsAsync(defaults);
        var old = _settingsWindow;
        _settingsWindow = null;
        old?.ClosePermanently();
        ShowSettings();
    }

    private async Task CopyDiagnosticsAsync()
    {
        if (_settingsWindow is null) return;
        var text = $"CodexQuota {VersionText}\nPlatform: {_platform.PlatformName}\nOS: {Environment.OSVersion}\n" +
                   $"Source: {_presentation.Snapshot?.Source ?? "unavailable"}\nWindows: {_presentation.Snapshot?.VisibleWindows.Count ?? 0}\n" +
                   $"History: {_presentation.History.Count}\nUsage events: {_presentation.Usage.Count}\nError: {_presentation.Error ?? "none"}";
        var clipboard = TopLevel.GetTopLevel(_settingsWindow)?.Clipboard;
        if (clipboard is not null) await clipboard.SetTextAsync(text);
        await ShowMessageAsync(T("诊断已复制", "Diagnostics copied"), T("内容已脱敏，不包含账户或会话文本。", "The text is redacted and contains no account or conversation content."));
    }

    private async Task CheckForUpdatesAsync(bool silent)
    {
        try
        {
            if (silent && await ReadUpdateCacheAsync() is { } cached &&
                DateTimeOffset.UtcNow - cached.LastCheckedUtc < TimeSpan.FromHours(24))
                return;
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            client.DefaultRequestHeaders.UserAgent.ParseAdd("CodexQuotaPanel-vNext");
            var json = await client.GetStringAsync("https://api.github.com/repos/yaozhihang2002/CodexQuotaPanel/releases?per_page=10", _lifetime.Token);
            using var document = JsonDocument.Parse(json);
            var latest = document.RootElement.EnumerateArray().FirstOrDefault(item => !item.GetProperty("draft").GetBoolean());
            if (latest.ValueKind == JsonValueKind.Undefined) throw new InvalidOperationException();
            var tag = latest.GetProperty("tag_name").GetString() ?? "unknown";
            var current = NormalizeVersion(VersionText);
            var remote = NormalizeVersion(tag);
            var newer = remote is not null && current is not null && remote > current;
            var releaseUrl = latest.GetProperty("html_url").GetString()!;
            await WriteUpdateCacheAsync(new UpdateCheckCache(DateTimeOffset.UtcNow, tag, releaseUrl));
            if (silent && !newer) return;
            var open = await ShowMessageAsync(T("检查更新", "Check for updates"), newer
                ? $"{T("发现新版本", "New version available")} {tag}"
                : $"{T("当前已是最新版本", "You are up to date")} · {VersionText}", newer);
            if (open) _platform.OpenUri(new Uri(releaseUrl));
        }
        catch when (!silent)
        {
            await ShowMessageAsync(T("检查失败", "Update check failed"), T("暂时无法连接 GitHub，请稍后重试。", "GitHub is temporarily unavailable."));
        }
    }

    private async Task<UpdateCheckCache?> ReadUpdateCacheAsync()
    {
        try
        {
            var path = Path.Combine(_dataRoot, "update-check.json");
            if (!File.Exists(path)) return null;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
            return await JsonSerializer.DeserializeAsync<UpdateCheckCache>(stream, cancellationToken: _lifetime.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { return null; }
    }

    private async Task WriteUpdateCacheAsync(UpdateCheckCache cache)
    {
        var path = Path.Combine(_dataRoot, "update-check.json");
        var temp = path + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None,
                             4096, FileOptions.Asynchronous | FileOptions.WriteThrough))
                await JsonSerializer.SerializeAsync(stream, cache, cancellationToken: _lifetime.Token)
                    .ConfigureAwait(false);
            File.Move(temp, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(temp); } catch { }
        }
    }

    private async Task<bool> ShowMessageAsync(string title, string message, bool open = false)
    {
        var dialog = new MessageWindow(_settings, title, message, open);
        PrepareNativeWindowTheme(dialog);
        Window? owner = _settingsWindow?.IsVisible == true ? _settingsWindow : _dashboard?.IsVisible == true ? _dashboard : null;
        if (owner is null && _orb is not null)
        {
            if (!_orb.IsVisible) _orb.Show();
            owner = _orb;
        }
        if (owner is null) return false;
        return await dialog.ShowDialog<bool>(owner);
    }

    private sealed record UpdateCheckCache(DateTimeOffset LastCheckedUtc, string LatestTag, string ReleaseUrl);

}
