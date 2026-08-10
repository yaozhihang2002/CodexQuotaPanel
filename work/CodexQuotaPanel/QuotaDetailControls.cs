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
    private Color _trendColor = UiPalette.Mint;

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
        if (trend.Length >= 2)
        {
            DrawTrend(e.Graphics, new RectangleF(S(12), S(27), Width - S(24), S(16)), trend, _trendColor, scale);
        }
        else
        {
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
    }

    private static void DrawTrend(
        Graphics graphics,
        RectangleF bounds,
        IReadOnlyList<QuotaHistoryPoint> points,
        Color color,
        float scale)
    {
        using var background = new SolidBrush(Color.FromArgb(55, UiPalette.Track));
        using var baseline = new Pen(Color.FromArgb(70, UiPalette.Border), Math.Max(1f, scale));
        graphics.FillRectangle(background, bounds);
        graphics.DrawLine(baseline, bounds.Left, bounds.Top + bounds.Height / 2f, bounds.Right, bounds.Top + bounds.Height / 2f);

        var nowMinute = DateTimeOffset.Now.ToUniversalTime().ToUnixTimeSeconds() / 60;
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
                 ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        UseVisualStyleBackColor = false;
        BackColor = Color.Transparent;
        Cursor = Cursors.Hand;
        TabStop = true;
        Font = UiPalette.Body(8.2f, FontStyle.Bold);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new RectangleF(0.5f, 0.5f, Width - 1, Height - 1);
        using var path = UiPalette.RoundedRect(bounds, 8);
        var background = Primary
            ? (_pressed
                ? UiPalette.Mix(UiPalette.Text, UiPalette.Canvas, 0.24f)
                : _hovered
                    ? UiPalette.Mix(UiPalette.Text, UiPalette.Canvas, 0.1f)
                    : UiPalette.Text)
            : (_pressed ? UiPalette.Track : _hovered ? UiPalette.SurfaceRaised : UiPalette.Surface);
        using var fill = new SolidBrush(background);
        e.Graphics.FillPath(fill, path);
        if (!Primary)
        {
            using var border = new Pen(UiPalette.Border, 1);
            e.Graphics.DrawPath(border, path);
        }

        var foreground = Primary ? UiPalette.Canvas : UiPalette.Text;
        using var textBrush = new SolidBrush(foreground);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            Trimming = StringTrimming.EllipsisCharacter,
            FormatFlags = StringFormatFlags.NoWrap
        };
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        e.Graphics.DrawString(Text, Font, textBrush, ClientRectangle, format);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = true; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
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
