using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CodexQuotaPanel;

internal sealed partial class QuotaForm : Form
{
    private static readonly Size SingleWindowPanelSize = new(368, 518);
    private static readonly Size DualWindowPanelSize = new(368, 596);
    private const int DefaultOrbLogicalSize = PanelPreferenceManager.DefaultOrbSize;
    private const int SnapThresholdLogicalPixels = 12;
    private const int TransitionDurationMs = 300;
    private const int TransitionTimerIntervalMs = 10;
    private const double TransitionOrbPhase = 0.22d;
    private const int OrbResizePreviewDurationMs = 110;
    private const int WsExTransparent = 0x00000020;
    private const int WsExLayered = 0x00080000;
    private const int WsExNoActivate = 0x08000000;
    private const int GwlExStyle = -20;
    private const int WmNcHitTest = 0x0084;
    private const int WmNcLeftButtonDown = 0x00A1;
    private const int WmSettingChange = 0x001A;
    private const int WmDisplayChange = 0x007E;
    private const int WmHotKey = 0x0312;
    private const uint LwaAlpha = 0x00000002;
    private const string PinGlyph = "\uE718";
    private const string UnpinGlyph = "\uE77A";
    private const string CollapseGlyph = "\uE70D";
    private static readonly IntPtr HtTransparent = new(-1);
    private const int HtCaption = 0x0002;

    public const int OrbViewState = 0;
    public const int DetailsViewState = 1;
    public const int HiddenViewState = 2;

    private readonly Label _planLabel;
    private readonly Label _brandLabel;
    private readonly Label _subtitleLabel;
    private readonly Label _heroLabel;
    private readonly Label _heroValue;
    private readonly Label _nextResetLabel;
    private readonly Label _freshnessLabel;
    private readonly Label _statusLabel;
    private readonly Label _creditsLabel;
    private readonly PillLabel _sourcePill;
    private readonly QuotaRingControl _ring;
    private readonly LimitRowControl _primaryRow;
    private readonly LimitRowControl _secondaryRow;
    private readonly DailyTokenUsageControl _dailyTokenUsage;
    private readonly Button _pinButton;
    private readonly Button _hideButton;
    private readonly Button _closeButton;
    private readonly Button _refreshButton;
    private readonly Label _sectionTitle;
    private readonly QuotaOrbControl _orb;
    private readonly HoverPeekForm _hoverPeek;
    private readonly System.Windows.Forms.Timer _clock;
    private readonly UiAnimationTimer _transition;
    private readonly System.Windows.Forms.Timer _orbResizePreview;
    private readonly System.Windows.Forms.Timer _hoverTimer;
    private readonly ToolTip _toolTip;
    private readonly Dictionary<Control, Rectangle> _detailLogicalBounds = new();
    private QuotaSnapshot? _snapshot;
    private string? _lastStatus;
    private IReadOnlyList<QuotaHistoryPoint> _history = [];
    private RingDisplayConfiguration _ringConfiguration = new(
        new RingWindowSelection(300, RingWindowRole.Primary),
        new RingWindowSelection(10080, RingWindowRole.Secondary),
        UiPalette.Mint,
        UiPalette.Sky);
    private bool _allowClose;
    private bool _collapsed;
    private bool _animating;
    private bool _transitionExpanding;
    private bool _orbDragged;
    private bool _orbClickThrough;
    private bool _positionLocked;
    private bool _snapToEdge;
    private bool _hoverPreviewEnabled = true;
    private bool _consumptionFlameEnabled = true;
    private bool _applyingLanguage;
    private bool _hasRestoredOrbLocation;
    private int _orbLogicalSize = DefaultOrbLogicalSize;
    private int _orbOpacityPercent = 100;
    private int _viewState = OrbViewState;
    private long _transitionStartedAt;
    private long _transitionPreparationMs;
    private long _transitionLastPaintAt;
    private long _transitionMaxPaintGapMs;
    private long _lastTransitionDurationMs;
    private int _transitionPaintFrames;
    private bool _transitionMetricsActive;
    private bool _highResolutionTimerActive;
    private double _transitionShapeProgress;
    private double _transitionOrbScale;
    private Bitmap? _transitionPreview;
    private Bitmap? _transitionOrbPreview;
    private Bitmap? _cachedExpandedPreview;
    private LayeredTransitionOverlay? _transitionOverlay;
    private bool _transitionPreviewCacheDirty = true;
    private bool _transitionPreviewRefreshQueued;
    private PointF _transitionAnchor;
    private Rectangle _transitionFrom;
    private Rectangle _transitionTo;
    private long _orbResizePreviewStartedAt;
    private Rectangle _orbResizePreviewFrom;
    private Rectangle _orbResizePreviewTo;
    private Rectangle _collapsedBounds;
    private Rectangle _expandedBounds;
    private Point? _orbReturnLocation;
    private Point _orbDragStartScreen;
    private int _detailLayoutDpi;
    private int _availableWindowCount;
    private bool _alwaysOnTopPreference = true;

    private Size ExpandedPanelSize => _availableWindowCount >= 2
        ? DualWindowPanelSize
        : SingleWindowPanelSize;

    public event Action? RefreshRequested;
    public event Action<bool>? TopMostChangedByUser;
    public event Action<Point>? OrbPositionChanged;
    public event Action<int>? ViewStateChanged;
    public event Action? GlobalHotKeyPressed;

    internal bool IsCollapsed => _collapsed;
    internal bool IsOrb => _viewState == OrbViewState;
    internal bool IsDetails => _viewState == DetailsViewState;
    internal bool IsHidden => _viewState == HiddenViewState;
    internal int ViewState => _viewState;
    internal bool IsAnimating => _animating;
    internal long TransitionPreparationMs => _transitionPreparationMs;
    internal long TransitionMaxPaintGapMs => _transitionMaxPaintGapMs;
    internal long LastTransitionDurationMs => _lastTransitionDurationMs;
    internal int TransitionPaintFrames => _transitionPaintFrames;
    internal (bool NativeVisible, int NonTransparentPixels, byte MaximumAlpha)? InspectTransitionOverlay() =>
        _transitionOverlay?.InspectFrame();
    internal Bitmap? CaptureTransitionOverlayFrame() => _transitionOverlay?.CaptureFrame();
    internal Rectangle OrbBounds => _orb.Bounds;
    internal bool IsClickThroughActive => _orbClickThrough && _collapsed && !_animating;
    internal bool HasClickThroughWindowStyle
    {
        get
        {
            if (!IsHandleCreated) return false;
            var style = GetWindowLong(Handle, GwlExStyle);
            var required = WsExTransparent | WsExLayered | WsExNoActivate;
            return (style & required) == required;
        }
    }
    internal int OrbOpacityPercent => _orbOpacityPercent;
    internal string BrandText => _brandLabel.Text;
    internal string SectionText => _sectionTitle.Text;
    internal string StatusText => _statusLabel.Text;
    internal string SourceText => _sourcePill.Text;
    internal string CreditsText => _creditsLabel.Text;
    internal QuotaOrbControl OrbControl => _orb;
    internal bool HoverPreviewEnabled => _hoverPreviewEnabled;
    internal bool ConsumptionFlameEnabled => _consumptionFlameEnabled;
    internal double ConsumptionIntensity { get; private set; }
    internal int OrbLogicalSize => _orbLogicalSize;
    internal int OrbPixelSize => ScaledOrbSize().Width;
    internal bool PositionLocked => _positionLocked;
    internal bool SnapToEdge => _snapToEdge;
    internal int OrbSnapThresholdPixels => ScaleLogicalPixels(SnapThresholdLogicalPixels);
    internal byte? NativeLayeredAlpha
    {
        get
        {
            if (!IsHandleCreated || !GetLayeredWindowAttributes(Handle, out _, out var alpha, out var flags))
                return null;
            return (flags & LwaAlpha) != 0 ? alpha : null;
        }
    }

    public QuotaForm()
    {
        AutoScaleDimensions = new SizeF(96f, 96f);
        // The orb and detail panel share one top-level window whose client size
        // changes radically. WinForms automatic DPI scaling can therefore size
        // the expanded window without scaling its absolute-positioned children.
        // Keep this form on one explicit logical-to-device layout path instead.
        AutoScaleMode = AutoScaleMode.None;
        ClientSize = SingleWindowPanelSize;
        FormBorderStyle = FormBorderStyle.None;
        BackColor = UiPalette.Canvas;
        ForeColor = UiPalette.Text;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        DoubleBuffered = true;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        UpdateStyles();
        KeyPreview = true;
        Text = L10n.AppTitle;
        AccessibleName = L10n.AppAccessible;
        _toolTip = new ToolTip { InitialDelay = 350, ReshowDelay = 100, AutoPopDelay = 5000, ShowAlways = true };

        _brandLabel = MakeLabel(L10n.Brand, new Point(18, 12), new Size(180, 26),
            UiPalette.Display(13f, FontStyle.Bold), UiPalette.Text);
        _brandLabel.MouseDown += DragWindow;
        Controls.Add(_brandLabel);

        _subtitleLabel = MakeLabel(L10n.LiveRateLimits, new Point(19, 39), new Size(165, 16),
            UiPalette.Mono(7.4f, FontStyle.Bold), UiPalette.Faint);
        _subtitleLabel.MouseDown += DragWindow;
        Controls.Add(_subtitleLabel);

        _sourcePill = new PillLabel
        {
            Text = L10n.ConnectingBadge,
            Location = new Point(218, 14),
            Size = new Size(78, 23),
            PillColor = UiPalette.Amber
        };
        Controls.Add(_sourcePill);

        _pinButton = MakeHeaderButton(PinGlyph, new Point(298, 10), L10n.AlwaysOnTop);
        _pinButton.Font = new Font("Segoe MDL2 Assets", 10f, FontStyle.Regular, GraphicsUnit.Point);
        _pinButton.ForeColor = UiPalette.Mint;
        _toolTip.SetToolTip(_pinButton, L10n.AlwaysOnTop);
        _pinButton.Click += (_, _) =>
        {
            var next = !_alwaysOnTopPreference;
            SetTopMostPreference(next);
            TopMostChangedByUser?.Invoke(next);
        };
        Controls.Add(_pinButton);

        _closeButton = MakeHeaderButton(CollapseGlyph, new Point(331, 10), L10n.CollapseOrb);
        _closeButton.Font = new Font("Segoe MDL2 Assets", 10f, FontStyle.Regular, GraphicsUnit.Point);
        _toolTip.SetToolTip(_closeButton, L10n.CollapseOrb);
        _closeButton.Click += (_, _) => CollapseToOrb();
        Controls.Add(_closeButton);

        var divider = new Panel
        {
            Location = new Point(18, 55),
            Size = new Size(332, 1),
            BackColor = UiPalette.Border
        };
        Controls.Add(divider);

        _ring = new QuotaRingControl { Location = new Point(20, 70) };
        Controls.Add(_ring);

        _planLabel = MakeLabel($"— {L10n.PlanSuffix}", new Point(154, 77), new Size(196, 18),
            UiPalette.Mono(8.2f, FontStyle.Bold), UiPalette.Mint);
        Controls.Add(_planLabel);

        _heroLabel = MakeLabel(L10n.TightestWindow, new Point(154, 101), new Size(196, 21),
            UiPalette.Body(9.3f, FontStyle.Bold), UiPalette.Muted);
        Controls.Add(_heroLabel);

        _heroValue = MakeLabel(L10n.WaitingData, new Point(152, 122), new Size(200, 40),
            UiPalette.Display(L10n.IsChinese ? 22.5f : 18f, FontStyle.Bold), UiPalette.Text);
        Controls.Add(_heroValue);

        _nextResetLabel = MakeLabel(L10n.WaitingQuotaEvent, new Point(154, 164), new Size(198, 38),
            UiPalette.Body(8.3f), UiPalette.Muted);
        Controls.Add(_nextResetLabel);

        _sectionTitle = MakeLabel(L10n.WindowSection, new Point(18, 205), new Size(140, 16),
            UiPalette.Mono(7.8f, FontStyle.Bold), UiPalette.Faint);
        Controls.Add(_sectionTitle);

        _primaryRow = new LimitRowControl { Location = new Point(18, 224), Width = 332, Height = 70, HistorySlot = 0 };
        _secondaryRow = new LimitRowControl { Location = new Point(18, 224), Width = 332, Height = 70, HistorySlot = 1, Visible = false };
        _primaryRow.SetBucket(null);
        _secondaryRow.SetBucket(null);
        Controls.Add(_primaryRow);
        Controls.Add(_secondaryRow);

        _dailyTokenUsage = new DailyTokenUsageControl
        {
            Location = new Point(18, 302),
            Width = 332,
            Height = 96
        };
        _dailyTokenUsage.DetailsRequested += ShowTokenUsageDetails;
        Controls.Add(_dailyTokenUsage);

        _creditsLabel = MakeLabel($"{L10n.Credits} · —", new Point(19, 406), new Size(331, 19),
            UiPalette.Mono(8f, FontStyle.Bold), UiPalette.Muted);
        Controls.Add(_creditsLabel);

        _statusLabel = MakeLabel(L10n.Connecting, new Point(19, 430), new Size(331, 18),
            UiPalette.Body(8.3f), UiPalette.Muted);
        Controls.Add(_statusLabel);

        _freshnessLabel = MakeLabel(L10n.NoSnapshot, new Point(19, 452), new Size(331, 17),
            UiPalette.Mono(7.2f), UiPalette.Faint);
        Controls.Add(_freshnessLabel);

        _refreshButton = MakeActionButton(L10n.Refresh, new Point(18, 480), new Size(84, 28));
        _refreshButton.Click += (_, _) => RefreshRequested?.Invoke();
        _refreshButton.AccessibleName = L10n.RefreshNow;
        _toolTip.SetToolTip(_refreshButton, L10n.RefreshNow);
        Controls.Add(_refreshButton);

        _hideButton = MakeActionButton(L10n.CollapseOrb, new Point(108, 480), new Size(242, 28), true);
        _hideButton.Click += (_, _) => CollapseToOrb();
        _hideButton.AccessibleName = L10n.CollapseOrb;
        Controls.Add(_hideButton);

        _orb = new QuotaOrbControl { Location = Point.Empty, Visible = false };
        _orb.MouseDown += OrbMouseDown;
        _orb.MouseMove += OrbMouseMove;
        _orb.MouseUp += OrbMouseUp;
        _orb.MouseCaptureChanged += (_, _) =>
        {
            if (!_orb.Capture) _orb.SetAnimationPaused(false);
        };
        _orb.KeyDown += OrbKeyDown;
        _orb.MouseEnter += (_, _) => ScheduleHoverPreview();
        _orb.MouseLeave += (_, _) => HideHoverPreview();
        Controls.Add(_orb);
        _orb.BringToFront();

        _hoverPeek = new HoverPeekForm();

        MouseDown += DragWindow;
        FormClosing += OnFormClosing;
        Shown += OnFormShown;

        _clock = new System.Windows.Forms.Timer { Interval = 1000 };
        _clock.Tick += (_, _) => TickDisplay();
        _clock.Start();

        _transition = new UiAnimationTimer(this, TransitionTimerIntervalMs,
            () => AnimateTransition(null, EventArgs.Empty));
        _orbResizePreview = new System.Windows.Forms.Timer { Interval = 15 };
        _orbResizePreview.Tick += AnimateOrbResizePreview;
        _hoverTimer = new System.Windows.Forms.Timer { Interval = 400 };
        _hoverTimer.Tick += (_, _) => ShowHoverPreview();
        CaptureDetailLogicalBounds();
        ApplyDetailLayoutForCurrentDpi();
        SetCollapsedInstant();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            if (IsClickThroughActive)
                parameters.ExStyle |= WsExTransparent | WsExLayered | WsExNoActivate;
            else
                parameters.ExStyle &= ~(WsExTransparent | WsExNoActivate);
            return parameters;
        }
    }

    protected override bool ShowWithoutActivation => _collapsed;

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyDetailLayoutForCurrentDpi(force: true);
        // Windows can recreate the native handle after DPI, display-session or
        // remote-desktop changes. TopMost is a native z-order state, so restore
        // the saved preference after the replacement handle is fully attached.
        try { BeginInvoke(ReassertTopMostPreference); }
        catch (InvalidOperationException) { }
    }

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmHotKey)
        {
            GlobalHotKeyPressed?.Invoke();
            message.Result = IntPtr.Zero;
            return;
        }
        if (message.Msg == WmNcHitTest && IsClickThroughActive)
        {
            message.Result = HtTransparent;
            return;
        }
        base.WndProc(ref message);

        if (message.Msg is WmDisplayChange or WmSettingChange)
            QueueEnsureVisible();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Escape &&
            (_viewState == DetailsViewState || (_animating && _transitionExpanding)))
        {
            CollapseToOrb();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        if (_collapsed && !_animating) return;
        if (_animating && _transitionPreview is not null)
        {
            if (_transitionMetricsActive && _transitionOverlay is null)
                RecordTransitionFrame();
            DrawGeniePreview(e.Graphics, _transitionPreview, _transitionAnchor, _transitionShapeProgress);
            if (_transitionOrbPreview is not null)
                DrawTransitionOrbPreview(e.Graphics, _transitionOrbPreview, _transitionAnchor, _transitionOrbScale);
            return;
        }
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var border = new Pen(UiPalette.Border, 1);
        using var path = UiPalette.RoundedRect(new RectangleF(0.5f, 0.5f, ClientSize.Width - 1, ClientSize.Height - 1), 16);
        e.Graphics.DrawPath(border, path);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        if (_collapsed && !_animating)
        {
            e.Graphics.Clear(_orb.WindowBackdropColor);
            return;
        }
        using var gradient = new LinearGradientBrush(
            ClientRectangle,
            UiPalette.SurfaceRaised,
            UiPalette.Canvas,
            LinearGradientMode.Vertical);
        e.Graphics.FillRectangle(gradient, ClientRectangle);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var glow = new SolidBrush(Color.FromArgb(10, UiPalette.Mint));
        e.Graphics.FillEllipse(glow, Width - 145, -95, 210, 175);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        if (_orb is not null)
        {
            if (_collapsed && !_animating)
            {
                _orb.Bounds = ClientRectangle;
            }
            else
            {
                var orbSize = ScaledOrbSize();
                _orb.Size = orbSize;
                _orb.Location = new Point(Math.Max(0, ClientSize.Width - orbSize.Width),
                    Math.Max(0, ClientSize.Height - orbSize.Height));
            }
        }
        UpdateRegion();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        if (_orb is not null)
            _orb.SetFlameAnimationEnabled(_consumptionFlameEnabled && Visible);
        if (Visible && IsHandleCreated)
        {
            try { BeginInvoke(ReassertTopMostPreference); }
            catch (InvalidOperationException) { }
        }
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);
        ApplyDetailLayoutForCurrentDpi(force: true);
        if (_collapsed && !_animating)
        {
            var previousLocation = Location;
            NormalizeCollapsedGeometry(ClampOrbLocation(e.SuggestedRectangle.Location));
            ApplyOrbPresentation();
            UpdateRegion();
            if (previousLocation != Location) OrbPositionChanged?.Invoke(Location);
        }
        else if (!_animating)
        {
            var previousOrbLocation = _collapsedBounds.Location;
            Bounds = ClampToWorkingArea(new Rectangle(Location, ScaledSize(ExpandedPanelSize)));
            _expandedBounds = Bounds;
            NormalizeStoredCollapsedBounds();
            if (previousOrbLocation != _collapsedBounds.Location)
                OrbPositionChanged?.Invoke(_collapsedBounds.Location);
        }
    }

    public void SetSharedContextMenu(ContextMenuStrip menu)
    {
        AssignContextMenu(this, menu);
    }

    private void TickDisplay()
    {
        _primaryRow.Tick();
        _secondaryRow.Tick();
        if (_snapshot is null)
        {
            _freshnessLabel.Text = L10n.NoSnapshotLong;
            return;
        }

        var age = DateTimeOffset.Now - _snapshot.ObservedAt;
        var ageText = L10n.FormatAge(_snapshot.ObservedAt);
        _freshnessLabel.Text = L10n.Pick(
            $"快照 {ageText} · {_snapshot.ObservedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}",
            $"Snapshot {ageText} · {_snapshot.ObservedAt.ToLocalTime():yyyy-MM-dd HH:mm:ss}");
        _freshnessLabel.ForeColor = age.TotalMinutes > 15 ? UiPalette.Amber : UiPalette.Faint;
        UpdateRunwayInsight();
        if (_hoverPeek.Visible) _hoverPeek.SetData(_snapshot, _ringConfiguration);
    }

    private void UpdateCredits(CreditInfo? credits)
    {
        if (credits?.Unlimited == true)
        {
            _creditsLabel.Text = $"{L10n.Credits} · ∞";
            _creditsLabel.ForeColor = UiPalette.Mint;
        }
        else if (!string.IsNullOrWhiteSpace(credits?.Balance))
        {
            _creditsLabel.Text = $"{L10n.Credits} · {credits.Balance}";
            _creditsLabel.ForeColor = UiPalette.Text;
        }
        else
        {
            _creditsLabel.Text = $"{L10n.Credits} · —";
            _creditsLabel.ForeColor = UiPalette.Muted;
        }
    }

    private void UpdateHistoryRows()
    {
        var primaryColor = TrendColorFor(_snapshot?.Primary, RingWindowRole.Primary);
        var secondaryColor = TrendColorFor(_snapshot?.Secondary, RingWindowRole.Secondary);
        _primaryRow.SetHistory(_history, primaryColor);
        _secondaryRow.SetHistory(_history, secondaryColor);
    }

    private Color TrendColorFor(LimitBucket? bucket, RingWindowRole role)
    {
        if (_ringConfiguration.Outer.Role == role)
            return _ringConfiguration.OuterColor;
        if (_ringConfiguration.Inner.Role == role)
            return _ringConfiguration.InnerColor;
        if (bucket?.WindowMinutes == _ringConfiguration.Outer.WindowMinutes &&
            (_ringConfiguration.Outer.Role == role ||
             _snapshot?.Buckets.Count(item => item.WindowMinutes == bucket.WindowMinutes) == 1))
            return _ringConfiguration.OuterColor;
        if (bucket?.WindowMinutes == _ringConfiguration.Inner.WindowMinutes &&
            (_ringConfiguration.Inner.Role == role ||
             _snapshot?.Buckets.Count(item => item.WindowMinutes == bucket.WindowMinutes) == 1))
            return _ringConfiguration.InnerColor;
        return bucket is null ? UiPalette.Muted : UiPalette.ForRemaining(bucket.RemainingPercent);
    }

    private void PositionAtWorkingAreaEdge()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        var margin = ScaleLogicalPixels(20);
        Bounds = ClampToArea(new Rectangle(
            area.Right - Width - margin,
            area.Bottom - Height - margin,
            Width,
            Height), area);
    }

    private void OnFormShown(object? sender, EventArgs e)
    {
        if (_collapsed && !_animating)
        {
            NormalizeCollapsedGeometry(Location);
            if (!_hasRestoredOrbLocation)
                PositionAtWorkingAreaEdge();
            _collapsedBounds = Bounds;
        }
        else if (!_animating)
        {
            Bounds = ClampToWorkingArea(Bounds);
            _expandedBounds = Bounds;
        }
        if (_snapshot is not null) QueueTransitionPreviewCacheRefresh();
        ReassertTopMostPreference();
    }

    private void UpdateRegion()
    {
        var client = ClientRectangle;
        using var path = new GraphicsPath();
        if (_collapsed && !_animating)
            path.AddEllipse(new RectangleF(client.X, client.Y, client.Width, client.Height));
        else if (_animating)
        {
            if (_transitionShapeProgress > 0.012d)
            {
                using var genie = CreateGeniePath(ClientSize, _transitionAnchor, _transitionShapeProgress);
                path.AddPath(genie, connect: false);
            }
            if (_transitionOrbPreview is not null && _transitionOrbScale > 0.015d)
            {
                var width = Math.Max(1f, (float)(_transitionOrbPreview.Width * _transitionOrbScale));
                var height = Math.Max(1f, (float)(_transitionOrbPreview.Height * _transitionOrbScale));
                path.AddEllipse(
                    _transitionAnchor.X - width / 2f,
                    _transitionAnchor.Y - height / 2f,
                    width,
                    height);
            }
        }
        else
        {
            using var rounded = UiPalette.RoundedRect(
                new RectangleF(client.X, client.Y, client.Width, client.Height),
                16f);
            path.AddPath(rounded, connect: false);
        }
        Region?.Dispose();
        Region = new Region(path);
    }

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose || e.CloseReason != CloseReason.UserClosing) return;
        e.Cancel = true;
        CollapseToOrb();
    }

    private void OrbMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_collapsed || _animating) return;
        if (!_positionLocked)
        {
            RunNativeOrbDrag();
            return;
        }

        HideHoverPreview();
        _orbDragStartScreen = Cursor.Position;
        _orbDragged = false;
        _orb.SetAnimationPaused(true);
        _orb.Capture = true;
    }

    private void OrbMouseMove(object? sender, MouseEventArgs e)
    {
        if (!_orb.Capture || e.Button != MouseButtons.Left || !_collapsed || _animating) return;
        var delta = new Size(Cursor.Position.X - _orbDragStartScreen.X, Cursor.Position.Y - _orbDragStartScreen.Y);
        var dragSize = SystemInformation.DragSize;
        if (!_orbDragged && Math.Abs(delta.Width) < dragSize.Width / 2 && Math.Abs(delta.Height) < dragSize.Height / 2)
            return;

        _orbDragged = true;
    }

    private void OrbMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var shouldExpand = _orb.Capture && !_orbDragged && _collapsed && !_animating;
        _orb.Capture = false;
        _orb.SetAnimationPaused(false);
        if (shouldExpand) ShowDetails();
    }

    private void RunNativeOrbDrag()
    {
        HideHoverPreview();
        var startCursor = Cursor.Position;
        var startLocation = Location;
        var bypassSnap = (ModifierKeys & Keys.Shift) == Keys.Shift;
        _orbDragged = false;
        _orb.SetAnimationPaused(true);
        try
        {
            // Let Windows own the modal move loop. DWM can then schedule the
            // top-level window at the monitor refresh cadence instead of making
            // the UI thread process hundreds of managed MouseMove callbacks.
            ReleaseCapture();
            SendMessage(Handle, WmNcLeftButtonDown, HtCaption, 0);

            var cursorDelta = new Size(
                Cursor.Position.X - startCursor.X,
                Cursor.Position.Y - startCursor.Y);
            var locationDelta = new Size(
                Location.X - startLocation.X,
                Location.Y - startLocation.Y);
            var dragSize = SystemInformation.DragSize;
            _orbDragged = IsOrbDragGesture(cursorDelta, locationDelta, dragSize);
            if (!_orbDragged)
            {
                ShowDetails();
                return;
            }

            bypassSnap |= (ModifierKeys & Keys.Shift) == Keys.Shift;
            var releasedLocation = ResolveReleasedOrbLocation(Location, bypassSnap);
            if (releasedLocation != Location) Location = releasedLocation;
            _collapsedBounds = Bounds;
            if (startLocation != Location) OrbPositionChanged?.Invoke(Location);
        }
        finally
        {
            _orb.SetAnimationPaused(false);
        }
    }

    private void OrbKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is not (Keys.Enter or Keys.Space)) return;
        e.Handled = true;
        e.SuppressKeyPress = true;
        ShowDetails();
    }

    private void ScheduleHoverPreview()
    {
        if (!CanShowHoverPreview()) return;
        _hoverTimer.Stop();
        _hoverTimer.Start();
    }

    private void ShowHoverPreview()
    {
        _hoverTimer.Stop();
        if (!CanShowHoverPreview() || _snapshot is null) return;
        _hoverPeek.SetData(_snapshot, _ringConfiguration);
        _hoverPeek.ShowNear(Bounds, TopMost);
    }

    private void HideHoverPreview()
    {
        _hoverTimer?.Stop();
        if (_hoverPeek is not null && !_hoverPeek.IsDisposed) _hoverPeek.Hide();
    }

    private bool CanShowHoverPreview() =>
        _hoverPreviewEnabled && _collapsed && !_animating && !_orbClickThrough && _snapshot is not null;

    private static string FormatPlan(string? plan) => string.IsNullOrWhiteSpace(plan)
        ? "CODEX"
        : plan.Replace('_', ' ').ToUpperInvariant();

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

    private static Button MakeHeaderButton(string text, Point location, string accessibleName) => new()
    {
        Text = text,
        Location = location,
        Size = new Size(31, 31),
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.Transparent,
        ForeColor = UiPalette.Muted,
        Font = UiPalette.Body(12, FontStyle.Bold),
        Cursor = Cursors.Hand,
        TabStop = true,
        AccessibleName = accessibleName,
        FlatAppearance = { BorderSize = 0, MouseOverBackColor = UiPalette.SurfaceRaised, MouseDownBackColor = UiPalette.Surface }
    };

    private static Button MakeActionButton(string text, Point location, Size size, bool primary = false) => new ActionButton
    {
        Text = text,
        Location = location,
        Size = size,
        Primary = primary
    };

    internal static void OpenOfficialHelp()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://help.openai.com/en/articles/11369540-codex-and-chatgpt-plan-usage-limits",
                UseShellExecute = true
            });
        }
        catch
        {
        }
    }

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var movingExpandedCard = !_collapsed && !_animating;
        var previousOrbLocation = _collapsedBounds.Location;
        ReleaseCapture();
        SendMessage(Handle, WmNcLeftButtonDown, HtCaption, 0);
        if (movingExpandedCard)
        {
            _expandedBounds = Bounds;
            var orbSize = ScaledOrbSize();
            _collapsedBounds = ClampToWorkingArea(new Rectangle(
                Bounds.Right - orbSize.Width,
                Bounds.Bottom - orbSize.Height,
                orbSize.Width,
                orbSize.Height));
            _orbReturnLocation = _collapsedBounds.Location;
            if (previousOrbLocation != _collapsedBounds.Location)
                OrbPositionChanged?.Invoke(_collapsedBounds.Location);
        }
    }

    private void CaptureDetailLogicalBounds()
    {
        _detailLogicalBounds.Clear();
        foreach (Control control in Controls)
        {
            if (!ReferenceEquals(control, _orb))
                _detailLogicalBounds[control] = control.Bounds;
        }
        _detailLayoutDpi = 0;
    }

    private void ApplyDetailLayoutForCurrentDpi(bool force = false)
    {
        if (_detailLogicalBounds.Count == 0) return;
        var dpi = Math.Max(96, DeviceDpi);
        if (!force && _detailLayoutDpi == dpi) return;

        SuspendLayout();
        try
        {
            foreach (var (control, logicalBounds) in _detailLogicalBounds)
                control.Bounds = ScaleLogicalBounds(logicalBounds, dpi);
            _detailLayoutDpi = dpi;
        }
        finally
        {
            ResumeLayout(performLayout: false);
        }
    }

    internal static Rectangle ScaleLogicalBounds(Rectangle logicalBounds, int dpi)
    {
        var scale = Math.Max(96, dpi) / 96f;
        int Scale(int value) => (int)Math.Round(value * scale, MidpointRounding.AwayFromZero);
        return new Rectangle(
            Scale(logicalBounds.X),
            Scale(logicalBounds.Y),
            Scale(logicalBounds.Width),
            Scale(logicalBounds.Height));
    }

    internal static bool IsOrbDragGesture(Size cursorDelta, Size locationDelta, Size dragSize) =>
        Math.Abs(cursorDelta.Width) >= Math.Max(1, dragSize.Width / 2) ||
        Math.Abs(cursorDelta.Height) >= Math.Max(1, dragSize.Height / 2) ||
        Math.Abs(locationDelta.Width) >= Math.Max(1, dragSize.Width / 2) ||
        Math.Abs(locationDelta.Height) >= Math.Max(1, dragSize.Height / 2);

    private Size ScaledSize(Size logical)
    {
        var scale = DeviceDpi / 96f;
        return new Size(
            Math.Max(1, (int)Math.Round(logical.Width * scale)),
            Math.Max(1, (int)Math.Round(logical.Height * scale)));
    }

    private Size ScaledOrbSize()
    {
        var side = ScaleLogicalPixels(_orbLogicalSize);
        return new Size(side, side);
    }

    private int ScaleLogicalPixels(int logicalPixels) =>
        Math.Max(1, (int)Math.Round(logicalPixels * DeviceDpi / 96f));

    private static int NormalizeOrbLogicalSize(int value) =>
        PanelPreferenceManager.NormalizeOrbSize(value);

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr hWnd, int index);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetLayeredWindowAttributes(IntPtr hWnd, uint colorKey, byte alpha, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool GetLayeredWindowAttributes(IntPtr hWnd, out uint colorKey, out byte alpha, out uint flags);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);

    [DllImport("winmm.dll", EntryPoint = "timeBeginPeriod")]
    private static extern uint TimeBeginPeriod(uint period);

    [DllImport("winmm.dll", EntryPoint = "timeEndPeriod")]
    private static extern uint TimeEndPeriod(uint period);

    [DllImport("dwmapi.dll")]
    private static extern int DwmFlush();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            EndHighResolutionAnimationClock();
            _clock.Dispose();
            _transition.Dispose();
            _orbResizePreview.Dispose();
            _hoverTimer.Dispose();
            _hoverPeek.Dispose();
            _toolTip.Dispose();
            _transitionPreview?.Dispose();
            _transitionOrbPreview?.Dispose();
            _cachedExpandedPreview?.Dispose();
            DisposeTransitionOverlay();
        }
        base.Dispose(disposing);
    }
}
