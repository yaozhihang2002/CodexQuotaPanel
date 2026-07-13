using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CodexQuotaPanel;

internal sealed class QuotaForm : Form
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
    private const int WmSettingChange = 0x001A;
    private const int WmDisplayChange = 0x007E;
    private const int WmHotKey = 0x0312;
    private const uint LwaAlpha = 0x00000002;
    private const string PinGlyph = "\uE718";
    private const string UnpinGlyph = "\uE77A";
    private const string CollapseGlyph = "\uE70D";
    private static readonly IntPtr HtTransparent = new(-1);

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
    private bool _orbSnapBypass;
    private bool _orbClickThrough;
    private bool _positionLocked;
    private bool _snapToEdge;
    private bool _hoverPreviewEnabled = true;
    private bool _consumptionFlameEnabled = true;
    private bool _applyingLanguage;
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
    private Point _orbDragStartForm;

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
        AutoScaleMode = AutoScaleMode.Dpi;
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
            e.Graphics.Clear(UiPalette.Canvas);
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
            Bounds = ClampToWorkingArea(Bounds);
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
        var soonestResetCredit = snapshot.ResetCredits?.SoonestExpiring;
        _nextResetLabel.Text = soonestResetCredit?.ExpiresAt is null
            ? L10n.ResetCreditExpiryUnavailable
            : L10n.Pick(
                $"最早到期重置卡\n{L10n.FormatLocalDate(soonestResetCredit.ExpiresAt.Value)}",
                $"Earliest reset-credit expiry\n{L10n.FormatLocalDate(soonestResetCredit.ExpiresAt.Value)}");

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
        var orbSize = ScaledOrbSize();
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
        var orbSize = ScaledOrbSize();
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

        var location = ClampOrbLocation(new Point(x.Value, y.Value));
        NormalizeCollapsedGeometry(location);
        ApplyOrbPresentation();
        UpdateRegion();
        Invalidate(true);
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
        var margin = ScaleLogicalPixels(20);
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
            margin = ScaleLogicalPixels(20);
        }

        var orbSize = ScaledOrbSize();
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

    private void BeginTransition(Rectangle target, bool expanding)
    {
        _transition.Stop();
        DisposeTransitionOverlay();
        var preparationStartedAt = Environment.TickCount64;
        var previousPreview = _transitionPreview;
        _transitionPreview = null;
        _transitionOrbPreview?.Dispose();
        _transitionOrbPreview = null;
        _transitionFrom = Bounds;
        _transitionTo = target;
        var fullBounds = expanding ? target : _transitionFrom;
        var orbBounds = expanding ? _transitionFrom : target;
        using (NativeRedrawScope.Suspend(this))
        {
            _transitionOrbPreview = CaptureOrbPreview();
            _transitionPreview = previousPreview is not null
                ? new Bitmap(previousPreview)
                : expanding
                    ? _cachedExpandedPreview is not null
                        ? new Bitmap(_cachedExpandedPreview)
                        : CaptureExpandedPreview(fullBounds)
                    : CaptureCurrentPreview();
        }
        if (_transitionPreview is not null && (!expanding || _cachedExpandedPreview is null))
        {
            ReplaceExpandedPreviewCache(new Bitmap(_transitionPreview));
            _transitionPreviewCacheDirty = false;
        }
        previousPreview?.Dispose();
        _transitionExpanding = expanding;
        _transitionAnchor = new PointF(
            orbBounds.Left + orbBounds.Width / 2f - fullBounds.Left,
            orbBounds.Top + orbBounds.Height / 2f - fullBounds.Top);
        UpdateTransitionVisualState(0d);

        try
        {
            _transitionOverlay = new LayeredTransitionOverlay(fullBounds, TopMost);
            _transitionOverlay.Present(
                _transitionPreview!, _transitionOrbPreview!, _transitionAnchor,
                _transitionShapeProgress, _transitionOrbScale);
            _transitionOverlay.Show();
        }
        catch (Exception ex) when (ex is Win32Exception or ExternalException or ArgumentException)
        {
            DisposeTransitionOverlay();
        }

        _animating = true;
        _collapsed = false;
        if (_transitionOverlay is not null)
        {
            base.Hide();
            Bounds = fullBounds;
            ApplyOrbPresentation();
            SetDetailControlsVisible(false);
            _orb.Visible = false;
            UpdateRegion();
        }
        else
        {
            using var redraw = NativeRedrawScope.Suspend(this);
            Bounds = fullBounds;
            ApplyOrbPresentation();
            SetDetailControlsVisible(false);
            _orb.Visible = false;
            UpdateRegion();
        }

        _transitionPreparationMs = Environment.TickCount64 - preparationStartedAt;
        _transitionPaintFrames = 0;
        _transitionMaxPaintGapMs = 0;
        _transitionLastPaintAt = 0;
        _transitionStartedAt = Environment.TickCount64;
        _transitionMetricsActive = true;
        if (_transitionOverlay is not null) RecordTransitionFrame();
        BeginHighResolutionAnimationClock();
        _transition.Start();
    }

    private void AnimateTransition(object? sender, EventArgs e)
    {
        var elapsed = Environment.TickCount64 - _transitionStartedAt;
        var progress = Math.Clamp(elapsed / (double)TransitionDurationMs, 0d, 1d);
        UpdateTransitionVisualState(progress);
        if (_transitionOverlay is not null)
        {
            try
            {
                _transitionOverlay.Present(
                    _transitionPreview!, _transitionOrbPreview!, _transitionAnchor,
                    _transitionShapeProgress, _transitionOrbScale);
                RecordTransitionFrame();
            }
            catch (Exception ex) when (ex is Win32Exception or ExternalException or ArgumentException)
            {
                DisposeTransitionOverlay();
                if (!Visible) Show();
                UpdateRegion();
                Invalidate();
            }
        }
        else
        {
            UpdateRegion();
            Invalidate();
        }
        if (progress >= 1d) CompleteTransition();
    }

    internal void SetTransitionPreviewProgress(double progress)
    {
        if (!_animating) return;
        _transition.Stop();
        EndHighResolutionAnimationClock();
        progress = Math.Clamp(progress, 0d, 1d);
        UpdateTransitionVisualState(progress);
        UpdateRegion();
        if (_transitionOverlay is not null)
            _transitionOverlay.Present(
                _transitionPreview!, _transitionOrbPreview!, _transitionAnchor,
                _transitionShapeProgress, _transitionOrbScale);
        Invalidate(true);
        Update();
    }

    private void UpdateTransitionVisualState(double progress)
    {
        progress = Math.Clamp(progress, 0d, 1d);
        if (_transitionExpanding)
        {
            var orbProgress = Math.Clamp(progress / TransitionOrbPhase, 0d, 1d);
            _transitionOrbScale = 1d - SmoothStep(orbProgress);
            var genieProgress = Math.Clamp(
                (progress - TransitionOrbPhase) / (1d - TransitionOrbPhase), 0d, 1d);
            _transitionShapeProgress = SmoothStep(genieProgress);
        }
        else
        {
            var genieEnd = 1d - TransitionOrbPhase;
            var genieProgress = Math.Clamp(progress / genieEnd, 0d, 1d);
            _transitionShapeProgress = SmoothStep(1d - genieProgress);
            var orbProgress = Math.Clamp(
                (progress - genieEnd) / TransitionOrbPhase, 0d, 1d);
            _transitionOrbScale = SmoothStep(orbProgress);
        }
    }

    private void CompleteTransition()
    {
        _transition.Stop();
        EndHighResolutionAnimationClock();
        _lastTransitionDurationMs = Environment.TickCount64 - _transitionStartedAt;
        _transitionMetricsActive = false;
        using (NativeRedrawScope.Suspend(this))
        {
            _animating = false;
            _transitionShapeProgress = _transitionExpanding ? 1d : 0d;
            if (_transitionExpanding)
            {
                _collapsed = false;
                Bounds = _transitionTo;
                _expandedBounds = Bounds;
                _orb.Visible = false;
                SetDetailControlsVisible(true);
            }
            else
            {
                _collapsed = true;
                NormalizeCollapsedGeometry(_transitionTo.Location);
                SetDetailControlsVisible(false);
                _orb.Visible = true;
                _orb.Bounds = ClientRectangle;
                OrbPositionChanged?.Invoke(Location);
            }
            ApplyOrbPresentation();
            UpdateRegion();
        }
        if (!IsHidden && !Visible) Show();
        if (Visible)
        {
            _transitionOverlay?.BringToFront();
            Invalidate(true);
            Update();
            DwmFlush();
        }
        if (_transitionExpanding)
        {
            Activate();
            BringToFront();
        }
        DisposeTransitionOverlay();
        _transitionPreview?.Dispose();
        _transitionPreview = null;
        _transitionOrbPreview?.Dispose();
        _transitionOrbPreview = null;
        if (!IsHidden)
            SetViewState(_transitionExpanding ? DetailsViewState : OrbViewState);
        if (_collapsed) QueueTransitionPreviewCacheRefresh();
    }

    private void RecordTransitionFrame()
    {
        var now = Environment.TickCount64;
        if (_transitionLastPaintAt > 0)
            _transitionMaxPaintGapMs = Math.Max(_transitionMaxPaintGapMs, now - _transitionLastPaintAt);
        _transitionLastPaintAt = now;
        _transitionPaintFrames++;
    }

    private void BeginHighResolutionAnimationClock()
    {
        if (_highResolutionTimerActive) return;
        _highResolutionTimerActive = TimeBeginPeriod(1) == 0;
    }

    private void EndHighResolutionAnimationClock()
    {
        if (!_highResolutionTimerActive) return;
        TimeEndPeriod(1);
        _highResolutionTimerActive = false;
    }

    private void DisposeTransitionOverlay()
    {
        if (_transitionOverlay is null) return;
        _transitionOverlay.Close();
        _transitionOverlay.Dispose();
        _transitionOverlay = null;
    }

    private void SetCollapsedInstant(Rectangle? bounds = null)
    {
        var restoreVisible = _animating && !IsHidden;
        _transition?.Stop();
        EndHighResolutionAnimationClock();
        DisposeTransitionOverlay();
        _transitionPreview?.Dispose();
        _transitionPreview = null;
        _transitionOrbPreview?.Dispose();
        _transitionOrbPreview = null;
        var location = bounds?.Location ?? Location;
        _animating = false;
        _collapsed = true;
        NormalizeCollapsedGeometry(location);
        ApplyOrbPresentation();
        SetDetailControlsVisible(false);
        _orb.Visible = true;
        _orb.Bounds = ClientRectangle;
        UpdateRegion();
        Invalidate(true);
        if (restoreVisible && !Visible) Show();
        MarkTransitionPreviewCacheDirty();
    }

    private void SetExpandedInstant(Rectangle bounds)
    {
        var restoreVisible = _animating && !IsHidden;
        _transition.Stop();
        EndHighResolutionAnimationClock();
        DisposeTransitionOverlay();
        _transitionPreview?.Dispose();
        _transitionPreview = null;
        _transitionOrbPreview?.Dispose();
        _transitionOrbPreview = null;
        Bounds = bounds;
        _animating = false;
        _collapsed = false;
        _expandedBounds = Bounds;
        ApplyOrbPresentation();
        _orb.Visible = false;
        SetDetailControlsVisible(true);
        UpdateRegion();
        Invalidate(true);
        if (restoreVisible && !Visible) Show();
    }

    private void SetDetailControlsVisible(bool visible)
    {
        foreach (Control control in Controls)
            if (!ReferenceEquals(control, _orb)) control.Visible = visible;
    }

    private void NormalizeCollapsedGeometry(Point location)
    {
        var side = ScaledOrbSize().Width;
        SuspendLayout();
        ClientSize = new Size(side, side);
        Location = location;
        _orb.Bounds = ClientRectangle;
        _collapsedBounds = Bounds;
        ResumeLayout(performLayout: false);
        Debug.Assert(ClientSize.Width == ClientSize.Height);
        Debug.Assert(_orb.Bounds == ClientRectangle);
    }

    private void ApplyOrbPresentation()
    {
        Opacity = _collapsed && !_animating ? _orbOpacityPercent / 100d : 1d;
        if (!IsHandleCreated) return;
        UpdateStyles();
        if (IsClickThroughActive)
        {
            var alpha = (byte)Math.Round(_orbOpacityPercent * 255d / 100d);
            SetLayeredWindowAttributes(Handle, 0, alpha, LwaAlpha);
        }
    }

    private void SetViewState(int state)
    {
        if (_viewState == state) return;
        _viewState = state;
        ViewStateChanged?.Invoke(state);
    }

    private void QueueEnsureVisible()
    {
        if (IsDisposed || Disposing || !IsHandleCreated) return;
        try
        {
            BeginInvoke(() => EnsureVisibleOnCurrentDisplays());
        }
        catch (InvalidOperationException)
        {
            // The native window can disappear between receiving a system message
            // and queuing the UI-thread recovery callback during shutdown.
        }
    }

    private void NormalizeStoredCollapsedBounds()
    {
        var orbSize = ScaledOrbSize();
        var candidate = _collapsedBounds.IsEmpty
            ? new Point(Bounds.Right - orbSize.Width, Bounds.Bottom - orbSize.Height)
            : _collapsedBounds.Location;
        _collapsedBounds = new Rectangle(ClampOrbLocation(candidate), orbSize);
    }

    private static void AssignContextMenu(Control root, ContextMenuStrip menu)
    {
        root.ContextMenuStrip = menu;
        foreach (Control child in root.Controls)
            AssignContextMenu(child, menu);
    }

    private Rectangle ClampToWorkingArea(Rectangle bounds)
    {
        var area = Screen.FromRectangle(bounds).WorkingArea;
        return ClampToArea(bounds, area);
    }

    private static Rectangle ClampToArea(Rectangle bounds, Rectangle area)
    {
        var maxX = Math.Max(area.Left, area.Right - bounds.Width);
        var maxY = Math.Max(area.Top, area.Bottom - bounds.Height);
        bounds.X = Math.Clamp(bounds.X, area.Left, maxX);
        bounds.Y = Math.Clamp(bounds.Y, area.Top, maxY);
        return bounds;
    }

    private Point ClampOrbLocation(Point location)
    {
        var side = ScaledOrbSize().Width;
        var target = new Rectangle(location, new Size(side, side));
        var matching = Screen.AllScreens.FirstOrDefault(screen => screen.WorkingArea.IntersectsWith(target));
        var area = (matching ?? Screen.FromPoint(new Point(
            location.X + side / 2,
            location.Y + side / 2))).WorkingArea;
        return new Point(
            Math.Clamp(location.X, area.Left, Math.Max(area.Left, area.Right - side)),
            Math.Clamp(location.Y, area.Top, Math.Max(area.Top, area.Bottom - side)));
    }

    private Point SnapOrbLocationToNearbyEdge(Point location)
    {
        var side = ScaledOrbSize().Width;
        var bounds = new Rectangle(location, new Size(side, side));
        var area = Screen.FromRectangle(bounds).WorkingArea;
        location = new Point(
            Math.Clamp(location.X, area.Left, Math.Max(area.Left, area.Right - side)),
            Math.Clamp(location.Y, area.Top, Math.Max(area.Top, area.Bottom - side)));

        var leftDistance = Math.Abs(location.X - area.Left);
        var rightDistance = Math.Abs(area.Right - (location.X + side));
        var topDistance = Math.Abs(location.Y - area.Top);
        var bottomDistance = Math.Abs(area.Bottom - (location.Y + side));
        var threshold = OrbSnapThresholdPixels;

        if (Math.Min(leftDistance, rightDistance) <= threshold)
            location.X = leftDistance <= rightDistance ? area.Left : area.Right - side;
        if (Math.Min(topDistance, bottomDistance) <= threshold)
            location.Y = topDistance <= bottomDistance ? area.Top : area.Bottom - side;
        return location;
    }

    internal Point ResolveReleasedOrbLocation(Point location, bool bypassSnap = false) =>
        _snapToEdge && !bypassSnap
            ? SnapOrbLocationToNearbyEdge(location)
            : ClampOrbLocation(location);

    private void MarkTransitionPreviewCacheDirty()
    {
        _transitionPreviewCacheDirty = true;
        QueueTransitionPreviewCacheRefresh();
    }

    private void QueueTransitionPreviewCacheRefresh()
    {
        if (!_transitionPreviewCacheDirty || _transitionPreviewRefreshQueued ||
            !IsHandleCreated || !Visible || !_collapsed || _animating || IsHidden || IsDisposed)
            return;

        _transitionPreviewRefreshQueued = true;
        try
        {
            BeginInvoke((Action)(() =>
            {
                _transitionPreviewRefreshQueued = false;
                if (!_transitionPreviewCacheDirty || !Visible || !_collapsed || _animating || IsHidden || IsDisposed)
                    return;
                RefreshTransitionPreviewCache();
            }));
        }
        catch (InvalidOperationException)
        {
            _transitionPreviewRefreshQueued = false;
        }
    }

    private void RefreshTransitionPreviewCache()
    {
        var orbBounds = Bounds;
        var expandedSize = ScaledSize(ExpandedPanelSize);
        var fullBounds = ClampToWorkingArea(new Rectangle(
            orbBounds.Right - expandedSize.Width,
            orbBounds.Bottom - expandedSize.Height,
            expandedSize.Width,
            expandedSize.Height));
        LayeredTransitionOverlay? cover = null;
        Bitmap? orbPreview = null;
        Bitmap? transparentPanel = null;
        var restoreVisible = Visible;
        try
        {
            if (restoreVisible)
            {
                orbPreview = CaptureOrbPreview();
                transparentPanel = new Bitmap(1, 1, PixelFormat.Format32bppPArgb);
                cover = new LayeredTransitionOverlay(orbBounds, TopMost);
                cover.Present(transparentPanel, orbPreview,
                    new PointF(orbBounds.Width / 2f, orbBounds.Height / 2f), 0d, 1d);
                cover.Show();
                base.Hide();
            }

            ReplaceExpandedPreviewCache(CaptureExpandedPreview(fullBounds));
            _transitionPreviewCacheDirty = false;
        }
        catch (Exception ex) when (ex is Win32Exception or ExternalException or ArgumentException)
        {
            _transitionPreviewCacheDirty = true;
        }
        finally
        {
            if (restoreVisible && !Visible && !IsHidden && !_animating) Show();
            cover?.Close();
            cover?.Dispose();
            orbPreview?.Dispose();
            transparentPanel?.Dispose();
        }
    }

    private void ReplaceExpandedPreviewCache(Bitmap preview)
    {
        var previous = _cachedExpandedPreview;
        _cachedExpandedPreview = preview;
        previous?.Dispose();
    }

    private Bitmap CaptureExpandedPreview(Rectangle fullBounds)
    {
        var savedBounds = Bounds;
        var savedCollapsed = _collapsed;
        var savedAnimating = _animating;
        var savedOrbVisible = _orb.Visible;
        SuspendLayout();
        try
        {
            _collapsed = false;
            _animating = false;
            Bounds = fullBounds;
            SetDetailControlsVisible(true);
            _orb.Visible = false;
            PerformLayout();
            UpdateRegion();
            return CaptureCurrentPreview();
        }
        finally
        {
            SetDetailControlsVisible(!savedCollapsed && !savedAnimating);
            _orb.Visible = savedOrbVisible;
            Bounds = savedBounds;
            _collapsed = savedCollapsed;
            _animating = savedAnimating;
            if (savedCollapsed && !savedAnimating)
                _orb.Bounds = ClientRectangle;
            UpdateRegion();
            ResumeLayout(performLayout: false);
        }
    }

    private Bitmap CaptureCurrentPreview()
    {
        var bitmap = new Bitmap(Math.Max(1, ClientSize.Width), Math.Max(1, ClientSize.Height),
            PixelFormat.Format32bppPArgb);
        DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        var bounds = new RectangleF(0.5f, 0.5f, bitmap.Width - 1f, bitmap.Height - 1f);
        var radius = Math.Min(ScaleLogicalPixels(16), Math.Min(bounds.Width, bounds.Height) / 2f);
        MaskRoundedCornersInPlace(bitmap, bounds, radius);
        return bitmap;
    }

    private Bitmap CaptureOrbPreview()
    {
        var savedBounds = _orb.Bounds;
        var savedVisible = _orb.Visible;
        var size = ScaledOrbSize();
        try
        {
            _orb.Visible = true;
            _orb.Bounds = new Rectangle(Point.Empty, size);
            _orb.PerformLayout();
            var bitmap = _orb.RenderTransparentPreview();
            MaskEllipseInPlace(bitmap, new RectangleF(1.25f, 1.25f, size.Width - 2.5f, size.Height - 2.5f));
            return bitmap;
        }
        finally
        {
            _orb.Bounds = savedBounds;
            _orb.Visible = savedVisible;
        }
    }

    private static void MaskRoundedCornersInPlace(Bitmap bitmap, RectangleF bounds, float radius)
    {
        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size),
            ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try
        {
            var row = new byte[bitmap.Width * 4];
            var edge = Math.Min(bitmap.Width / 2, Math.Max(1, (int)Math.Ceiling(radius + 1f)));
            var leftCenter = bounds.Left + radius;
            var rightCenter = bounds.Right - radius;
            var topCenter = bounds.Top + radius;
            var bottomCenter = bounds.Bottom - radius;
            for (var y = 0; y < bitmap.Height; y++)
            {
                var pixelY = y + 0.5f;
                var centerY = pixelY < topCenter ? topCenter : pixelY > bottomCenter ? bottomCenter : float.NaN;
                if (float.IsNaN(centerY)) continue;
                var rowPointer = data.Scan0 + y * data.Stride;
                Marshal.Copy(rowPointer, row, 0, row.Length);
                for (var x = 0; x < edge; x++)
                {
                    ApplyCoverage(row, x, CircleCoverage(x + 0.5f, pixelY, leftCenter, centerY, radius));
                    var rightX = bitmap.Width - 1 - x;
                    ApplyCoverage(row, rightX, CircleCoverage(rightX + 0.5f, pixelY, rightCenter, centerY, radius));
                }
                Marshal.Copy(row, 0, rowPointer, row.Length);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static void MaskEllipseInPlace(Bitmap bitmap, RectangleF bounds)
    {
        var data = bitmap.LockBits(new Rectangle(Point.Empty, bitmap.Size),
            ImageLockMode.ReadWrite, PixelFormat.Format32bppPArgb);
        try
        {
            var row = new byte[bitmap.Width * 4];
            var centerX = bounds.Left + bounds.Width / 2f;
            var centerY = bounds.Top + bounds.Height / 2f;
            var radiusX = Math.Max(0.5f, bounds.Width / 2f);
            var radiusY = Math.Max(0.5f, bounds.Height / 2f);
            var edgeRadius = Math.Min(radiusX, radiusY);
            for (var y = 0; y < bitmap.Height; y++)
            {
                var rowPointer = data.Scan0 + y * data.Stride;
                Marshal.Copy(rowPointer, row, 0, row.Length);
                var normalizedY = (y + 0.5f - centerY) / radiusY;
                for (var x = 0; x < bitmap.Width; x++)
                {
                    var normalizedX = (x + 0.5f - centerX) / radiusX;
                    var normalizedDistance = Math.Sqrt(normalizedX * normalizedX + normalizedY * normalizedY);
                    var coverage = Math.Clamp((1d - normalizedDistance) * edgeRadius + 0.5d, 0d, 1d);
                    ApplyCoverage(row, x, coverage);
                }
                Marshal.Copy(row, 0, rowPointer, row.Length);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }
    }

    private static double CircleCoverage(float x, float y, float centerX, float centerY, float radius)
    {
        var deltaX = x - centerX;
        var deltaY = y - centerY;
        var distance = Math.Sqrt(deltaX * deltaX + deltaY * deltaY);
        return Math.Clamp(radius + 0.5d - distance, 0d, 1d);
    }

    private static void ApplyCoverage(byte[] row, int x, double coverage)
    {
        if (coverage >= 0.999d) return;
        var multiplier = Math.Clamp((int)Math.Round(coverage * 255d), 0, 255);
        var offset = x * 4;
        row[offset] = (byte)((row[offset] * multiplier + 127) / 255);
        row[offset + 1] = (byte)((row[offset + 1] * multiplier + 127) / 255);
        row[offset + 2] = (byte)((row[offset + 2] * multiplier + 127) / 255);
        row[offset + 3] = (byte)((row[offset + 3] * multiplier + 127) / 255);
    }

    private static GraphicsPath CreateGeniePath(Size size, PointF anchor, double appearance)
    {
        const int samples = 12;
        var points = new List<PointF>((samples + 1) * 2);
        for (var index = 0; index <= samples; index++)
        {
            var y = size.Height * index / (float)samples;
            points.Add(MapGeniePoint(size, anchor, new PointF(0f, y), appearance));
        }
        for (var index = samples; index >= 0; index--)
        {
            var y = size.Height * index / (float)samples;
            points.Add(MapGeniePoint(size, anchor, new PointF(size.Width, y), appearance));
        }

        var path = new GraphicsPath();
        path.AddPolygon(points.ToArray());
        path.CloseFigure();
        return path;
    }

    internal static void DrawGeniePreview(Graphics graphics, Bitmap preview, PointF anchor, double appearance)
    {
        if (appearance <= 0.012d) return;
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.InterpolationMode = InterpolationMode.Bilinear;
        graphics.PixelOffsetMode = PixelOffsetMode.HighSpeed;
        graphics.CompositingQuality = CompositingQuality.HighSpeed;

        var stripHeight = Math.Max(5, preview.Height / 24);
        var size = preview.Size;
        for (var sourceY = 0; sourceY < preview.Height; sourceY += stripHeight)
        {
            var height = Math.Min(stripHeight + 1, preview.Height - sourceY);
            var topLeft = MapGeniePoint(size, anchor, new PointF(0f, sourceY), appearance);
            var topRight = MapGeniePoint(size, anchor, new PointF(preview.Width, sourceY), appearance);
            var bottomLeft = MapGeniePoint(size, anchor, new PointF(0f, sourceY + height), appearance);
            PointF[] destination = [topLeft, topRight, bottomLeft];
            graphics.DrawImage(preview, destination,
                new RectangleF(0f, sourceY, preview.Width, height),
                GraphicsUnit.Pixel);
        }
    }

    internal static void DrawTransitionOrbPreview(
        Graphics graphics,
        Bitmap preview,
        PointF anchor,
        double scale)
    {
        if (scale <= 0.015d) return;
        var width = Math.Max(1f, (float)(preview.Width * scale));
        var height = Math.Max(1f, (float)(preview.Height * scale));
        var destination = new RectangleF(
            anchor.X - width / 2f,
            anchor.Y - height / 2f,
            width,
            height);
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.DrawImage(preview, destination);
    }

    internal static PointF MapGeniePoint(Size size, PointF anchor, PointF source, double appearance)
    {
        appearance = Math.Clamp(appearance, 0d, 1d);
        if (appearance <= 0d) return anchor;
        if (appearance >= 1d) return source;

        var verticalFactor = EaseOutCubic(appearance);
        var maximumVerticalDistance = Math.Max(1f, Math.Max(anchor.Y, size.Height - anchor.Y));
        var distanceFromAnchor = Math.Min(1d, Math.Abs(source.Y - anchor.Y) / maximumVerticalDistance);
        var localAppearance = Math.Clamp(appearance * (1d + distanceFromAnchor * 0.22d), 0d, 1d);
        var horizontalFactor = SmoothStep(localAppearance);
        var yRatio = size.Height <= 0 ? 0d : source.Y / size.Height;
        var twist = Math.Sin((yRatio * 1.7d + appearance * 0.55d) * Math.PI) *
                    Math.Sin(appearance * Math.PI) * 10d;

        return new PointF(
            (float)(anchor.X + (source.X - anchor.X) * horizontalFactor + twist),
            (float)(anchor.Y + (source.Y - anchor.Y) * verticalFactor));
    }

    private static Rectangle Interpolate(Rectangle from, Rectangle to, double amount) => new(
        (int)Math.Round(from.X + (to.X - from.X) * amount),
        (int)Math.Round(from.Y + (to.Y - from.Y) * amount),
        (int)Math.Round(from.Width + (to.Width - from.Width) * amount),
        (int)Math.Round(from.Height + (to.Height - from.Height) * amount));

    internal static Rectangle InterpolateLampTransition(Rectangle from, Rectangle to, double progress, bool expanding)
    {
        progress = Math.Clamp(progress, 0d, 1d);
        var amount = expanding
            ? EaseOutQuart(progress)
            : 1d - EaseOutQuart(1d - progress);
        var width = Lerp(from.Width, to.Width, amount);
        var height = Lerp(from.Height, to.Height, amount);
        var right = Lerp(from.Right, to.Right, amount);
        var bottom = Lerp(from.Bottom, to.Bottom, amount);
        return new Rectangle(right - width, bottom - height, width, height);
    }

    private static double EaseOutQuart(double value) => 1d - Math.Pow(1d - value, 4d);

    private static double EaseOutCubic(double value) => 1d - Math.Pow(1d - value, 3d);

    private static double SmoothStep(double value)
    {
        value = Math.Clamp(value, 0d, 1d);
        return value * value * (3d - 2d * value);
    }

    private static int Lerp(int from, int to, double amount) =>
        (int)Math.Round(from + (to - from) * amount);

    private void OrbMouseDown(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left || !_collapsed || _animating) return;
        HideHoverPreview();
        _orbDragStartScreen = Cursor.Position;
        _orbDragStartForm = Location;
        _orbDragged = false;
        _orbSnapBypass = (ModifierKeys & Keys.Shift) == Keys.Shift;
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
        _orbSnapBypass |= (ModifierKeys & Keys.Shift) == Keys.Shift;
        if (_positionLocked) return;
        var next = new Point(_orbDragStartForm.X + delta.Width, _orbDragStartForm.Y + delta.Height);
        Location = next;
        _collapsedBounds = Bounds;
    }

    private void OrbMouseUp(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        var shouldExpand = _orb.Capture && !_orbDragged && _collapsed && !_animating;
        _orb.Capture = false;
        if (_orbDragged && !_positionLocked)
        {
            var bypassSnap = _orbSnapBypass || (ModifierKeys & Keys.Shift) == Keys.Shift;
            var location = ResolveReleasedOrbLocation(Location, bypassSnap);
            Location = location;
            _collapsedBounds = Bounds;
            OrbPositionChanged?.Invoke(Location);
        }
        _orbSnapBypass = false;
        if (shouldExpand) ShowDetails();
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
        ReleaseCapture();
        SendMessage(Handle, 0xA1, 0x2, 0);
        if (movingExpandedCard)
        {
            _expandedBounds = Bounds;
            var orbSize = ScaledOrbSize();
            _collapsedBounds = ClampToWorkingArea(new Rectangle(
                Bounds.Right - orbSize.Width,
                Bounds.Bottom - orbSize.Height,
                orbSize.Width,
                orbSize.Height));
        }
    }

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
