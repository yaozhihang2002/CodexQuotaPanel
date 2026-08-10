using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.ComponentModel;

namespace CodexQuotaPanel;

/// <summary>
/// A single, staged settings surface. Preference previews are reversible: closing
/// with Cancel raises one final preview containing the most recently saved values.
/// Saving applies immediately without closing, destructive history clearing is
/// intentionally immediate, and a defaults reset remains staged until Save.
/// </summary>
internal sealed class SettingsForm : Form
{
    private static readonly Size CompactClientSize = new(800, 520);
    private static readonly Size CompactMinimumSize = new(780, 480);

    private PanelPreferences _savedPreferences;
    private bool _savedStartupEnabled;
    private readonly QuotaSnapshot? _snapshot;
    private readonly string _diagnostics;
    private readonly List<SettingsNavButton> _navigation = [];
    private readonly List<Control?> _pages = [];
    private readonly List<Func<Control>> _pageFactories = [];
    private readonly HashSet<Control> _nativeThemedPages = [];
    private Control _pageHost = null!;

    private PanelPreferences _workingPreferences;
    private bool _initializing;
    private bool _resetPending;
    private bool _syncingOrbSize;
    private bool _syncingFontScale;
    private bool _relocalizing;
    private int _selectedPageIndex = -1;
    private int _appliedLayoutScalePercent = 100;

    private ActionButton _saveButton = null!;
    private Label _saveStatusLabel = null!;
    private QuotaOrbControl? _orbPreview;
    private readonly TableLayoutPanel _rootLayout;

    private readonly SettingsToggle _startupToggle;
    private readonly ComboBox _startupViewCombo;
    private readonly ComboBox _languageCombo;
    private readonly ComboBox _themeCombo;
    private readonly SettingsToggle _topMostToggle;
    private readonly SettingsSlider _orbSizeSlider;
    private readonly NumericUpDown _orbSizeInput;
    private readonly SettingsSlider _fontScaleSlider;
    private readonly NumericUpDown _fontScaleInput;
    private readonly Label _opacitySummary;
    private readonly Label _orbBackgroundSummary;
    private readonly RingColorButton _orbBackgroundColorButton;
    private readonly ActionButton _orbBackgroundDefaultButton;
    private readonly Label _ringSummary;
    private readonly SettingsToggle _consumptionFlameToggle;
    private readonly ComboBox _flameStyleCombo;
    private readonly SettingsToggle _positionLockedToggle;
    private readonly SettingsToggle _snapToEdgeToggle;
    private readonly SettingsToggle _clickThroughToggle;
    private readonly SettingsToggle _clickThroughReminderToggle;
    private readonly SettingsToggle _hoverPreviewToggle;
    private readonly SettingsToggle _globalHotKeyToggle;
    private readonly Label _alertSummary;
    private readonly SettingsToggle _alertSoundToggle;
    private readonly SettingsToggle _trendRecordingToggle;
    private readonly SettingsToggle _checkUpdatesToggle;
    private readonly CancellationTokenSource _operationLifetime = new();
    private readonly System.Windows.Forms.Timer _fontScalePreviewTimer;
    private readonly System.Windows.Forms.Timer _pagePrewarmTimer;
    private ActionButton _updateCheckButton = null!;
    private Label _updateStatusLabel = null!;
    private bool _checkingForUpdates;
    private bool _managedResourcesDisposed;
    private bool _initialNativeThemeApplied;
    private bool _interactiveResize;
    private int _pagePaintGeneration;

    public PanelPreferences SelectedPreferences => PanelPreferenceManager.Normalize(_workingPreferences);
    public bool StartupEnabled => _startupToggle.Checked;
    internal bool IsDirty => _resetPending ||
        SelectedPreferences != _savedPreferences || StartupEnabled != _savedStartupEnabled;
    internal int SelectedOrbSize => (int)_orbSizeInput.Value;
    internal int SelectedFontScalePercent => (int)_fontScaleInput.Value;
    internal bool SaveButtonVisible => _saveButton.Parent is { } parent &&
        !_saveButton.IsDisposed && _saveButton.Width > 0 && _saveButton.Height > 0 &&
        parent.ClientRectangle.IntersectsWith(_saveButton.Bounds);

    internal void SetClickThroughReminderEnabled(bool enabled)
    {
        _workingPreferences = _workingPreferences with { ShowClickThroughReminder = enabled };
        _initializing = true;
        _clickThroughReminderToggle.Checked = enabled;
        _initializing = false;
        UpdateDirtyState();
    }

    public event Action<PanelPreferences>? PreviewPreferencesChanged;
    public event Action? MoveToCurrentDisplayRequested;
    public event Action? ClearHistoryRequested;
    public event Action? ResetRequested;
    public event Action? SaveRequested;
    public event Func<CancellationToken, Task<UpdateCheckResult>>? CheckForUpdatesRequested;

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
        SuspendLayout();

        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = CompactClientSize;
        MinimumSize = CompactMinimumSize;
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
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        SetStyle(ControlStyles.ResizeRedraw, false);

        _rootLayout = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.Canvas,
            ColumnCount = 2,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 166f));
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72f));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 60f));
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
            Padding = new Padding(16, 10, 16, 10)
        };
        contentHost.Resize += (_, _) =>
        {
            if (_interactiveResize) ResizeSelectedPageToViewport();
            else ResizeSettingsPagesToViewport();
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
        _orbSizeSlider = new SettingsSlider
        {
            Dock = DockStyle.Fill,
            Minimum = PanelPreferenceManager.MinimumOrbSize,
            Maximum = PanelPreferenceManager.MaximumOrbSize,
            TickFrequency = 16,
            SmallChange = 1,
            LargeChange = 8,
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
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = UiPalette.SurfaceRaised,
            ForeColor = UiPalette.Text,
            Font = UiPalette.Body(8f),
            Margin = Padding.Empty,
            AccessibleName = L10n.OrbSize
        };
        _fontScaleSlider = new SettingsSlider
        {
            Dock = DockStyle.Fill,
            Minimum = PanelPreferenceManager.MinimumSettingsFontScale,
            Maximum = PanelPreferenceManager.MaximumSettingsFontScale,
            TickFrequency = 5,
            SmallChange = 1,
            LargeChange = 5,
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
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = UiPalette.SurfaceRaised,
            ForeColor = UiPalette.Text,
            Font = UiPalette.Body(8f),
            Margin = Padding.Empty,
            AccessibleName = L10n.SettingsFontSize
        };
        _opacitySummary = MakeSummaryLabel();
        _orbBackgroundSummary = MakeSummaryLabel();
        _orbBackgroundColorButton = new RingColorButton
        {
            Size = new Size(94, 34),
            MinimumSize = new Size(94, 34),
            SelectedColor = UiPalette.DefaultOrbBackground,
            AccessibleName = L10n.OrbBackground
        };
        _orbBackgroundDefaultButton = MakeActionButton(L10n.RestoreDefault, 134, primary: false);
        _ringSummary = MakeSummaryLabel();
        _consumptionFlameToggle = MakeToggle(L10n.ConsumptionFlame);
        _flameStyleCombo = MakeCombo();
        AddItems(_flameStyleCombo, L10n.FlameStyleEmber, L10n.FlameStyleFluid, L10n.FlameStylePixel);

        _positionLockedToggle = MakeToggle(L10n.PositionLock);
        _snapToEdgeToggle = MakeToggle(L10n.SnapToEdge);
        _clickThroughToggle = MakeToggle(L10n.ClickThrough);
        _clickThroughReminderToggle = MakeToggle(L10n.ClickThroughReminder);
        _hoverPreviewToggle = MakeToggle(L10n.HoverPreview);
        _globalHotKeyToggle = MakeToggle(L10n.GlobalHotKey);

        _alertSummary = MakeSummaryLabel();
        _alertSoundToggle = MakeToggle(L10n.AlertSound);
        _trendRecordingToggle = MakeToggle(L10n.TrendRecording);
        _checkUpdatesToggle = MakeToggle(L10n.CheckUpdatesOnStartup);
        _fontScalePreviewTimer = new System.Windows.Forms.Timer { Interval = 45 };
        _fontScalePreviewTimer.Tick += (_, _) => ApplyPendingFontScalePreview();
        _pagePrewarmTimer = new System.Windows.Forms.Timer { Interval = 650 };
        _pagePrewarmTimer.Tick += (_, _) => PrewarmNextSettingsPage();

        _pageHost = contentHost;
        _pageFactories.AddRange([
            BuildGeneralPage,
            BuildAppearancePage,
            BuildInteractionPage,
            BuildNotificationsPage,
            BuildDataPage
        ]);
        for (var index = 0; index < _pageFactories.Count; index++) _pages.Add(null);
        EnsurePageBuilt(0);

        var footer = BuildFooter();
        _rootLayout.Controls.Add(footer, 0, 2);
        _rootLayout.SetColumnSpan(footer, 2);

        ApplyPreferencesToControls(_workingPreferences, startupEnabled);
        WireControlEvents();
        SelectPage(0);
        _initializing = false;
        UpdateDirtyState();
        ResumeLayout(performLayout: false);

        FormClosing += OnSettingsFormClosing;
        Shown += (_, _) => _pagePrewarmTimer.Start();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing && !_managedResourcesDisposed)
        {
            _managedResourcesDisposed = true;
            _operationLifetime.Cancel();
            _operationLifetime.Dispose();
            _fontScalePreviewTimer.Stop();
            _fontScalePreviewTimer.Dispose();
            _pagePrewarmTimer.Stop();
            _pagePrewarmTimer.Dispose();
            Control[] lazilyParentedControls =
            [
                _topMostToggle, _orbSizeSlider, _orbSizeInput, _fontScaleSlider, _fontScaleInput,
                _opacitySummary, _orbBackgroundSummary, _orbBackgroundColorButton,
                _orbBackgroundDefaultButton, _ringSummary, _consumptionFlameToggle, _flameStyleCombo,
                _positionLockedToggle, _snapToEdgeToggle, _clickThroughToggle,
                _clickThroughReminderToggle, _hoverPreviewToggle, _globalHotKeyToggle,
                _alertSummary, _alertSoundToggle, _trendRecordingToggle, _checkUpdatesToggle
            ];
            foreach (var control in lazilyParentedControls)
            {
                if (control.Parent is null && !control.IsDisposed) control.Dispose();
            }
        }
        base.Dispose(disposing);
    }

    protected override void OnLoad(EventArgs e)
    {
        // OnLoad runs before the first visible frame, so finish monitor sizing
        // directly while the window is still hidden. Suspending native redraw
        // here forced an unnecessary full-tree RedrawWindow before Show().
        FitToCurrentDisplay();
        ApplyCompactTypographyMetrics(this, _workingPreferences.SettingsFontScalePercent);
        if (!_initialNativeThemeApplied)
        {
            NativeTheme.ApplyCaption(this);
            _initialNativeThemeApplied = true;
        }
        if (_pages[0] is { IsHandleCreated: true } generalPage)
            _nativeThemedPages.Add(generalPage);
        RestoreSelectedPageZOrder();
        base.OnLoad(e);
    }

    protected override void OnResizeBegin(EventArgs e)
    {
        _interactiveResize = true;
        _orbPreview?.SetAnimationPaused(true);
        base.OnResizeBegin(e);
    }

    protected override void OnResizeEnd(EventArgs e)
    {
        base.OnResizeEnd(e);
        _interactiveResize = false;
        ResizeSettingsPagesToViewport();
        if (_selectedPageIndex >= 0 && _pages[_selectedPageIndex] is { } selectedPage)
            HideInactivePages(selectedPage);
        if (_selectedPageIndex == 1) _orbPreview?.SetAnimationPaused(false);
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ResizeSettingsPagesToViewport();
        UpdateOrbPreview();
    }

    private void RestoreSelectedPageZOrder()
    {
        if (_selectedPageIndex < 0 || _pages[_selectedPageIndex] is not { } selectedPage) return;
        NativeZOrder.BringToFront(selectedPage);
    }

    private void FitToCurrentDisplay()
    {
        var workArea = Screen.FromControl(this).WorkingArea;
        var dpiScale = Math.Max(0.5f, DeviceDpi / 96f);
        var margin = Math.Max(8, (int)Math.Round(20 * dpiScale));
        var availableWidth = Math.Max(1, workArea.Width - margin * 2);
        var availableHeight = Math.Max(1, workArea.Height - margin * 2);
        // WinForms DPI scaling already turns these logical dimensions into
        // physical pixels. Typography is intentionally excluded here: a larger
        // reading size must not turn this lightweight settings window into a
        // near-full-screen surface on high-DPI or remote-desktop sessions.
        var minimumWidth = Math.Max(1, (int)Math.Round(CompactMinimumSize.Width * dpiScale));
        var minimumHeight = Math.Max(1, (int)Math.Round(CompactMinimumSize.Height * dpiScale));

        MinimumSize = new Size(
            Math.Min(minimumWidth, availableWidth),
            Math.Min(minimumHeight, availableHeight));
        Size = new Size(
            Math.Min(Width, availableWidth),
            Math.Min(Height, availableHeight));

        var left = Math.Clamp(Left, workArea.Left + margin,
            Math.Max(workArea.Left + margin, workArea.Right - Width - margin));
        var top = Math.Clamp(Top, workArea.Top + margin,
            Math.Max(workArea.Top + margin, workArea.Bottom - Height - margin));
        Location = new Point(left, top);
    }

    private void ApplySettingsLayoutScale(int scalePercent)
    {
        scalePercent = PanelPreferenceManager.NormalizeSettingsFontScale(scalePercent);
        if (scalePercent == _appliedLayoutScalePercent) return;

        // Font scale is a readability preference, not a zoom command. Scaling
        // the complete control tree also scales ClientSize, fixed columns,
        // margins and padding; on a 150% display that compounded with DPI and
        // made the small utility almost fill the screen. The baseline geometry
        // below is deliberately roomy enough for 150% typography, while long
        // pages remain scrollable.
        _appliedLayoutScalePercent = scalePercent;
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        // Keep the first frame cheap: the settings controls already use the
        // app's owner-drawn palette, so only the native title bar must be set
        // before the form becomes visible. A full native-tree refresh remains
        // available for an explicit live theme change.
        NativeTheme.ApplyCaption(this);
        _initialNativeThemeApplied = true;
        if (_pages.Count > 0 && _pages[0] is { IsHandleCreated: true } generalPage)
            _nativeThemedPages.Add(generalPage);
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
            Padding = new Padding(18, 9, 17, 8),
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 3f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 112f));
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
            Font = UiPalette.Display(14.5f, FontStyle.Bold),
            Margin = new Padding(11, 0, 8, 0),
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
            BackColor = UiPalette.Surface,
            Padding = new Padding(8, 10, 8, 8)
        };
        var flow = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.Surface,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        host.Controls.Add(flow);
        host.Controls.Add(new Panel
        {
            Dock = DockStyle.Right,
            Width = 1,
            BackColor = UiPalette.Border,
            Margin = Padding.Empty
        });

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
            Size = new Size(148, 38),
            Margin = new Padding(0, 0, 0, 4),
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
            L10n.Pick("选择悬浮球、详情、仅托盘或恢复上次状态", "Choose the orb, details, tray only, or restore last state"),
            BuildChoiceSelector(_startupViewCombo, columns: 2), rightColumnWidth: 334));
        page.AddItem(MakeControlRow(L10n.InterfaceLanguage, L10n.LanguageRestartHint,
            BuildChoiceSelector(_languageCombo, columns: 2), rightColumnWidth: 334));
        page.AddItem(MakeControlRow(L10n.InterfaceTheme, L10n.ThemeHint,
            BuildChoiceSelector(_themeCombo, columns: 3), rightColumnWidth: 334));
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
        page.AddItem(MakeControlRow(L10n.OrbBackground, L10n.OrbBackgroundHint,
            BuildOrbBackgroundEditor(), rightColumnWidth: 334));
        page.AddItem(MakeEditorRow(L10n.DualRingDisplay,
            L10n.Pick("选择额度窗口并分别设置环形颜色", "Choose quota windows and a color for each ring"), _ringSummary, EditRings));
        page.AddItem(MakeControlRow(L10n.FlameStyle,
            L10n.FlameStyleHint, BuildChoiceSelector(_flameStyleCombo, columns: 3), rightColumnWidth: 334));
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

    private Control BuildOrbBackgroundEditor()
    {
        var layout = new TableLayoutPanel
        {
            Size = new Size(320, 40),
            BackColor = Color.Transparent,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 108f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 104f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _orbBackgroundSummary.Dock = DockStyle.Fill;
        _orbBackgroundSummary.TextAlign = ContentAlignment.MiddleCenter;
        _orbBackgroundSummary.Margin = Padding.Empty;
        layout.Controls.Add(_orbBackgroundSummary, 0, 0);
        _orbBackgroundColorButton.Dock = DockStyle.Fill;
        _orbBackgroundColorButton.Margin = new Padding(4, 3, 6, 3);
        layout.Controls.Add(_orbBackgroundColorButton, 1, 0);
        _orbBackgroundDefaultButton.Dock = DockStyle.Fill;
        _orbBackgroundDefaultButton.Margin = new Padding(4, 3, 0, 3);
        layout.Controls.Add(_orbBackgroundDefaultButton, 2, 0);
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
        return card;
    }

    private void CenterOrbPreview(Control host)
    {
        if (_orbPreview is null) return;
        var viewport = host.DisplayRectangle;
        _orbPreview.Location = new Point(
            viewport.Left + Math.Max(0, (viewport.Width - _orbPreview.Width) / 2),
            viewport.Top + Math.Max(0, (viewport.Height - _orbPreview.Height) / 2));
    }

    private Control BuildInteractionPage()
    {
        var page = MakePage();
        page.AddItem(MakePageIntro(L10n.SettingsInteraction, L10n.InteractionIntro));
        page.AddItem(MakeToggleRow(L10n.PositionLock, L10n.PositionLockHint, _positionLockedToggle));
        page.AddItem(MakeToggleRow(L10n.SnapToEdge, L10n.SnapToEdgeHint, _snapToEdgeToggle));
        page.AddItem(MakeToggleRow(L10n.ClickThrough, L10n.ClickThroughHint, _clickThroughToggle));
        page.AddItem(MakeToggleRow(L10n.ClickThroughReminder, L10n.ClickThroughReminderHint, _clickThroughReminderToggle));
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
        page.AddItem(BuildVersionUpdatesCard());
        page.AddItem(MakeControlRow(
            L10n.SettingsTransfer,
            L10n.SettingsTransferHint,
            BuildSettingsTransferControl(),
            318));
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

        var version = ProductVersionInfo.Current;
        var about = new SettingsCard { Size = new Size(570, 144), Margin = new Padding(0, 0, 0, 10) };
        var aboutLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 3,
            Padding = new Padding(18, 14, 18, 14),
            Margin = Padding.Empty
        };
        aboutLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        aboutLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 142f));
        aboutLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34f));
        aboutLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        aboutLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
        var aboutTitle = MakeDockLabel(L10n.AboutThisApp,
            UiPalette.Body(9f, FontStyle.Bold), UiPalette.Text);
        aboutTitle.Padding = new Padding(0, 0, 0, 4);
        aboutLayout.Controls.Add(aboutTitle, 0, 0);
        var sourceText = _snapshot is null
            ? L10n.Pick("数据源 · 等待连接", "Source · Waiting for connection")
            : L10n.Pick(
                $"数据源 · {L10n.SourceName(_snapshot.Source)} · 更新于 {_snapshot.ObservedAt.ToLocalTime():HH:mm:ss}",
                $"Source · {L10n.SourceName(_snapshot.Source)} · Updated {_snapshot.ObservedAt.ToLocalTime():HH:mm:ss}");
        var privacyLabel = MakeDockLabel(L10n.LocalPrivacyNote, UiPalette.Body(7.6f), UiPalette.Muted);
        privacyLabel.Padding = new Padding(0, 0, 0, 2);
        aboutLayout.Controls.Add(privacyLabel, 0, 1);
        aboutLayout.SetColumnSpan(privacyLabel, 2);
        var sourceLabel = MakeDockLabel(sourceText, UiPalette.Mono(6.7f, FontStyle.Bold), UiPalette.Faint);
        sourceLabel.Padding = new Padding(0, 0, 0, 3);
        aboutLayout.Controls.Add(sourceLabel, 0, 2);
        aboutLayout.SetColumnSpan(sourceLabel, 2);
        var versionLabel = MakeDockLabel($"{L10n.VersionLabel} {version}",
            UiPalette.Mono(7f, FontStyle.Bold), UiPalette.Mint);
        versionLabel.TextAlign = ContentAlignment.MiddleRight;
        versionLabel.Padding = new Padding(0, 0, 0, 4);
        aboutLayout.Controls.Add(versionLabel, 1, 0);
        about.Controls.Add(aboutLayout);
        page.AddItem(about);
        return page;
    }

    private Control BuildSettingsTransferControl()
    {
        var layout = new TableLayoutPanel
        {
            Size = new Size(304, 38),
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        var import = MakeActionButton(L10n.ImportSettings, 142, primary: false);
        import.Dock = DockStyle.Fill;
        import.Margin = new Padding(0, 3, 5, 3);
        import.Click += (_, _) => ImportSettings();
        layout.Controls.Add(import, 0, 0);

        var export = MakeActionButton(L10n.ExportSettings, 142, primary: false);
        export.Dock = DockStyle.Fill;
        export.Margin = new Padding(5, 3, 0, 3);
        export.Click += (_, _) => ExportSettings();
        layout.Controls.Add(export, 1, 0);
        return layout;
    }

    private Control BuildVersionUpdatesCard()
    {
        const string githubUrl = "https://github.com/yaozhihang2002/CodexQuotaPanel";

        var card = new SettingsCard
        {
            Size = new Size(570, 274),
            Margin = new Padding(0, 0, 0, 10),
            AccessibleName = L10n.VersionAndUpdates
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 7,
            Padding = new Padding(18, 12, 18, 12),
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 13f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32f));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

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
        var version = ProductVersionInfo.Current;
        header.Controls.Add(MakeDockLabel($"{L10n.VersionAndUpdates} · v{version}",
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

        var separator = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.Border,
            Margin = new Padding(0, 6, 0, 6),
            AccessibleRole = AccessibleRole.Separator
        };
        layout.Controls.Add(separator, 0, 3);

        var automaticUpdateHeader = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        automaticUpdateHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        automaticUpdateHeader.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 64f));
        automaticUpdateHeader.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        automaticUpdateHeader.Controls.Add(MakeDockLabel(
            L10n.AutomaticUpdateChecks,
            UiPalette.Body(8.7f, FontStyle.Bold),
            UiPalette.Text), 0, 0);
        _checkUpdatesToggle.Anchor = AnchorStyles.None;
        _checkUpdatesToggle.Margin = Padding.Empty;
        automaticUpdateHeader.Controls.Add(_checkUpdatesToggle, 1, 0);
        layout.Controls.Add(automaticUpdateHeader, 0, 4);

        var updateHint = new ResponsiveTextLabel
        {
            Text = L10n.CheckUpdatesOnStartupHint,
            Dock = DockStyle.Fill,
            AutoSize = false,
            AutoEllipsis = true,
            Font = UiPalette.Body(7.1f),
            ForeColor = UiPalette.Muted,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            TextAlign = ContentAlignment.TopLeft,
            UseCompatibleTextRendering = false
        };
        layout.Controls.Add(updateHint, 0, 5);

        var updateActions = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        updateActions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        updateActions.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 132f));
        updateActions.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        _updateStatusLabel = MakeDockLabel(
            L10n.UpdateNotChecked,
            UiPalette.Body(7.1f, FontStyle.Bold),
            UiPalette.Faint);
        _updateStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        _updateStatusLabel.Margin = new Padding(0, 0, 10, 0);
        updateActions.Controls.Add(_updateStatusLabel, 0, 0);
        _updateCheckButton = MakeActionButton(L10n.CheckNow, 124, primary: false);
        _updateCheckButton.Dock = DockStyle.Fill;
        _updateCheckButton.Margin = new Padding(0, 3, 0, 3);
        _updateCheckButton.Click += async (_, _) => await CheckForUpdatesAsync();
        updateActions.Controls.Add(_updateCheckButton, 1, 0);
        layout.Controls.Add(updateActions, 0, 6);

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
        var link = new BaselineSafeLinkLabel
        {
            Text = $"{caption}  \u00B7  {text}",
            PrefixText = $"{caption}  \u00B7  ",
            ProjectText = text,
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
            Padding = new Padding(0, 2, 0, 2),
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

    private async Task CheckForUpdatesAsync()
    {
        if (_checkingForUpdates || IsDisposed) return;
        var check = CheckForUpdatesRequested;
        if (check is null)
        {
            _updateStatusLabel.Text = L10n.UpdateUnavailable;
            _updateStatusLabel.ForeColor = UiPalette.Amber;
            return;
        }

        _checkingForUpdates = true;
        _updateCheckButton.Enabled = false;
        _updateStatusLabel.Text = L10n.UpdateChecking;
        _updateStatusLabel.ForeColor = UiPalette.Faint;
        try
        {
            var result = await check(_operationLifetime.Token);
            if (IsDisposed || _operationLifetime.IsCancellationRequested) return;
            switch (result.Status)
            {
                case UpdateCheckStatus.UpdateAvailable when
                    result.ReleaseUri is not null && !string.IsNullOrWhiteSpace(result.LatestTag):
                    _updateStatusLabel.Text = L10n.UpdateAvailable(result.LatestTag);
                    _updateStatusLabel.ForeColor = UiPalette.Mint;
                    if (MessageBox.Show(
                            this,
                            L10n.OpenReleasePrompt(result.LatestTag),
                            L10n.CheckForUpdates,
                            MessageBoxButtons.YesNo,
                            MessageBoxIcon.Information,
                            MessageBoxDefaultButton.Button1) == DialogResult.Yes)
                        OpenExternalLink(result.ReleaseUri.AbsoluteUri);
                    break;
                case UpdateCheckStatus.UpToDate:
                    _updateStatusLabel.Text = L10n.UpdateCurrent(result.CurrentVersion);
                    _updateStatusLabel.ForeColor = UiPalette.Mint;
                    break;
                default:
                    _updateStatusLabel.Text = L10n.UpdateUnavailable;
                    _updateStatusLabel.ForeColor = UiPalette.Amber;
                    MessageBox.Show(
                        this,
                        L10n.UpdateUnavailable,
                        L10n.CheckForUpdates,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    break;
            }
        }
        catch (OperationCanceledException) when (_operationLifetime.IsCancellationRequested)
        {
        }
        finally
        {
            _checkingForUpdates = false;
            if (!IsDisposed) _updateCheckButton.Enabled = true;
        }
    }

    private void ImportSettings()
    {
        using var dialog = new OpenFileDialog
        {
            Title = L10n.ImportSettings,
            Filter = L10n.SettingsFileFilter,
            CheckFileExists = true,
            Multiselect = false,
            RestoreDirectory = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!SettingsTransferService.TryImport(
                dialog.FileName,
                _savedPreferences,
                out var imported,
                out var failure))
        {
            ShowSettingsTransferFailure(failure);
            return;
        }

        var previous = _workingPreferences;
        var startupEnabled = StartupEnabled;
        _workingPreferences = imported;
        ApplyPreferencesToControls(_workingPreferences, startupEnabled);
        if (previous.ThemeMode != _workingPreferences.ThemeMode)
        {
            var previousColors = UiPalette.ResolveColors(previous.ThemeMode);
            UiPalette.SetTheme(_workingPreferences.ThemeMode);
            ApplyThemeToOpenForm(previousColors);
        }
        if (previous.Language != _workingPreferences.Language)
        {
            L10n.SetLanguage((AppLanguage)_workingPreferences.Language);
            ApplyLanguageToOpenForm();
        }
        RaisePreview();
        MessageBox.Show(
            this,
            L10n.ImportSettingsSuccess,
            L10n.SettingsTransfer,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ExportSettings()
    {
        UpdateFromDirectControls();
        using var dialog = new SaveFileDialog
        {
            Title = L10n.ExportSettings,
            Filter = L10n.SettingsFileFilter,
            AddExtension = true,
            DefaultExt = "json",
            FileName = $"CodexQuotaPanel-settings-{DateTime.Now:yyyyMMdd}.json",
            OverwritePrompt = true,
            RestoreDirectory = true
        };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        if (!SettingsTransferService.TryExport(dialog.FileName, SelectedPreferences, out var failure))
        {
            ShowSettingsTransferFailure(failure);
            return;
        }
        MessageBox.Show(
            this,
            L10n.ExportSettingsSuccess,
            L10n.SettingsTransfer,
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    private void ShowSettingsTransferFailure(SettingsTransferFailure failure)
    {
        var message = failure switch
        {
            SettingsTransferFailure.TooLarge => L10n.SettingsTransferTooLarge,
            SettingsTransferFailure.UnsupportedVersion => L10n.SettingsTransferUnsupported,
            SettingsTransferFailure.InvalidFormat => L10n.SettingsTransferInvalid,
            _ => L10n.SettingsTransferIoError
        };
        MessageBox.Show(
            this,
            message,
            L10n.SettingsTransfer,
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    internal void SelectPageForTest(int index) => SelectPage(index);
    internal Control SelectedPageForTest =>
        _selectedPageIndex >= 0 && _pages[_selectedPageIndex] is { } page
            ? page
            : throw new InvalidOperationException("No settings page is selected.");
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
                    if (ReferenceEquals(section, _pageHost) &&
                        _selectedPageIndex >= 0 && _pages[_selectedPageIndex] is { } selectedPage)
                    {
                        using var layerGraphics = Graphics.FromImage(layer);
                        layerGraphics.Clear(_pageHost.BackColor);
                        using var pageLayer = new Bitmap(selectedPage.Width, selectedPage.Height);
                        selectedPage.DrawToBitmap(pageLayer,
                            new Rectangle(Point.Empty, selectedPage.Size));
                        layerGraphics.DrawImageUnscaled(pageLayer, selectedPage.Left, selectedPage.Top);
                    }
                    else
                    {
                        section.DrawToBitmap(layer, new Rectangle(Point.Empty, section.Size));
                    }
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
            BackColor = UiPalette.Surface,
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
            Padding = new Padding(16, 8, 16, 8),
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 94f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 140f));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));

        _saveStatusLabel = MakeDockLabel(L10n.SettingsSavedState,
            UiPalette.Body(7.7f, FontStyle.Bold), UiPalette.Faint);
        _saveStatusLabel.TextAlign = ContentAlignment.MiddleLeft;
        // A Label can exceed an absolute TableLayoutPanel cell at high DPI when
        // its translated preferred width grows. A clipping host makes the cell
        // boundary authoritative so status text can never cover the buttons.
        var statusHost = new Panel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        statusHost.Controls.Add(_saveStatusLabel);
        layout.Controls.Add(statusHost, 0, 0);

        var cancel = MakeActionButton(L10n.Cancel, 92, primary: false);
        cancel.Dock = DockStyle.Fill;
        cancel.Margin = new Padding(4, 5, 7, 5);
        cancel.Click += (_, _) => CancelAndClose();
        layout.Controls.Add(cancel, 1, 0);

        _saveButton = MakeActionButton(L10n.Save, 138, primary: true);
        _saveButton.Dock = DockStyle.Fill;
        _saveButton.Margin = new Padding(4, 5, 0, 5);
        _saveButton.Click += (_, _) => SaveAndStayOpen();
        layout.Controls.Add(_saveButton, 2, 0);
        footer.Controls.Add(layout);

        AcceptButton = _saveButton;
        CancelButton = cancel;
        return footer;
    }

    private Control EnsurePageBuilt(int index)
    {
        if (_pages[index] is { } existing) return existing;
        var page = _pageFactories[index]();
        page.Dock = DockStyle.None;
        page.Anchor = AnchorStyles.None;
        page.Visible = false;
        _pages[index] = page;
        _pageHost.Controls.Add(page);

        // Pages are built lazily after the form's automatic DPI pass. WinForms
        // does not retroactively scale those newly created descendants, so give
        // them the monitor DPI exactly once before applying the independent
        // user typography preference.
        if (IsHandleCreated)
        {
            var dpiScale = Math.Max(0.5f, DeviceDpi / 96f);
            if (Math.Abs(dpiScale - 1f) > 0.01f)
                page.Scale(new SizeF(dpiScale, dpiScale));
        }

        if (_appliedLayoutScalePercent != 100)
        {
            UiPalette.ApplyScaledTypography(page, _appliedLayoutScalePercent);
            ApplyCompactTypographyMetrics(page, _appliedLayoutScalePercent);
        }
        if (page is ResponsiveSettingsPage responsivePage)
            responsivePage.CompleteBuild();
        page.Bounds = _pageHost.DisplayRectangle;
        if (index == 1) UpdateOrbPreview();
        return page;
    }

    private void PrewarmNextSettingsPage()
    {
        if (IsDisposed || !Visible)
        {
            _pagePrewarmTimer.Stop();
            return;
        }
        if (_interactiveResize || MouseButtons != MouseButtons.None || !NativeInputIdle.IsIdleFor(700))
            return;

        var index = Enumerable.Range(0, _pages.Count).FirstOrDefault(candidate => _pages[candidate] is null, -1);
        if (index < 0)
        {
            _pagePrewarmTimer.Stop();
            return;
        }

        // WinForms controls must be created on the UI thread. Build exactly one
        // hidden page per idle-like timer slice, after the first visible frame,
        // so opening Settings remains fast and later tab clicks are immediate.
        var elapsed = Stopwatch.StartNew();
        _pageHost.SuspendLayout();
        try
        {
            var page = EnsurePageBuilt(index);
            PrepareHiddenSettingsPage(page);
        }
        finally
        {
            _pageHost.ResumeLayout(performLayout: false);
        }

        elapsed.Stop();
        // Keep each hidden-page preparation well separated. If the user moves
        // the mouse or types, the idle guard postpones the next slice again.
        _pagePrewarmTimer.Interval = elapsed.ElapsedMilliseconds > 100 ? 850 : 620;
    }

    internal void PrewarmAllPagesForTest()
    {
        for (var index = 0; index < _pages.Count; index++)
        {
            if (_pages[index] is not null) continue;
            var page = EnsurePageBuilt(index);
            PrepareHiddenSettingsPage(page);
        }
    }

    internal int BuiltPageCountForTest => _pages.Count(page => page is not null);

    private void PrepareHiddenSettingsPage(Control page)
    {
        page.Visible = false;
        CreateControlTree(page);
        page.PerformLayout();
        if (page.IsHandleCreated && _nativeThemedPages.Add(page)) NativeTheme.Apply(page);

        // A hidden WinForms subtree still defers part of its first Visible=true
        // path. Pay that cost behind the selected opaque page while native
        // redraw is suspended, then return the warmed page to hidden state.
        if (Visible && _selectedPageIndex >= 0 && _pages[_selectedPageIndex] is { } selectedPage &&
            !ReferenceEquals(page, selectedPage))
        {
            using var redraw = NativeRedrawScope.Suspend(_pageHost);
            page.Visible = true;
            page.SendToBack();
            CreateControlTree(page);
            page.PerformLayout();
            page.Visible = false;
            NativeZOrder.BringToFront(selectedPage);
        }

        // One discarded off-screen frame also warms fonts and owner-drawn
        // controls without exposing a partially rendered tab to the user.
        if (page.Width <= 0 || page.Height <= 0) return;
        using var warmFrame = new Bitmap(page.Width, page.Height);
        try
        {
            page.DrawToBitmap(warmFrame, new Rectangle(Point.Empty, page.Size));
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or
                                   System.Runtime.InteropServices.ExternalException or
                                   ArgumentException or InvalidOperationException)
        {
            // Some disconnected RDP sessions reject native-control capture.
            // Handles and layout are still warmed, so fall back without failing.
        }
    }

    private static void CreateControlTree(Control root)
    {
        root.CreateControl();
        foreach (Control child in root.Controls) CreateControlTree(child);
    }

    private void ResizeSelectedPageToViewport()
    {
        if (_pageHost is null || _selectedPageIndex < 0 ||
            _pages[_selectedPageIndex] is not { } selectedPage) return;
        var viewport = _pageHost.DisplayRectangle;
        if (selectedPage.Bounds != viewport) selectedPage.Bounds = viewport;
    }

    private void ResizeSettingsPagesToViewport()
    {
        if (_pageHost is null) return;
        var viewport = _pageHost.DisplayRectangle;
        foreach (var page in _pages)
            if (page is not null && page.Bounds != viewport) page.Bounds = viewport;
    }

    private void SelectPage(int index)
    {
        if (index < 0 || index >= _pages.Count) return;
        if (index == _selectedPageIndex) return;
        var pageWasBuilt = _pages[index] is not null;
        var previousPage = _selectedPageIndex >= 0 && _pages[_selectedPageIndex] is { } previous
            ? previous
            : null;
        Control nextPage;

        // Prepare a newly requested page completely behind the currently visible
        // page. The user keeps seeing the previous stable frame during control
        // construction, scaling and native theming; only the final z-order swap
        // is visible.
        _pageHost.SuspendLayout();
        try
        {
            nextPage = EnsurePageBuilt(index);
            nextPage.Bounds = _pageHost.DisplayRectangle;
            nextPage.Visible = true;
            if (previousPage is { Visible: true }) nextPage.SendToBack();
            if (_orbPreview is not null && index == 1) _orbPreview.Visible = true;
            nextPage.CreateControl();
            if (!pageWasBuilt)
            {
                nextPage.PerformLayout();
                if (nextPage.IsHandleCreated && _nativeThemedPages.Add(nextPage))
                    NativeTheme.Apply(nextPage);
            }
            // Adjust only the native child-window order. WinForms BringToFront
            // runs a layout pass for the complete settings tree; SetWindowPos
            // avoids that cost while still allowing ordinary Windows repainting.
            NativeZOrder.BringToFront(nextPage);
            if (_orbPreview is not null && index != 1) _orbPreview.Hide();
            _selectedPageIndex = index;
            for (var i = 0; i < _navigation.Count; i++) _navigation[i].Active = i == index;
            QueueSelectedPagePaint(nextPage, index);
        }
        finally
        {
            _pageHost.ResumeLayout(performLayout: false);
        }
    }

    private void QueueSelectedPagePaint(Control page, int pageIndex)
    {
        if (!IsHandleCreated || IsDisposed) return;
        var generation = ++_pagePaintGeneration;
        BeginInvoke((Action)(() =>
        {
            if (IsDisposed || generation != _pagePaintGeneration ||
                pageIndex != _selectedPageIndex || page.IsDisposed) return;
            NativeRedrawScope.RedrawNow(page);
            HideInactivePages(page);
        }));
    }

    private void HideInactivePages(Control selectedPage)
    {
        foreach (var page in _pages)
            if (page is not null && !ReferenceEquals(page, selectedPage) && page.Visible)
                page.Visible = false;
    }

    protected override bool ProcessTabKey(bool forward)
    {
        var candidates = EnumerateActiveTabStops(_rootLayout).ToArray();
        if (candidates.Length == 0) return base.ProcessTabKey(forward);
        var current = Array.FindIndex(candidates, control => control.Focused || control.ContainsFocus);
        var next = current < 0
            ? forward ? 0 : candidates.Length - 1
            : (current + (forward ? 1 : -1) + candidates.Length) % candidates.Length;
        return candidates[next].Focus() || base.ProcessTabKey(forward);
    }

    private IEnumerable<Control> EnumerateActiveTabStops(Control root)
    {
        var children = root.Controls.Cast<Control>().OrderBy(control => control.TabIndex);
        foreach (var child in children)
        {
            if (_pages.Contains(child) && !ReferenceEquals(child, SelectedPageForTest)) continue;
            if (child.TabStop && child.CanSelect) yield return child;
            foreach (var descendant in EnumerateActiveTabStops(child))
                yield return descendant;
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

    internal static int ScalePreviewPixels(int logicalPixels, int dpi) =>
        Math.Max(1, (int)Math.Round(logicalPixels * Math.Max(48, dpi) / 96d));

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

    private static Control MakePageIntro(string title, string subtitle)
    {
        var layout = new BufferedTableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(570, 0),
            Size = new Size(570, 74),
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 3,
            Margin = new Padding(0, 0, 0, 14),
            Padding = new Padding(4, 3, 2, 0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 15f));
        var titleLabel = MakeDockLabel(title,
            UiPalette.Display(12.2f, FontStyle.Bold), UiPalette.Text);
        titleLabel.AutoSize = true;
        titleLabel.Margin = new Padding(0, 0, 0, 5);
        titleLabel.TextAlign = ContentAlignment.BottomLeft;
        layout.Controls.Add(titleLabel, 0, 0);
        var subtitleLabel = MakeDockLabel(subtitle,
            UiPalette.Body(7.4f), UiPalette.Muted);
        subtitleLabel.AutoSize = true;
        subtitleLabel.Margin = new Padding(0, 1, 0, 0);
        subtitleLabel.TextAlign = ContentAlignment.TopLeft;
        layout.Controls.Add(subtitleLabel, 0, 1);
        layout.Controls.Add(new Panel
        {
            Dock = DockStyle.Bottom,
            Height = 1,
            Margin = new Padding(0, 12, 0, 0),
            BackColor = UiPalette.Border
        }, 0, 2);
        return layout;
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
        var height = Math.Max(84, rightControl.Height + 24);
        var card = new SettingsCard
        {
            Size = new Size(570, height),
            Margin = new Padding(0, 0, 0, 9)
        };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(16, 11, 16, 11),
            Margin = Padding.Empty
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, rightColumnWidth));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        layout.Controls.Add(MakeTextBlock(title, hint, height - 22), 0, 0);
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
        text.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        text.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        var titleLabel = MakeDockLabel(title,
            UiPalette.Body(8.5f, FontStyle.Bold), UiPalette.Text);
        titleLabel.AutoSize = true;
        titleLabel.Margin = new Padding(0, 0, 0, 5);
        titleLabel.TextAlign = ContentAlignment.BottomLeft;
        text.Controls.Add(titleLabel, 0, 0);
        var hintLabel = MakeDockLabel(hint, UiPalette.Body(7f), UiPalette.Muted);
        hintLabel.Margin = new Padding(0, 2, 0, 0);
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

    private static ComboBox MakeCombo() => new ThemedComboBox
    {
        Size = new Size(176, 34),
        BackColor = UiPalette.SurfaceRaised,
        ForeColor = UiPalette.Text,
        Font = UiPalette.Body(8f),
        DropDownHeight = 160
    };

    private static Control BuildChoiceSelector(ComboBox model, int columns)
    {
        columns = Math.Clamp(columns, 1, Math.Max(1, model.Items.Count));
        var rows = Math.Max(1, (int)Math.Ceiling(model.Items.Count / (double)columns));
        var host = new Panel
        {
            Size = new Size(320, rows * 38),
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        var choices = new BufferedTableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = columns,
            RowCount = rows,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        for (var column = 0; column < columns; column++)
            choices.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
        for (var row = 0; row < rows; row++)
            choices.RowStyles.Add(new RowStyle(SizeType.Percent, 100f / rows));

        var buttons = new List<SettingsChoiceButton>();
        for (var index = 0; index < model.Items.Count; index++)
        {
            var choiceIndex = index;
            var button = new SettingsChoiceButton
            {
                Text = model.Items[index]?.ToString() ?? string.Empty,
                AccessibleName = model.Items[index]?.ToString() ?? string.Empty,
                Dock = DockStyle.Fill,
                Margin = new Padding(3)
            };
            button.Click += (_, _) => model.SelectedIndex = choiceIndex;
            buttons.Add(button);
            choices.Controls.Add(button, index % columns, index / columns);
        }

        void SynchronizeSelection()
        {
            for (var index = 0; index < buttons.Count; index++)
            {
                buttons[index].Active = model.SelectedIndex == index;
                buttons[index].Enabled = model.Enabled;
            }
        }

        model.SelectedIndexChanged += (_, _) => SynchronizeSelection();
        model.EnabledChanged += (_, _) => SynchronizeSelection();
        model.Visible = false;
        model.TabStop = false;
        host.Controls.Add(model);
        host.Controls.Add(choices);
        choices.BringToFront();
        SynchronizeSelection();
        return host;
    }

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

}
