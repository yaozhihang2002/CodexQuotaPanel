using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace CodexQuotaPanel;

internal sealed class QuotaRingControl : Control
{
    private double _remaining;
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public double Remaining
    {
        get => _remaining;
        set { _remaining = Math.Clamp(value, 0d, 100d); Invalidate(); }
    }

    public QuotaRingControl()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
        Size = new Size(118, 118);
        AccessibleName = L10n.Pick("最紧额度剩余百分比", "Tightest quota remaining percentage");
    }

    public void ApplyLanguage()
    {
        AccessibleName = L10n.Pick("最紧额度剩余百分比", "Tightest quota remaining percentage");
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = Math.Max(0.5f, Math.Min(Width, Height) / 118f);
        var bounds = new RectangleF(10 * scale, 10 * scale, Width - 20 * scale, Height - 20 * scale);
        using var track = new Pen(UiPalette.Track, 8 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var value = new Pen(UiPalette.ForRemaining(Remaining), 8 * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawArc(track, bounds, -225, 270);
        e.Graphics.DrawArc(value, bounds, -225, (float)(270 * Remaining / 100d));

        var percent = $"{Math.Round(Remaining):0}%";
        using var numberFont = UiPalette.Display(25, FontStyle.Bold);
        using var labelFont = UiPalette.Body(8.3f, FontStyle.Bold);
        using var textBrush = new SolidBrush(UiPalette.Text);
        using var mutedBrush = new SolidBrush(UiPalette.Muted);
        var numberSize = e.Graphics.MeasureString(percent, numberFont);
        var numberY = (Height - numberSize.Height) / 2f - 5 * scale;
        e.Graphics.DrawString(percent, numberFont, textBrush, (Width - numberSize.Width) / 2f, numberY);
        var label = L10n.Remaining;
        var labelSize = e.Graphics.MeasureString(label, labelFont);
        e.Graphics.DrawString(label, labelFont, mutedBrush, (Width - labelSize.Width) / 2f,
            numberY + numberSize.Height - scale);
    }
}

internal sealed class LimitRowControl : Control
{
    private LimitBucket? _bucket;
    private string _label = L10n.FormatWindow(null);
    private IReadOnlyList<QuotaHistoryPoint> _history = [];
    private IReadOnlyList<QuotaHistoryPoint> _visibleTrend = [];
    private Color _trendColor = UiPalette.Mint;
    private RectangleF _trendBounds = RectangleF.Empty;
    private QuotaHistoryPoint? _hoveredTrendPoint;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int HistorySlot { get; set; }

    public LimitRowControl()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.UserPaint, true);
        BackColor = Color.Transparent;
        Height = 66;
        AccessibleRole = AccessibleRole.ProgressBar;
    }

    public void SetBucket(LimitBucket? bucket)
    {
        _bucket = bucket;
        _hoveredTrendPoint = null;
        _label = FormatWindow(bucket?.WindowMinutes);
        AccessibleName = _bucket is null ? _label : L10n.Pick(
            $"{_label}，剩余 {Math.Round(_bucket.RemainingPercent):0}%",
            $"{_label}, {Math.Round(_bucket.RemainingPercent):0}% remaining");
        Invalidate();
    }

    public void Tick() => Invalidate();

    public void SetHistory(IReadOnlyList<QuotaHistoryPoint> history, Color trendColor)
    {
        _history = history;
        _trendColor = trendColor;
        _hoveredTrendPoint = null;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = Math.Max(0.5f, DeviceDpi / 96f);
        float S(float value) => value * scale;
        using var cardPath = UiPalette.RoundedRect(
            new RectangleF(S(0.5f), S(0.5f), Width - S(1), Height - S(1)), S(10));
        using var cardBrush = new SolidBrush(UiPalette.Surface);
        using var borderPen = new Pen(UiPalette.Border, Math.Max(1f, scale));
        e.Graphics.FillPath(cardBrush, cardPath);
        e.Graphics.DrawPath(borderPen, cardPath);

        using var labelFont = UiPalette.Display(11f, FontStyle.Bold);
        using var valueFont = UiPalette.Mono(9.5f, FontStyle.Bold);
        using var detailFont = UiPalette.Body(8.2f);
        using var textBrush = new SolidBrush(UiPalette.Text);
        using var mutedBrush = new SolidBrush(UiPalette.Muted);

        e.Graphics.DrawString(_label, labelFont, textBrush, S(12), S(7));
        var remainingText = _bucket is null ? "—" : L10n.Pick(
            $"{Math.Round(_bucket.RemainingPercent):0}% 剩余",
            $"{Math.Round(_bucket.RemainingPercent):0}% left");
        var remainingSize = e.Graphics.MeasureString(remainingText, valueFont);
        var color = _bucket is null ? UiPalette.Muted : UiPalette.ForRemaining(_bucket.RemainingPercent);
        using var valueBrush = new SolidBrush(color);
        e.Graphics.DrawString(remainingText, valueFont, valueBrush, Width - remainingSize.Width - S(12), S(8));

        var cutoffMinute = DateTimeOffset.Now.ToUniversalTime().ToUnixTimeSeconds() / 60 - 24 * 60;
        var trend = _bucket?.WindowMinutes is > 0
            ? _history.Where(point => point.Slot == HistorySlot && point.WindowMinutes == _bucket.WindowMinutes.Value)
                .Where(point => point.UtcMinute >= cutoffMinute)
                .OrderBy(point => point.UtcMinute)
                .ToArray()
            : [];
        _visibleTrend = trend;
        if (trend.Length >= 2)
        {
            _trendBounds = new RectangleF(S(12), S(27), Width - S(24), S(16));
            var nowMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
            DrawTrend(e.Graphics, _trendBounds, trend, _trendColor, scale, nowMinute);
            if (_hoveredTrendPoint is not null)
                DrawTrendHover(e.Graphics, _trendBounds, _hoveredTrendPoint, _trendColor, scale, nowMinute);
        }
        else
        {
            _trendBounds = RectangleF.Empty;
            _hoveredTrendPoint = null;
            var trackRect = new RectangleF(S(12), S(30), Width - S(24), S(5));
            using var trackPath = UiPalette.RoundedRect(trackRect, S(2.5f));
            using var trackBrush = new SolidBrush(UiPalette.Track);
            e.Graphics.FillPath(trackBrush, trackPath);
            if (_bucket is not null && _bucket.RemainingPercent > 0)
            {
                var fillWidth = Math.Max(S(6), trackRect.Width * (float)(_bucket.RemainingPercent / 100d));
                using var fillPath = UiPalette.RoundedRect(
                    new RectangleF(trackRect.X, trackRect.Y, fillWidth, trackRect.Height), S(2.5f));
                using var fillBrush = new SolidBrush(color);
                e.Graphics.FillPath(fillBrush, fillPath);
            }
        }

        var detail = _bucket is null ? L10n.WaitingSnapshot : FormatReset(_bucket.ResetsAt);
        e.Graphics.DrawString(detail, detailFont, mutedBrush, S(12), S(48));
        using var trendFont = UiPalette.Mono(6.8f, FontStyle.Bold);
        var trendLabel = trend.Length >= 2 ? L10n.Trend24Hours : L10n.TrendAccumulating;
        var trendSize = e.Graphics.MeasureString(trendLabel, trendFont);
        e.Graphics.DrawString(trendLabel, trendFont, mutedBrush, Width - trendSize.Width - S(12), S(50));

        if (_hoveredTrendPoint is not null)
            DrawTrendHoverLabel(e.Graphics, _hoveredTrendPoint, _trendColor, scale);
    }

    private static void DrawTrend(
        Graphics graphics,
        RectangleF bounds,
        IReadOnlyList<QuotaHistoryPoint> points,
        Color color,
        float scale,
        long nowMinute)
    {
        using var background = new SolidBrush(Color.FromArgb(55, UiPalette.Track));
        using var baseline = new Pen(Color.FromArgb(70, UiPalette.Border), Math.Max(1f, scale));
        graphics.FillRectangle(background, bounds);
        graphics.DrawLine(baseline, bounds.Left, bounds.Top + bounds.Height / 2f, bounds.Right, bounds.Top + bounds.Height / 2f);

        var cutoff = nowMinute - 24 * 60;
        using var line = new Pen(color, 1.6f * scale) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        PointF? previousPoint = null;
        QuotaHistoryPoint? previousSample = null;
        PointF? lastPoint = null;
        foreach (var sample in points.Where(point => point.UtcMinute >= cutoff))
        {
            var x = bounds.Left + (float)Math.Clamp((sample.UtcMinute - cutoff) / (24d * 60d), 0d, 1d) * bounds.Width;
            var y = bounds.Top + (float)((100d - sample.RemainingPercent) / 100d) * bounds.Height;
            var current = new PointF(x, y);
            if (previousPoint is not null && previousSample is not null &&
                sample.UtcMinute - previousSample.UtcMinute <= 15 &&
                sample.RemainingPercent - previousSample.RemainingPercent <= 20)
            {
                graphics.DrawLine(line, previousPoint.Value, current);
            }
            previousPoint = current;
            previousSample = sample;
            lastPoint = current;
        }
        if (lastPoint is null) return;
        using var dot = new SolidBrush(color);
        graphics.FillEllipse(dot,
            lastPoint.Value.X - 2.2f * scale,
            lastPoint.Value.Y - 2.2f * scale,
            4.4f * scale,
            4.4f * scale);
    }

    private static void DrawTrendHover(
        Graphics graphics,
        RectangleF bounds,
        QuotaHistoryPoint sample,
        Color color,
        float scale,
        long nowMinute)
    {
        var point = TrendPoint(bounds, sample, nowMinute);
        using var guide = new Pen(Color.FromArgb(105, color), Math.Max(1f, scale * 0.8f))
        {
            DashStyle = DashStyle.Dot
        };
        graphics.DrawLine(guide, point.X, bounds.Top - 1f * scale, point.X, bounds.Bottom + 1f * scale);

        using var halo = new SolidBrush(Color.FromArgb(74, color));
        using var dot = new SolidBrush(color);
        graphics.FillEllipse(halo, point.X - 5f * scale, point.Y - 5f * scale, 10f * scale, 10f * scale);
        graphics.FillEllipse(dot, point.X - 2.5f * scale, point.Y - 2.5f * scale, 5f * scale, 5f * scale);
    }

    private void DrawTrendHoverLabel(Graphics graphics, QuotaHistoryPoint sample, Color color, float scale)
    {
        var text = FormatTrendHoverText(sample);
        using var font = UiPalette.Body(7.2f, FontStyle.Bold);
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        var measured = TextRenderer.MeasureText(graphics, text, font, Size.Empty, flags);
        var horizontalPadding = 9f * scale;
        var bubbleWidth = Math.Min(Width - 16f * scale, measured.Width + horizontalPadding * 2f);
        var bubbleHeight = Math.Max(20f * scale, measured.Height + 6f * scale);
        var nowMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var point = TrendPoint(_trendBounds, sample, nowMinute);
        var bubbleX = Math.Clamp(point.X - bubbleWidth / 2f, 8f * scale, Width - bubbleWidth - 8f * scale);
        var bubbleY = Height - bubbleHeight - 4f * scale;
        var bubbleBounds = new RectangleF(bubbleX, bubbleY, bubbleWidth, bubbleHeight);

        using var shadowPath = UiPalette.RoundedRect(
            new RectangleF(bubbleBounds.X, bubbleBounds.Y + 1.5f * scale, bubbleBounds.Width, bubbleBounds.Height),
            7f * scale);
        using var shadow = new SolidBrush(Color.FromArgb(
            UiPalette.Canvas.GetBrightness() < 0.5f ? 88 : 30,
            Color.Black));
        graphics.FillPath(shadow, shadowPath);

        using var bubblePath = UiPalette.RoundedRect(bubbleBounds, 7f * scale);
        using var fill = new SolidBrush(UiPalette.SurfaceRaised);
        using var border = new Pen(UiPalette.Mix(UiPalette.Border, color, 0.44f), Math.Max(1f, scale));
        graphics.FillPath(fill, bubblePath);
        graphics.DrawPath(border, bubblePath);

        TextRenderer.DrawText(graphics, text, font, Rectangle.Round(bubbleBounds), UiPalette.Text,
            flags | TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
    }

    private static PointF TrendPoint(RectangleF bounds, QuotaHistoryPoint sample, long nowMinute)
    {
        var cutoff = nowMinute - 24 * 60;
        var x = bounds.Left + (float)Math.Clamp((sample.UtcMinute - cutoff) / (24d * 60d), 0d, 1d) * bounds.Width;
        var y = bounds.Top + (float)((100d - sample.RemainingPercent) / 100d) * bounds.Height;
        return new PointF(x, y);
    }

    internal static string FormatTrendHoverText(QuotaHistoryPoint sample)
    {
        var local = sample.Timestamp.ToLocalTime();
        return L10n.Pick(
            $"{local.Month}月{local.Day}日 {local:HH:mm}  ·  剩余 {sample.RemainingPercent:0.#}%",
            $"{local:MMM d, HH:mm}  ·  {sample.RemainingPercent:0.#}% left");
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var hitBounds = _trendBounds;
        hitBounds.Inflate(0, Math.Max(4f, DeviceDpi / 96f * 4f));
        if (_visibleTrend.Count < 2 || !hitBounds.Contains(e.Location))
        {
            ClearTrendHover();
            return;
        }

        var nowMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var targetMinute = nowMinute - 24 * 60 +
            (long)Math.Round(Math.Clamp((e.X - _trendBounds.Left) / _trendBounds.Width, 0f, 1f) * 24d * 60d);
        var nearest = _visibleTrend.MinBy(point => Math.Abs(point.UtcMinute - targetMinute));
        if (nearest is null)
        {
            ClearTrendHover();
            return;
        }

        var nearestPosition = TrendPoint(_trendBounds, nearest, nowMinute);
        var maximumSnapDistance = Math.Max(18f, DeviceDpi / 96f * 18f);
        if (Math.Abs(nearestPosition.X - e.X) > maximumSnapDistance)
        {
            ClearTrendHover();
            return;
        }

        Cursor = Cursors.Cross;
        if (Equals(_hoveredTrendPoint, nearest)) return;
        _hoveredTrendPoint = nearest;
        AccessibleDescription = FormatTrendHoverText(nearest);
        Invalidate();
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        ClearTrendHover();
        base.OnMouseLeave(e);
    }

    private void ClearTrendHover()
    {
        if (_hoveredTrendPoint is null)
        {
            Cursor = Cursors.Default;
            return;
        }

        _hoveredTrendPoint = null;
        Cursor = Cursors.Default;
        AccessibleDescription = null;
        Invalidate();
    }

    internal string? ShowTrendHoverForTest(int sampleIndex)
    {
        if (_visibleTrend.Count < 2) return null;
        var sample = _visibleTrend[Math.Clamp(sampleIndex, 0, _visibleTrend.Count - 1)];
        var nowMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        var point = TrendPoint(_trendBounds, sample, nowMinute);
        OnMouseMove(new MouseEventArgs(MouseButtons.None, 0,
            (int)Math.Round(point.X), (int)Math.Round(point.Y), 0));
        return _hoveredTrendPoint is null ? null : FormatTrendHoverText(_hoveredTrendPoint);
    }

    public static string FormatWindow(int? minutes) => L10n.FormatWindow(minutes);

    private static string FormatReset(DateTimeOffset? reset)
    {
        if (reset is null) return L10n.ResetUnknown;
        var remaining = reset.Value - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero) return L10n.WaitingRefresh;
        if (remaining.TotalDays >= 1)
            return L10n.Pick(
                $"{(int)remaining.TotalDays}天 {remaining.Hours:00}:{remaining.Minutes:00} 后 · {L10n.FormatLocalDate(reset.Value)}",
                $"{(int)remaining.TotalDays}d {remaining.Hours:00}:{remaining.Minutes:00} · {L10n.FormatLocalDate(reset.Value)}");
        return L10n.Pick(
            $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00} 后 · {reset.Value.ToLocalTime():HH:mm}",
            $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00} · {reset.Value.ToLocalTime():HH:mm}");
    }
}

internal sealed class PillLabel : Label
{
    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color PillColor { get; set; } = UiPalette.Mint;

    public PillLabel()
    {
        AutoSize = false;
        TextAlign = ContentAlignment.MiddleCenter;
        Font = UiPalette.Mono(7f, FontStyle.Bold);
        ForeColor = UiPalette.Mint;
        BackColor = Color.Transparent;
        DoubleBuffered = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiPalette.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), Height / 2f - 1);
        using var background = new SolidBrush(Color.FromArgb(26, PillColor));
        using var border = new Pen(Color.FromArgb(95, PillColor));
        e.Graphics.FillPath(background, path);
        e.Graphics.DrawPath(border, path);
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, PillColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
    }
}

internal sealed class ActionButton : Button
{
    private bool _hovered;
    private bool _pressed;
    private bool _primary;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public bool Primary
    {
        get => _primary;
        set { _primary = value; Invalidate(); }
    }

    public ActionButton()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.SupportsTransparentBackColor, false);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = UiPalette.Surface;
        Cursor = Cursors.Hand;
        TabStop = true;
        Font = UiPalette.Body(8.2f, FontStyle.Bold);
    }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        e.Graphics.Clear(UiPalette.ResolveControlBackground(this, BackColor));

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(UiPalette.ResolveControlBackground(this, BackColor));
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var bounds = new RectangleF(1f, 1f, Math.Max(1f, Width - 2f), Math.Max(1f, Height - 2f));
        var radius = Math.Min(8f, Math.Max(2f, bounds.Height / 2f - 1f));
        using var path = UiPalette.RoundedRect(bounds, radius);
        var background = !Enabled
            ? UiPalette.Mix(UiPalette.Surface, UiPalette.Track, 0.34f)
            : Primary
                ? (_pressed
                    ? UiPalette.Mix(UiPalette.Text, UiPalette.Canvas, 0.24f)
                    : _hovered
                        ? UiPalette.Mix(UiPalette.Text, UiPalette.Canvas, 0.08f)
                        : UiPalette.Text)
                : (_pressed
                    ? UiPalette.Mix(UiPalette.Track, UiPalette.SurfaceRaised, 0.34f)
                    : _hovered
                        ? UiPalette.SurfaceRaised
                        : UiPalette.Surface);
        using var fill = new SolidBrush(background);
        e.Graphics.FillPath(fill, path);

        var borderColor = !Enabled
            ? UiPalette.Mix(UiPalette.Border, UiPalette.Surface, 0.45f)
            : Primary
                ? UiPalette.Mix(UiPalette.Text, UiPalette.Mint, _hovered ? 0.20f : 0.08f)
                : _hovered
                    ? UiPalette.Mix(UiPalette.Border, UiPalette.Mint, 0.22f)
                    : UiPalette.Border;
        using var border = new Pen(borderColor, 1f);
        e.Graphics.DrawPath(border, path);

        using var highlightPath = UiPalette.RoundedRect(
            new RectangleF(bounds.X + 1f, bounds.Y + 1f,
                Math.Max(1f, bounds.Width - 2f), Math.Max(1f, bounds.Height - 2f)),
            Math.Max(1f, radius - 1f));
        using var highlight = new Pen(Color.FromArgb(Primary ? 30 : 18, Color.White), 1f);
        e.Graphics.DrawPath(highlight, highlightPath);

        var foreground = !Enabled ? UiPalette.Faint : Primary ? UiPalette.Canvas : UiPalette.Text;
        using var textBrush = new SolidBrush(foreground);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var textBounds = Rectangle.Inflate(ClientRectangle, -8, -2);
        e.Graphics.DrawString(Text, Font, textBrush, textBounds, format);

        if (Focused && ShowFocusCues)
        {
            using var focusPath = UiPalette.RoundedRect(
                new RectangleF(bounds.X + 2.5f, bounds.Y + 2.5f,
                    Math.Max(1f, bounds.Width - 5f), Math.Max(1f, bounds.Height - 5f)),
                Math.Max(1f, radius - 2.5f));
            using var focus = new Pen(Color.FromArgb(170, UiPalette.Sky), 1f)
            {
                DashStyle = DashStyle.Dot
            };
            e.Graphics.DrawPath(focus, focusPath);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { if (e.Button == MouseButtons.Left) _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnEnabledChanged(EventArgs e) { _pressed = false; Invalidate(); base.OnEnabledChanged(e); }
}

internal sealed class AppToolStripRenderer : ToolStripProfessionalRenderer
{
    public AppToolStripRenderer() : base(new AppToolStripColorTable())
    {
        RoundedEdges = false;
    }

    protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
    {
        e.TextColor = e.Item.Enabled ? UiPalette.Text : UiPalette.Muted;
        base.OnRenderItemText(e);
    }

    protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
    {
        using var pen = new Pen(UiPalette.Border);
        var y = e.Item.Height / 2;
        e.Graphics.DrawLine(pen, 9, y, Math.Max(9, e.Item.Width - 9), y);
    }

    protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
    {
        e.ArrowColor = e.Item?.Enabled != false ? UiPalette.Text : UiPalette.Muted;
        base.OnRenderArrow(e);
    }

    private sealed class AppToolStripColorTable : ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground => UiPalette.Surface;
        public override Color MenuBorder => UiPalette.Border;
        public override Color MenuItemBorder => UiPalette.Mint;
        public override Color MenuItemSelected => UiPalette.SurfaceRaised;
        public override Color MenuItemSelectedGradientBegin => UiPalette.SurfaceRaised;
        public override Color MenuItemSelectedGradientEnd => UiPalette.SurfaceRaised;
        public override Color MenuItemPressedGradientBegin => UiPalette.SurfaceRaised;
        public override Color MenuItemPressedGradientMiddle => UiPalette.SurfaceRaised;
        public override Color MenuItemPressedGradientEnd => UiPalette.SurfaceRaised;
        public override Color CheckBackground => UiPalette.SurfaceRaised;
        public override Color CheckSelectedBackground => UiPalette.SurfaceRaised;
        public override Color CheckPressedBackground => UiPalette.SurfaceRaised;
        public override Color ImageMarginGradientBegin => UiPalette.Surface;
        public override Color ImageMarginGradientMiddle => UiPalette.Surface;
        public override Color ImageMarginGradientEnd => UiPalette.Surface;
        public override Color SeparatorDark => UiPalette.Border;
        public override Color SeparatorLight => UiPalette.Border;
    }
}
