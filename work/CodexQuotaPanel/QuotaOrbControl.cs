using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace CodexQuotaPanel;

internal sealed partial class QuotaOrbControl : Control
{
    private readonly System.Windows.Forms.Timer _flameTimer;
    private QuotaSnapshot? _snapshot;
    private LimitBucket? _outerBucket;
    private LimitBucket? _innerBucket;
    private RingDisplayConfiguration _configuration = new(
        new RingWindowSelection(300, RingWindowRole.Primary),
        new RingWindowSelection(10080, RingWindowRole.Secondary),
        UiPalette.Mint,
        UiPalette.Sky);
    private bool _live;
    private bool _flameAnimationEnabled = true;
    private bool _animationPaused;
    private int _flameStyle = 1;
    private double _flameIntensity;
    private double _targetFlameIntensity;
    private double _flamePhase;
    private FlameActivityLevel _flameActivity = FlameActivityLevel.Frozen;
    private Color? _backgroundColor;

    internal string OuterLabel => RingWindowCatalog.FormatShort(_configuration.Outer.WindowMinutes);
    internal string InnerLabel => RingWindowCatalog.FormatShort(_configuration.Inner.WindowMinutes);
    internal Color OuterColor => _configuration.OuterColor;
    internal Color InnerColor => _configuration.InnerColor;
    internal bool OuterAvailable => _outerBucket is not null;
    internal bool InnerAvailable => _innerBucket is not null;
    internal double ConsumptionIntensity => _targetFlameIntensity;
    internal bool FlameAnimationEnabled => _flameAnimationEnabled;
    internal int FlameStyle => _flameStyle;
    internal bool FlameTimerRunning => _flameTimer.Enabled;
    internal FlameActivityLevel ActivityLevel => FlameActivity.Classify(_targetFlameIntensity);
    internal Color WindowBackdropColor => ResolveOrbSurface().End;

    public QuotaOrbControl()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        Size = new Size(88, 88);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = L10n.Pick("Codex 额度悬浮球，单击展开详情", "Codex quota orb, click to open details");

        _flameTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _flameTimer.Tick += (_, _) =>
        {
            _flameIntensity += (_targetFlameIntensity - _flameIntensity) * 0.18d;
            if (_targetFlameIntensity <= FlameActivity.FrozenMaximum &&
                _flameIntensity <= FlameActivity.FrozenMaximum)
            {
                _flameIntensity = 0d;
                _flamePhase = 0d;
                _flameActivity = FlameActivityLevel.Frozen;
                _flameTimer.Stop();
            }
            else
            {
                _flameActivity = FlameActivity.Classify(_flameIntensity, _flameActivity);
                _flamePhase += 0.07d + _flameIntensity * 0.24d;
            }
            Invalidate();
        };
    }

    public void SetSnapshot(QuotaSnapshot snapshot, bool live)
    {
        _snapshot = snapshot;
        _live = live;
        UpdateBuckets();
    }

    public void ConfigureRings(RingDisplayConfiguration configuration)
    {
        _configuration = configuration with
        {
            OuterColor = Color.FromArgb(255, configuration.OuterColor),
            InnerColor = Color.FromArgb(255, configuration.InnerColor)
        };
        UpdateBuckets();
    }

    private void UpdateBuckets()
    {
        _outerBucket = RingWindowCatalog.FindBucket(_snapshot, _configuration.Outer);
        _innerBucket = RingWindowCatalog.FindBucket(_snapshot, _configuration.Inner);
        var outerText = _outerBucket is null ? L10n.TemporarilyUnavailable :
            L10n.Pick($"剩余 {Math.Round(_outerBucket.RemainingPercent):0}%", $"{Math.Round(_outerBucket.RemainingPercent):0}% remaining");
        var innerText = _innerBucket is null ? L10n.TemporarilyUnavailable :
            L10n.Pick($"剩余 {Math.Round(_innerBucket.RemainingPercent):0}%", $"{Math.Round(_innerBucket.RemainingPercent):0}% remaining");
        AccessibleName = L10n.Pick(
            $"Codex 额度悬浮球，{RingWindowCatalog.FormatLong(_configuration.Outer.WindowMinutes)}{outerText}，" +
            $"{RingWindowCatalog.FormatLong(_configuration.Inner.WindowMinutes)}{innerText}，单击展开详情",
            $"Codex quota orb, {RingWindowCatalog.FormatLong(_configuration.Outer.WindowMinutes)} {outerText}, " +
            $"{RingWindowCatalog.FormatLong(_configuration.Inner.WindowMinutes)} {innerText}, click to open details");
        Invalidate();
    }

    public void SetConnectionState(bool live)
    {
        _live = live;
        Invalidate();
    }

    public void SetBackgroundColor(int? argb)
    {
        _backgroundColor = argb is { } value
            ? Color.FromArgb(255, Color.FromArgb(value))
            : null;
        Invalidate();
    }

    public void SetConsumptionIntensity(double intensity)
    {
        _targetFlameIntensity = Math.Clamp(intensity, 0d, 1d);
        // Hidden/test controls render the requested state immediately.  A live,
        // visible orb instead eases out of the frozen state so a sudden sample
        // cannot flash directly from ice to a full inferno.
        if (!_flameTimer.Enabled &&
            (!IsHandleCreated || !Visible || !_flameAnimationEnabled))
            _flameIntensity = _targetFlameIntensity;
        if (_targetFlameIntensity <= FlameActivity.FrozenMaximum &&
            _flameIntensity <= FlameActivity.FrozenMaximum)
        {
            _flameIntensity = 0d;
            _flamePhase = 0d;
            _flameActivity = FlameActivityLevel.Frozen;
        }
        else
            _flameActivity = FlameActivity.Classify(_flameIntensity, _flameActivity);
        UpdateFlameTimer();
        Invalidate();
    }

    public void SetFlameAnimationEnabled(bool enabled)
    {
        _flameAnimationEnabled = enabled;
        UpdateFlameTimer();
        Invalidate();
    }

    public void SetAnimationPaused(bool paused)
    {
        if (_animationPaused == paused) return;
        _animationPaused = paused;
        UpdateFlameTimer();
        if (!paused) Invalidate();
    }

    public void SetFlameStyle(int value)
    {
        _flameStyle = Math.Clamp(value, 0, 2);
        Invalidate();
    }

    internal void SetFlamePhaseForTest(double phase)
    {
        _flameTimer.Stop();
        _flamePhase = phase;
        Invalidate();
        Update();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateFlameTimer();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateFlameTimer();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        DrawOrb(e.Graphics);
    }

    internal Bitmap RenderTransparentPreview(float dpi = 96f)
    {
        var bitmap = new Bitmap(Math.Max(1, Width), Math.Max(1, Height),
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        if (dpi is >= 48f and <= 960f)
            bitmap.SetResolution(dpi, dpi);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        DrawOrb(graphics);
        return bitmap;
    }

    private void DrawOrb(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = Math.Max(0.5f, Math.Min(Width, Height) / 88f);
        using var shell = UiPalette.RoundedRect(new RectangleF(1.5f, 1.5f, Width - 3, Height - 3), (Width - 3) / 2f);
        var (surfaceStart, surfaceEnd, borderColor, trackColor) = ResolveOrbSurface();
        using var background = new LinearGradientBrush(
            ClientRectangle,
            surfaceStart,
            surfaceEnd,
            LinearGradientMode.ForwardDiagonal);
        using var border = new Pen(borderColor, 1);
        graphics.FillPath(background, shell);
        graphics.DrawPath(border, shell);

        if (surfaceEnd.GetBrightness() > 0.62f)
        {
            using var innerShell = UiPalette.RoundedRect(
                new RectangleF(3f, 3f, Width - 6f, Height - 6f),
                (Width - 6f) / 2f);
            using var innerHighlight = new Pen(Color.FromArgb(92, Color.White), Math.Max(0.65f, 0.7f * scale));
            graphics.DrawPath(innerHighlight, innerShell);
        }

        var outerBounds = new RectangleF(8 * scale, 8 * scale, Width - 16 * scale, Height - 16 * scale);
        DrawArc(graphics, outerBounds, 7 * scale,
            _outerBucket?.RemainingPercent, _configuration.OuterColor, trackColor);
        DrawArc(graphics, new RectangleF(19 * scale, 19 * scale, Width - 38 * scale, Height - 38 * scale), 4.5f * scale,
            _innerBucket?.RemainingPercent, _configuration.InnerColor, trackColor);

        using var labelFont = UiPalette.MonoPixels(LabelPixelSize(scale), FontStyle.Bold);
        var outerText = $"{OuterLabel} {FormatPercent(_outerBucket)}";
        var innerText = $"{InnerLabel} {FormatPercent(_innerBucket)}";
        TextRenderer.DrawText(graphics, outerText, labelFont, ScaleRectangle(new RectangleF(22, 29, 44, 14), scale), _configuration.OuterColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(graphics, innerText, labelFont, ScaleRectangle(new RectangleF(22, 43, 44, 14), scale), _configuration.InnerColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        if (_flameAnimationEnabled)
        {
            var activity = FlameActivity.Classify(_flameIntensity, _flameActivity);
            switch (_flameStyle)
            {
                case 0:
                    DrawMinimalEmber(graphics, scale, activity);
                    break;
                case 2:
                    DrawPixelFlame(graphics, scale, activity);
                    break;
                default:
                    DrawFluidFlame(graphics, scale, activity);
                    break;
            }
        }

        var statusCenter = ArcEndpoint(outerBounds, _outerBucket?.RemainingPercent);
        var statusDiameter = 5f * scale;
        using var statusBorder = new SolidBrush(surfaceEnd);
        graphics.FillEllipse(statusBorder,
            statusCenter.X - statusDiameter * 0.72f,
            statusCenter.Y - statusDiameter * 0.72f,
            statusDiameter * 1.44f,
            statusDiameter * 1.44f);
        using var statusBrush = new SolidBrush(_live ? UiPalette.Mint : UiPalette.Amber);
        graphics.FillEllipse(statusBrush,
            statusCenter.X - statusDiameter / 2f,
            statusCenter.Y - statusDiameter / 2f,
            statusDiameter,
            statusDiameter);
    }

    private (Color Start, Color End, Color Border, Color Track) ResolveOrbSurface()
    {
        if (_backgroundColor is { } custom)
        {
            var bright = custom.GetBrightness() > 0.58f;
            return bright
                ? (Blend(custom, Color.Black, 0.09f), Blend(custom, Color.White, 0.08f),
                    Blend(custom, Color.Black, 0.27f), Blend(custom, Color.Black, 0.18f))
                : (Blend(custom, Color.White, 0.12f), Blend(custom, Color.Black, 0.10f),
                    Blend(custom, Color.White, 0.28f), Blend(custom, Color.White, 0.17f));
        }

        return (
            Color.FromArgb(27, 31, 29),
            Color.FromArgb(7, 9, 8),
            Color.FromArgb(66, 76, 71),
            Color.FromArgb(50, 58, 54));
    }

    internal static float LabelPixelSize(float scale) =>
        6.4f * 96f / 72f * Math.Max(0.5f, scale);

    private void DrawFluidFlame(Graphics graphics, float scale, FlameActivityLevel activity)
    {
        if (activity == FlameActivityLevel.Frozen)
        {
            DrawFluidFrostSeed(graphics, scale);
            return;
        }

        var intensity = (float)Math.Clamp(_flameIntensity, 0d, 1d);
        var inferno = activity == FlameActivityLevel.Inferno
            ? SmoothStep(FlameActivity.Progress(intensity, activity))
            : 0f;
        var pulse = (float)Math.Sin(_flamePhase);
        var sway = pulse * (0.35f + intensity * 1.05f + inferno * 0.25f) * scale;
        var width = (5.8f + intensity * 5.1f + inferno * 2.9f) * scale;
        // Grow an inferno mostly sideways: the centre labels end around y=57 at
        // the reference size, so extra height would collide with quota text.
        var height = (7.2f + intensity * 9.2f + Math.Abs(pulse) * intensity * 1.5f + inferno * 1.1f) * scale;
        var centerX = Width / 2f + sway;
        var baseY = Math.Min(Height - 8f * scale, 76f * scale);
        var topY = baseY - height;

        var flameColor = FlameColor(intensity, activity);

        if (inferno > 0.01f)
        {
            using var sideBrush = new SolidBrush(Color.FromArgb(
                Math.Clamp((int)(220f * inferno), 0, 220),
                Blend(flameColor, Color.FromArgb(255, 178, 72), 0.42f)));
            using var leftTongue = CreateFlamePath(
                centerX - width * 0.43f,
                baseY + 0.2f * scale,
                width * (0.34f + inferno * 0.24f),
                height * (0.45f + inferno * 0.25f),
                -sway * 0.42f - 0.7f * scale);
            using var rightTongue = CreateFlamePath(
                centerX + width * 0.43f,
                baseY + 0.2f * scale,
                width * (0.31f + inferno * 0.23f),
                height * (0.42f + inferno * 0.24f),
                sway * 0.35f + 0.65f * scale);
            graphics.FillPath(sideBrush, leftTongue);
            graphics.FillPath(sideBrush, rightTongue);
        }

        using var outer = CreateFlamePath(centerX, baseY, width, height, sway * 0.45f);
        // A restrained halo softens the tiny flame silhouette without turning
        // it into a blurry badge at the smallest supported orb size.
        using (var halo = new Pen(Color.FromArgb(34 + (int)(intensity * 24f), flameColor), 1.8f * scale)
               {
                   LineJoin = LineJoin.Round
               })
            graphics.DrawPath(halo, outer);
        using var outerBrush = new LinearGradientBrush(
            new RectangleF(centerX - width, topY, width * 2f, height),
            Color.FromArgb(235, Blend(Color.White, flameColor, 0.48f)),
            Color.FromArgb(225, flameColor),
            LinearGradientMode.Vertical);
        graphics.FillPath(outerBrush, outer);

        var innerIntensity = Math.Clamp((intensity - 0.08f) / 0.92f, 0f, 1f);
        if (innerIntensity > 0f)
        {
            var innerHeight = height * (0.46f + innerIntensity * 0.12f);
            var innerWidth = width * 0.48f;
            using var inner = CreateFlamePath(
                centerX - sway * 0.2f,
                baseY - 0.7f * scale,
                innerWidth,
                innerHeight,
                -sway * 0.18f);
            var coreColor = FlameCoreColor(intensity, activity);
            using var innerBrush = new SolidBrush(Color.FromArgb(215 + (int)(inferno * 32f), coreColor));
            graphics.FillPath(innerBrush, inner);
        }

        var emberVisibility = activity switch
        {
            FlameActivityLevel.Hot => SmoothStep(FlameActivity.Progress(intensity, activity)),
            FlameActivityLevel.Inferno => 1f,
            _ => 0f
        };
        if (emberVisibility > 0.01f)
            DrawFluidEmbers(graphics, centerX, topY, width, height, sway, scale,
                intensity, flameColor, emberVisibility, inferno);
    }

    private void DrawFluidEmbers(
        Graphics graphics,
        float centerX,
        float topY,
        float flameWidth,
        float flameHeight,
        float sway,
        float scale,
        float intensity,
        Color flameColor,
        float visibility,
        float inferno)
    {
        // Staggered, tapered embers replace the old isolated circular spark.
        // Their short lifetime and opposing drift keep the motion organic while
        // remaining legible on a 56 px orb.
        ReadOnlySpan<float> phaseOffsets = stackalloc float[] { 0.08f, 0.47f, 0.79f, 0.28f, 0.66f };
        ReadOnlySpan<float> sideOffsets = stackalloc float[] { 0.62f, -0.42f, 0.88f, -0.84f, 0.25f };
        var emberCount = inferno > 0f
            ? 3 + (int)MathF.Round(inferno * 2f)
            : 1 + (int)MathF.Round(visibility * 2f);

        for (var index = 0; index < emberCount; index++)
        {
            var life = (float)((_flamePhase * (0.205d + index * 0.018d) + phaseOffsets[index]) % 1d);
            var envelope = MathF.Sin(life * MathF.PI);
            if (envelope < 0.12f) continue;

            var direction = MathF.Sign(sideOffsets[index]);
            var drift = direction * life * (1.3f + index * 0.35f) * scale;
            var x = centerX + flameWidth * sideOffsets[index] - sway * 0.32f + drift;
            var y = topY + flameHeight * (0.56f - life * 0.62f);
            var emberHeight = (1.25f + intensity * 1.05f - life * 0.34f) * scale;
            var emberWidth = Math.Max(0.34f * scale, emberHeight * 0.3f);
            var alpha = Math.Clamp((int)(168f * envelope * (1f - life * 0.35f) * visibility), 0, 168);

            using var trail = new Pen(Color.FromArgb(alpha / 4, flameColor), Math.Max(0.42f, 0.34f * scale))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(trail,
                x + direction * 0.2f * scale,
                y - emberHeight * 0.15f,
                x - direction * 0.45f * scale,
                y - emberHeight * 1.25f);

            using var glow = new SolidBrush(Color.FromArgb(alpha / 5, flameColor));
            graphics.FillEllipse(glow,
                x - emberWidth * 1.25f,
                y - emberHeight * 0.9f,
                emberWidth * 2.5f,
                emberHeight * 2.5f);

            using var ember = CreateFlamePath(x, y + emberHeight * 0.45f, emberWidth, emberHeight, drift * 0.12f);
            using var emberBrush = new SolidBrush(Color.FromArgb(
                alpha,
                Blend(Color.FromArgb(255, 231, 164), flameColor, 0.58f)));
            graphics.FillPath(emberBrush, ember);
        }
    }

    private void DrawMinimalEmber(Graphics graphics, float scale, FlameActivityLevel activity)
    {
        if (activity == FlameActivityLevel.Frozen)
        {
            DrawMinimalFrostSeed(graphics, scale);
            return;
        }

        var intensity = (float)Math.Clamp(_flameIntensity, 0d, 1d);
        var breath = 0.5f + 0.5f * (float)Math.Sin(_flamePhase * 0.72d);
        var drift = (float)Math.Sin(_flamePhase * 0.43d + 0.8d) * 0.42f * scale;
        var inferno = activity == FlameActivityLevel.Inferno
            ? SmoothStep(FlameActivity.Progress(intensity, activity))
            : 0f;
        var color = FlameColor(intensity, activity);
        var emberWidth = (5.2f + intensity * 3.3f + breath * 0.45f + inferno * 1.4f) * scale;
        var emberHeight = (3.3f + intensity * 2.2f + breath * 0.35f + inferno * 0.5f) * scale;
        var centerX = Width / 2f + drift;
        var baseY = Math.Min(Height - 8.5f * scale, 76f * scale);
        var centerY = baseY - emberHeight * 0.45f;

        using (var wideGlow = new SolidBrush(Color.FromArgb(16 + (int)(intensity * 20f), color)))
            graphics.FillEllipse(wideGlow,
                centerX - emberWidth * 1.35f,
                centerY - emberHeight * 1.5f,
                emberWidth * 2.7f,
                emberHeight * 3f);
        using (var closeGlow = new SolidBrush(Color.FromArgb(42 + (int)(intensity * 42f), color)))
            graphics.FillEllipse(closeGlow,
                centerX - emberWidth * 0.82f,
                centerY - emberHeight * 0.92f,
                emberWidth * 1.64f,
                emberHeight * 1.84f);

        using var ember = CreateEmberPath(centerX, centerY, emberWidth, emberHeight);
        using (var rim = new Pen(Color.FromArgb(165, Blend(UiPalette.Canvas, color, 0.7f)), Math.Max(0.7f, 0.8f * scale))
               {
                   LineJoin = LineJoin.Round
               })
            graphics.DrawPath(rim, ember);
        using (var fill = new LinearGradientBrush(
                   new RectangleF(centerX - emberWidth / 2f, centerY - emberHeight / 2f, emberWidth, emberHeight),
                   Blend(Color.White, color, 0.5f),
                   Blend(color, UiPalette.Canvas, 0.2f),
                   LinearGradientMode.Vertical))
            graphics.FillPath(fill, ember);

        using (var heatLine = new Pen(
                   Color.FromArgb(205, Blend(Color.White, color, 0.36f)),
                   Math.Max(0.8f, 1.05f * scale))
               {
                   StartCap = LineCap.Round,
                   EndCap = LineCap.Round
               })
            graphics.DrawLine(heatLine,
                centerX - emberWidth * 0.22f,
                centerY - emberHeight * 0.04f,
                centerX + emberWidth * (0.13f + breath * 0.08f),
                centerY - emberHeight * 0.2f);

        var wispVisibility = activity switch
        {
            FlameActivityLevel.Hot => SmoothStep(FlameActivity.Progress(intensity, activity)),
            FlameActivityLevel.Inferno => 1f,
            _ => 0f
        };
        if (wispVisibility <= 0.01f) return;
        var wispLife = 0.5f + 0.5f * (float)Math.Sin(_flamePhase * 0.8d + 1.1d);
        var wispHeight = (1.4f + wispVisibility * (0.7f + intensity * 2.6f + wispLife * 1.2f)) * scale;
        using var wisp = CreateFlamePath(
            centerX + emberWidth * 0.16f,
            centerY - emberHeight * 0.35f,
            Math.Max(0.75f * scale, emberWidth * 0.16f),
            wispHeight,
            -drift * 0.7f);
        using var wispBrush = new SolidBrush(Color.FromArgb(
            Math.Clamp((int)((55f + intensity * 55f) * wispVisibility), 0, 110),
            Blend(Color.White, color, 0.55f)));
        graphics.FillPath(wispBrush, wisp);

        if (inferno <= 0.01f) return;
        var secondLife = 0.5f + 0.5f * (float)Math.Sin(_flamePhase * 0.92d + 3.2d);
        using var secondWisp = CreateFlamePath(
            centerX - emberWidth * 0.18f,
            centerY - emberHeight * 0.25f,
            Math.Max(0.7f * scale, emberWidth * 0.14f),
            (2.6f + inferno * 3.2f + secondLife) * scale,
            drift * 0.62f);
        using var secondBrush = new SolidBrush(Color.FromArgb(
            Math.Clamp((int)(120f * inferno), 0, 120),
            Blend(Color.FromArgb(255, 241, 183), color, 0.38f)));
        graphics.FillPath(secondBrush, secondWisp);
    }

    private void DrawPixelFlame(Graphics graphics, float scale, FlameActivityLevel activity)
    {
        if (activity == FlameActivityLevel.Frozen)
        {
            DrawPixelFrostSeed(graphics, scale, 0f);
            return;
        }

        var intensity = (float)Math.Clamp(_flameIntensity, 0d, 1d);
        if (activity == FlameActivityLevel.Cool)
        {
            var thaw = SmoothStep(FlameActivity.Progress(intensity, activity));
            if (thaw < 0.35f)
            {
                DrawPixelFrostSeed(graphics, scale, thaw);
                return;
            }
        }
        var frame = (int)Math.Floor((_flamePhase * 1.65d) % 4d);
        var color = FlameColor(intensity, activity);
        var cell = Math.Max(2, (int)Math.Round((2.35f + intensity * 0.55f) * scale));
        var originX = (int)Math.Round(Width / 2f - cell / 2f);
        var baseY = (int)Math.Round(Math.Min(Height - 7f * scale, 77f * scale));
        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;

            using var outer = new SolidBrush(Color.FromArgb(245, Blend(color, UiPalette.Canvas, 0.18f)));
            using var middle = new SolidBrush(Color.FromArgb(250, color));
            var coreColor = FlameCoreColor(intensity, activity);
            using var core = new SolidBrush(coreColor);

            Span<Point> outerCells = stackalloc Point[]
            {
                new(-2, 0), new(-1, 0), new(0, 0), new(1, 0), new(2, 0),
                new(-2, 1), new(-1, 1), new(0, 1), new(1, 1), new(2, 1),
                new(-1, 2), new(0, 2), new(1, 2),
                new(frame is 0 or 3 ? -1 : 0, 3),
                new(frame is 0 or 1 ? 0 : 1, 4)
            };
            DrawPixelCells(graphics, outer, originX, baseY, cell, outerCells);

            Span<Point> middleCells = stackalloc Point[]
            {
                new(-1, 0), new(0, 0), new(1, 0),
                new(-1, 1), new(0, 1), new(1, 1),
                new(0, 2),
                new(frame is 1 or 2 ? 0 : -1, 3)
            };
            DrawPixelCells(graphics, middle, originX, baseY, cell, middleCells);

            Span<Point> coreCells = stackalloc Point[]
            {
                new(0, 0), new(0, 1), new(frame == 2 ? 1 : 0, 2)
            };
            DrawPixelCells(graphics, core, originX, baseY, cell, coreCells);

            var hotProgress = activity switch
            {
                FlameActivityLevel.Hot => SmoothStep(FlameActivity.Progress(intensity, activity)),
                FlameActivityLevel.Inferno => 1f,
                _ => 0f
            };
            var infernoProgress = activity == FlameActivityLevel.Inferno
                ? SmoothStep(FlameActivity.Progress(intensity, activity))
                : 0f;

            if (hotProgress > 0.18f)
            {
                var emberX = frame is 0 or 1 ? 2 : -2;
                FillPixelCell(graphics, middle, originX, baseY, cell, emberX, 5);
                if (hotProgress > 0.68f && frame is 1 or 3)
                    FillPixelCell(graphics, outer, originX, baseY, cell, -emberX, 4);
            }

            if (infernoProgress > 0.12f)
            {
                FillPixelCell(graphics, outer, originX, baseY, cell, -3, 0);
                FillPixelCell(graphics, outer, originX, baseY, cell, 3, 0);
            }
            if (infernoProgress > 0.38f)
                FillPixelCell(graphics, middle, originX, baseY, cell, frame is 0 or 3 ? -2 : 2, 2);
            if (infernoProgress > 0.68f)
                FillPixelCell(graphics, middle, originX, baseY, cell, frame is 0 or 1 ? 1 : -1, 5);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private void DrawMinimalFrostSeed(Graphics graphics, float scale)
    {
        var width = 5.35f * scale;
        var height = 3.45f * scale;
        var centerX = Width / 2f;
        var baseY = Math.Min(Height - 8.5f * scale, 76f * scale);
        var centerY = baseY - height * 0.45f;
        var ice = Color.FromArgb(112, 205, 252);

        using (var glow = new SolidBrush(Color.FromArgb(36, ice)))
            graphics.FillEllipse(glow,
                centerX - width * 0.95f,
                centerY - height * 1.1f,
                width * 1.9f,
                height * 2.2f);
        using var seed = CreateEmberPath(centerX, centerY, width, height);
        using (var fill = new LinearGradientBrush(
                   new RectangleF(centerX - width / 2f, centerY - height / 2f, width, height),
                   Blend(Color.White, ice, 0.26f),
                   Blend(ice, UiPalette.Canvas, 0.18f),
                   LinearGradientMode.Vertical))
            graphics.FillPath(fill, seed);
        using (var rim = new Pen(Color.FromArgb(205, ice), Math.Max(0.65f, 0.72f * scale))
               {
                   LineJoin = LineJoin.Round
               })
            graphics.DrawPath(rim, seed);
        using var glint = new Pen(Color.FromArgb(220, Color.White), Math.Max(0.55f, 0.6f * scale))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawLine(glint,
            centerX - width * 0.2f, centerY - height * 0.12f,
            centerX + width * 0.08f, centerY - height * 0.27f);
    }

    private void DrawFluidFrostSeed(Graphics graphics, float scale)
    {
        var width = 5.9f * scale;
        var height = 7.45f * scale;
        var centerX = Width / 2f;
        var baseY = Math.Min(Height - 8f * scale, 76f * scale);
        var topY = baseY - height;
        var ice = Color.FromArgb(108, 201, 255);

        using var seed = CreateFlamePath(centerX, baseY, width, height, 0f);
        using (var halo = new Pen(Color.FromArgb(42, ice), Math.Max(1f, 1.55f * scale))
               {
                   LineJoin = LineJoin.Round
               })
            graphics.DrawPath(halo, seed);
        using (var fill = new LinearGradientBrush(
                   new RectangleF(centerX - width, topY, width * 2f, height),
                   Blend(Color.White, ice, 0.22f),
                   Blend(ice, UiPalette.Canvas, 0.16f),
                   LinearGradientMode.Vertical))
            graphics.FillPath(fill, seed);
        using (var rim = new Pen(Color.FromArgb(195, ice), Math.Max(0.62f, 0.7f * scale))
               {
                   LineJoin = LineJoin.Round
               })
            graphics.DrawPath(rim, seed);

        using var facet = new Pen(Color.FromArgb(180, Blend(Color.White, ice, 0.25f)),
            Math.Max(0.52f, 0.58f * scale))
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawLine(facet, centerX, topY + height * 0.22f, centerX, baseY - height * 0.18f);
        graphics.DrawLine(facet,
            centerX - width * 0.19f, topY + height * 0.52f,
            centerX + width * 0.19f, topY + height * 0.52f);
    }

    private void DrawPixelFrostSeed(Graphics graphics, float scale, float thaw)
    {
        var cell = Math.Max(2, (int)Math.Round(2.2f * scale));
        var originX = (int)Math.Round(Width / 2f - cell / 2f);
        var baseY = (int)Math.Round(Math.Min(Height - 7f * scale, 77f * scale));
        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;
            using var edge = new SolidBrush(Blend(Color.FromArgb(102, 190, 255), UiPalette.Canvas, 0.1f));
            using var body = new SolidBrush(Color.FromArgb(120, 214, 255));
            using var shine = new SolidBrush(Color.FromArgb(225, 250, 255));

            Span<Point> coreSnowflake = stackalloc Point[]
            {
                new(0, 0), new(0, 1), new(0, 2), new(0, 3), new(0, 4),
                new(-1, 2), new(1, 2)
            };
            DrawPixelCells(graphics, edge, originX, baseY, cell, coreSnowflake);
            if (thaw < 0.24f)
            {
                Span<Point> diagonals = stackalloc Point[]
                {
                    new(-1, 1), new(1, 1), new(-1, 3), new(1, 3)
                };
                DrawPixelCells(graphics, edge, originX, baseY, cell, diagonals);
            }
            if (thaw < 0.12f)
            {
                FillPixelCell(graphics, edge, originX, baseY, cell, -2, 2);
                FillPixelCell(graphics, edge, originX, baseY, cell, 2, 2);
            }
            Span<Point> inner = stackalloc Point[]
            {
                new(0, 1), new(0, 2), new(0, 3), new(-1, 2), new(1, 2)
            };
            DrawPixelCells(graphics, body, originX, baseY, cell, inner);
            FillPixelCell(graphics, shine, originX, baseY, cell, 0, 2);
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private static GraphicsPath CreateEmberPath(float centerX, float centerY, float width, float height)
    {
        var path = new GraphicsPath();
        path.StartFigure();
        path.AddBezier(
            centerX - width / 2f, centerY,
            centerX - width * 0.42f, centerY - height * 0.58f,
            centerX + width * 0.28f, centerY - height * 0.62f,
            centerX + width / 2f, centerY - height * 0.08f);
        path.AddBezier(
            centerX + width / 2f, centerY - height * 0.08f,
            centerX + width * 0.34f, centerY + height * 0.48f,
            centerX - width * 0.34f, centerY + height * 0.45f,
            centerX - width / 2f, centerY);
        path.CloseFigure();
        return path;
    }

    private static void DrawPixelCells(
        Graphics graphics,
        Brush brush,
        int originX,
        int baseY,
        int cell,
        ReadOnlySpan<Point> cells)
    {
        foreach (var point in cells)
            FillPixelCell(graphics, brush, originX, baseY, cell, point.X, point.Y);
    }

    private static void FillPixelCell(
        Graphics graphics,
        Brush brush,
        int originX,
        int baseY,
        int cell,
        int x,
        int y) =>
        graphics.FillRectangle(brush, originX + x * cell, baseY - (y + 1) * cell, cell, cell);

    private static Color FlameColor(float intensity, FlameActivityLevel activity)
    {
        var progress = SmoothStep(FlameActivity.Progress(intensity, activity));
        var frost = Color.FromArgb(102, 196, 255);
        var cool = Color.FromArgb(122, 216, 247);
        // A direct cyan-to-orange RGB blend crosses a dull grey-brown in the
        // middle.  Route the warm stage through a luminous champagne tone so
        // the representative "warm flame" remains bright at tiny orb sizes.
        var warmBridge = Color.FromArgb(255, 237, 158);
        var warm = Color.FromArgb(255, 198, 76);
        var hot = Color.FromArgb(255, 100, 66);
        var inferno = Color.FromArgb(220, 49, 45);
        return activity switch
        {
            FlameActivityLevel.Cool => Blend(frost, cool, progress),
            FlameActivityLevel.Warm => BlendThrough(cool, warmBridge, warm, progress),
            FlameActivityLevel.Hot => Blend(warm, hot, progress),
            FlameActivityLevel.Inferno => Blend(hot, inferno, progress),
            _ => frost
        };
    }

    private static Color FlameCoreColor(float intensity, FlameActivityLevel activity)
    {
        var progress = SmoothStep(FlameActivity.Progress(intensity, activity));
        var frost = Color.FromArgb(218, 247, 255);
        var cool = Color.FromArgb(228, 250, 255);
        var warmBridge = Color.FromArgb(255, 255, 232);
        var warm = Color.FromArgb(255, 249, 202);
        var hot = Color.FromArgb(255, 238, 145);
        var inferno = Color.FromArgb(255, 224, 118);
        return activity switch
        {
            FlameActivityLevel.Cool => Blend(frost, cool, progress),
            FlameActivityLevel.Warm => BlendThrough(cool, warmBridge, warm, progress),
            FlameActivityLevel.Hot => Blend(warm, hot, progress),
            FlameActivityLevel.Inferno => Blend(hot, inferno, progress),
            _ => frost
        };
    }

    private static float SmoothStep(float value)
    {
        value = Math.Clamp(value, 0f, 1f);
        return value * value * (3f - 2f * value);
    }

    private static GraphicsPath CreateFlamePath(float centerX, float baseY, float width, float height, float sway)
    {
        var topY = baseY - height;
        var path = new GraphicsPath();
        path.StartFigure();
        path.AddBezier(
            new PointF(centerX - width / 2f, baseY),
            new PointF(centerX - width * 0.76f, baseY - height * 0.33f),
            new PointF(centerX - width * 0.18f + sway, topY + height * 0.32f),
            new PointF(centerX + sway, topY));
        path.AddBezier(
            new PointF(centerX + sway, topY),
            new PointF(centerX + width * 0.16f + sway, topY + height * 0.26f),
            new PointF(centerX + width * 0.78f, baseY - height * 0.36f),
            new PointF(centerX + width / 2f, baseY));
        path.AddBezier(
            new PointF(centerX + width / 2f, baseY),
            new PointF(centerX + width * 0.18f, baseY + height * 0.08f),
            new PointF(centerX - width * 0.18f, baseY + height * 0.08f),
            new PointF(centerX - width / 2f, baseY));
        path.CloseFigure();
        return path;
    }

    private static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(from.A + (to.A - from.A) * amount),
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }

    private static Color BlendThrough(Color from, Color midpoint, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        const float midpointPosition = 0.48f;
        return amount <= midpointPosition
            ? Blend(from, midpoint, amount / midpointPosition)
            : Blend(midpoint, to, (amount - midpointPosition) / (1f - midpointPosition));
    }

    private void UpdateFlameTimer()
    {
        var hasMotion = _targetFlameIntensity > FlameActivity.FrozenMaximum ||
                        _flameIntensity > FlameActivity.FrozenMaximum;
        if (_flameAnimationEnabled && !_animationPaused && hasMotion && Visible && IsHandleCreated && !DesignMode)
            _flameTimer.Start();
        else
            _flameTimer.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _flameTimer.Dispose();
        base.Dispose(disposing);
    }

    private static void DrawArc(
        Graphics graphics,
        RectangleF bounds,
        float width,
        double? remaining,
        Color baseColor,
        Color trackColor)
    {
        const float start = -220;
        const float sweep = 260;
        using var track = new Pen(trackColor, width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawArc(track, bounds, start, sweep);
        if (remaining is null or <= 0) return;
        using var value = new Pen(baseColor, width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawArc(value, bounds, start, sweep * (float)(Math.Clamp(remaining.Value, 0, 100) / 100d));
    }

    private static PointF ArcEndpoint(RectangleF bounds, double? remaining)
    {
        const float start = -220f;
        const float sweep = 260f;
        var progress = Math.Clamp(remaining ?? 0d, 0d, 100d) / 100d;
        var angle = (start + sweep * progress) * Math.PI / 180d;
        return new PointF(
            bounds.Left + bounds.Width / 2f + bounds.Width / 2f * (float)Math.Cos(angle),
            bounds.Top + bounds.Height / 2f + bounds.Height / 2f * (float)Math.Sin(angle));
    }

    private static string FormatPercent(LimitBucket? bucket) => bucket is null
        ? "—"
        : $"{Math.Round(bucket.RemainingPercent):0}";

    private static Rectangle ScaleRectangle(RectangleF rectangle, float scale) => Rectangle.Round(new RectangleF(
        rectangle.X * scale,
        rectangle.Y * scale,
        rectangle.Width * scale,
        rectangle.Height * scale));
}
