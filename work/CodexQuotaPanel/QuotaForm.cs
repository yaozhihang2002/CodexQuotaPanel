using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CodexQuotaPanel;

internal sealed partial class QuotaForm : Form
{
    private static readonly Size ExpandedPanelSize = new(368, 500);
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
    private Point _orbDragStartScreen;
    private int _detailLayoutDpi;

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
        ClientSize = ExpandedPanelSize;
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
            TopMost = !TopMost;
            _pinButton.ForeColor = TopMost ? UiPalette.Mint : UiPalette.Muted;
            _pinButton.Text = TopMost ? PinGlyph : UnpinGlyph;
            TopMostChangedByUser?.Invoke(TopMost);
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
        _secondaryRow = new LimitRowControl { Location = new Point(18, 302), Width = 332, Height = 70, HistorySlot = 1 };
        _primaryRow.SetBucket(null);
        _secondaryRow.SetBucket(null);
        Controls.Add(_primaryRow);
        Controls.Add(_secondaryRow);

        _creditsLabel = MakeLabel($"{L10n.Credits} · —", new Point(19, 380), new Size(331, 19),
            UiPalette.Mono(8f, FontStyle.Bold), UiPalette.Muted);
        Controls.Add(_creditsLabel);

        _statusLabel = MakeLabel(L10n.Connecting, new Point(19, 404), new Size(331, 18),
            UiPalette.Body(8.3f), UiPalette.Muted);
        Controls.Add(_statusLabel);

        _freshnessLabel = MakeLabel(L10n.NoSnapshot, new Point(19, 426), new Size(331, 17),
            UiPalette.Mono(7.2f), UiPalette.Faint);
        Controls.Add(_freshnessLabel);

        _refreshButton = MakeActionButton(L10n.Refresh, new Point(18, 462), new Size(84, 28));
        _refreshButton.Click += (_, _) => RefreshRequested?.Invoke();
        _refreshButton.AccessibleName = L10n.RefreshNow;
        _toolTip.SetToolTip(_refreshButton, L10n.RefreshNow);
        Controls.Add(_refreshButton);

        _hideButton = MakeActionButton(L10n.CollapseOrb, new Point(108, 462), new Size(242, 28), true);
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

    public void ApplySnapshot(QuotaSnapshot snapshot)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ApplySnapshot(snapshot));
            return;
        }

        _snapshot = snapshot;
        var remaining = snapshot.RemainingPercent;
        var color = UiPalette.ForRemaining(remaining);
        _ring.Remaining = remaining;
        _planLabel.Text = $"{FormatPlan(snapshot.PlanType)} {L10n.PlanSuffix}";
        _planLabel.ForeColor = color;
        _heroValue.Text = snapshot.IsBlocked ? L10n.QuotaFull :
            remaining <= 20 ? L10n.NearlyUsed :
            remaining <= 45 ? L10n.WatchBalance : L10n.QuotaHealthy;
        _heroValue.ForeColor = color;

        var tightest = snapshot.Buckets.OrderBy(bucket => bucket.RemainingPercent).FirstOrDefault();
        _heroLabel.Text = tightest is null ? L10n.TightestWindow : L10n.Pick(
            $"最紧 · {LimitRowControl.FormatWindow(tightest.WindowMinutes)}",
            $"Tightest · {LimitRowControl.FormatWindow(tightest.WindowMinutes)}");
        UpdateRunwayInsight(force: true);

        _primaryRow.SetBucket(snapshot.Primary);
        _secondaryRow.SetBucket(snapshot.Secondary);
        UpdateHistoryRows();
        UpdateCredits(snapshot.Credits);

        var rpc = string.Equals(snapshot.Source, "App Server", StringComparison.Ordinal);
        _orb.SetSnapshot(snapshot, live: true);
        _sourcePill.Text = rpc ? L10n.LiveRpc : L10n.LocalLive;
        _sourcePill.PillColor = rpc ? UiPalette.Mint : UiPalette.Amber;
        _statusLabel.Text = rpc
            ? L10n.Pick("● 实时同步 · 每 60 秒校准", "● Live sync · Calibrates every 60s")
            : L10n.Pick("● 本地监听 · Codex 活动后更新", "● Local watch · Updates after Codex activity");
        _statusLabel.ForeColor = rpc ? UiPalette.Mint : UiPalette.Amber;
        if (snapshot.AdditionalLimitCount > 0)
            _statusLabel.Text += L10n.Pick($" · +{snapshot.AdditionalLimitCount} 组",
                $" · +{snapshot.AdditionalLimitCount} {(snapshot.AdditionalLimitCount == 1 ? "group" : "groups")}");
        if (rpc && !_applyingLanguage)
        {
            _lastStatus = null;
        }
        if (_lastStatus is not null && L10n.IsDisconnectedStatus(_lastStatus))
        {
            _statusLabel.Text = L10n.TranslateStatus(_lastStatus);
            _statusLabel.ForeColor = UiPalette.Amber;
            _orb.SetConnectionState(false);
        }
        if (_hoverPeek.Visible) _hoverPeek.SetData(snapshot, _ringConfiguration);
        TickDisplay();
        MarkTransitionPreviewCacheDirty();
    }

    public void SetStatus(string status)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetStatus(status));
            return;
        }
        _lastStatus = status;
        var disconnected = L10n.IsDisconnectedStatus(status);
        if (disconnected) _orb.SetConnectionState(false);
        if (_snapshot is null || disconnected)
        {
            _statusLabel.Text = L10n.TranslateStatus(status);
            _statusLabel.ForeColor = disconnected ? UiPalette.Amber : UiPalette.Muted;
        }
    }

    public void SetTopMostPreference(bool value)
    {
        TopMost = value;
        _hoverPeek.TopMost = value;
        _pinButton.ForeColor = value ? UiPalette.Mint : UiPalette.Muted;
        _pinButton.Text = value ? PinGlyph : UnpinGlyph;
    }

    public void ApplyLanguage()
    {
        Text = L10n.AppTitle;
        AccessibleName = L10n.AppAccessible;
        _heroValue.Font = UiPalette.Display(L10n.IsChinese ? 22.5f : 18f, FontStyle.Bold);
        _brandLabel.Text = L10n.Brand;
        _subtitleLabel.Text = L10n.LiveRateLimits;
        _closeButton.AccessibleName = L10n.CollapseOrb;
        _pinButton.AccessibleName = L10n.AlwaysOnTop;
        _toolTip.SetToolTip(_closeButton, L10n.CollapseOrb);
        _toolTip.SetToolTip(_pinButton, L10n.AlwaysOnTop);
        _refreshButton.Text = L10n.Refresh;
        _refreshButton.AccessibleName = L10n.RefreshNow;
        _toolTip.SetToolTip(_refreshButton, L10n.RefreshNow);
        _hideButton.Text = L10n.CollapseOrb;
        _hideButton.AccessibleName = L10n.CollapseOrb;
        _sectionTitle.Text = L10n.WindowSection;
        if (_snapshot is null)
        {
            _heroLabel.Text = L10n.TightestWindow;
            _heroValue.Text = L10n.WaitingData;
            _nextResetLabel.Text = L10n.WaitingQuotaEvent;
            _planLabel.Text = $"— {L10n.PlanSuffix}";
            _sourcePill.Text = L10n.ConnectingBadge;
            _statusLabel.Text = _lastStatus is null ? L10n.Connecting : L10n.TranslateStatus(_lastStatus);
            _statusLabel.ForeColor = _lastStatus is not null && L10n.IsDisconnectedStatus(_lastStatus)
                ? UiPalette.Amber
                : UiPalette.Muted;
            _freshnessLabel.Text = L10n.NoSnapshot;
        }
        else
        {
            _applyingLanguage = true;
            try { ApplySnapshot(_snapshot); }
            finally { _applyingLanguage = false; }
        }
        _primaryRow.SetBucket(_snapshot?.Primary);
        _secondaryRow.SetBucket(_snapshot?.Secondary);
        _ring.ApplyLanguage();
        _orb.ConfigureRings(_ringConfiguration);
        _hoverPeek.ApplyLanguage();
        UiPalette.ApplyTypography(_hoverPeek);
        if (_snapshot is not null && _hoverPeek.Visible) _hoverPeek.SetData(_snapshot, _ringConfiguration);
        UiPalette.ApplyTypography(this);
        Invalidate(true);
        MarkTransitionPreviewCacheDirty();
    }

    public void SetOrbOpacityPercent(int value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetOrbOpacityPercent(value));
            return;
        }

        _orbOpacityPercent = PanelPreferenceManager.NormalizeOpacity(value);
        ApplyOrbPresentation();
    }

    public void SetOrbBackgroundColor(int? argb)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetOrbBackgroundColor(argb));
            return;
        }

        _orb.SetBackgroundColor(argb);
        ApplyOrbPresentation();
        Invalidate(true);
        MarkTransitionPreviewCacheDirty();
    }

    public void ApplyTheme(UiPalette.Colors previousColors)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ApplyTheme(previousColors));
            return;
        }

        UiPalette.ApplyTheme(this, previousColors);
        UiPalette.ApplyTheme(_hoverPeek, previousColors);
        if (_snapshot is not null)
            ApplySnapshot(_snapshot);
        else
        {
            BackColor = UiPalette.Canvas;
            ForeColor = UiPalette.Text;
        }
        ApplyOrbPresentation();
        UpdateRegion();
        Invalidate(true);
        MarkTransitionPreviewCacheDirty();
    }

    public void SetOrbSize(int value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetOrbSize(value));
            return;
        }

        var normalized = NormalizeOrbLogicalSize(value);
        var previewWasRunning = _orbResizePreview.Enabled;
        _orbResizePreview.Stop();
        if (_orbLogicalSize == normalized && !previewWasRunning) return;

        var previousOrbBounds = _collapsed && !_animating
            ? Bounds
            : _collapsedBounds.IsEmpty
                ? new Rectangle(Location, ScaledOrbSize())
                : _collapsedBounds;
        var center = new Point(
            previousOrbBounds.Left + previousOrbBounds.Width / 2,
            previousOrbBounds.Top + previousOrbBounds.Height / 2);

        _orbLogicalSize = normalized;
        var targetScreen = DisplayPlacement.SelectScreen(previousOrbBounds);
        var targetDpi = DisplayPlacement.GetEffectiveDpi(targetScreen, DeviceDpi);
        var orbSide = DisplayPlacement.ScaleLogicalPixels(_orbLogicalSize, targetDpi);
        var orbSize = new Size(orbSide, orbSide);
        var nextLocation = ClampOrbLocation(new Point(
            center.X - orbSize.Width / 2,
            center.Y - orbSize.Height / 2));
        if (_snapToEdge) nextLocation = SnapOrbLocationToNearbyEdge(nextLocation);

        if (_collapsed && !_animating)
        {
            var locationChanged = Location != nextLocation;
            NormalizeCollapsedGeometry(nextLocation);
            ApplyOrbPresentation();
            UpdateRegion();
            Invalidate(true);
            if (locationChanged) OrbPositionChanged?.Invoke(Location);
        }
        else
        {
            _collapsedBounds = new Rectangle(nextLocation, orbSize);
            _orb.Size = orbSize;
            _orb.Location = new Point(
                Math.Max(0, ClientSize.Width - orbSize.Width),
                Math.Max(0, ClientSize.Height - orbSize.Height));
        }
    }

    public void PreviewOrbSize(int value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => PreviewOrbSize(value));
            return;
        }

        var normalized = NormalizeOrbLogicalSize(value);
        if (_orbLogicalSize == normalized) return;
        if (!_collapsed || _animating || !Visible)
        {
            SetOrbSize(normalized);
            return;
        }

        var currentBounds = Bounds;
        var center = new Point(
            currentBounds.Left + currentBounds.Width / 2,
            currentBounds.Top + currentBounds.Height / 2);
        _orbLogicalSize = normalized;
        var targetScreen = DisplayPlacement.SelectScreen(currentBounds);
        var targetDpi = DisplayPlacement.GetEffectiveDpi(targetScreen, DeviceDpi);
        var orbSide = DisplayPlacement.ScaleLogicalPixels(_orbLogicalSize, targetDpi);
        var orbSize = new Size(orbSide, orbSide);
        var nextLocation = ClampOrbLocation(new Point(
            center.X - orbSize.Width / 2,
            center.Y - orbSize.Height / 2));
        if (_snapToEdge) nextLocation = SnapOrbLocationToNearbyEdge(nextLocation);

        _orbResizePreviewFrom = currentBounds;
        _orbResizePreviewTo = new Rectangle(nextLocation, orbSize);
        _orbResizePreviewStartedAt = Environment.TickCount64;
        _orbResizePreview.Start();
    }

    private void AnimateOrbResizePreview(object? sender, EventArgs e)
    {
        var elapsed = Environment.TickCount64 - _orbResizePreviewStartedAt;
        var progress = Math.Clamp(elapsed / (double)OrbResizePreviewDurationMs, 0d, 1d);
        var eased = 1d - Math.Pow(1d - progress, 3d);
        Bounds = Interpolate(_orbResizePreviewFrom, _orbResizePreviewTo, eased);
        if (progress < 1d) return;

        _orbResizePreview.Stop();
        var previousLocation = _collapsedBounds.Location;
        NormalizeCollapsedGeometry(_orbResizePreviewTo.Location);
        ApplyOrbPresentation();
        UpdateRegion();
        Invalidate(true);
        if (previousLocation != Location) OrbPositionChanged?.Invoke(Location);
    }

    public void SetPositionLocked(bool value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetPositionLocked(value));
            return;
        }

        _positionLocked = value;
    }

    public void SetSnapToEdge(bool value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetSnapToEdge(value));
            return;
        }

        _snapToEdge = value;
    }

    public void SetOrbClickThroughPreference(bool value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetOrbClickThroughPreference(value));
            return;
        }

        _orbClickThrough = value;
        if (value) HideHoverPreview();
        ApplyOrbPresentation();
    }

    public void SetHoverPreviewEnabled(bool value)
    {
        _hoverPreviewEnabled = value;
        if (!value) HideHoverPreview();
    }

    public void SetConsumptionFlameEnabled(bool value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetConsumptionFlameEnabled(value));
            return;
        }

        _consumptionFlameEnabled = value;
        _orb.SetFlameAnimationEnabled(value);
        _orb.SetConsumptionIntensity(value ? ConsumptionIntensity : 0d);
    }

    public void SetConsumptionFlameStyle(int value)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetConsumptionFlameStyle(value));
            return;
        }

        _orb.SetFlameStyle(value);
    }

    public void ConfigureRings(RingDisplayConfiguration configuration)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ConfigureRings(configuration));
            return;
        }
        _ringConfiguration = configuration;
        _orb.ConfigureRings(configuration);
        UpdateHistoryRows();
        if (_snapshot is not null && _hoverPeek.Visible) _hoverPeek.SetData(_snapshot, configuration);
        MarkTransitionPreviewCacheDirty();
    }

    public void SetHistory(IReadOnlyList<QuotaHistoryPoint> history)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => SetHistory(history));
            return;
        }
        _history = history;
        ConsumptionIntensity = CalculateConsumptionIntensity(history);
        _orb.SetConsumptionIntensity(_consumptionFlameEnabled ? ConsumptionIntensity : 0d);
        UpdateHistoryRows();
        UpdateRunwayInsight(force: true);
        MarkTransitionPreviewCacheDirty();
    }

    internal static double CalculateConsumptionIntensity(
        IReadOnlyList<QuotaHistoryPoint> history,
        DateTimeOffset? now = null) => QuotaConsumptionRate.Evaluate(history, now).Intensity;

    public void SetSharedContextMenu(ContextMenuStrip menu)
    {
        AssignContextMenu(this, menu);
    }

    public void RestoreOrbLocation(int? x, int? y)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => RestoreOrbLocation(x, y));
            return;
        }
        if (x is null || y is null || !_collapsed || _animating) return;

        _hasRestoredOrbLocation = true;
        var location = ClampOrbLocation(new Point(x.Value, y.Value));
        NormalizeCollapsedGeometry(location);
        ApplyOrbPresentation();
        UpdateRegion();
        Invalidate(true);
    }

    public Point GetRestorableOrbLocation()
    {
        if (_collapsed && !_animating)
            return ClampOrbLocation(Location);

        if (!_collapsedBounds.IsEmpty)
            return ClampOrbLocation(_collapsedBounds.Location);

        var orbSize = ScaledOrbSize();
        return ClampOrbLocation(new Point(
            Bounds.Right - orbSize.Width,
            Bounds.Bottom - orbSize.Height));
    }

    public void EnsureVisibleOnCurrentDisplays()
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => EnsureVisibleOnCurrentDisplays());
            return;
        }

        if (IsDisposed) return;
        HideHoverPreview();

        if (_animating)
        {
            var wasHidden = IsHidden;
            if (_transitionExpanding)
                SetExpandedInstant(ClampToWorkingArea(_transitionTo));
            else
                SetCollapsedInstant(new Rectangle(ClampOrbLocation(_transitionTo.Location), ScaledOrbSize()));
            if (!wasHidden)
                SetViewState(_transitionExpanding ? DetailsViewState : OrbViewState);
        }

        if (_collapsed)
        {
            var previousLocation = Location;
            var location = ClampOrbLocation(Location);
            NormalizeCollapsedGeometry(location);
            ApplyOrbPresentation();
            UpdateRegion();
            Invalidate(true);
            if (previousLocation != Location) OrbPositionChanged?.Invoke(Location);
            return;
        }

        var previousOrbLocation = _collapsedBounds.Location;
        Bounds = ClampToWorkingArea(Bounds);
        _expandedBounds = Bounds;
        NormalizeStoredCollapsedBounds();
        if (previousOrbLocation != _collapsedBounds.Location)
            OrbPositionChanged?.Invoke(_collapsedBounds.Location);
    }

    public void RefreshDisplayEnvironment()
    {
        if (InvokeRequired)
        {
            BeginInvoke(RefreshDisplayEnvironment);
            return;
        }

        if (IsDisposed) return;
        ApplyDetailLayoutForCurrentDpi(force: true);
        EnsureVisibleOnCurrentDisplays();
        MarkTransitionPreviewCacheDirty();
        _orb.Invalidate();
        UpdateRegion();
        Invalidate(true);
    }

    public void MoveOrbToCurrentDisplay()
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => MoveOrbToCurrentDisplay());
            return;
        }

        if (IsDisposed) return;
        HideHoverPreview();

        if (_animating)
        {
            var wasHidden = IsHidden;
            if (_transitionExpanding)
                SetExpandedInstant(_transitionTo);
            else
                SetCollapsedInstant(_transitionTo);
            if (!wasHidden)
                SetViewState(_transitionExpanding ? DetailsViewState : OrbViewState);
        }

        var targetScreen = Screen.FromPoint(Cursor.Position);
        var area = targetScreen.WorkingArea;
        var targetDpi = DisplayPlacement.GetEffectiveDpi(targetScreen, DeviceDpi);
        var margin = DisplayPlacement.ScaleLogicalPixels(20, targetDpi);
        var previousOrbLocation = _collapsedBounds.IsEmpty ? Location : _collapsedBounds.Location;

        if (!_collapsed)
        {
            var cardSize = Bounds.Size;
            var cardLocation = new Point(
                Math.Max(area.Left, area.Right - cardSize.Width - margin),
                Math.Max(area.Top, area.Bottom - cardSize.Height - margin));
            Bounds = ClampToArea(new Rectangle(cardLocation, cardSize), area);
            _expandedBounds = Bounds;

            // Moving across monitors can synchronously update DeviceDpi, so calculate
            // the stored orb anchor after the expanded card has reached its display.
            targetScreen = Screen.FromRectangle(Bounds);
            area = targetScreen.WorkingArea;
            targetDpi = DisplayPlacement.GetEffectiveDpi(targetScreen, DeviceDpi);
            margin = DisplayPlacement.ScaleLogicalPixels(20, targetDpi);
        }

        var orbSide = DisplayPlacement.ScaleLogicalPixels(_orbLogicalSize, targetDpi);
        var orbSize = new Size(orbSide, orbSide);
        var orbLocation = new Point(
            Math.Max(area.Left, area.Right - orbSize.Width - margin),
            Math.Max(area.Top, area.Bottom - orbSize.Height - margin));
        orbLocation = ClampOrbLocation(orbLocation);
        _collapsedBounds = new Rectangle(orbLocation, orbSize);

        if (_collapsed)
        {
            NormalizeCollapsedGeometry(orbLocation);
            ApplyOrbPresentation();
            UpdateRegion();
            Invalidate(true);
        }

        if (previousOrbLocation != _collapsedBounds.Location)
            OrbPositionChanged?.Invoke(_collapsedBounds.Location);
    }

    public void ShowDetails(bool animate = true)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ShowDetails(animate));
            return;
        }

        HideHoverPreview();
        if (_animating)
        {
            if (_transitionExpanding) return;
            if (animate) BeginTransition(_expandedBounds, expanding: true);
            else SetExpandedInstant(_expandedBounds);
        }
        else if (_collapsed)
        {
            _collapsedBounds = Bounds;
            var expandedSize = ScaledSize(ExpandedPanelSize);
            var target = ClampToWorkingArea(new Rectangle(
                Bounds.Right - expandedSize.Width,
                Bounds.Bottom - expandedSize.Height,
                expandedSize.Width,
                expandedSize.Height));
            _expandedBounds = target;
            if (animate) BeginTransition(target, expanding: true);
            else SetExpandedInstant(target);
        }

        if (!Visible && !_animating) Show();
        WindowState = FormWindowState.Normal;
        Activate();
        BringToFront();
        if (!_animating) SetViewState(DetailsViewState);
    }

    public void ShowOrb(bool animate = true)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => ShowOrb(animate));
            return;
        }

        if (!Visible && !_animating) Show();
        WindowState = FormWindowState.Normal;
        if (_animating)
        {
            if (_transitionExpanding) CollapseToOrb(animate);
        }
        else if (!_collapsed)
        {
            CollapseToOrb(animate);
        }
        BringToFront();
        if (!_animating) SetViewState(OrbViewState);
    }

    public void CollapseToOrb(bool animate = true)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => CollapseToOrb(animate));
            return;
        }

        if (_animating)
        {
            if (!_transitionExpanding) return;
            if (animate) BeginTransition(_collapsedBounds, expanding: false);
            else
            {
                SetCollapsedInstant(_collapsedBounds);
                if (!IsHidden) SetViewState(OrbViewState);
                OrbPositionChanged?.Invoke(Location);
            }
            return;
        }
        if (_collapsed)
        {
            if (Visible) SetViewState(OrbViewState);
            return;
        }

        _expandedBounds = Bounds;
        var orbSize = ScaledOrbSize();
        var target = _collapsedBounds.IsEmpty
            ? new Rectangle(Bounds.Right - orbSize.Width, Bounds.Bottom - orbSize.Height, orbSize.Width, orbSize.Height)
            : new Rectangle(_collapsedBounds.Location, orbSize);
        target = ClampToWorkingArea(target);
        _collapsedBounds = target;
        if (animate) BeginTransition(target, expanding: false);
        else
        {
            SetCollapsedInstant(target);
            if (!IsHidden) SetViewState(OrbViewState);
            OrbPositionChanged?.Invoke(Location);
        }
    }

    public void ShowPanel() => ShowDetails();

    public void HidePanel()
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => HidePanel());
            return;
        }

        HideHoverPreview();
        if (_animating)
        {
            if (_transitionExpanding)
                SetExpandedInstant(_transitionTo);
            else
                SetCollapsedInstant(_transitionTo);
        }
        Hide();
        SetViewState(HiddenViewState);
    }

    public void CloseForExit()
    {
        _allowClose = true;
        Close();
    }

    public void SavePreview(string path)
    {
        CreateControl();
        foreach (Control child in Controls) child.CreateControl();
        PerformLayout();
        using var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
        DrawToBitmap(bitmap, new Rectangle(Point.Empty, ClientSize));
        if (_animating && Region is not null)
        {
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CompositingMode = CompositingMode.SourceCopy;
            using var outside = new Region(new Rectangle(Point.Empty, bitmap.Size));
            outside.Exclude(Region);
            using var transparent = new SolidBrush(Color.Transparent);
            graphics.FillRegion(transparent, outside);
        }
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
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
