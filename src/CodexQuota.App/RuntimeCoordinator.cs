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

internal sealed partial class RuntimeCoordinator : IAsyncDisposable
{
    private readonly IClassicDesktopStyleApplicationLifetime _desktop;
    private readonly IPlatformShell _platform;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly string _dataRoot;
    private readonly JsonSettingsStore _settingsStore;
    private readonly SqliteUsageHistoryStore _history;
    private readonly JsonlQuotaSource _localQuota = new();
    private readonly CodexAppServerQuotaSource _liveQuota = new();
    private AppSettings _settings = AppSettings.Default;
    private QuotaPresentation _presentation = QuotaPresentation.Empty;
    private OrbWindow? _orb;
    private DashboardWindow? _dashboard;
    private SettingsWindow? _settingsWindow;
    private UsageDetailsWindow? _usageWindow;
    private TrayIcon? _tray;
    private NativeMenuItem? _clickThroughTrayItem;
    private NativeMenuItem? _moveOrbTrayItem;
    private IDisposable? _recoveryShortcut;
    private Task? _refreshLoop;
    private Task? _usageLoop;
    private Task? _topmostLoop;
    private string? _cycleAlertDismissal;
    private bool _temporaryMoveMode;

    public RuntimeCoordinator(IClassicDesktopStyleApplicationLifetime desktop)
    {
        _desktop = desktop;
        _desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
        _platform = OperatingSystem.IsWindows() ? new WindowsPlatformShell() : new MacOSPlatformShell();
        var isolatedDataRoot = string.Equals(
            Environment.GetEnvironmentVariable("CODEXQUOTA_ALLOW_ISOLATED_SMOKE"), "1", StringComparison.Ordinal)
            ? Environment.GetEnvironmentVariable("CODEXQUOTA_SMOKE_DATA_ROOT")
            : null;
        _dataRoot = string.IsNullOrWhiteSpace(isolatedDataRoot)
            ? LocalDataPathResolver.ResolveApplicationData()
            : Path.GetFullPath(isolatedDataRoot);
        _settingsStore = new JsonSettingsStore(Path.Combine(_dataRoot, "settings-vnext.json"),
            Path.Combine(_dataRoot, "preferences.json"));
        _history = new SqliteUsageHistoryStore(Path.Combine(_dataRoot, "history-vnext.db"));
    }

    public async Task StartAsync()
    {
        Directory.CreateDirectory(_dataRoot);
        var storedSettings = await _settingsStore.ReadAsync(_lifetime.Token).ConfigureAwait(true);
        _settings = (storedSettings ?? AppSettings.Default).Normalize() with
        {
            StartWithSystem = _platform.GetStartWithSystem(),
            Language = storedSettings is null ? _platform.GetInitialLanguage() ?? AppSettings.Default.Language : storedSettings.Language
        };
        if (storedSettings is null)
            await _settingsStore.WriteAsync(_settings, _lifetime.Token).ConfigureAwait(true);
        await _history.InitializeAsync(_lifetime.Token).ConfigureAwait(true);
        ApplyApplicationTheme(_settings);
        CreateOrb();
        CreateTray();
        ConfigureRecoveryShortcut();
        ShowStartupView();
        _refreshLoop = RefreshLoopAsync(_lifetime.Token);
        _usageLoop = RunUsagePipelineAsync(_lifetime.Token);
        _topmostLoop = TopmostLoopAsync(_lifetime.Token);
        await RefreshAsync().ConfigureAwait(true);
        if (_settings.CheckForUpdatesOnStartup) _ = CheckForUpdatesAsync(true);
    }

    public void ActivatePrimarySurface()
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_dashboard?.IsVisible == true) { _dashboard.Activate(); return; }
            if (_settingsWindow?.IsVisible == true) { _settingsWindow.Activate(); return; }
            _ = OpenDashboardAsync();
        });
    }

    public void RequestExitFromInstaller() => Dispatcher.UIThread.Post(Exit);

    private void CreateOrb()
    {
        _orb = new OrbWindow();
        _orb.ApplySettings(_settings);
        _orb.ApplyPresentation(_presentation);
        _orb.RestorePosition(_settings.OrbX, _settings.OrbY);
        _orb.OpenDetailsRequested += async (_, _) => await OpenDashboardAsync();
        _orb.MoveCompleted += async (_, _) =>
        {
            var placement = _orb.ConstrainPosition(_settings.SnapToEdge);
            _settings = _settings with { OrbX = placement.Position.X, OrbY = placement.Position.Y,
                OrbDisplayId = placement.DisplayId, LastView = StartupViewMode.Orb };
            await _settingsStore.WriteAsync(_settings, _lifetime.Token).ConfigureAwait(false);
            if (_temporaryMoveMode)
            {
                _temporaryMoveMode = false;
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    _orb.SetMoveMode(false);
                    ApplyNativeOrbSettings();
                    UpdateTrayState();
                });
            }
        };
        _orb.Opened += (_, _) => ApplyNativeOrbSettings();
        _desktop.MainWindow = _orb;
    }

    private void CreateTray()
    {
        var menu = new NativeMenu();
        AddTrayItem(menu, T("展开额度详情", "Open quota details"), () => _ = OpenDashboardAsync());
        AddTrayItem(menu, T("显示/隐藏悬浮球", "Show/hide orb"), ToggleOrb);
        _moveOrbTrayItem = AddTrayItem(menu, T("移动悬浮球…", "Move orb…"), BeginOrbMoveMode);
        AddTrayItem(menu, T("立即刷新", "Refresh now"), () => _ = RefreshAsync());
        _clickThroughTrayItem = AddTrayItem(menu, ClickThroughTrayHeader(), () => _ = ToggleClickThroughAsync());
        _clickThroughTrayItem.IsChecked = false;
        menu.Items.Add(new NativeMenuItemSeparator());
        AddTrayItem(menu, T("设置…", "Settings…"), ShowSettings);
        AddTrayItem(menu, T("官方额度说明", "Official quota help"), () =>
            _platform.OpenUri(new Uri("https://help.openai.com/en/articles/11369540-codex-in-chatgpt")));
        AddTrayItem(menu, T("重新启动应用", "Restart application"), Restart);
        menu.Items.Add(new NativeMenuItemSeparator());
        AddTrayItem(menu, T("退出", "Exit"), Exit);
        using var iconStream = AssetLoader.Open(new Uri("avares://CodexQuotaPanel/Assets/CodexQuotaPanel.ico"));
        _tray = new TrayIcon
        {
            Icon = new WindowIcon(iconStream),
            ToolTipText = "CodexQuota",
            Menu = menu,
            IsVisible = true
        };
        _tray.Clicked += (_, _) => _ = OpenDashboardAsync();
    }

    private NativeMenuItem AddTrayItem(NativeMenu menu, string header, Action action)
    {
        var item = new NativeMenuItem(header);
        item.Click += (_, _) => action();
        menu.Items.Add(item);
        return item;
    }

    private void ShowStartupView()
    {
        var mode = _settings.StartupView == StartupViewMode.RestorePrevious ? _settings.LastView : _settings.StartupView;
        switch (mode)
        {
            case StartupViewMode.Details:
                _ = OpenDashboardAsync();
                break;
            case StartupViewMode.TrayOnly:
                break;
            default:
                _orb!.Show();
                _orb.RestorePosition(_settings.OrbX, _settings.OrbY);
                break;
        }
    }

    private async Task OpenDashboardAsync()
    {
        if (_dashboard?.IsVisible == true) { _dashboard.Activate(); return; }
        EnsureDashboard();
        if (!_dashboard!.RestorePosition(_settings.DashboardX, _settings.DashboardY, _settings.DashboardDisplayId) &&
            _orb is not null)
            _dashboard.PlaceNear(_orb.Position, _settings.OrbSize);
        if (_orb?.IsVisible == true) await _orb.AnimateOutAsync();
        _dashboard!.ApplyPresentation(_presentation);
        await _dashboard.AnimateInAsync();
        _settings = _settings with { LastView = StartupViewMode.Details };
        await _settingsStore.WriteAsync(_settings, _lifetime.Token).ConfigureAwait(false);
    }

    private void EnsureDashboard()
    {
        if (_dashboard is not null) return;
        _dashboard = new DashboardWindow(_settings, IsSystemDark());
        PrepareNativeWindowTheme(_dashboard);
        _dashboard.CollapseRequested += async (_, _) => await CollapseDashboardAsync();
        _dashboard.RefreshRequested += async (_, _) => await RefreshAsync();
        _dashboard.SettingsRequested += (_, _) => ShowSettings();
        _dashboard.UsageDetailsRequested += (_, _) => ShowUsageDetails();
        _dashboard.PlacementCommitted += (position, displayId) =>
            _ = SaveDashboardPlacementAsync(position, displayId);
    }

    private async Task SaveDashboardPlacementAsync(PixelPoint position, string displayId)
    {
        _settings = _settings with
        {
            DashboardX = position.X,
            DashboardY = position.Y,
            DashboardDisplayId = displayId
        };
        await _settingsStore.WriteAsync(_settings, _lifetime.Token).ConfigureAwait(false);
    }

    private async Task CollapseDashboardAsync()
    {
        if (_dashboard?.IsVisible == true) await _dashboard.AnimateOutAsync();
        if (_orb is not null)
        {
            _orb.RestorePosition(_settings.OrbX, _settings.OrbY);
            await _orb.AnimateInAsync();
            ApplyNativeOrbSettings();
        }
        _settings = _settings with { LastView = StartupViewMode.Orb };
        await _settingsStore.WriteAsync(_settings, _lifetime.Token).ConfigureAwait(false);
    }

    private void ShowUsageDetails()
    {
        var alreadyVisible = _usageWindow?.IsVisible == true;
        if (_usageWindow is null)
        {
            _usageWindow = new UsageDetailsWindow(_settings, IsSystemDark());
            PrepareNativeWindowTheme(_usageWindow);
            _usageWindow.Closed += (_, _) => _usageWindow = null;
        }
        var longest = _presentation.Snapshot?.VisibleWindows.OrderByDescending(window => window.WindowMinutes).FirstOrDefault();
        var start = longest?.ResetsAt?.AddMinutes(-longest.WindowMinutes);
        _usageWindow.ApplyUsage(_presentation.Usage, start, longest?.ResetsAt);
        if (!alreadyVisible)
        {
            _usageWindow.Show();
            _usageWindow.Activate();
        }
    }

    private void ToggleOrb()
    {
        if (_orb is null) return;
        if (_orb.IsVisible) _orb.Hide();
        else { _orb.Show(); _orb.RestorePosition(_settings.OrbX, _settings.OrbY); ApplyNativeOrbSettings(); }
    }

    private void BeginOrbMoveMode()
    {
        if (_orb is null) return;
        if (_temporaryMoveMode)
        {
            _temporaryMoveMode = false;
            _orb.SetMoveMode(false);
            ApplyNativeOrbSettings();
            UpdateTrayState();
            return;
        }
        _temporaryMoveMode = true;
        _orb.SetMoveMode(true);
        if (!_orb.IsVisible) _orb.Show();
        _orb.RestorePosition(_settings.OrbX, _settings.OrbY);
        if (_orb.TryGetPlatformHandle()?.Handle is { } handle && handle != 0)
        {
            _platform.SetClickThrough(handle, false);
            _platform.SetWindowTopMost(handle, true);
        }
        UpdateTrayState();
    }

    private void ApplyApplicationTheme(AppSettings settings)
    {
        if (global::Avalonia.Application.Current is null) return;
        UiElements.ScaleFactor = settings.InterfaceScalePercent / 100d;
        global::Avalonia.Application.Current.RequestedThemeVariant = settings.Theme switch
        {
            AppTheme.Dark => ThemeVariant.Dark,
            AppTheme.Light => ThemeVariant.Light,
            _ => ThemeVariant.Default
        };
    }

    private void ApplyNativeOrbSettings()
    {
        if (_orb?.TryGetPlatformHandle()?.Handle is { } orbHandle && orbHandle != 0)
        {
            _platform.SetWindowTopMost(orbHandle, _settings.AlwaysOnTop);
            _platform.SetClickThrough(orbHandle, _settings.ClickThrough && !_temporaryMoveMode);
        }

        if (_dashboard?.IsVisible == true &&
            _dashboard.TryGetPlatformHandle()?.Handle is { } dashboardHandle && dashboardHandle != 0)
        {
            _dashboard.Topmost = _settings.AlwaysOnTop;
            _platform.SetWindowTopMost(dashboardHandle, _settings.AlwaysOnTop);
        }
    }

    private void ApplyNativeWindowTheme(Window? window, AppSettings? settings = null)
    {
        if (window?.TryGetPlatformHandle()?.Handle is not { } handle || handle == 0) return;
        settings ??= _settings;
        var dark = settings.Theme == AppTheme.Dark || settings.Theme == AppTheme.System && IsSystemDark();
        _platform.SetWindowDarkMode(handle, dark);
    }

    private void PrepareNativeWindowTheme(Window window, AppSettings? settings = null)
    {
        window.Opened += (_, _) =>
        {
            ApplyNativeWindowTheme(window, settings);
            if (ReferenceEquals(window, _dashboard)) ApplyNativeOrbSettings();
            Dispatcher.UIThread.Post(() => ApplyNativeWindowTheme(window, settings), DispatcherPriority.Background);
            if (ReferenceEquals(window, _dashboard))
                Dispatcher.UIThread.Post(ApplyNativeOrbSettings, DispatcherPriority.Background);
        };
    }

    private void ConfigureRecoveryShortcut()
    {
        _recoveryShortcut?.Dispose();
        _recoveryShortcut = _settings.GlobalRecoveryShortcutEnabled
            ? _platform.RegisterRecoveryShortcut(() => Dispatcher.UIThread.Post(async () =>
            {
                if (_settings.ClickThrough) await ToggleClickThroughAsync();
                else { _orb?.Show(); ApplyNativeOrbSettings(); }
            }))
            : null;
    }

    private void UpdateTrayState()
    {
        if (_clickThroughTrayItem is not null)
        {
            // Native checked items reserve a left gutter. Keep the label stable and
            // use the conventional right-aligned accelerator column for the mark.
            _clickThroughTrayItem.IsChecked = false;
            _clickThroughTrayItem.Header = ClickThroughTrayHeader();
        }
        if (_moveOrbTrayItem is not null)
            _moveOrbTrayItem.Header = _temporaryMoveMode
                ? T("取消移动模式", "Cancel move mode")
                : T("移动悬浮球…", "Move orb…");
    }

    private string ClickThroughTrayHeader() => _settings.ClickThrough
        ? T("悬浮球鼠标穿透\t✓", "Orb click-through\t✓")
        : T("悬浮球鼠标穿透", "Orb click-through");

    private void Restart()
    {
        _platform.RestartApplication();
        Exit();
    }

    private void Exit()
    {
        _lifetime.Cancel();
        _tray?.Dispose();
        _desktop.Shutdown();
    }

    private bool IsSystemDark() => global::Avalonia.Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
    private string T(string zh, string en) => _settings.Language == AppLanguage.SimplifiedChinese ? zh : en;
    private static string VersionText => Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion.Split('+')[0] ?? "0.0.0";
    private static Version? NormalizeVersion(string value)
    {
        var clean = value.Trim().TrimStart('v', 'V').Split('-', '+')[0];
        return Version.TryParse(clean, out var version) ? version : null;
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        _recoveryShortcut?.Dispose();
        _tray?.Dispose();
        foreach (var task in new[] { _refreshLoop, _usageLoop, _topmostLoop }.Where(task => task is not null))
            try { await task!; } catch (OperationCanceledException) { }
        _refreshGate.Dispose();
        await _liveQuota.DisposeAsync();
        _lifetime.Dispose();
    }

}
