namespace CodexQuotaPanel;

internal sealed partial class SettingsForm
{
    private void WireControlEvents()
    {
        _startupToggle.CheckedChanged += (_, _) => UpdateDirtyState();
        _startupViewCombo.SelectedIndexChanged += (_, _) => UpdateFromDirectControls();
        _languageCombo.SelectedIndexChanged += (_, _) => UpdateFromDirectControls();
        _themeCombo.SelectedIndexChanged += (_, _) => UpdateFromDirectControls();
        _topMostToggle.CheckedChanged += (_, _) => UpdateFromDirectControls();
        _consumptionFlameToggle.CheckedChanged += (_, _) => UpdateFromDirectControls();
        _flameStyleCombo.SelectedIndexChanged += (_, _) => UpdateFromDirectControls();
        _orbSizeSlider.ValueChanged += (_, _) => OrbSizeSliderChanged();
        _orbSizeInput.ValueChanged += (_, _) => OrbSizeInputChanged();
        _fontScaleSlider.ValueChanged += (_, _) => FontScaleSliderChanged();
        _fontScaleInput.ValueChanged += (_, _) => FontScaleInputChanged();
        _orbBackgroundColorButton.Click += (_, _) => ChooseOrbBackgroundColor();
        _orbBackgroundDefaultButton.Click += (_, _) => RestoreDefaultOrbBackground();
        _positionLockedToggle.CheckedChanged += (_, _) => UpdateFromDirectControls();
        _snapToEdgeToggle.CheckedChanged += (_, _) => UpdateFromDirectControls();
        _clickThroughToggle.CheckedChanged += (_, _) => UpdateFromDirectControls();
        _clickThroughReminderToggle.CheckedChanged += (_, _) => UpdateFromDirectControls();
        _hoverPreviewToggle.CheckedChanged += (_, _) => UpdateFromDirectControls();
        _globalHotKeyToggle.CheckedChanged += (_, _) => UpdateFromDirectControls();
        _alertSoundToggle.CheckedChanged += (_, _) => UpdateFromDirectControls();
        _trendRecordingToggle.CheckedChanged += (_, _) => UpdateFromDirectControls();
        _checkUpdatesToggle.CheckedChanged += (_, _) => UpdateFromDirectControls();
    }

    private void OrbSizeSliderChanged()
    {
        if (_initializing || _syncingOrbSize) return;
        _syncingOrbSize = true;
        try { _orbSizeInput.Value = _orbSizeSlider.Value; }
        finally { _syncingOrbSize = false; }
        UpdateFromDirectControls();
    }

    private void OrbSizeInputChanged()
    {
        if (_initializing || _syncingOrbSize) return;
        _syncingOrbSize = true;
        try { _orbSizeSlider.Value = (int)_orbSizeInput.Value; }
        finally { _syncingOrbSize = false; }
        UpdateFromDirectControls();
    }

    private void FontScaleSliderChanged()
    {
        if (_initializing || _syncingFontScale) return;
        _syncingFontScale = true;
        try { _fontScaleInput.Value = _fontScaleSlider.Value; }
        finally { _syncingFontScale = false; }
        UpdateFromDirectControls();
    }

    private void FontScaleInputChanged()
    {
        if (_initializing || _syncingFontScale) return;
        _syncingFontScale = true;
        try { _fontScaleSlider.Value = (int)_fontScaleInput.Value; }
        finally { _syncingFontScale = false; }
        UpdateFromDirectControls();
    }

    private void UpdateFromDirectControls()
    {
        if (_initializing) return;
        var previousPreferences = _workingPreferences;
        var selectedLanguage = Math.Max(0, _languageCombo.SelectedIndex);
        var languageChanged = selectedLanguage != _workingPreferences.Language;
        var selectedTheme = Math.Max(0, _themeCombo.SelectedIndex);
        var themeChanged = selectedTheme != _workingPreferences.ThemeMode;
        _workingPreferences = PanelPreferenceManager.Normalize(_workingPreferences with
        {
            AlwaysOnTop = _topMostToggle.Checked,
            ConsumptionFlameEnabled = _consumptionFlameToggle.Checked,
            ConsumptionFlameStyle = Math.Max(0, _flameStyleCombo.SelectedIndex),
            StartupViewMode = Math.Max(0, _startupViewCombo.SelectedIndex),
            OrbSize = SelectedOrbSize,
            SettingsFontScalePercent = SelectedFontScalePercent,
            PositionLocked = _positionLockedToggle.Checked,
            SnapToEdge = _snapToEdgeToggle.Checked,
            OrbClickThrough = _clickThroughToggle.Checked,
            ShowClickThroughReminder = _clickThroughReminderToggle.Checked,
            HoverPreviewEnabled = _hoverPreviewToggle.Checked,
            GlobalHotKeyEnabled = _globalHotKeyToggle.Checked,
            AlertSoundEnabled = _alertSoundToggle.Checked,
            TrendRecordingEnabled = _trendRecordingToggle.Checked,
            CheckForUpdatesOnStartup = _checkUpdatesToggle.Checked,
            ThemeMode = selectedTheme,
            Language = selectedLanguage
        });
        if (languageChanged) L10n.SetLanguage((AppLanguage)_workingPreferences.Language);
        if (themeChanged)
        {
            var previousColors = UiPalette.ResolveColors(previousPreferences.ThemeMode);
            UiPalette.SetTheme(_workingPreferences.ThemeMode);
            ApplyThemeToOpenForm(previousColors);
        }
        TopMost = _workingPreferences.AlwaysOnTop;
        _flameStyleCombo.Enabled = _workingPreferences.ConsumptionFlameEnabled;
        if (!languageChanged &&
            previousPreferences.SettingsFontScalePercent != _workingPreferences.SettingsFontScalePercent)
            QueueFontScalePreview();
        RaisePreview();
        if (languageChanged) ApplyLanguageToOpenForm();
    }

    private void ApplyThemeToOpenForm(UiPalette.Colors previousColors)
    {
        using (NativeRedrawScope.Suspend(this))
        {
            UiPalette.ApplyTheme(this, previousColors);
            NativeTheme.Apply(this);
            UpdateOrbBackgroundControls();
            UpdateOrbPreview();
            PerformLayout();
        }
    }

    private void QueueFontScalePreview()
    {
        _fontScalePreviewTimer.Stop();
        _fontScalePreviewTimer.Start();
    }

    private void ApplyPendingFontScalePreview()
    {
        _fontScalePreviewTimer.Stop();
        var target = _workingPreferences.SettingsFontScalePercent;
        if (_appliedLayoutScalePercent == target) return;
        using (NativeRedrawScope.Suspend(this))
        {
            ApplySettingsLayoutScale(target);
            UiPalette.ApplyScaledTypography(this, target);
            ApplyCompactTypographyMetrics(this, target);
            ResizeSettingsPagesToViewport();
            PerformLayout();
        }
    }

    private void ApplyLanguageToOpenForm()
    {
        if (_relocalizing || IsDisposed) return;
        _relocalizing = true;
        var wasInitializing = _initializing;
        _initializing = true;
        try
        {
            // Pick() records every bilingual pair as controls are built. Update
            // the live tree in place instead of constructing a second hidden
            // SettingsForm with five complete pages, which used to make the
            // first language switch pause noticeably.
            using (NativeRedrawScope.Suspend(this))
            {
                Text = L10n.Translate(Text);
                AccessibleName = L10n.Translate(AccessibleName ?? string.Empty);
                RelocalizeControlTree(this);
                UiPalette.ApplyScaledTypography(this, _workingPreferences.SettingsFontScalePercent);
                ApplyCompactTypographyMetrics(this, _workingPreferences.SettingsFontScalePercent);
                // Native dark/light theming is independent of language. Walking
                // every prewarmed child HWND here only adds a visible pause.
                UpdateSummaries();
                UpdateDirtyState();
                UpdateOrbPreview();
                PerformLayout();
            }
        }
        finally
        {
            _initializing = wasInitializing;
            _relocalizing = false;
        }
        Invalidate(invalidateChildren: true);
    }

    private static void RelocalizeControlTree(Control root)
    {
        foreach (Control control in root.Controls)
        {
            if (control is ComboBox combo)
            {
                var selectedIndex = combo.SelectedIndex;
                combo.BeginUpdate();
                try
                {
                    for (var index = 0; index < combo.Items.Count; index++)
                    {
                        if (combo.Items[index] is string item)
                            combo.Items[index] = L10n.Translate(item);
                    }
                    combo.SelectedIndex = Math.Clamp(
                        selectedIndex,
                        combo.Items.Count == 0 ? -1 : 0,
                        combo.Items.Count - 1);
                }
                finally { combo.EndUpdate(); }
            }
            else if (control is BaselineSafeLinkLabel link)
            {
                link.PrefixText = $"{L10n.GitHubProject}  \u00B7  ";
                link.Text = link.PrefixText + link.ProjectText;
            }
            else if (control is Label or ButtonBase or GroupBox)
                control.Text = L10n.Translate(control.Text);

            if (!string.IsNullOrWhiteSpace(control.AccessibleName))
                control.AccessibleName = L10n.Translate(control.AccessibleName);
            if (!string.IsNullOrWhiteSpace(control.AccessibleDescription))
                control.AccessibleDescription = L10n.Translate(control.AccessibleDescription);
            RelocalizeControlTree(control);
        }
    }

    private void ApplyPreferencesToControls(PanelPreferences preferences, bool startupEnabled)
    {
        _initializing = true;
        preferences = PanelPreferenceManager.Normalize(preferences);
        _startupToggle.Checked = startupEnabled;
        _startupViewCombo.SelectedIndex = preferences.StartupViewMode;
        _languageCombo.SelectedIndex = preferences.Language;
        _themeCombo.SelectedIndex = preferences.ThemeMode;
        _topMostToggle.Checked = preferences.AlwaysOnTop;
        _consumptionFlameToggle.Checked = preferences.ConsumptionFlameEnabled;
        _flameStyleCombo.SelectedIndex = preferences.ConsumptionFlameStyle;
        _flameStyleCombo.Enabled = preferences.ConsumptionFlameEnabled;
        var orbSize = PanelPreferenceManager.NormalizeOrbSize(preferences.OrbSize);
        _syncingOrbSize = true;
        try
        {
            _orbSizeSlider.Value = orbSize;
            _orbSizeInput.Value = orbSize;
        }
        finally { _syncingOrbSize = false; }
        var fontScale = PanelPreferenceManager.NormalizeSettingsFontScale(preferences.SettingsFontScalePercent);
        _syncingFontScale = true;
        try
        {
            _fontScaleSlider.Value = fontScale;
            _fontScaleInput.Value = fontScale;
        }
        finally { _syncingFontScale = false; }
        _positionLockedToggle.Checked = preferences.PositionLocked;
        _snapToEdgeToggle.Checked = preferences.SnapToEdge;
        _clickThroughToggle.Checked = preferences.OrbClickThrough;
        _clickThroughReminderToggle.Checked = preferences.ShowClickThroughReminder;
        _hoverPreviewToggle.Checked = preferences.HoverPreviewEnabled;
        _globalHotKeyToggle.Checked = preferences.GlobalHotKeyEnabled;
        _alertSoundToggle.Checked = preferences.AlertSoundEnabled;
        _trendRecordingToggle.Checked = preferences.TrendRecordingEnabled;
        _checkUpdatesToggle.Checked = preferences.CheckForUpdatesOnStartup;
        TopMost = preferences.AlwaysOnTop;
        UpdateSummaries();
        _initializing = false;
        ApplySettingsLayoutScale(fontScale);
        UiPalette.ApplyScaledTypography(this, preferences.SettingsFontScalePercent);
        if (IsHandleCreated)
            ApplyCompactTypographyMetrics(this, preferences.SettingsFontScalePercent);
        UpdateOrbPreview();
        UpdateDirtyState();
    }

    private static void ApplyCompactTypographyMetrics(Control root, int scalePercent)
    {
        foreach (Control child in root.Controls)
        {
            if (child is SettingsCard card)
                card.ApplyTypographyDensity(scalePercent);
            ApplyCompactTypographyMetrics(child, scalePercent);
        }
    }

    private void EditOpacity()
    {
        var before = _workingPreferences;
        using var editor = new OpacityEditorForm(before.OrbOpacityPercent);
        UiPalette.ApplyScaledTypography(editor, _workingPreferences.SettingsFontScalePercent);
        editor.PreviewChanged += opacity =>
        {
            _workingPreferences = _workingPreferences with { OrbOpacityPercent = opacity };
            UpdateSummaries();
            RaisePreview();
        };
        if (editor.ShowDialog(this) == DialogResult.OK)
        {
            _workingPreferences = _workingPreferences with { OrbOpacityPercent = editor.SelectedOpacity };
            RaisePreview();
        }
        else
        {
            _workingPreferences = before;
            RaisePreview();
        }
        UpdateSummaries();
    }

    private void ChooseOrbBackgroundColor()
    {
        var initial = _workingPreferences.OrbBackgroundColorArgb is { } argb
            ? Color.FromArgb(argb)
            : UiPalette.DefaultOrbBackground;
        using var picker = new ColorDialog
        {
            Color = initial,
            FullOpen = true,
            AnyColor = true,
            SolidColorOnly = true
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        _workingPreferences = PanelPreferenceManager.Normalize(_workingPreferences with
        {
            OrbBackgroundColorArgb = Color.FromArgb(255, picker.Color).ToArgb()
        });
        UpdateOrbBackgroundControls();
        RaisePreview();
    }

    private void RestoreDefaultOrbBackground()
    {
        if (_workingPreferences.OrbBackgroundColorArgb is null) return;
        _workingPreferences = _workingPreferences with { OrbBackgroundColorArgb = null };
        UpdateOrbBackgroundControls();
        RaisePreview();
    }

    private void EditRings()
    {
        var before = _workingPreferences;
        using var editor = new RingSettingsForm(_snapshot, RingDisplayConfiguration.FromPreferences(before));
        UiPalette.ApplyScaledTypography(editor, _workingPreferences.SettingsFontScalePercent);
        editor.PreviewChanged += configuration =>
        {
            ApplyRingConfiguration(configuration);
            RaisePreview();
        };
        if (editor.ShowDialog(this) == DialogResult.OK)
        {
            ApplyRingConfiguration(editor.SelectedConfiguration);
            RaisePreview();
        }
        else
        {
            _workingPreferences = before;
            RaisePreview();
        }
        UpdateSummaries();
    }

    private void ApplyRingConfiguration(RingDisplayConfiguration configuration)
    {
        _workingPreferences = _workingPreferences with
        {
            OuterWindowMinutes = configuration.Outer.WindowMinutes,
            InnerWindowMinutes = configuration.Inner.WindowMinutes,
            OuterWindowRole = (int)configuration.Outer.Role,
            InnerWindowRole = (int)configuration.Inner.Role,
            OuterColorArgb = configuration.OuterColor.ToArgb(),
            InnerColorArgb = configuration.InnerColor.ToArgb()
        };
        UpdateSummaries();
    }

    private void EditAlerts()
    {
        using var editor = new AlertSettingsForm(_workingPreferences);
        UiPalette.ApplyScaledTypography(editor, _workingPreferences.SettingsFontScalePercent);
        if (editor.ShowDialog(this) != DialogResult.OK) return;
        var selected = editor.SelectedValues;
        _workingPreferences = PanelPreferenceManager.Normalize(_workingPreferences with
        {
            AlertsEnabled = selected.Enabled,
            WarningThreshold = selected.WarningThreshold,
            CriticalThreshold = selected.CriticalThreshold,
            QuietHoursEnabled = selected.QuietHoursEnabled,
            QuietStartMinutes = selected.QuietStartMinutes,
            QuietEndMinutes = selected.QuietEndMinutes
        });
        UpdateSummaries();
        RaisePreview();
    }

    private void UpdateSummaries()
    {
        _opacitySummary.Text = $"{_workingPreferences.OrbOpacityPercent}%";
        UpdateOrbBackgroundControls();
        _ringSummary.Text = $"{RingWindowCatalog.FormatShort(_workingPreferences.OuterWindowMinutes)}  /  " +
                            RingWindowCatalog.FormatShort(_workingPreferences.InnerWindowMinutes);
        _alertSummary.Text = _workingPreferences.AlertsEnabled
            ? L10n.AlertsSummary(_workingPreferences.WarningThreshold, _workingPreferences.CriticalThreshold)
            : L10n.AlertsOff;
    }

    private void UpdateOrbBackgroundControls()
    {
        var custom = _workingPreferences.OrbBackgroundColorArgb;
        _orbBackgroundSummary.Text = custom is null ? L10n.DefaultBlack : L10n.CustomColor;
        _orbBackgroundColorButton.SelectedColor = custom is { } argb
            ? Color.FromArgb(argb)
            : UiPalette.DefaultOrbBackground;
        _orbBackgroundDefaultButton.Enabled = custom is not null;
    }

    private void RequestClearHistory()
    {
        if (MessageBox.Show(this, L10n.ClearHistoryConfirm, L10n.ClearHistory,
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        ClearHistoryRequested?.Invoke();
    }

    private void CopyDiagnostics()
    {
        try
        {
            Clipboard.SetText(_diagnostics);
            MessageBox.Show(this,
                L10n.Pick("脱敏诊断信息已复制。", "Sanitized diagnostics copied."),
                L10n.SettingsTitle, MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.ExternalException or ThreadStateException)
        {
            MessageBox.Show(this,
                L10n.Pick("当前无法访问剪贴板，请稍后重试。", "The clipboard is unavailable. Please try again."),
                L10n.SettingsTitle, MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void StageReset()
    {
        if (MessageBox.Show(this, L10n.RestoreDefaultsConfirm, L10n.RestoreDefaults,
                MessageBoxButtons.YesNo, MessageBoxIcon.Question, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
            return;
        _resetPending = true;
        _workingPreferences = PanelPreferenceManager.Default;
        ApplyPreferencesToControls(_workingPreferences, startupEnabled: false);
        RaisePreview();
    }

    private void SaveAndStayOpen()
    {
        UpdateFromDirectControls();
        ApplyPendingFontScalePreview();
        if (_resetPending) ResetRequested?.Invoke();
        if (SaveRequested is null)
        {
            MarkSaved(StartupEnabled);
            return;
        }

        SaveRequested.Invoke();
    }

    internal void MarkSaved(bool startupEnabled)
    {
        _savedPreferences = SelectedPreferences;
        _savedStartupEnabled = startupEnabled;
        _resetPending = false;
        UpdateDirtyState();
    }

    private void CancelAndClose()
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void OnSettingsFormClosing(object? sender, FormClosingEventArgs e)
    {
        _operationLifetime.Cancel();
        _fontScalePreviewTimer.Stop();
        _workingPreferences = _savedPreferences;
        _initializing = true;
        _startupToggle.Checked = _savedStartupEnabled;
        _initializing = false;
        PreviewPreferencesChanged?.Invoke(_savedPreferences);
        if (DialogResult == DialogResult.None) DialogResult = DialogResult.Cancel;
    }

    private void RaisePreview()
    {
        if (_initializing) return;
        UpdateOrbPreview();
        UpdateDirtyState();
        PreviewPreferencesChanged?.Invoke(SelectedPreferences);
    }

    private void UpdateDirtyState()
    {
        if (_saveStatusLabel is null) return;
        _saveStatusLabel.Text = IsDirty ? L10n.SettingsUnsavedState : L10n.SettingsSavedState;
        _saveStatusLabel.ForeColor = IsDirty ? UiPalette.Amber : UiPalette.Faint;
    }

    private void UpdateOrbPreview()
    {
        if (_orbPreview is null) return;
        var size = PanelPreferenceManager.NormalizeOrbSize(_workingPreferences.OrbSize);
        const int minimumPreviewSize = 64;
        const int maximumPreviewSize = 140;
        var previewProgress = (size - PanelPreferenceManager.MinimumOrbSize) /
                              (double)(PanelPreferenceManager.MaximumOrbSize - PanelPreferenceManager.MinimumOrbSize);
        var logicalPreviewSize = minimumPreviewSize + (int)Math.Round(
            previewProgress * (maximumPreviewSize - minimumPreviewSize));
        var previewDpi = _orbPreview.IsHandleCreated
            ? _orbPreview.DeviceDpi
            : IsHandleCreated ? DeviceDpi : 96;
        var previewSize = ScalePreviewPixels(logicalPreviewSize, previewDpi);
        _orbPreview.Size = new Size(previewSize, previewSize);
        _orbPreview.ConfigureRings(RingDisplayConfiguration.FromPreferences(_workingPreferences));
        _orbPreview.SetBackgroundColor(_workingPreferences.OrbBackgroundColorArgb);
        _orbPreview.SetFlameAnimationEnabled(_workingPreferences.ConsumptionFlameEnabled);
        _orbPreview.SetFlameStyle(_workingPreferences.ConsumptionFlameStyle);
        if (_snapshot is not null) _orbPreview.SetSnapshot(_snapshot, live: true);
        if (_orbPreview.Parent is { } host) CenterOrbPreview(host);
    }
}
