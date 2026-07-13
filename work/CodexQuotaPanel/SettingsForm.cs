using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CodexQuotaPanel;

/// <summary>
/// A single, staged settings surface. Preference previews are reversible: closing
/// with Cancel raises one final preview containing the most recently saved values.
/// Saving applies immediately without closing, destructive history clearing is
/// intentionally immediate, and a defaults reset remains staged until Save.
/// </summary>
internal sealed class SettingsForm : Form
{
    private PanelPreferences _savedPreferences;
    private bool _savedStartupEnabled;
    private readonly QuotaSnapshot? _snapshot;
    private readonly string _diagnostics;
    private readonly List<SettingsNavButton> _navigation = [];
    private readonly List<Control> _pages = [];

    private PanelPreferences _workingPreferences;
    private bool _initializing;
    private bool _resetPending;
    private bool _syncingOrbSize;
    private bool _syncingFontScale;
    private bool _relocalizing;
    private int _selectedPageIndex = -1;

    private ActionButton _saveButton = null!;
    private Label _saveStatusLabel = null!;
    private QuotaOrbControl? _orbPreview;
    private readonly TableLayoutPanel _rootLayout;

    private readonly SettingsToggle _startupToggle;
    private readonly ComboBox _startupViewCombo;
    private readonly ComboBox _languageCombo;
    private readonly ComboBox _themeCombo;
    private readonly SettingsToggle _topMostToggle;
    private readonly TrackBar _orbSizeSlider;
    private readonly NumericUpDown _orbSizeInput;
    private readonly TrackBar _fontScaleSlider;
    private readonly NumericUpDown _fontScaleInput;
    private readonly Label _opacitySummary;
    private readonly Label _ringSummary;
    private readonly SettingsToggle _consumptionFlameToggle;
    private readonly ComboBox _flameStyleCombo;
    private readonly SettingsToggle _positionLockedToggle;
    private readonly SettingsToggle _snapToEdgeToggle;
    private readonly SettingsToggle _clickThroughToggle;
    private readonly SettingsToggle _hoverPreviewToggle;
    private readonly SettingsToggle _globalHotKeyToggle;
    private readonly Label _alertSummary;
    private readonly SettingsToggle _alertSoundToggle;
    private readonly SettingsToggle _trendRecordingToggle;

    public PanelPreferences SelectedPreferences => PanelPreferenceManager.Normalize(_workingPreferences);
    public bool StartupEnabled => _startupToggle.Checked;
    internal bool IsDirty => _resetPending ||
        SelectedPreferences != _savedPreferences || StartupEnabled != _savedStartupEnabled;
    internal int SelectedOrbSize => (int)_orbSizeInput.Value;
    internal int SelectedFontScalePercent => (int)_fontScaleInput.Value;
    internal bool SaveButtonVisible => _saveButton.Parent is { } parent &&
        !_saveButton.IsDisposed && _saveButton.Width > 0 && _saveButton.Height > 0 &&
        parent.ClientRectangle.IntersectsWith(_saveButton.Bounds);

    public event Action<PanelPreferences>? PreviewPreferencesChanged;
    public event Action? MoveToCurrentDisplayRequested;
    public event Action? ClearHistoryRequested;
    public event Action? ResetRequested;
    public event Action? SaveRequested;

    public SettingsForm(
        PanelPreferences preferences,
        bool startupEnabled,
        QuotaSnapshot? snapshot = null,
        string? diagnostics = null)
    {
        _savedPreferences = PanelPreferenceManager.Normalize(preferences);
        _workingPreferences = _savedPreferences;
        UiPalette.SetTheme(_workingPreferences.ThemeMode);
        _savedStartupEnabled = startupEnabled;
        _snapshot = snapshot;
        _diagnostics = diagnostics ?? L10n.Pick("诊断信息暂不可用", "Diagnostics are currently unavailable");
        _initializing = true;

        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(860, 620);
        MinimumSize = new Size(700, 520);
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterParent;
        ShowInTaskbar = false;
        BackColor = UiPalette.Canvas;
        ForeColor = UiPalette.Text;
        Font = UiPalette.Body(8.5f);
        Text = L10n.SettingsTitle;
        AccessibleName = L10n.SettingsTitle;
        DoubleBuffered = true;

        _rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.Canvas,
            ColumnCount = 2,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 176f));
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 92f));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76f));
        Controls.Add(_rootLayout);

        var header = BuildHeader();
        _rootLayout.Controls.Add(header, 0, 0);
        _rootLayout.SetColumnSpan(header, 2);

        var navHost = BuildNavigation();
        _rootLayout.Controls.Add(navHost, 0, 1);

        var contentHost = new BufferedSettingsHost
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.Canvas,
            Padding = new Padding(16, 6, 16, 8)
        };
        _rootLayout.Controls.Add(contentHost, 1, 1);

        _startupToggle = MakeToggle(L10n.StartWithWindows);
        _startupViewCombo = MakeCombo();
        AddItems(_startupViewCombo, L10n.StartupRestore, L10n.StartupOrb, L10n.StartupDetails, L10n.StartupTray);
        _languageCombo = MakeCombo();
        AddItems(_languageCombo, L10n.Chinese, L10n.English);
        _themeCombo = MakeCombo();
        AddItems(_themeCombo, L10n.ThemeSystem, L10n.ThemeDark, L10n.ThemeLight);

        _topMostToggle = MakeToggle(L10n.AlwaysOnTop);
        _orbSizeSlider = new TrackBar
        {
            Dock = DockStyle.Fill,
            Minimum = PanelPreferenceManager.MinimumOrbSize,
            Maximum = PanelPreferenceManager.MaximumOrbSize,
            TickFrequency = 16,
            SmallChange = 1,
            LargeChange = 8,
            TickStyle = TickStyle.None,
            BackColor = UiPalette.Surface,
            Margin = new Padding(0, 1, 8, 0),
            AccessibleName = L10n.OrbSize
        };
        _orbSizeInput = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = PanelPreferenceManager.MinimumOrbSize,
            Maximum = PanelPreferenceManager.MaximumOrbSize,
            Increment = 1,
            DecimalPlaces = 0,
            TextAlign = HorizontalAlignment.Center,
            BackColor = UiPalette.SurfaceRaised,
            ForeColor = UiPalette.Text,
            Font = UiPalette.Body(8f),
            Margin = Padding.Empty,
            AccessibleName = L10n.OrbSize
        };
        _fontScaleSlider = new TrackBar
        {
            Dock = DockStyle.Fill,
            Minimum = PanelPreferenceManager.MinimumSettingsFontScale,
            Maximum = PanelPreferenceManager.MaximumSettingsFontScale,
            TickFrequency = 5,
            SmallChange = 1,
            LargeChange = 5,
            TickStyle = TickStyle.None,
            BackColor = UiPalette.Surface,
            Margin = new Padding(0, 1, 8, 0),
            AccessibleName = L10n.SettingsFontSize
        };
        _fontScaleInput = new NumericUpDown
        {
            Dock = DockStyle.Fill,
            Minimum = PanelPreferenceManager.MinimumSettingsFontScale,
            Maximum = PanelPreferenceManager.MaximumSettingsFontScale,
            Increment = 1,
            DecimalPlaces = 0,
            TextAlign = HorizontalAlignment.Center,
            BackColor = UiPalette.SurfaceRaised,
            ForeColor = UiPalette.Text,
            Font = UiPalette.Body(8f),
            Margin = Padding.Empty,
            AccessibleName = L10n.SettingsFontSize
        };
        _opacitySummary = MakeSummaryLabel();
        _ringSummary = MakeSummaryLabel();
        _consumptionFlameToggle = MakeToggle(L10n.ConsumptionFlame);
        _flameStyleCombo = MakeCombo();
        AddItems(_flameStyleCombo, L10n.FlameStyleEmber, L10n.FlameStyleFluid, L10n.FlameStylePixel);

        _positionLockedToggle = MakeToggle(L10n.PositionLock);
        _snapToEdgeToggle = MakeToggle(L10n.SnapToEdge);
        _clickThroughToggle = MakeToggle(L10n.ClickThrough);
        _hoverPreviewToggle = MakeToggle(L10n.HoverPreview);
        _globalHotKeyToggle = MakeToggle(L10n.GlobalHotKey);

        _alertSummary = MakeSummaryLabel();
        _alertSoundToggle = MakeToggle(L10n.AlertSound);
        _trendRecordingToggle = MakeToggle(L10n.TrendRecording);

        AddPage(contentHost, BuildGeneralPage());
        AddPage(contentHost, BuildAppearancePage());
        AddPage(contentHost, BuildInteractionPage());
        AddPage(contentHost, BuildNotificationsPage());
        AddPage(contentHost, BuildDataPage());

        var footer = BuildFooter();
        _rootLayout.Controls.Add(footer, 0, 2);
        _rootLayout.SetColumnSpan(footer, 2);

        ApplyPreferencesToControls(_workingPreferences, startupEnabled);
        WireControlEvents();
        SelectPage(0);
        _initializing = false;
        UpdateDirtyState();

        FormClosing += OnSettingsFormClosing;
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        var workArea = Screen.FromControl(this).WorkingArea;
        const int margin = 20;
        var availableWidth = Math.Max(1, workArea.Width - margin * 2);
        var availableHeight = Math.Max(1, workArea.Height - margin * 2);

        MinimumSize = new Size(
            Math.Min(700, availableWidth),
            Math.Min(520, availableHeight));
        Size = new Size(
            Math.Min(Width, availableWidth),
            Math.Min(Height, availableHeight));

        var left = Math.Clamp(Left, workArea.Left + margin,
            Math.Max(workArea.Left + margin, workArea.Right - Width - margin));
        var top = Math.Clamp(Top, workArea.Top + margin,
            Math.Max(workArea.Top + margin, workArea.Bottom - Height - margin));
        Location = new Point(left, top);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Use a dark native caption where supported (Windows 10 1809+ / Windows 11).
        // Failure is harmless on older builds.
        try
        {
            var enabled = 1;
            _ = DwmSetWindowAttribute(Handle, 20, ref enabled, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    private Panel BuildHeader()
    {
        var header = new SettingsHeaderPanel { Dock = DockStyle.Fill };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(24, 16, 22, 14),
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 4f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        layout.Controls.Add(new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.Mint,
            Margin = Padding.Empty
        }, 0, 0);

        var title = new SettingsBrandTitle
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Font = UiPalette.Display(18.5f, FontStyle.Bold),
            Margin = new Padding(14, 0, 10, 0),
            AccessibleName = L10n.SettingsTitle
        };
        layout.Controls.Add(title, 1, 0);

        var badge = MakeDockLabel("CODEX · SETTINGS", UiPalette.Mono(6.5f, FontStyle.Bold), UiPalette.Mint);
        badge.TextAlign = ContentAlignment.MiddleRight;
        layout.Controls.Add(badge, 2, 0);
        header.Controls.Add(layout);
        return header;
    }

    private Panel BuildNavigation()
    {
        var host = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.Canvas,
            Padding = new Padding(12, 10, 12, 10)
        };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.Canvas,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        host.Controls.Add(flow);

        AddNav(flow, L10n.SettingsGeneral, 0);
        AddNav(flow, L10n.SettingsAppearance, 1);
        AddNav(flow, L10n.SettingsInteraction, 2);
        AddNav(flow, L10n.SettingsNotifications, 3);
        AddNav(flow, L10n.SettingsDataAbout, 4);
        return host;
    }

    private void AddNav(FlowLayoutPanel flow, string text, int pageIndex)
    {
        var button = new SettingsNavButton
        {
            Text = text,
            Size = new Size(152, 46),
            Margin = new Padding(0, 0, 0, 5),
            AccessibleName = text
        };
        button.Click += (_, _) => SelectPage(pageIndex);
        _navigation.Add(button);
        flow.Controls.Add(button);
    }

    private static ResponsiveSettingsPage MakePage() => new();

    private Control BuildGeneralPage()
    {
        var page = MakePage();
        page.AddItem(MakePageIntro(L10n.SettingsGeneral, L10n.GeneralIntro));
        page.AddItem(MakeToggleRow(L10n.StartWithWindows, L10n.StartWithWindowsHint, _startupToggle));
        page.AddItem(MakeControlRow(L10n.StartupBehavior,
            L10n.Pick("选择悬浮球、详情、仅托盘或恢复上次状态", "Choose the orb, details, tray only, or restore last state"), _startupViewCombo));
        page.AddItem(MakeControlRow(L10n.InterfaceLanguage, L10n.LanguageRestartHint, _languageCombo));
        page.AddItem(MakeControlRow(L10n.InterfaceTheme, L10n.ThemeHint, _themeCombo));
        return page;
    }

    private Control BuildAppearancePage()
    {
        var page = MakePage();
        page.AddItem(MakePageIntro(L10n.SettingsAppearance, L10n.AppearanceIntro));
        page.AddItem(BuildOrbPreviewCard());
        page.AddItem(MakeToggleRow(L10n.AlwaysOnTop,
            L10n.Pick("让悬浮球和详情面板保持在其他窗口上方", "Keep the orb and details above other windows"), _topMostToggle));
        page.AddItem(MakeControlRow(L10n.OrbSize,
            L10n.OrbSizePreciseHint, BuildOrbSizeEditor(), rightColumnWidth: 258));
        page.AddItem(MakeControlRow(L10n.SettingsFontSize,
            L10n.SettingsFontSizeHint, BuildFontScaleEditor(), rightColumnWidth: 258));
        page.AddItem(MakeEditorRow(L10n.OrbOpacity,
            L10n.Pick("可使用滑块或直接输入精确数值", "Use a slider or enter an exact value"), _opacitySummary, EditOpacity));
        page.AddItem(MakeEditorRow(L10n.DualRingDisplay,
            L10n.Pick("选择额度窗口并分别设置环形颜色", "Choose quota windows and a color for each ring"), _ringSummary, EditRings));
        page.AddItem(MakeControlRow(L10n.FlameStyle,
            L10n.FlameStyleHint, _flameStyleCombo));
        page.AddItem(MakeToggleRow(L10n.ConsumptionFlame,
            L10n.ConsumptionFlameHint, _consumptionFlameToggle));
        return page;
    }

    private Control BuildOrbSizeEditor()
    {
        var layout = new TableLayoutPanel
        {
            Size = new Size(244, 58),
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        layout.Controls.Add(_orbSizeSlider, 0, 0);
        layout.Controls.Add(_orbSizeInput, 1, 0);
        var presets = MakeDockLabel(L10n.OrbSizePresetHint, UiPalette.Body(7f), UiPalette.Faint);
        presets.TextAlign = ContentAlignment.MiddleCenter;
        layout.Controls.Add(presets, 0, 1);
        layout.SetColumnSpan(presets, 2);
        return layout;
    }

    private Control BuildFontScaleEditor()
    {
        var layout = new TableLayoutPanel
        {
            Size = new Size(244, 58),
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 72f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24f));
        layout.Controls.Add(_fontScaleSlider, 0, 0);
        layout.Controls.Add(_fontScaleInput, 1, 0);
        var presets = MakeDockLabel(L10n.SettingsFontSizePresetHint, UiPalette.Body(7f), UiPalette.Faint);
        presets.TextAlign = ContentAlignment.MiddleCenter;
        layout.Controls.Add(presets, 0, 1);
        layout.SetColumnSpan(presets, 2);
        return layout;
    }

    private Control BuildOrbPreviewCard()
    {
        var card = new SettingsCard
        {
            Size = new Size(570, 176),
            Margin = new Padding(0, 0, 0, 10)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(18, 12, 18, 12),
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.Controls.Add(MakeTextBlock(
            L10n.LiveOrbPreview,
            L10n.LiveOrbPreviewHint,
            minimumHeight: 142), 0, 0);

        var previewHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.SurfaceRaised,
            Margin = new Padding(8, 0, 0, 0),
            Padding = new Padding(6)
        };
        _orbPreview = new QuotaOrbControl
        {
            Cursor = Cursors.Default,
            TabStop = false,
            Anchor = AnchorStyles.None
        };
        previewHost.Controls.Add(_orbPreview);
        previewHost.Resize += (_, _) => CenterOrbPreview(previewHost);
        layout.Controls.Add(previewHost, 1, 0);
        card.Controls.Add(layout);
        UpdateOrbPreview();
        return card;
    }

    private void CenterOrbPreview(Control host)
    {
        if (_orbPreview is null) return;
        _orbPreview.Location = new Point(
            Math.Max(0, (host.ClientSize.Width - _orbPreview.Width) / 2),
            Math.Max(0, (host.ClientSize.Height - _orbPreview.Height) / 2));
    }

    private Control BuildInteractionPage()
    {
        var page = MakePage();
        page.AddItem(MakePageIntro(L10n.SettingsInteraction, L10n.InteractionIntro));
        page.AddItem(MakeToggleRow(L10n.PositionLock, L10n.PositionLockHint, _positionLockedToggle));
        page.AddItem(MakeToggleRow(L10n.SnapToEdge, L10n.SnapToEdgeHint, _snapToEdgeToggle));
        page.AddItem(MakeToggleRow(L10n.ClickThrough, L10n.ClickThroughHint, _clickThroughToggle));
        page.AddItem(MakeToggleRow(L10n.HoverPreview, L10n.HoverPreviewHint, _hoverPreviewToggle));
        page.AddItem(MakeToggleRow(L10n.GlobalHotKey, L10n.GlobalHotKeyHint, _globalHotKeyToggle));

        var moveButton = MakeActionButton(L10n.MoveToCurrentDisplay, 150, primary: false);
        moveButton.Click += (_, _) => MoveToCurrentDisplayRequested?.Invoke();
        page.AddItem(MakeControlRow(L10n.MoveToCurrentDisplay, L10n.MoveToCurrentDisplayHint, moveButton));
        return page;
    }

    private Control BuildNotificationsPage()
    {
        var page = MakePage();
        page.AddItem(MakePageIntro(L10n.SettingsNotifications, L10n.NotificationIntro));
        page.AddItem(MakeEditorRow(L10n.QuotaAlerts,
            L10n.Pick("设置警告、严重阈值和免打扰时段", "Set warning and critical thresholds plus quiet hours"), _alertSummary, EditAlerts));
        page.AddItem(MakeToggleRow(L10n.AlertSound, L10n.AlertSoundHint, _alertSoundToggle));
        return page;
    }

    private Control BuildDataPage()
    {
        var page = MakePage();
        page.AddItem(MakePageIntro(L10n.SettingsDataAbout, L10n.DataIntro));
        page.AddItem(BuildReleaseNotesCard());
        page.AddItem(MakeToggleRow(L10n.TrendRecording, L10n.TrendRecordingHint, _trendRecordingToggle));

        var clearButton = MakeActionButton(L10n.ClearHistory, 138, primary: false);
        clearButton.Click += (_, _) => RequestClearHistory();
        page.AddItem(MakeControlRow(L10n.ClearHistory,
            L10n.Pick("删除本机保存的趋势点，不影响额度数据源", "Deletes saved trend points without affecting the quota source"), clearButton));

        var diagnosticsButton = MakeActionButton(L10n.Pick("复制诊断", "Copy diagnostics"), 138, primary: false);
        diagnosticsButton.Click += (_, _) => CopyDiagnostics();
        page.AddItem(MakeControlRow(
            L10n.Pick("脱敏诊断信息", "Sanitized diagnostics"),
            L10n.Pick("仅包含版本、系统、数据源和趋势状态", "Includes only version, system, source, and trend status"),
            diagnosticsButton));

        var resetButton = MakeActionButton(L10n.RestoreDefaults, 138, primary: false);
        resetButton.Click += (_, _) => StageReset();
        page.AddItem(MakeControlRow(L10n.RestoreDefaults,
            L10n.Pick("重置界面、交互、提醒和本地数据选项", "Reset appearance, interaction, alerts, and local-data options"), resetButton));

        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "—";
        var about = new SettingsCard { Size = new Size(570, 126), Margin = new Padding(0, 0, 0, 10) };
        var aboutLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(18, 12, 18, 12),
            Margin = Padding.Empty
        };
        aboutLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        aboutLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142f));
        aboutLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 26f));
        aboutLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        aboutLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        aboutLayout.Controls.Add(MakeDockLabel(L10n.AboutThisApp,
            UiPalette.Body(9f, FontStyle.Bold), UiPalette.Text), 0, 0);
        var sourceText = _snapshot is null
            ? L10n.Pick("数据源 · 等待连接", "Source · Waiting for connection")
            : L10n.Pick(
                $"数据源 · {L10n.SourceName(_snapshot.Source)} · 更新于 {_snapshot.ObservedAt.ToLocalTime():HH:mm:ss}",
                $"Source · {L10n.SourceName(_snapshot.Source)} · Updated {_snapshot.ObservedAt.ToLocalTime():HH:mm:ss}");
        var privacyLabel = MakeDockLabel(L10n.LocalPrivacyNote, UiPalette.Body(7.6f), UiPalette.Muted);
        aboutLayout.Controls.Add(privacyLabel, 0, 1);
        aboutLayout.SetColumnSpan(privacyLabel, 2);
        var sourceLabel = MakeDockLabel(sourceText, UiPalette.Mono(6.7f, FontStyle.Bold), UiPalette.Faint);
        aboutLayout.Controls.Add(sourceLabel, 0, 2);
        aboutLayout.SetColumnSpan(sourceLabel, 2);
        var versionLabel = MakeDockLabel($"{L10n.VersionLabel} {version}",
            UiPalette.Mono(7f, FontStyle.Bold), UiPalette.Mint);
        versionLabel.TextAlign = ContentAlignment.MiddleRight;
        aboutLayout.Controls.Add(versionLabel, 1, 0);
        about.Controls.Add(aboutLayout);
        page.AddItem(about);
        return page;
    }

    private Control BuildReleaseNotesCard()
    {
        const string githubUrl = "https://github.com/yaozhihang2002/CodexQuotaPanel";

        var card = new SettingsCard { Size = new Size(570, 154), Margin = new Padding(0, 0, 0, 10) };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(18, 12, 18, 12),
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42f));

        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 118f));
        header.Controls.Add(MakeDockLabel($"{L10n.ReleaseNotesTitle} · v0.2.0",
            UiPalette.Body(9f, FontStyle.Bold), UiPalette.Text), 0, 0);
        var badge = new PillLabel
        {
            Text = L10n.PreReleaseLabel,
            PillColor = UiPalette.Mint,
            Dock = DockStyle.Fill,
            Margin = new Padding(8, 1, 0, 3)
        };
        header.Controls.Add(badge, 1, 0);
        layout.Controls.Add(header, 0, 0);

        var summary = new ResponsiveTextLabel
        {
            Text = L10n.ReleaseNotesSummary,
            Dock = DockStyle.Fill,
            AutoSize = true,
            Font = UiPalette.Body(7.6f),
            ForeColor = UiPalette.Muted,
            BackColor = Color.Transparent,
            Margin = new Padding(0, 5, 0, 5),
            TextAlign = ContentAlignment.TopLeft,
            UseCompatibleTextRendering = false
        };
        layout.Controls.Add(summary, 0, 1);

        var github = BuildInfoLink(L10n.GitHubProject, "yaozhihang2002/CodexQuotaPanel", githubUrl);
        layout.Controls.Add(github, 0, 2);

        card.Controls.Add(layout);
        return card;
    }

    private Control BuildInfoLink(string caption, string text, string target)
    {
        var block = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.SurfaceRaised,
            Padding = new Padding(10, 5, 10, 5)
        };
        var link = new LinkLabel
        {
            Text = $"{caption}  ·  {text}",
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoEllipsis = true,
            Font = UiPalette.Body(7.8f, FontStyle.Bold),
            ForeColor = UiPalette.Mint,
            LinkColor = UiPalette.Mint,
            ActiveLinkColor = UiPalette.Sky,
            VisitedLinkColor = UiPalette.Mint,
            BackColor = Color.Transparent,
            TextAlign = ContentAlignment.MiddleLeft,
            LinkBehavior = LinkBehavior.HoverUnderline,
            Cursor = Cursors.Hand,
            TabStop = true,
            Margin = Padding.Empty,
            UseMnemonic = false
        };
        link.LinkArea = new LinkArea(0, link.Text.Length);
        link.LinkClicked += (_, _) => OpenExternalLink(target);
        block.Controls.Add(link);
        return block;
    }

    private void OpenExternalLink(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            MessageBox.Show(this, L10n.OpenLinkFailed, L10n.SettingsTitle,
                MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    internal void SelectPageForTest(int index) => SelectPage(index);
    internal void CenterOnDisplay(Point screenPoint)
    {
        var area = Screen.FromPoint(screenPoint).WorkingArea;
        StartPosition = FormStartPosition.Manual;
        Location = new Point(
            area.Left + Math.Max(0, (area.Width - Width) / 2),
            area.Top + Math.Max(0, (area.Height - Height) / 2));
    }

    internal void SaveForTest() => SaveAndStayOpen();
    internal void SetLanguageForTest(int language)
    {
        language = Math.Clamp(language, 0, 1);
        if (_languageCombo.SelectedIndex != language)
            _languageCombo.SelectedIndex = language;
        else
        {
            L10n.SetLanguage((AppLanguage)language);
            ApplyLanguageToOpenForm();
        }
    }

    internal void SetThemeForTest(int themeMode)
    {
        themeMode = Math.Clamp(themeMode, 0, 2);
        if (_themeCombo.SelectedIndex != themeMode)
            _themeCombo.SelectedIndex = themeMode;
    }

    internal void SavePreview(string path)
    {
        CreateControl();
        foreach (Control child in Controls) child.CreateControl();
        PerformLayout();
        Refresh();
        Application.DoEvents();
        var previewSize = _rootLayout.ClientSize;
        using var bitmap = new Bitmap(previewSize.Width, previewSize.Height);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(UiPalette.Canvas);
            foreach (Control section in _rootLayout.Controls)
            {
                if (!section.Visible || section.Width <= 0 || section.Height <= 0) continue;
                using var layer = new Bitmap(section.Width, section.Height);
                try
                {
                    section.DrawToBitmap(layer, new Rectangle(Point.Empty, section.Size));
                    graphics.DrawImageUnscaled(layer, section.Left, section.Top);
                }
                catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or
                                           System.Runtime.InteropServices.ExternalException or
                                           ArgumentException or InvalidOperationException)
                {
                    // A failed owner-draw section must not crash the standalone QA tool.
                    // The canvas remains intact and the next section can still render.
                }
            }
        }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private Panel BuildFooter()
    {
        var footer = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.Canvas,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        footer.Controls.Add(new Panel
        {
            Dock = DockStyle.Top,
            Height = 1,
            BackColor = UiPalette.Border
        });

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(22, 13, 20, 13),
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 150f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _saveStatusLabel = MakeDockLabel(L10n.SettingsSavedState,
            UiPalette.Body(7.7f, FontStyle.Bold), UiPalette.Faint);
        _saveStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        layout.Controls.Add(_saveStatusLabel, 0, 0);

        var cancel = MakeActionButton(L10n.Cancel, 92, primary: false);
        cancel.Dock = DockStyle.Fill;
        cancel.Margin = new Padding(4, 7, 8, 7);
        cancel.Click += (_, _) => CancelAndClose();
        layout.Controls.Add(cancel, 1, 0);

        _saveButton = MakeActionButton(L10n.Save, 138, primary: true);
        _saveButton.Dock = DockStyle.Fill;
        _saveButton.Margin = new Padding(4, 7, 0, 7);
        _saveButton.Click += (_, _) => SaveAndStayOpen();
        layout.Controls.Add(_saveButton, 2, 0);
        footer.Controls.Add(layout);

        AcceptButton = _saveButton;
        CancelButton = cancel;
        return footer;
    }

    private void AddPage(Control host, Control page)
    {
        page.Visible = false;
        _pages.Add(page);
        host.Controls.Add(page);
    }

    private void SelectPage(int index)
    {
        if (index < 0 || index >= _pages.Count) return;
        if (index == _selectedPageIndex) return;
        var previousIndex = _selectedPageIndex;
        var nextPage = _pages[index];
        var host = nextPage.Parent;
        using (NativeRedrawScope.Suspend(this))
        {
            SuspendLayout();
            host?.SuspendLayout();
            try
            {
                if (_orbPreview is not null && index != 1)
                    _orbPreview.Hide();
                if (previousIndex >= 0 && previousIndex < _pages.Count)
                    _pages[previousIndex].Visible = false;
                nextPage.Visible = true;
                nextPage.BringToFront();
                _selectedPageIndex = index;
                for (var i = 0; i < _navigation.Count; i++) _navigation[i].Active = i == index;
                if (_orbPreview is not null)
                    _orbPreview.Visible = index == 1;
            }
            finally
            {
                host?.ResumeLayout(performLayout: true);
                ResumeLayout(performLayout: false);
            }
            nextPage.PerformLayout();
        }
    }

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
        _positionLockedToggle.CheckedChanged += (_, _) => UpdateFromDirectControls();
        _snapToEdgeToggle.CheckedChanged += (_, _) => UpdateFromDirectControls();
        _clickThroughToggle.CheckedChanged += (_, _) => UpdateFromDirectControls();
        _hoverPreviewToggle.CheckedChanged += (_, _) => UpdateFromDirectControls();
        _globalHotKeyToggle.CheckedChanged += (_, _) => UpdateFromDirectControls();
        _alertSoundToggle.CheckedChanged += (_, _) => UpdateFromDirectControls();
        _trendRecordingToggle.CheckedChanged += (_, _) => UpdateFromDirectControls();
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
            HoverPreviewEnabled = _hoverPreviewToggle.Checked,
            GlobalHotKeyEnabled = _globalHotKeyToggle.Checked,
            AlertSoundEnabled = _alertSoundToggle.Checked,
            TrendRecordingEnabled = _trendRecordingToggle.Checked,
            ThemeMode = selectedTheme,
            Language = selectedLanguage
        });
        if (languageChanged) L10n.SetLanguage((AppLanguage)_workingPreferences.Language);
        if (themeChanged)
        {
            var previousColors = UiPalette.ResolveColors(previousPreferences.ThemeMode);
            UiPalette.SetTheme(_workingPreferences.ThemeMode);
            UiPalette.ApplyTheme(this, previousColors);
        }
        TopMost = _workingPreferences.AlwaysOnTop;
        _flameStyleCombo.Enabled = _workingPreferences.ConsumptionFlameEnabled;
        if (!languageChanged &&
            previousPreferences.SettingsFontScalePercent != _workingPreferences.SettingsFontScalePercent)
            UiPalette.ApplyScaledTypography(this, _workingPreferences.SettingsFontScalePercent);
        RaisePreview();
        if (languageChanged) ApplyLanguageToOpenForm();
    }

    private void ApplyLanguageToOpenForm()
    {
        if (_relocalizing || IsDisposed) return;
        _relocalizing = true;
        var wasInitializing = _initializing;
        _initializing = true;
        try
        {
            using var template = new SettingsForm(
                _workingPreferences,
                StartupEnabled,
                _snapshot,
                _diagnostics);
            Text = template.Text;
            AccessibleName = template.AccessibleName;
            // Page BringToFront calls change the Controls z-order. Copy common
            // chrome while skipping page subtrees, then pair the five pages by
            // their stable logical index so localization never crosses pages.
            CopyLocalizedControlTree(this, template, skipSettingsPages: true);
            for (var index = 0; index < Math.Min(_pages.Count, template._pages.Count); index++)
                CopyLocalizedControlTree(_pages[index], template._pages[index], skipSettingsPages: false);
        }
        finally
        {
            _initializing = wasInitializing;
            _relocalizing = false;
        }
        UiPalette.ApplyScaledTypography(this, _workingPreferences.SettingsFontScalePercent);
        UpdateSummaries();
        UpdateDirtyState();
        UpdateOrbPreview();
        PerformLayout();
        foreach (var navigationButton in _navigation)
        {
            navigationButton.Invalidate();
            navigationButton.Update();
        }
        Invalidate(invalidateChildren: true);
    }

    private static void CopyLocalizedControlTree(Control target, Control source, bool skipSettingsPages)
    {
        var count = Math.Min(target.Controls.Count, source.Controls.Count);
        for (var index = 0; index < count; index++)
        {
            var destination = target.Controls[index];
            var localized = source.Controls[index];
            if (skipSettingsPages &&
                (destination is ResponsiveSettingsPage || localized is ResponsiveSettingsPage))
                continue;

            if (destination is ComboBox destinationCombo && localized is ComboBox localizedCombo)
            {
                var selectedIndex = destinationCombo.SelectedIndex;
                destinationCombo.BeginUpdate();
                try
                {
                    destinationCombo.Items.Clear();
                    foreach (var item in localizedCombo.Items) destinationCombo.Items.Add(item);
                    destinationCombo.SelectedIndex = Math.Clamp(
                        selectedIndex,
                        destinationCombo.Items.Count == 0 ? -1 : 0,
                        destinationCombo.Items.Count - 1);
                }
                finally { destinationCombo.EndUpdate(); }
            }
            else if (destination is Label or ButtonBase)
            {
                destination.Text = localized.Text;
            }

            if (!string.IsNullOrWhiteSpace(localized.AccessibleName))
                destination.AccessibleName = localized.AccessibleName;
            CopyLocalizedControlTree(destination, localized, skipSettingsPages);
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
        _hoverPreviewToggle.Checked = preferences.HoverPreviewEnabled;
        _globalHotKeyToggle.Checked = preferences.GlobalHotKeyEnabled;
        _alertSoundToggle.Checked = preferences.AlertSoundEnabled;
        _trendRecordingToggle.Checked = preferences.TrendRecordingEnabled;
        TopMost = preferences.AlwaysOnTop;
        UpdateSummaries();
        _initializing = false;
        UiPalette.ApplyScaledTypography(this, preferences.SettingsFontScalePercent);
        UpdateOrbPreview();
        UpdateDirtyState();
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
        _ringSummary.Text = $"{RingWindowCatalog.FormatShort(_workingPreferences.OuterWindowMinutes)}  /  " +
                            RingWindowCatalog.FormatShort(_workingPreferences.InnerWindowMinutes);
        _alertSummary.Text = _workingPreferences.AlertsEnabled
            ? L10n.AlertsSummary(_workingPreferences.WarningThreshold, _workingPreferences.CriticalThreshold)
            : L10n.AlertsOff;
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
        var previewSize = minimumPreviewSize + (int)Math.Round(
            previewProgress * (maximumPreviewSize - minimumPreviewSize));
        _orbPreview.Size = new Size(previewSize, previewSize);
        _orbPreview.ConfigureRings(RingDisplayConfiguration.FromPreferences(_workingPreferences));
        _orbPreview.SetFlameAnimationEnabled(_workingPreferences.ConsumptionFlameEnabled);
        _orbPreview.SetFlameStyle(_workingPreferences.ConsumptionFlameStyle);
        if (_snapshot is not null) _orbPreview.SetSnapshot(_snapshot, live: true);
        if (_orbPreview.Parent is { } host) CenterOrbPreview(host);
    }

    internal void SetOrbSizeForTest(int value)
    {
        value = PanelPreferenceManager.NormalizeOrbSize(value);
        _orbSizeInput.Value = value;
        if (_orbSizeSlider.Value != value) _orbSizeSlider.Value = value;
        if (!_initializing && SelectedOrbSize == value && _workingPreferences.OrbSize != value)
            UpdateFromDirectControls();
    }

    internal void SetFontScaleForTest(int value)
    {
        value = PanelPreferenceManager.NormalizeSettingsFontScale(value);
        _fontScaleInput.Value = value;
        if (_fontScaleSlider.Value != value) _fontScaleSlider.Value = value;
        if (!_initializing && SelectedFontScalePercent == value &&
            _workingPreferences.SettingsFontScalePercent != value)
            UpdateFromDirectControls();
    }

    private static Panel MakePageIntro(string title, string subtitle)
    {
        var panel = new Panel
        {
            Size = new Size(570, 62),
            Margin = new Padding(0, 0, 0, 8),
            BackColor = Color.Transparent
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = new Padding(2, 0, 2, 0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.Controls.Add(MakeDockLabel(title,
            UiPalette.Display(13f, FontStyle.Bold), UiPalette.Text), 0, 0);
        layout.Controls.Add(MakeDockLabel(subtitle,
            UiPalette.Body(7.7f), UiPalette.Muted), 0, 1);
        panel.Controls.Add(layout);
        return panel;
    }

    private static SettingsCard MakeToggleRow(string title, string hint, SettingsToggle toggle) =>
        MakeBaseRow(title, hint, toggle, 78);

    private static SettingsCard MakeControlRow(
        string title,
        string hint,
        Control control,
        int rightColumnWidth = 190) =>
        MakeBaseRow(title, hint, control, rightColumnWidth);

    private static SettingsCard MakeEditorRow(string title, string hint, Label summary, Action edit)
    {
        var editor = new TableLayoutPanel
        {
            Size = new Size(304, 38),
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112f));
        editor.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        summary.Dock = DockStyle.Fill;
        summary.Margin = new Padding(0, 2, 10, 2);
        editor.Controls.Add(summary, 0, 0);
        var button = MakeActionButton(L10n.Edit, 104, primary: false);
        button.Dock = DockStyle.Fill;
        button.Margin = new Padding(0, 3, 0, 3);
        button.Click += (_, _) => edit();
        editor.Controls.Add(button, 1, 0);
        return MakeBaseRow(title, hint, editor, 318);
    }

    private static SettingsCard MakeBaseRow(
        string title,
        string hint,
        Control rightControl,
        int rightColumnWidth)
    {
        var height = Math.Max(86, rightControl.Height + 26);
        var card = new SettingsCard
        {
            Size = new Size(570, height),
            Margin = new Padding(0, 0, 0, 10)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(18, 10, 18, 10),
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, rightColumnWidth));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.Controls.Add(MakeTextBlock(title, hint, height - 20), 0, 0);
        rightControl.Anchor = AnchorStyles.None;
        rightControl.Margin = new Padding(12, 0, 0, 0);
        layout.Controls.Add(rightControl, 1, 0);
        card.Controls.Add(layout);
        return card;
    }

    private static Control MakeTextBlock(string title, string hint, int minimumHeight)
    {
        var text = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            MinimumSize = new Size(0, minimumHeight),
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        text.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        text.RowStyles.Add(new RowStyle(SizeType.Absolute, 27f));
        text.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        text.Controls.Add(MakeDockLabel(title,
            UiPalette.Body(8.7f, FontStyle.Bold), UiPalette.Text), 0, 0);
        var hintLabel = MakeDockLabel(hint, UiPalette.Body(7.1f), UiPalette.Muted);
        hintLabel.TextAlign = ContentAlignment.TopLeft;
        text.Controls.Add(hintLabel, 0, 1);
        return text;
    }

    private static SettingsToggle MakeToggle(string accessibleName) => new()
    {
        Size = new Size(44, 24),
        AccessibleName = accessibleName,
        TabStop = true
    };

    private static ComboBox MakeCombo() => new()
    {
        Size = new Size(166, 32),
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = UiPalette.SurfaceRaised,
        ForeColor = UiPalette.Text,
        Font = UiPalette.Body(8f),
        IntegralHeight = false,
        DropDownHeight = 160
    };

    private static void AddItems(ComboBox comboBox, params string[] items) => comboBox.Items.AddRange(items);

    private static Label MakeSummaryLabel() => new()
    {
        AutoSize = false,
        BackColor = Color.Transparent,
        ForeColor = UiPalette.Mint,
        Font = UiPalette.Mono(7.2f, FontStyle.Bold),
        TextAlign = ContentAlignment.MiddleRight,
        AutoEllipsis = true,
        UseMnemonic = false
    };

    private static ActionButton MakeActionButton(string text, int width, bool primary) => new()
    {
        Text = text,
        Size = new Size(width, 32),
        Primary = primary,
        AccessibleName = text
    };

    private static Label MakeLabel(string text, Point location, Size size, Font font, Color color) => new()
    {
        Text = text,
        Location = location,
        Size = size,
        Font = font,
        ForeColor = color,
        BackColor = Color.Transparent,
        AutoEllipsis = true
    };

    private static Label MakeDockLabel(string text, Font font, Color color) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        Font = font,
        ForeColor = color,
        BackColor = Color.Transparent,
        AutoEllipsis = true,
        Margin = Padding.Empty,
        TextAlign = ContentAlignment.MiddleLeft,
        UseMnemonic = false
    };

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);
}

internal sealed class BufferedSettingsHost : Panel
{
    public BufferedSettingsHost()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.Opaque, true);
        UpdateStyles();
    }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        e.Graphics.Clear(BackColor);
}

internal sealed class ResponsiveSettingsPage : Panel
{
    private readonly TableLayoutPanel _content;

    public ResponsiveSettingsPage()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Opaque, true);
        Dock = DockStyle.Fill;
        AutoScroll = true;
        BackColor = UiPalette.Canvas;
        Margin = Padding.Empty;
        Padding = new Padding(2, 2, 7, 10);

        _content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = UiPalette.Canvas,
            ColumnCount = 1,
            RowCount = 0,
            GrowStyle = TableLayoutPanelGrowStyle.AddRows,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        Controls.Add(_content);
    }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        e.Graphics.Clear(UiPalette.Canvas);

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        // WinForms normally accelerates scrolling by copying the previous pixels.
        // Force one buffered repaint so rounded cards and transparent labels cannot
        // leave copied text behind during rapid mouse-wheel input.
        Invalidate(invalidateChildren: true);
        Update();
    }

    public void AddItem(Control control)
    {
        control.Dock = DockStyle.Fill;
        var rowIndex = _content.RowCount;
        _content.RowCount = rowIndex + 1;
        _content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _content.Controls.Add(control, 0, rowIndex);
    }
}

internal sealed class SettingsHeaderPanel : Panel
{
    public SettingsHeaderPanel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var gradient = new LinearGradientBrush(ClientRectangle,
            UiPalette.SurfaceRaised, UiPalette.Canvas, LinearGradientMode.Horizontal);
        e.Graphics.FillRectangle(gradient, ClientRectangle);
        using var line = new Pen(UiPalette.Border);
        e.Graphics.DrawLine(line, 0, Height - 1, Width, Height - 1);
    }
}

internal sealed class SettingsBrandTitle : Control
{
    public SettingsBrandTitle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var brand = "Codex";
        var separator = "  /  ";
        var product = L10n.Pick("额度面板", "Quota Panel");
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var brandSize = TextRenderer.MeasureText(e.Graphics, brand, Font, Size.Empty, flags);
        var separatorSize = TextRenderer.MeasureText(e.Graphics, separator, Font, Size.Empty, flags);
        var productSize = TextRenderer.MeasureText(e.Graphics, product, Font, Size.Empty, flags);
        var totalWidth = brandSize.Width + separatorSize.Width + productSize.Width;
        var x = 0;
        var y = Math.Max(0, (Height - Math.Max(brandSize.Height, productSize.Height)) / 2);

        TextRenderer.DrawText(e.Graphics, brand, Font,
            new Point(x, y), UiPalette.Text, flags);
        x += brandSize.Width;
        TextRenderer.DrawText(e.Graphics, separator, Font,
            new Point(x, y), UiPalette.Mint, flags);
        x += separatorSize.Width;
        TextRenderer.DrawText(e.Graphics, product, Font,
            new Point(x, y), UiPalette.Muted, flags);

        if (totalWidth > Width)
            TextRenderer.DrawText(e.Graphics, L10n.Pick("Codex / 额度面板", "Codex / Quota Panel"), Font,
                ClientRectangle, UiPalette.Text,
                flags | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
    }
}

internal sealed class SettingsCard : Panel
{
    public SettingsCard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = UiPalette.Canvas;
    }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        e.Graphics.Clear(UiPalette.Canvas);

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiPalette.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 10);
        using var fill = new SolidBrush(UiPalette.Surface);
        using var border = new Pen(UiPalette.Border);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        base.OnPaint(e);
    }
}

internal sealed class SettingsNavButton : Button
{
    private bool _active;
    private bool _hovered;

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Active
    {
        get => _active;
        set { _active = value; Invalidate(); }
    }

    public SettingsNavButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = UiPalette.Canvas;
        ForeColor = UiPalette.Text;
        Font = UiPalette.Body(8.5f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        TextAlign = ContentAlignment.MiddleLeft;
        Padding = new Padding(20, 0, 8, 0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);
        if (_active || _hovered)
        {
            using var path = UiPalette.RoundedRect(new RectangleF(0, 1, Width - 1, Height - 2), 9);
            using var fill = new SolidBrush(_active ? UiPalette.SurfaceRaised : UiPalette.Surface);
            e.Graphics.FillPath(fill, path);
        }
        if (_active)
        {
            using var rail = UiPalette.RoundedRect(new RectangleF(5, 12, 3, Height - 24), 1.5f);
            using var fill = new SolidBrush(UiPalette.Mint);
            e.Graphics.FillPath(fill, rail);
        }
        var textBounds = new Rectangle(20, 0, Math.Max(0, Width - 28), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds,
            _active ? UiPalette.Text : UiPalette.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
}

internal sealed class SettingsToggle : CheckBox
{
    private bool _hovered;

    public SettingsToggle()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        Appearance = Appearance.Button;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = UiPalette.Surface;
        Cursor = Cursors.Hand;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(BackColor);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var trackBounds = new RectangleF(0.5f, 1.5f, Width - 1, Height - 3);
        using var track = UiPalette.RoundedRect(trackBounds, trackBounds.Height / 2f);
        var trackColor = Checked
            ? (_hovered ? Color.FromArgb(127, 238, 190) : UiPalette.Mint)
            : (_hovered ? Color.FromArgb(70, 75, 71) : UiPalette.Track);
        using var trackBrush = new SolidBrush(trackColor);
        e.Graphics.FillPath(trackBrush, track);

        var diameter = Height - 8f;
        var x = Checked ? Width - diameter - 4f : 4f;
        using var knob = new SolidBrush(Checked ? UiPalette.Canvas : UiPalette.Muted);
        e.Graphics.FillEllipse(knob, x, 4f, diameter, diameter);

        if (Focused)
        {
            using var focus = new Pen(Color.FromArgb(150, UiPalette.Sky)) { DashStyle = DashStyle.Dot };
            e.Graphics.DrawPath(focus, track);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnCheckedChanged(EventArgs e) { Invalidate(); base.OnCheckedChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
}
