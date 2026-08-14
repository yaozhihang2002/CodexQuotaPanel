namespace CodexQuotaPanel;

internal sealed partial class QuotaApplicationContext
{
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
            _form.SetOrbBackgroundColor(preferences.OrbBackgroundColorArgb);
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
            if (previous.OrbBackgroundColorArgb != next.OrbBackgroundColorArgb)
                _form.SetOrbBackgroundColor(next.OrbBackgroundColorArgb);
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
        var lastPreview = _preferences;
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
        settings.CheckForUpdatesRequested += cancellationToken =>
            _updateService.CheckAsync(force: true, cancellationToken);
        settings.ResetRequested += () => resetRequested = true;
        settings.SaveRequested += () =>
        {
            var selected = PanelPreferenceManager.Normalize(settings.SelectedPreferences);
            var clickThroughNewlyEnabled = !_preferences.OrbClickThrough && selected.OrbClickThrough;
            if (!resetRequested)
            {
                var deviceState = _preferences;
                selected = selected with
                {
                    OrbX = deviceState.OrbX,
                    OrbY = deviceState.OrbY,
                    LastViewMode = deviceState.LastViewMode
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

            if (resetRequested)
            {
                _form.ShowOrb(animate: false);
                _form.MoveOrbToCurrentDisplay();
                var location = _form.GetRestorableOrbLocation();
                selected = selected with
                {
                    OrbX = location.X,
                    OrbY = location.Y,
                    LastViewMode = QuotaForm.OrbViewState
                };
            }

            if (clickThroughNewlyEnabled && selected.ShowClickThroughReminder &&
                ShowClickThroughReminder(settings, selected))
            {
                selected = selected with { ShowClickThroughReminder = false };
                settings.SetClickThroughReminderEnabled(false);
            }
            if (!PanelPreferenceManager.TrySave(selected))
            {
                MessageBox.Show(
                    settings,
                    L10n.Pick(
                        "设置无法写入本机配置文件。当前预览仍然有效，但尚未保存；请检查磁盘空间或文件权限后重试。",
                        "Settings could not be written to the local configuration file. The preview is still active but not saved; check disk space or file permissions and try again."),
                    L10n.SettingsTitle,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var languageChanged = selected.Language != _preferences.Language;
            _preferences = selected;
            ApplyPreferencePreview(lastPreview, _preferences);
            lastPreview = _preferences;
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
            // FormClosing emits a reversible preview, but an in-flight orb-size
            // animation can otherwise finish on its old target after that event.
            // Reapply the authoritative persisted state and use SetOrbSize,
            // which stops any preview timer. This also covers Esc, Cancel and X.
            ApplyPreferencePreview(lastPreview, _preferences);
            _form.SetOrbSize(_preferences.OrbSize);
            lastPreview = _preferences;
            _settingsItem.Enabled = true;
        }
        UpdateRuntimeMenu();
    }

    private void SaveCurrentOrbLocation()
    {
        if (_form.IsDisposed) return;
        var location = _form.GetRestorableOrbLocation();
        _preferences = _preferences with { OrbX = location.X, OrbY = location.Y };
        PersistRuntimePreferences();
    }

    private void PersistRuntimePreferences()
    {
        PanelPreferenceManager.TrySave(_preferences);
    }
}
