using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CodexQuotaPanel;

internal static class NativeInputIdle
{
    [StructLayout(LayoutKind.Sequential)]
    private struct LastInputInfo
    {
        internal uint Size;
        internal uint Tick;
    }

    internal static bool IsIdleFor(uint milliseconds)
    {
        var information = new LastInputInfo { Size = (uint)Marshal.SizeOf<LastInputInfo>() };
        return GetLastInputInfo(ref information) &&
               unchecked((uint)Environment.TickCount - information.Tick) >= milliseconds;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetLastInputInfo(ref LastInputInfo information);
}

internal sealed class BufferedSettingsHost : Panel
{
    private const int WsExComposited = 0x02000000;

    public BufferedSettingsHost()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.Opaque, true);
        UpdateStyles();
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            // Native child controls are composed only inside the scrollable
            // content viewport. Keeping WS_EX_COMPOSITED off the complete form
            // avoids serializing title, navigation and footer painting.
            parameters.ExStyle |= WsExComposited;
            return parameters;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        e.Graphics.Clear(BackColor);

    protected override void OnPaint(PaintEventArgs e)
    {
        // ControlStyles.Opaque skips the normal background pass. Clear during
        // the real paint pass so live dark/light switching cannot expose the
        // default black surface in the host padding.
        e.Graphics.Clear(BackColor);
        base.OnPaint(e);
    }
}

internal sealed class ResponsiveSettingsPage : Panel
{
    private readonly TableLayoutPanel _content;
    private bool _repaintQueued;
    private bool _building = true;

    public ResponsiveSettingsPage()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        SetStyle(ControlStyles.Opaque, false);
        Dock = DockStyle.Fill;
        AutoScroll = true;
        BackColor = UiPalette.Canvas;
        Margin = Padding.Empty;
        Padding = new Padding(2, 2, 7, 10);

        _content = new BufferedTableLayoutPanel
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
        SuspendLayout();
        _content.SuspendLayout();
    }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        e.Graphics.Clear(BackColor);

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
    }

    protected override void OnScroll(ScrollEventArgs se)
    {
        base.OnScroll(se);
        // ScrollableControl moves child windows before the background is repainted.
        // Coalesce wheel messages, then erase and redraw the viewport plus every
        // child in one native paint pass so copied text cannot survive as trails.
        if (_repaintQueued || !IsHandleCreated) return;
        _repaintQueued = true;
        BeginInvoke((Action)(() =>
        {
            _repaintQueued = false;
            if (!IsDisposed) NativeRedrawScope.RedrawNow(this);
        }));
    }

    public void AddItem(Control control)
    {
        control.Dock = DockStyle.Fill;
        var rowIndex = _content.RowCount;
        _content.RowCount = rowIndex + 1;
        _content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _content.Controls.Add(control, 0, rowIndex);
    }

    internal void CompleteBuild()
    {
        if (!_building) return;
        _building = false;
        _content.ResumeLayout(performLayout: false);
        ResumeLayout(performLayout: false);
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
            UiPalette.Mix(UiPalette.SurfaceRaised, UiPalette.Mint, 0.035f),
            UiPalette.Surface,
            LinearGradientMode.Horizontal);
        e.Graphics.FillRectangle(gradient, ClientRectangle);
        using var line = new Pen(UiPalette.Mix(UiPalette.Border, UiPalette.Canvas, 0.22f));
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
        var separator = " / ";
        var product = L10n.Pick("额度面板", "Quota Panel");
        var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
        using var productFont = UiPalette.Body(
            Math.Max(8f, Font.SizeInPoints * 0.78f), FontStyle.Bold);
        var brandSize = TextRenderer.MeasureText(e.Graphics, brand, Font, Size.Empty, flags);
        var separatorSize = TextRenderer.MeasureText(e.Graphics, separator, Font, Size.Empty, flags);
        var productSize = TextRenderer.MeasureText(e.Graphics, product, productFont, Size.Empty, flags);
        var totalWidth = brandSize.Width + separatorSize.Width + productSize.Width;
        var x = 0;
        var y = Math.Max(0, (Height - Math.Max(brandSize.Height, productSize.Height)) / 2);

        TextRenderer.DrawText(e.Graphics, brand, Font,
            new Point(x, y), UiPalette.Text, flags);
        x += brandSize.Width;
        TextRenderer.DrawText(e.Graphics, separator, Font,
            new Point(x, y), UiPalette.Mint, flags);
        x += separatorSize.Width;
        TextRenderer.DrawText(e.Graphics, product, productFont,
            new Point(x, y), UiPalette.Muted, flags);

        if (totalWidth > Width)
            TextRenderer.DrawText(e.Graphics, L10n.Pick("Codex / 额度面板", "Codex / Quota Panel"), Font,
                ClientRectangle, UiPalette.Text,
                flags | TextFormatFlags.EndEllipsis | TextFormatFlags.VerticalCenter);
    }
}

internal sealed class SettingsCard : Panel
{
    private int _baselineHeight;

    public SettingsCard()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = UiPalette.Canvas;
    }

    internal void ApplyTypographyDensity(int scalePercent)
    {
        if (_baselineHeight <= 0) _baselineHeight = Math.Max(1, Height);
        // Only vertical reading room grows, and at half the typography rate.
        // This preserves both text lines at 150% without zooming the window,
        // card width, gutters, and every other piece of geometry.
        var density = scalePercent <= 100
            ? 1f
            : 1f + (scalePercent - 100) / 200f;
        Height = (int)Math.Ceiling(_baselineHeight * density);
    }

    protected override void OnPaintBackground(PaintEventArgs e) =>
        e.Graphics.Clear(UiPalette.Canvas);

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiPalette.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 13);
        using var fill = new LinearGradientBrush(
            ClientRectangle,
            UiPalette.Mix(UiPalette.Surface, UiPalette.SurfaceRaised, 0.34f),
            UiPalette.Surface,
            LinearGradientMode.Horizontal);
        using var border = new Pen(UiPalette.Mix(UiPalette.Border, UiPalette.Canvas, 0.25f));
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        using var highlight = new Pen(Color.FromArgb(28, UiPalette.Mint));
        e.Graphics.DrawLine(highlight, 14, 1.5f, Math.Max(14, Width - 14), 1.5f);
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
        BackColor = UiPalette.Surface;
        ForeColor = UiPalette.Text;
        Font = UiPalette.Body(8.2f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        TextAlign = ContentAlignment.MiddleLeft;
        Padding = new Padding(22, 0, 10, 0);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(BackColor);
        if (_active || _hovered)
        {
            using var path = UiPalette.RoundedRect(new RectangleF(0, 1, Width - 1, Height - 2), 10);
            using var fill = new SolidBrush(_active
                ? UiPalette.Mix(UiPalette.SurfaceRaised, UiPalette.Mint, 0.075f)
                : UiPalette.SurfaceRaised);
            e.Graphics.FillPath(fill, path);
        }
        if (_active)
        {
            using var rail = UiPalette.RoundedRect(new RectangleF(6, 10, 3, Height - 20), 1.5f);
            using var fill = new SolidBrush(UiPalette.Mint);
            e.Graphics.FillPath(fill, rail);
        }
        var textBounds = new Rectangle(22, 0, Math.Max(0, Width - 32), Height);
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds,
            _active ? UiPalette.Text : UiPalette.Muted,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
}

internal sealed class SettingsChoiceButton : Button
{
    private bool _active;
    private bool _hovered;

    [System.ComponentModel.Browsable(false)]
    [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            Invalidate();
        }
    }

    public SettingsChoiceButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        BackColor = UiPalette.Surface;
        ForeColor = UiPalette.Text;
        Font = UiPalette.Body(7.6f, FontStyle.Bold);
        Cursor = Cursors.Hand;
        TabStop = true;
        UseMnemonic = false;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(UiPalette.Surface);
        var bounds = new RectangleF(0.5f, 0.5f, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
        using var path = UiPalette.RoundedRect(bounds, 9);
        var fillColor = !Enabled
            ? UiPalette.Mix(UiPalette.Surface, UiPalette.Track, 0.24f)
            : _active
                ? UiPalette.Mix(UiPalette.SurfaceRaised, UiPalette.Mint, _hovered ? 0.18f : 0.12f)
                : _hovered
                    ? UiPalette.SurfaceRaised
                    : UiPalette.Mix(UiPalette.Surface, UiPalette.SurfaceRaised, 0.34f);
        var borderColor = _active && Enabled
            ? UiPalette.Mix(UiPalette.Border, UiPalette.Mint, 0.72f)
            : UiPalette.Mix(UiPalette.Border, UiPalette.Surface, 0.18f);
        using var fill = new SolidBrush(fillColor);
        using var border = new Pen(borderColor, _active ? 1.35f : 1f);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);

        var textBounds = new Rectangle(8, 1, Math.Max(0, Width - 16), Math.Max(0, Height - 2));
        var textColor = !Enabled ? UiPalette.Faint : _active ? UiPalette.Text : UiPalette.Muted;
        TextRenderer.DrawText(e.Graphics, Text, Font, textBounds, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);

        if (Focused && ShowFocusCues)
        {
            using var focusPath = UiPalette.RoundedRect(new RectangleF(2.5f, 2.5f,
                Math.Max(1, Width - 5), Math.Max(1, Height - 5)), 7);
            using var focus = new Pen(UiPalette.Sky) { DashStyle = DashStyle.Dot };
            e.Graphics.DrawPath(focus, focusPath);
        }
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
}

internal sealed class BaselineSafeLinkLabel : LinkLabel
{
    private bool _hovered;
    private bool _pressed;
    private Font? _latinFont;

    internal string PrefixText = string.Empty;
    internal string ProjectText = string.Empty;

    public BaselineSafeLinkLabel()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.SupportsTransparentBackColor, true);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(UiPalette.SurfaceRaised);
        var color = !Enabled
            ? UiPalette.Faint
            : _pressed
                ? ActiveLinkColor
                : LinkColor;
        _latinFont ??= UiPalette.LatinBody(Font.SizeInPoints, Font.Style);
        using var format = new StringFormat(StringFormat.GenericTypographic)
        {
            Alignment = StringAlignment.Near,
            FormatFlags = StringFormatFlags.NoWrap,
            HotkeyPrefix = System.Drawing.Text.HotkeyPrefix.None
        };

        using var prefixPath = CreateGlyphPath(
            string.IsNullOrEmpty(PrefixText) ? Text : PrefixText, Font, e.Graphics, format);
        using var projectPath = CreateGlyphPath(ProjectText, _latinFont, e.Graphics, format);
        var prefixBounds = prefixPath.GetBounds();
        var projectBounds = projectPath.PointCount == 0 ? RectangleF.Empty : projectPath.GetBounds();
        var usableTop = Padding.Top + 1f;
        var usableBottom = Math.Max(usableTop, ClientSize.Height - Padding.Bottom - 1f);
        var centerY = (usableTop + usableBottom) / 2f;
        var x = (float)Padding.Left;
        PositionGlyphPath(prefixPath, prefixBounds, x, centerY);
        var gap = Math.Max(4f, e.Graphics.DpiX / 96f * 5f);
        x += prefixBounds.Width + gap;
        PositionGlyphPath(projectPath, projectBounds, x, centerY);

        var previousSmoothing = e.Graphics.SmoothingMode;
        var previousPixelOffset = e.Graphics.PixelOffsetMode;
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        using var brush = new SolidBrush(color);
        e.Graphics.FillPath(brush, prefixPath);
        e.Graphics.FillPath(brush, projectPath);
        if (_hovered)
        {
            var combinedBounds = RectangleF.Union(prefixPath.GetBounds(), projectPath.GetBounds());
            var underlineY = Math.Min(ClientSize.Height - Padding.Bottom - 1f, combinedBounds.Bottom + 1f);
            using var underline = new Pen(color, Math.Max(1f, e.Graphics.DpiY / 96f));
            e.Graphics.DrawLine(underline, combinedBounds.Left, underlineY,
                Math.Min(ClientSize.Width - Padding.Right, combinedBounds.Right), underlineY);
        }
        e.Graphics.SmoothingMode = previousSmoothing;
        e.Graphics.PixelOffsetMode = previousPixelOffset;

        if (Focused && ShowFocusCues)
        {
            var focus = Rectangle.Inflate(ClientRectangle, -2, -2);
            ControlPaint.DrawFocusRectangle(e.Graphics, focus, color, Color.Transparent);
        }
    }

    private static GraphicsPath CreateGlyphPath(
        string text,
        Font font,
        Graphics graphics,
        StringFormat format)
    {
        var path = new GraphicsPath();
        if (string.IsNullOrEmpty(text)) return path;
        var emSize = font.SizeInPoints * graphics.DpiY / 72f;
        var outlineStyle = font.Style & (FontStyle.Bold | FontStyle.Italic);
        path.AddString(text, font.FontFamily, (int)outlineStyle, emSize, PointF.Empty, format);
        return path;
    }

    private static void PositionGlyphPath(
        GraphicsPath path,
        RectangleF bounds,
        float left,
        float centerY)
    {
        if (path.PointCount == 0) return;
        using var transform = new Matrix();
        transform.Translate(left - bounds.Left, centerY - (bounds.Top + bounds.Height / 2f));
        path.Transform(transform);
    }

    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; _pressed = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnMouseDown(MouseEventArgs e) { _pressed = e.Button == MouseButtons.Left; Invalidate(); base.OnMouseDown(e); }
    protected override void OnMouseUp(MouseEventArgs e) { _pressed = false; Invalidate(); base.OnMouseUp(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }
    protected override void OnFontChanged(EventArgs e)
    {
        _latinFont?.Dispose();
        _latinFont = null;
        Invalidate();
        base.OnFontChanged(e);
    }
    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _latinFont?.Dispose();
            _latinFont = null;
        }
        base.Dispose(disposing);
    }
}

internal sealed class SettingsSlider : Control
{
    private int _minimum;
    private int _maximum = 100;
    private int _value;
    private bool _dragging;
    private bool _hovered;

    public SettingsSlider()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                 ControlStyles.Selectable, true);
        TabStop = true;
        Cursor = Cursors.Hand;
        AccessibleRole = AccessibleRole.Slider;
        Size = new Size(160, 30);
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Minimum
    {
        get => _minimum;
        set
        {
            _minimum = value;
            if (_maximum < value) _maximum = value;
            Value = _value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Maximum
    {
        get => _maximum;
        set
        {
            _maximum = Math.Max(value, _minimum);
            Value = _value;
            Invalidate();
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            var normalized = Math.Clamp(value, _minimum, _maximum);
            if (_value == normalized) return;
            _value = normalized;
            AccessibleDescription = $"{_value} ({_minimum}–{_maximum})";
            Invalidate();
            if (IsHandleCreated)
                AccessibilityNotifyClients(AccessibleEvents.ValueChange, -1);
            ValueChanged?.Invoke(this, EventArgs.Empty);
        }
    }

    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int TickFrequency { get; set; } = 10;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int SmallChange { get; set; } = 1;
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int LargeChange { get; set; } = 10;
    public event EventHandler? ValueChanged;

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.Clear(UiPalette.Surface);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

        var knobDiameter = Math.Clamp(Height * 0.44f, 12f, 18f);
        var radius = knobDiameter / 2f;
        var left = radius + 2f;
        var right = Math.Max(left + 1f, Width - radius - 2f);
        var centerY = Height / 2f;
        var progress = _maximum == _minimum ? 0f : (_value - _minimum) / (float)(_maximum - _minimum);
        var knobX = left + (right - left) * progress;
        var trackHeight = Math.Clamp(Height * 0.13f, 3f, 5f);

        using var fullTrack = UiPalette.RoundedRect(
            new RectangleF(left, centerY - trackHeight / 2f, right - left, trackHeight),
            trackHeight / 2f);
        using var trackBrush = new SolidBrush(UiPalette.Track);
        e.Graphics.FillPath(trackBrush, fullTrack);

        if (knobX > left)
        {
            using var activeTrack = UiPalette.RoundedRect(
                new RectangleF(left, centerY - trackHeight / 2f, knobX - left, trackHeight),
                trackHeight / 2f);
            using var activeBrush = new SolidBrush(UiPalette.Mint);
            e.Graphics.FillPath(activeBrush, activeTrack);
        }

        var knobColor = !Enabled
            ? UiPalette.Faint
            : _hovered || _dragging
                ? UiPalette.Mix(UiPalette.Mint, UiPalette.Text, 0.10f)
                : UiPalette.Mint;
        using var knobBrush = new SolidBrush(knobColor);
        using var knobBorder = new Pen(UiPalette.Surface, 2f);
        e.Graphics.FillEllipse(knobBrush, knobX - radius, centerY - radius, knobDiameter, knobDiameter);
        e.Graphics.DrawEllipse(knobBorder, knobX - radius, centerY - radius, knobDiameter, knobDiameter);

        if (Focused)
        {
            using var focus = new Pen(Color.FromArgb(170, UiPalette.Sky)) { DashStyle = DashStyle.Dot };
            e.Graphics.DrawRectangle(focus, 1, 1, Math.Max(1, Width - 3), Math.Max(1, Height - 3));
        }
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        _dragging = true;
        Capture = true;
        SetValueFromPointer(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging) SetValueFromPointer(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = false;
        Capture = false;
    }

    protected override void OnMouseCaptureChanged(EventArgs e)
    {
        if (!Capture) _dragging = false;
        Invalidate();
        base.OnMouseCaptureChanged(e);
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        Value += Math.Sign(e.Delta) * Math.Max(1, SmallChange);
        if (e is HandledMouseEventArgs handled) handled.Handled = true;
        base.OnMouseWheel(e);
    }

    protected override bool IsInputKey(Keys keyData) =>
        keyData is Keys.Left or Keys.Right or Keys.Up or Keys.Down or
            Keys.PageUp or Keys.PageDown or Keys.Home or Keys.End ||
        base.IsInputKey(keyData);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        switch (e.KeyCode)
        {
            case Keys.Left:
            case Keys.Down:
                Value -= Math.Max(1, SmallChange);
                e.Handled = true;
                break;
            case Keys.Right:
            case Keys.Up:
                Value += Math.Max(1, SmallChange);
                e.Handled = true;
                break;
            case Keys.PageDown:
                Value -= Math.Max(1, LargeChange);
                e.Handled = true;
                break;
            case Keys.PageUp:
                Value += Math.Max(1, LargeChange);
                e.Handled = true;
                break;
            case Keys.Home:
                Value = Minimum;
                e.Handled = true;
                break;
            case Keys.End:
                Value = Maximum;
                e.Handled = true;
                break;
        }
        base.OnKeyDown(e);
    }

    protected override void OnGotFocus(EventArgs e) { Invalidate(); base.OnGotFocus(e); }
    protected override void OnLostFocus(EventArgs e) { Invalidate(); base.OnLostFocus(e); }
    protected override void OnMouseEnter(EventArgs e) { _hovered = true; Invalidate(); base.OnMouseEnter(e); }
    protected override void OnMouseLeave(EventArgs e) { _hovered = false; Invalidate(); base.OnMouseLeave(e); }
    protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

    private void SetValueFromPointer(int x)
    {
        var radius = Math.Clamp(Height * 0.44f, 12f, 18f) / 2f;
        var left = radius + 2f;
        var right = Math.Max(left + 1f, Width - radius - 2f);
        var progress = Math.Clamp((x - left) / (right - left), 0f, 1f);
        Value = _minimum + (int)Math.Round(progress * (_maximum - _minimum));
    }
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
        // Paint the card surface explicitly. A previously selected dark native
        // theme must never leak through the rounded corners in light mode.
        e.Graphics.Clear(UiPalette.Surface);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var trackBounds = new RectangleF(0.5f, 1.5f, Width - 1, Height - 3);
        using var track = UiPalette.RoundedRect(trackBounds, trackBounds.Height / 2f);
        var trackColor = Checked
            ? (_hovered ? UiPalette.Mix(UiPalette.Mint, UiPalette.Text, 0.12f) : UiPalette.Mint)
            : (_hovered ? UiPalette.Mix(UiPalette.Track, UiPalette.Text, 0.12f) : UiPalette.Track);
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
