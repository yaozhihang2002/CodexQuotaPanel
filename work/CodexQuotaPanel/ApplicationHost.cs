using Microsoft.Win32;
using System.Drawing.Drawing2D;
using System.Media;
using System.Runtime.InteropServices;

namespace CodexQuotaPanel;

internal sealed class QuotaApplicationContext : ApplicationContext
{
    private readonly QuotaCoordinator _coordinator = new();
    private readonly QuotaHistoryStore _history;
    private readonly QuotaForm _form;
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu;
    private readonly ToolStripMenuItem _clickThroughItem;
    private readonly ToolStripMenuItem _expandItem;
    private readonly ToolStripMenuItem _toggleOrbItem;
    private readonly ToolStripMenuItem _refreshItem;
    private readonly ToolStripMenuItem _settingsItem;
    private readonly ToolStripMenuItem _helpItem;
    private readonly ToolStripMenuItem _exitItem;
    private readonly EventWaitHandle _showSignal;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly AlertDedupState _alertState = AlertStateStore.Load();
    private const int GlobalHotKeyId = 0x4351;
    private const uint ModAlt = 0x0001;
    private const uint ModControl = 0x0002;
    private const uint ModNoRepeat = 0x4000;
    private Task? _showTask;
    private Task? _coordinatorStartTask;
    private bool _applyingPreferences;
    private bool _hotKeyRegistered;
    private bool _systemEventsAttached;
    private bool _exiting;
    private int? _trayIconRemainingPercent;
    private bool _trayIconLive;
    private PanelPreferences _preferences;
    private QuotaSnapshot? _latestSnapshot;

    public QuotaApplicationContext(EventWaitHandle showSignal)
    {
        _preferences = PanelPreferenceManager.Load();
        L10n.SetLanguage((AppLanguage)_preferences.Language);
        UiPalette.SetTheme(_preferences.ThemeMode);
        _history = new QuotaHistoryStore(enabled: _preferences.TrendRecordingEnabled);
        _form = new QuotaForm();
        _showSignal = showSignal;
        _form.RefreshRequested += () => _ = RefreshAsync();
        _form.OrbPositionChanged += point =>
        {
            _preferences = _preferences with { OrbX = point.X, OrbY = point.Y };
            PanelPreferenceManager.Save(_preferences);
        };
        _form.TopMostChangedByUser += value =>
        {
            if (_applyingPreferences) return;
            _preferences = _preferences with { AlwaysOnTop = value };
            PanelPreferenceManager.Save(_preferences);
        };
        _form.ViewStateChanged += state =>
        {
            if (_applyingPreferences) return;
            _preferences = _preferences with { LastViewMode = state };
            PanelPreferenceManager.Save(_preferences);
            UpdateMenuState();
        };
        _form.GlobalHotKeyPressed += OnGlobalHotKeyPressed;
        ApplyPreferencesToForm(_preferences);
        _form.SetHistory(_history.GetRecent());

        _menu = new ContextMenuStrip
        {
            BackColor = UiPalette.Surface,
            ForeColor = UiPalette.Text,
            Font = UiPalette.Body(9),
            ShowImageMargin = false,
            ShowCheckMargin = true,
            ShowItemToolTips = true,
            Renderer = new AppToolStripRenderer()
        };
        _expandItem = new ToolStripMenuItem(L10n.ExpandDetails, null, (_, _) => _form.ShowDetails());
        _toggleOrbItem = new ToolStripMenuItem(string.Empty, null, (_, _) => ToggleOrbVisibility());
        _refreshItem = new ToolStripMenuItem(L10n.RefreshNow, null, (_, _) => _ = RefreshAsync());
        _menu.Items.Add(_expandItem);
        _menu.Items.Add(_toggleOrbItem);
        _menu.Items.Add(_refreshItem);
        _menu.Items.Add(new ToolStripSeparator());

        _clickThroughItem = new ToolStripMenuItem(L10n.ClickThrough)
        {
            Checked = _preferences.OrbClickThrough,
            CheckOnClick = true,
            ToolTipText = L10n.ClickThroughHint
        };
        _clickThroughItem.CheckedChanged += ClickThroughItemOnCheckedChanged;
        _menu.Items.Add(_clickThroughItem);
        _settingsItem = new ToolStripMenuItem(L10n.Pick("设置…", "Settings…"), null, (_, _) => ShowSettingsCenter());
        _menu.Items.Add(_settingsItem);
        _menu.Items.Add(new ToolStripSeparator());
        _helpItem = new ToolStripMenuItem(L10n.OfficialHelp, null, (_, _) => QuotaForm.OpenOfficialHelp());
        _exitItem = new ToolStripMenuItem(L10n.Exit, null, (_, _) => ExitApplication());
        _menu.Items.Add(_helpItem);
        _menu.Items.Add(_exitItem);
        _menu.Opening += (_, _) => UpdateMenuState();

        _form.SetSharedContextMenu(_menu);

        _tray = new NotifyIcon
        {
            Text = L10n.Pick("Codex 额度面板 · 正在连接", "Codex quota panel · Connecting"),
            Icon = IconFactory.CreateTrayIcon(null, live: false),
            Visible = true,
            ContextMenuStrip = _menu
        };
        _tray.DoubleClick += (_, _) => ShowDetailsFromTray();
        _tray.Click += (_, e) =>
        {
            if (e is MouseEventArgs { Button: MouseButtons.Left }) ShowDetailsFromTray();
        };

        _coordinator.SnapshotChanged += OnSnapshotChanged;
        _coordinator.StatusChanged += status => SafeUi(() =>
        {
            _form.SetStatus(status);
            if (L10n.IsDisconnectedStatus(status))
                UpdateTrayIcon(_latestSnapshot?.RemainingPercent, live: false);
        });
        AttachSystemEvents();
        _form.ShowOrb(animate: false);
        _form.RestoreOrbLocation(_preferences.OrbX, _preferences.OrbY);
        ApplyStartupView();
        if (_preferences.LastViewMode != _form.ViewState)
        {
            _preferences = _preferences with { LastViewMode = _form.ViewState };
            PanelPreferenceManager.Save(_preferences);
        }
        UpdateGlobalHotKeyRegistration(showFailure: false);
        UpdateMenuState();
        _coordinatorStartTask = _coordinator.StartAsync();
        _showTask = Task.Run(WaitForShowSignal);
    }

    private void ApplyPreferencesToForm(PanelPreferences preferences)
    {
        preferences = PanelPreferenceManager.Normalize(preferences);
        _applyingPreferences = true;
        try
        {
            L10n.SetLanguage((AppLanguage)preferences.Language);
            var previousColors = UiPalette.CurrentColors;
            UiPalette.SetTheme(preferences.ThemeMode);
            _form.ApplyTheme(previousColors);
            _form.SetOrbOpacityPercent(preferences.OrbOpacityPercent);
            _form.SetOrbClickThroughPreference(preferences.OrbClickThrough);
            _form.SetHoverPreviewEnabled(preferences.HoverPreviewEnabled);
            _form.SetTopMostPreference(preferences.AlwaysOnTop);
            _form.SetOrbSize(preferences.OrbSize);
            _form.SetPositionLocked(preferences.PositionLocked);
            _form.SetSnapToEdge(preferences.SnapToEdge);
            _form.SetConsumptionFlameEnabled(preferences.ConsumptionFlameEnabled);
            _form.SetConsumptionFlameStyle(preferences.ConsumptionFlameStyle);
            _form.ConfigureRings(RingDisplayConfiguration.FromPreferences(preferences));
            _history.SetEnabled(preferences.TrendRecordingEnabled);
            _form.SetHistory(preferences.TrendRecordingEnabled ? _history.GetRecent() : []);
            _form.ApplyLanguage();
        }
        finally
        {
            _applyingPreferences = false;
        }
    }

    private void ApplyPreferencePreview(PanelPreferences previous, PanelPreferences next)
    {
        previous = PanelPreferenceManager.Normalize(previous);
        next = PanelPreferenceManager.Normalize(next);
        if (previous == next) return;

        _applyingPreferences = true;
        try
        {
            if (previous.ThemeMode != next.ThemeMode)
            {
                var previousColors = UiPalette.ResolveColors(previous.ThemeMode);
                UiPalette.SetTheme(next.ThemeMode);
                _form.ApplyTheme(previousColors);
                _menu.BackColor = UiPalette.Surface;
                _menu.ForeColor = UiPalette.Text;
                _menu.Invalidate();
            }
            if (previous.Language != next.Language)
            {
                L10n.SetLanguage((AppLanguage)next.Language);
                _form.ApplyLanguage();
            }
            if (previous.OrbOpacityPercent != next.OrbOpacityPercent)
                _form.SetOrbOpacityPercent(next.OrbOpacityPercent);
            if (previous.OrbClickThrough != next.OrbClickThrough)
                _form.SetOrbClickThroughPreference(next.OrbClickThrough);
            if (previous.HoverPreviewEnabled != next.HoverPreviewEnabled)
                _form.SetHoverPreviewEnabled(next.HoverPreviewEnabled);
            if (previous.AlwaysOnTop != next.AlwaysOnTop)
                _form.SetTopMostPreference(next.AlwaysOnTop);
            if (previous.OrbSize != next.OrbSize)
                _form.PreviewOrbSize(next.OrbSize);
            if (previous.PositionLocked != next.PositionLocked)
                _form.SetPositionLocked(next.PositionLocked);
            if (previous.SnapToEdge != next.SnapToEdge)
                _form.SetSnapToEdge(next.SnapToEdge);
            if (previous.ConsumptionFlameEnabled != next.ConsumptionFlameEnabled)
                _form.SetConsumptionFlameEnabled(next.ConsumptionFlameEnabled);
            if (previous.ConsumptionFlameStyle != next.ConsumptionFlameStyle)
                _form.SetConsumptionFlameStyle(next.ConsumptionFlameStyle);

            var previousRings = RingDisplayConfiguration.FromPreferences(previous);
            var nextRings = RingDisplayConfiguration.FromPreferences(next);
            if (previousRings != nextRings)
                _form.ConfigureRings(nextRings);

            if (previous.TrendRecordingEnabled != next.TrendRecordingEnabled)
            {
                _history.SetEnabled(next.TrendRecordingEnabled);
                _form.SetHistory(next.TrendRecordingEnabled ? _history.GetRecent() : []);
            }
        }
        finally
        {
            _applyingPreferences = false;
        }
    }

    private void ApplyStartupView()
    {
        var view = _preferences.StartupViewMode switch
        {
            1 => QuotaForm.OrbViewState,
            2 => QuotaForm.DetailsViewState,
            3 => QuotaForm.HiddenViewState,
            _ => _preferences.LastViewMode
        };
        switch (view)
        {
            case QuotaForm.DetailsViewState:
                _form.ShowDetails(animate: false);
                break;
            case QuotaForm.HiddenViewState:
                _form.HidePanel();
                break;
            default:
                _form.ShowOrb(animate: false);
                break;
        }
    }

    private void ShowSettingsCenter()
    {
        _menu.Close();
        var original = _preferences;
        var originalStartup = StartupManager.IsEnabled();
        var resetRequested = false;
        var lastPreview = original;
        var diagnostics = SanitizedDiagnostics.Create(
            _latestSnapshot?.Source,
            _latestSnapshot?.ObservedAt,
            _history);
        using var settings = new SettingsForm(original, originalStartup, _latestSnapshot, diagnostics);
        settings.CenterOnDisplay(Cursor.Position);
        settings.PreviewPreferencesChanged += preview =>
        {
            preview = PanelPreferenceManager.Normalize(preview);
            ApplyPreferencePreview(lastPreview, preview);
            lastPreview = preview;
        };
        settings.MoveToCurrentDisplayRequested += () =>
        {
            _form.MoveOrbToCurrentDisplay();
            SaveCurrentOrbLocation();
        };
        settings.ClearHistoryRequested += () =>
        {
            var cleared = _history.Clear();
            _form.SetHistory([]);
            ShowInformation(
                cleared ? L10n.Pick("趋势数据已清除", "Trend history cleared") : L10n.Pick("未能完全清除趋势数据", "Trend history could not be fully cleared"),
                cleared ? ToolTipIcon.Info : ToolTipIcon.Warning);
        };
        settings.ResetRequested += () => resetRequested = true;
        settings.SaveRequested += () =>
        {
            var selected = PanelPreferenceManager.Normalize(settings.SelectedPreferences);
            if (!resetRequested)
            {
                selected = selected with
                {
                    OrbX = _preferences.OrbX,
                    OrbY = _preferences.OrbY,
                    LastViewMode = _preferences.LastViewMode
                };
            }

            try
            {
                if (settings.StartupEnabled != StartupManager.IsEnabled())
                    StartupManager.SetEnabled(settings.StartupEnabled);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                       System.Security.SecurityException or ArgumentException)
            {
                MessageBox.Show(
                    L10n.Pick("无法修改开机启动设置，请检查当前用户权限。", "Could not change the startup setting. Check the current user permissions."),
                    L10n.AppTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            if (resetRequested) PanelPreferenceManager.DeleteAll();
            var languageChanged = selected.Language != _preferences.Language;
            _preferences = selected;
            ApplyPreferencePreview(lastPreview, _preferences);
            lastPreview = settings.SelectedPreferences;
            if (resetRequested)
            {
                _form.ShowOrb(animate: false);
                _form.MoveOrbToCurrentDisplay();
                _preferences = _preferences with { LastViewMode = QuotaForm.OrbViewState };
                SaveCurrentOrbLocation();
            }
            PanelPreferenceManager.Save(_preferences);
            UpdateGlobalHotKeyRegistration(showFailure: true);
            if (languageChanged) ApplyMenuLanguage();
            UpdateRuntimeMenu();
            settings.MarkSaved(StartupManager.IsEnabled());
            resetRequested = false;
        };

        _settingsItem.Enabled = false;
        try
        {
            settings.ShowDialog();
        }
        finally
        {
            _settingsItem.Enabled = true;
        }
        UpdateRuntimeMenu();
    }

    private void SaveCurrentOrbLocation()
    {
        if (!_form.IsOrb) return;
        _preferences = _preferences with { OrbX = _form.Location.X, OrbY = _form.Location.Y };
        PanelPreferenceManager.Save(_preferences);
    }

    private void ShowDetailsFromTray()
    {
        _form.ShowDetails();
        UpdateMenuState();
    }

    private void ToggleOrbVisibility()
    {
        if (_form.IsHidden)
            _form.ShowOrb();
        else
            _form.HidePanel();
        UpdateMenuState();
    }

    private void UpdateRuntimeMenu()
    {
        _applyingPreferences = true;
        try { _clickThroughItem.Checked = _preferences.OrbClickThrough; }
        finally { _applyingPreferences = false; }
        ApplyMenuLanguage();
        UpdateMenuState();
    }

    private void UpdateMenuState()
    {
        if (_form.IsDisposed) return;
        _expandItem.Enabled = !_form.IsDetails;
        _toggleOrbItem.Text = _form.IsHidden
            ? L10n.Pick("显示悬浮球", "Show quota orb")
            : L10n.Pick("隐藏悬浮球（仅托盘）", "Hide orb (tray only)");
    }

    private void ApplyMenuLanguage()
    {
        _menu.Font = UiPalette.Body(9);
        _expandItem.Text = L10n.ExpandDetails;
        _refreshItem.Text = L10n.RefreshNow;
        _clickThroughItem.Text = L10n.ClickThrough;
        _clickThroughItem.ToolTipText = L10n.ClickThroughHint;
        _settingsItem.Text = L10n.Pick("设置…", "Settings…");
        _helpItem.Text = L10n.OfficialHelp;
        _exitItem.Text = L10n.Exit;
        UpdateMenuState();
        _tray.Text = _latestSnapshot is null
            ? L10n.Pick("Codex 额度面板 · 正在连接", "Codex quota panel · Connecting")
            : TruncateTooltip(L10n.Pick(
                $"Codex 额度 · {Math.Round(_latestSnapshot.RemainingPercent):0}% 剩余 · {L10n.SourceName(_latestSnapshot.Source)}",
                $"Codex quota · {Math.Round(_latestSnapshot.RemainingPercent):0}% left · {L10n.SourceName(_latestSnapshot.Source)}"));
    }

    private void ClickThroughItemOnCheckedChanged(object? sender, EventArgs e)
    {
        if (_applyingPreferences) return;
        _preferences = _preferences with { OrbClickThrough = _clickThroughItem.Checked };
        _form.SetOrbClickThroughPreference(_preferences.OrbClickThrough);
        PanelPreferenceManager.Save(_preferences);
        if (!_preferences.OrbClickThrough) return;

        _tray.BalloonTipTitle = L10n.Pick("悬浮球已开启鼠标穿透", "Orb click-through enabled");
        _tray.BalloonTipText = L10n.Pick(
            "悬浮球将不再响应点击和拖动；可从托盘展开详情或关闭穿透。",
            "The orb no longer responds to clicks or dragging. Use the tray to open details or disable click-through.");
        _tray.BalloonTipIcon = ToolTipIcon.Info;
        _tray.ShowBalloonTip(5000);
    }

    private void ShowInformation(string text, ToolTipIcon icon)
    {
        _tray.BalloonTipTitle = L10n.AppTitle;
        _tray.BalloonTipText = text;
        _tray.BalloonTipIcon = icon;
        _tray.ShowBalloonTip(4500);
    }

    private async Task RefreshAsync()
    {
        SafeUi(() => _form.SetStatus("正在刷新额度"));
        await _coordinator.RefreshAsync().ConfigureAwait(false);
    }

    private void OnSnapshotChanged(QuotaSnapshot snapshot)
    {
        SafeUi(() =>
        {
            _latestSnapshot = snapshot;
            _history.Record(snapshot);
            _form.SetHistory(_history.GetRecent());
            _form.ApplySnapshot(snapshot);
            var remaining = snapshot.RemainingPercent;
            UpdateTrayIcon(remaining, live: true);
            _tray.Text = TruncateTooltip(L10n.Pick(
                $"Codex 额度 · {Math.Round(remaining):0}% 剩余 · {L10n.SourceName(snapshot.Source)}",
                $"Codex quota · {Math.Round(remaining):0}% left · {L10n.SourceName(snapshot.Source)}"));
            MaybeNotify(snapshot);
        });
    }

    private void UpdateTrayIcon(double? remainingPercent, bool live)
    {
        int? roundedRemaining = remainingPercent is null
            ? null
            : (int)Math.Round(Math.Clamp(remainingPercent.Value, 0d, 100d));
        if (_trayIconRemainingPercent == roundedRemaining && _trayIconLive == live)
            return;

        var nextIcon = IconFactory.CreateTrayIcon(roundedRemaining, live);
        var previousIcon = _tray.Icon;
        _tray.Icon = nextIcon;
        _trayIconRemainingPercent = roundedRemaining;
        _trayIconLive = live;
        previousIcon?.Dispose();
    }

    private void MaybeNotify(QuotaSnapshot snapshot)
    {
        var decision = QuotaAlertEvaluator.Evaluate(snapshot, _preferences, DateTimeOffset.Now, _alertState);
        AlertStateStore.Save(_alertState);
        if (decision is null) return;

        var window = LimitRowControl.FormatWindow(decision.WindowMinutes);
        _tray.BalloonTipTitle = decision.Level == QuotaAlertLevel.Critical
            ? L10n.Pick("Codex 额度严重不足", "Codex quota critically low")
            : L10n.Pick("Codex 额度提醒", "Codex quota alert");
        _tray.BalloonTipText = L10n.Pick(
            $"{window}还剩 {Math.Round(decision.RemainingPercent):0}%。点击托盘图标查看重置时间。",
            $"{window} has {Math.Round(decision.RemainingPercent):0}% left. Click the tray icon for reset details.");
        _tray.BalloonTipIcon = decision.Level == QuotaAlertLevel.Critical ? ToolTipIcon.Warning : ToolTipIcon.Info;
        if (_preferences.AlertSoundEnabled)
        {
            try { SystemSounds.Exclamation.Play(); } catch { }
        }
        _tray.ShowBalloonTip(6000);
    }

    private void OnGlobalHotKeyPressed()
    {
        if (_preferences.OrbClickThrough)
        {
            _preferences = _preferences with { OrbClickThrough = false };
            _form.SetOrbClickThroughPreference(false);
            PanelPreferenceManager.Save(_preferences);
            UpdateRuntimeMenu();
            _form.ShowOrb();
            ShowInformation(L10n.Pick("已关闭鼠标穿透并恢复悬浮球操作", "Mouse click-through disabled; orb controls restored"), ToolTipIcon.Info);
            return;
        }
        ToggleOrbVisibility();
    }

    private void UpdateGlobalHotKeyRegistration(bool showFailure)
    {
        if (_hotKeyRegistered && _form.IsHandleCreated)
        {
            UnregisterHotKey(_form.Handle, GlobalHotKeyId);
            _hotKeyRegistered = false;
        }
        if (!_preferences.GlobalHotKeyEnabled || !_form.IsHandleCreated) return;

        _hotKeyRegistered = RegisterHotKey(
            _form.Handle,
            GlobalHotKeyId,
            ModControl | ModAlt | ModNoRepeat,
            (uint)Keys.Q);
        if (!_hotKeyRegistered && showFailure)
            ShowInformation(L10n.Pick("Ctrl+Alt+Q 已被其他程序占用，全局快捷键未启用", "Ctrl+Alt+Q is already in use; the global shortcut was not enabled"), ToolTipIcon.Warning);
    }

    private void AttachSystemEvents()
    {
        if (_systemEventsAttached) return;
        try
        {
            SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
            SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            _systemEventsAttached = true;
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void DetachSystemEvents()
    {
        if (!_systemEventsAttached) return;
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        _systemEventsAttached = false;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => QueueEnsureVisible();

    private void OnUserPreferenceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.Desktop or UserPreferenceCategory.General or UserPreferenceCategory.Window)
        {
            QueueEnsureVisible();
            if (_preferences.ThemeMode == 0)
            {
                SafeUi(() =>
                {
                    var previousColors = UiPalette.CurrentColors;
                    UiPalette.SetTheme(0);
                    if (previousColors == UiPalette.CurrentColors) return;
                    _form.ApplyTheme(previousColors);
                    _menu.BackColor = UiPalette.Surface;
                    _menu.ForeColor = UiPalette.Text;
                    _menu.Invalidate();
                });
            }
        }
    }

    private void QueueEnsureVisible() => SafeUi(() =>
    {
        _form.EnsureVisibleOnCurrentDisplays();
        SaveCurrentOrbLocation();
    });

    private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode != PowerModes.Resume) return;
        QueueEnsureVisible();
        _ = RefreshAsync();
    }

    private void WaitForShowSignal()
    {
        var handles = new[] { _showSignal, _lifetime.Token.WaitHandle };
        while (true)
        {
            var signaled = WaitHandle.WaitAny(handles);
            if (signaled == 1 || _lifetime.IsCancellationRequested) return;
            SafeUi(() => _form.ShowDetails());
        }
    }

    private void SafeUi(Action action)
    {
        if (_exiting || _form.IsDisposed) return;
        try
        {
            if (_form.IsHandleCreated) _form.BeginInvoke(action);
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ExitApplication()
    {
        if (_exiting) return;
        _exiting = true;
        _lifetime.Cancel();
        _tray.Visible = false;
        _form.CloseForExit();
        ExitThread();
    }

    protected override void ExitThreadCore()
    {
        if (!_exiting) _exiting = true;
        _lifetime.Cancel();
        _tray.Visible = false;
        DetachSystemEvents();
        if (_hotKeyRegistered && _form.IsHandleCreated)
        {
            UnregisterHotKey(_form.Handle, GlobalHotKeyId);
            _hotKeyRegistered = false;
        }
        try { _showTask?.Wait(TimeSpan.FromSeconds(1)); } catch { }
        try { _coordinatorStartTask?.GetAwaiter().GetResult(); } catch { }
        try { _coordinator.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        var trayIcon = _tray.Icon;
        _tray.Icon = null;
        _tray.Dispose();
        trayIcon?.Dispose();
        _form.Dispose();
        _menu.Dispose();
        _lifetime.Dispose();
        base.ExitThreadCore();
    }

    private static string TruncateTooltip(string text) => text.Length <= 63 ? text : text[..63];

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
}

internal static class StartupManager
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "CodexQuotaPanel";

    public static bool IsEnabled() => IsEnabled(
        Registry.CurrentUser,
        RunKey,
        ValueName,
        Environment.ProcessPath ?? Application.ExecutablePath);

    internal static bool IsEnabled(
        RegistryKey root,
        string runKey,
        string valueName,
        string executable)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentException.ThrowIfNullOrWhiteSpace(runKey);
            ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
            ArgumentException.ThrowIfNullOrWhiteSpace(executable);
            using var key = root.OpenSubKey(runKey, writable: false);
            if (key?.GetValue(valueName) is not string value || string.IsNullOrWhiteSpace(value))
                return false;
            var configuredPath = value.Trim().Trim('"');
            return File.Exists(configuredPath) &&
                   string.Equals(Path.GetFullPath(configuredPath), Path.GetFullPath(executable), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException or ArgumentException)
        {
            return false;
        }
    }

    public static void SetEnabled(bool enabled) => SetEnabled(
        Registry.CurrentUser,
        RunKey,
        ValueName,
        Environment.ProcessPath ?? Application.ExecutablePath,
        enabled);

    internal static void SetEnabled(
        RegistryKey root,
        string runKey,
        string valueName,
        string executable,
        bool enabled)
    {
        ArgumentNullException.ThrowIfNull(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(runKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(valueName);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        using var key = root.CreateSubKey(runKey, writable: true);
        if (enabled)
        {
            key.SetValue(valueName, $"\"{executable}\"", RegistryValueKind.String);
        }
        else
        {
            key.DeleteValue(valueName, throwOnMissingValue: false);
        }
    }
}

internal static class IconFactory
{
    public static Icon CreateTrayIcon(double? remainingPercent = null, bool live = false)
    {
        using var bitmap = new Bitmap(32, 32);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.Clear(Color.Transparent);
        using var background = new SolidBrush(UiPalette.Canvas);
        graphics.FillEllipse(background, 2, 2, 28, 28);

        const float startAngle = -210f;
        const float fullSweep = 300f;
        using var track = new Pen(Color.FromArgb(70, UiPalette.Track), 3.2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawArc(track, 6, 6, 20, 20, startAngle, fullSweep);
        if (remainingPercent is not null && remainingPercent.Value > 0d)
        {
            var remaining = Math.Clamp(remainingPercent.Value, 0d, 100d);
            using var value = new Pen(UiPalette.ForRemaining(remaining), 3.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawArc(value, 6, 6, 20, 20, startAngle, fullSweep * (float)(remaining / 100d));
        }

        var statusProgress = Math.Clamp(remainingPercent ?? 0d, 0d, 100d) / 100d;
        var statusAngle = (startAngle + fullSweep * statusProgress) * Math.PI / 180d;
        var statusCenter = new PointF(
            16f + 10f * (float)Math.Cos(statusAngle),
            16f + 10f * (float)Math.Sin(statusAngle));
        using var dotBorder = new SolidBrush(UiPalette.Canvas);
        graphics.FillEllipse(dotBorder, statusCenter.X - 3.5f, statusCenter.Y - 3.5f, 7, 7);
        using var dot = new SolidBrush(live ? UiPalette.Mint : UiPalette.Amber);
        graphics.FillEllipse(dot, statusCenter.X - 2f, statusCenter.Y - 2f, 4, 4);
        var handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            DestroyIcon(handle);
        }
    }

    [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Auto)]
    private static extern bool DestroyIcon(IntPtr handle);
}
