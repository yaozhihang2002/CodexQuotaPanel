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
internal sealed partial class SettingsForm : Form
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
    private readonly SettingsWheelMessageFilter _sizeEditorWheelFilter;
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
        _orbSizeInput = new WheelSafeNumericUpDown
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
        _fontScaleInput = new WheelSafeNumericUpDown
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
        _sizeEditorWheelFilter = new SettingsWheelMessageFilter(
            _orbSizeSlider, _orbSizeInput, _fontScaleSlider, _fontScaleInput);
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
        Shown += (_, _) =>
        {
            _sizeEditorWheelFilter.Install();
            _pagePrewarmTimer.Start();
        };
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
            _sizeEditorWheelFilter.Dispose();
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

    internal void SimulateSizeEditorsMouseWheelForTest(int delta)
    {
        _orbSizeSlider.SimulateMouseWheelForTest(delta);
        ((WheelSafeNumericUpDown)_orbSizeInput).SimulateMouseWheelForTest(delta);
        _fontScaleSlider.SimulateMouseWheelForTest(delta);
        ((WheelSafeNumericUpDown)_fontScaleInput).SimulateMouseWheelForTest(delta);
    }

    internal bool SimulateNativeSizeEditorMouseWheelForTest(int delta)
    {
        var numericChild = _orbSizeInput.Controls.Cast<Control>().FirstOrDefault() ?? _orbSizeInput;
        return _sizeEditorWheelFilter.SimulateForTest(numericChild, delta);
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
