using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CodexQuotaPanel;

internal sealed class OpacityEditorForm : Form
{
    private readonly OpacitySliderControl _slider;
    private readonly TextBox _input;
    private bool _syncing;

    public event Action<int>? PreviewChanged;

    public int SelectedOpacity => _slider.Value;

    public OpacityEditorForm(int opacity)
    {
        opacity = PanelPreferenceManager.NormalizeOpacity(opacity);
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(430, 300);
        MinimumSize = new Size(340, 260);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = UiPalette.Canvas;
        ForeColor = UiPalette.Text;
        Font = UiPalette.Body(8.5f);
        DoubleBuffered = true;
        Text = L10n.Pick("精确设置悬浮球不透明度", "Precise orb opacity");
        AccessibleName = L10n.Pick("悬浮球不透明度精确设置", "Precise orb opacity settings");

        _slider = new OpacitySliderControl
        {
            Dock = DockStyle.Fill,
            MinimumSize = new Size(180, 42),
            Value = opacity,
            Margin = new Padding(0, 0, 12, 0)
        };

        _input = new TextBox
        {
            Text = opacity.ToString(),
            Dock = DockStyle.Fill,
            MinimumSize = new Size(48, 24),
            BorderStyle = BorderStyle.None,
            BackColor = UiPalette.SurfaceRaised,
            ForeColor = UiPalette.Text,
            Font = UiPalette.Mono(9f, FontStyle.Bold),
            TextAlign = HorizontalAlignment.Right,
            MaxLength = 3,
            Margin = new Padding(0, 2, 3, 0),
            AccessibleName = L10n.Pick("不透明度百分比", "Opacity percentage")
        };

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = new Padding(1)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Controls.Add(root);
        root.Controls.Add(BuildHeader(), 0, 0);

        var contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.Transparent,
            Padding = new Padding(20, 8, 20, 8),
            Margin = Padding.Empty,
            TabStop = false
        };
        root.Controls.Add(contentHost, 0, 1);
        contentHost.Controls.Add(BuildContent());

        var cancelButton = MakeActionButton(
            L10n.Pick("取消", "Cancel"),
            L10n.Pick("取消不透明度修改", "Cancel opacity changes"));
        cancelButton.Click += (_, _) => CancelAndClose();
        var applyButton = MakeActionButton(
            L10n.Pick("应用", "Apply"),
            L10n.Pick("应用不透明度", "Apply opacity"),
            primary: true);
        applyButton.Click += (_, _) => ApplyAndClose();
        root.Controls.Add(BuildFooter(cancelButton, applyButton), 0, 2);

        AcceptButton = applyButton;
        CancelButton = cancelButton;

        _slider.ValueChanged += (_, _) => SyncFromSlider();
        _input.TextChanged += (_, _) => SyncFromInput();
        _input.KeyDown += InputOnKeyDown;
        _input.KeyPress += (_, e) =>
        {
            if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
        };
        _input.Leave += (_, _) => CommitInput();
        Shown += (_, _) => PositionNearCursor();
        FormClosing += (_, _) =>
        {
            if (DialogResult == DialogResult.None) DialogResult = DialogResult.Cancel;
        };
        MouseDown += DragWindow;
        UpdateRegion();
    }

    internal void SetOpacityForTest(int value) => SetEditorValue(value, notify: true);

    internal void SavePreview(string path)
    {
        CreateControl();
        foreach (Control child in Controls) child.CreateControl();
        PerformLayout();
        using var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
        DrawToBitmap(bitmap, new Rectangle(Point.Empty, ClientSize));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            const int CsDropShadow = 0x00020000;
            var parameters = base.CreateParams;
            parameters.ClassStyle |= CsDropShadow;
            return parameters;
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        using var gradient = new LinearGradientBrush(
            ClientRectangle,
            UiPalette.SurfaceRaised,
            UiPalette.Canvas,
            LinearGradientMode.Vertical);
        e.Graphics.FillRectangle(gradient, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var border = new Pen(UiPalette.Border, 1);
        using var path = UiPalette.RoundedRect(
            new RectangleF(0.5f, 0.5f, ClientSize.Width - 1, ClientSize.Height - 1),
            Math.Max(8f, 14f * DeviceDpi / 96f));
        e.Graphics.DrawPath(border, path);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRegion();
    }

    private Control BuildHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(18, 10, 10, 6),
            Margin = Padding.Empty
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var title = MakeWrappingLabel(L10n.Pick("悬浮球不透明度", "Orb opacity"),
            UiPalette.Display(12f, FontStyle.Bold), UiPalette.Text);
        title.Margin = Padding.Empty;
        title.MouseDown += DragWindow;
        header.Controls.Add(title, 0, 0);

        var subtitle = MakeWrappingLabel(L10n.OpacitySettingsEyebrow,
            UiPalette.Mono(6.8f, FontStyle.Bold), UiPalette.Faint);
        subtitle.Margin = new Padding(1, 1, 0, 0);
        subtitle.MouseDown += DragWindow;
        header.Controls.Add(subtitle, 0, 1);

        var closeButton = MakeCloseButton();
        closeButton.Click += (_, _) => CancelAndClose();
        header.Controls.Add(closeButton, 1, 0);
        header.SetRowSpan(closeButton, 2);
        header.MouseDown += DragWindow;
        return header;
    }

    private Control BuildContent()
    {
        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var editorRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        editorRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        editorRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        editorRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editorRow.Controls.Add(_slider, 0, 0);
        editorRow.Controls.Add(BuildInputShell(), 1, 0);
        content.Controls.Add(editorRow, 0, 0);

        var scaleRow = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(1, 1, 84, 0),
            Padding = Padding.Empty
        };
        scaleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        scaleRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        var minimum = MakeWrappingLabel(PanelPreferenceManager.MinimumOpacity.ToString(),
            UiPalette.Mono(6.8f), UiPalette.Faint);
        minimum.Margin = Padding.Empty;
        var maximum = MakeWrappingLabel(PanelPreferenceManager.MaximumOpacity.ToString(),
            UiPalette.Mono(6.8f), UiPalette.Faint);
        maximum.TextAlign = ContentAlignment.TopRight;
        maximum.Margin = Padding.Empty;
        scaleRow.Controls.Add(minimum, 0, 0);
        scaleRow.Controls.Add(maximum, 1, 0);
        content.Controls.Add(scaleRow, 0, 1);

        var hint = MakeWrappingLabel(
            L10n.Pick("调整时可实时预览；展开详情仍保持 100%",
                "Live preview while adjusting; details stay at 100%"),
            UiPalette.Body(7.4f), UiPalette.Muted);
        hint.Margin = new Padding(0, 12, 0, 0);
        content.Controls.Add(hint, 0, 2);
        return content;
    }

    private Control BuildInputShell()
    {
        var shell = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = UiPalette.SurfaceRaised,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(8, 5, 8, 5),
            Margin = Padding.Empty
        };
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        shell.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        shell.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        shell.Controls.Add(_input, 0, 0);
        var percent = MakeWrappingLabel("%", UiPalette.Mono(8f, FontStyle.Bold), UiPalette.Muted);
        percent.Margin = new Padding(0, 3, 0, 0);
        shell.Controls.Add(percent, 1, 0);
        return shell;
    }

    private static Control BuildFooter(Control cancelButton, Control applyButton)
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = UiPalette.Surface,
            ColumnCount = 3,
            RowCount = 1,
            Padding = new Padding(18, 10, 18, 12),
            Margin = Padding.Empty
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        cancelButton.Margin = new Padding(0, 0, 8, 0);
        applyButton.Margin = Padding.Empty;
        footer.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 0, 0);
        footer.Controls.Add(cancelButton, 1, 0);
        footer.Controls.Add(applyButton, 2, 0);
        return footer;
    }

    private void SyncFromSlider()
    {
        if (_syncing) return;
        _syncing = true;
        _input.Text = _slider.Value.ToString();
        _syncing = false;
        PreviewChanged?.Invoke(_slider.Value);
    }

    private void SyncFromInput()
    {
        if (_syncing || !int.TryParse(_input.Text, out var value)) return;
        if (value is < PanelPreferenceManager.MinimumOpacity or > PanelPreferenceManager.MaximumOpacity) return;
        SetEditorValue(value, notify: true);
    }

    private void InputOnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode is Keys.Up or Keys.Down)
        {
            SetEditorValue(_slider.Value + (e.KeyCode == Keys.Up ? 1 : -1), notify: true);
            e.Handled = true;
            e.SuppressKeyPress = true;
        }
    }

    private void CommitInput()
    {
        var value = int.TryParse(_input.Text, out var parsed) ? parsed : _slider.Value;
        SetEditorValue(value, notify: true);
    }

    private void SetEditorValue(int value, bool notify)
    {
        value = PanelPreferenceManager.NormalizeOpacity(value);
        _syncing = true;
        _slider.Value = value;
        _input.Text = value.ToString();
        _syncing = false;
        if (notify) PreviewChanged?.Invoke(value);
    }

    private void ApplyAndClose()
    {
        CommitInput();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void CancelAndClose()
    {
        DialogResult = DialogResult.Cancel;
        Close();
    }

    private void PositionNearCursor()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        ConstrainTo(area);
        var preferredAbove = Cursor.Position.Y - Height - 14;
        var y = preferredAbove >= area.Top + 8
            ? preferredAbove
            : Math.Clamp(Cursor.Position.Y + 14, area.Top + 8, area.Bottom - Height - 8);
        Location = new Point(
            Math.Clamp(Cursor.Position.X - Width / 2, area.Left + 8, area.Right - Width - 8),
            y);
    }

    private void ConstrainTo(Rectangle area)
    {
        var maximum = new Size(Math.Max(280, area.Width - 16), Math.Max(240, area.Height - 16));
        MaximumSize = maximum;
        if (Width > maximum.Width || Height > maximum.Height)
            Size = new Size(Math.Min(Width, maximum.Width), Math.Min(Height, maximum.Height));
    }

    private void UpdateRegion()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        using var path = UiPalette.RoundedRect(
            new RectangleF(ClientRectangle.X, ClientRectangle.Y, ClientRectangle.Width, ClientRectangle.Height),
            Math.Max(8f, 14f * DeviceDpi / 96f));
        Region?.Dispose();
        Region = new Region(path);
    }

    private static Button MakeCloseButton() => new()
    {
        Text = "×",
        Size = new Size(34, 34),
        MinimumSize = new Size(34, 34),
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.Transparent,
        ForeColor = UiPalette.Muted,
        Font = UiPalette.Body(12f),
        Cursor = Cursors.Hand,
        AccessibleName = L10n.Pick("取消并关闭", "Cancel and close"),
        Margin = Padding.Empty,
        FlatAppearance = { BorderSize = 0, MouseOverBackColor = UiPalette.SurfaceRaised }
    };

    private static ActionButton MakeActionButton(string text, string accessibleName, bool primary = false) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowOnly,
        MinimumSize = new Size(82, 34),
        Padding = new Padding(12, 4, 12, 4),
        Primary = primary,
        AccessibleName = accessibleName
    };

    private static Label MakeWrappingLabel(string text, Font font, Color color) => new ResponsiveTextLabel
    {
        Text = text,
        Dock = DockStyle.Fill,
        AutoSize = true,
        Font = font,
        ForeColor = color,
        BackColor = Color.Transparent,
        UseCompatibleTextRendering = false
    };

    private void DragWindow(object? sender, MouseEventArgs e)
    {
        if (e.Button != MouseButtons.Left) return;
        ReleaseCapture();
        SendMessage(Handle, 0xA1, 0x2, 0);
    }

    [DllImport("user32.dll")]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hWnd, int msg, int wParam, int lParam);
}

internal sealed class OpacitySliderControl : Control
{
    private int _value = PanelPreferenceManager.MaximumOpacity;
    private bool _dragging;

    public event EventHandler? ValueChanged;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int Value
    {
        get => _value;
        set
        {
            var normalized = PanelPreferenceManager.NormalizeOpacity(value);
            if (_value == normalized) return;
            _value = normalized;
            ValueChanged?.Invoke(this, EventArgs.Empty);
            Invalidate();
        }
    }

    public OpacitySliderControl()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.Selectable, true);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.Slider;
        AccessibleName = L10n.Pick("悬浮球不透明度滑块", "Orb opacity slider");
        AccessibleDescription = L10n.Pick("使用左右方向键精确调整，PageUp 和 PageDown 每次调整 5%",
            "Use arrow keys for 1% steps, Page Up and Page Down for 5% steps");
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = DeviceDpi / 96f;
        var padding = 9f * scale;
        var trackHeight = 5f * scale;
        var track = new RectangleF(padding, (Height - trackHeight) / 2f,
            Math.Max(1, Width - padding * 2), trackHeight);
        var ratio = (_value - PanelPreferenceManager.MinimumOpacity) /
                    (double)(PanelPreferenceManager.MaximumOpacity - PanelPreferenceManager.MinimumOpacity);
        var thumbX = track.Left + track.Width * (float)ratio;

        using var trackPath = UiPalette.RoundedRect(track, trackHeight / 2f);
        using var trackBrush = new SolidBrush(UiPalette.Track);
        e.Graphics.FillPath(trackBrush, trackPath);

        var fillWidth = Math.Max(trackHeight, thumbX - track.Left);
        var fill = new RectangleF(track.Left, track.Top, fillWidth, track.Height);
        using var fillPath = UiPalette.RoundedRect(fill, trackHeight / 2f);
        using var fillBrush = new SolidBrush(UiPalette.Mint);
        e.Graphics.FillPath(fillBrush, fillPath);

        var thumbSize = 15f * scale;
        var thumb = new RectangleF(thumbX - thumbSize / 2f, Height / 2f - thumbSize / 2f, thumbSize, thumbSize);
        using var halo = new SolidBrush(Color.FromArgb(Focused ? 42 : 22, UiPalette.Mint));
        e.Graphics.FillEllipse(halo, RectangleF.Inflate(thumb, 4f * scale, 4f * scale));
        using var thumbBrush = new SolidBrush(UiPalette.Text);
        using var thumbBorder = new Pen(UiPalette.Mint, 2f * scale);
        e.Graphics.FillEllipse(thumbBrush, thumb);
        e.Graphics.DrawEllipse(thumbBorder, thumb);
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.Button != MouseButtons.Left) return;
        Focus();
        _dragging = true;
        Capture = true;
        SetFromMouse(e.X);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_dragging && e.Button == MouseButtons.Left) SetFromMouse(e.X);
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.Button != MouseButtons.Left) return;
        _dragging = false;
        Capture = false;
    }

    protected override void OnMouseWheel(MouseEventArgs e)
    {
        Value += e.Delta > 0 ? 1 : -1;
        base.OnMouseWheel(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var delta = e.KeyCode switch
        {
            Keys.Left or Keys.Down => -1,
            Keys.Right or Keys.Up => 1,
            Keys.PageDown => -5,
            Keys.PageUp => 5,
            _ => 0
        };
        if (e.KeyCode == Keys.Home) Value = PanelPreferenceManager.MinimumOpacity;
        else if (e.KeyCode == Keys.End) Value = PanelPreferenceManager.MaximumOpacity;
        else if (delta != 0) Value += delta;
        else
        {
            base.OnKeyDown(e);
            return;
        }
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    protected override void OnGotFocus(EventArgs e) { base.OnGotFocus(e); Invalidate(); }
    protected override void OnLostFocus(EventArgs e) { base.OnLostFocus(e); Invalidate(); }

    private void SetFromMouse(int x)
    {
        var padding = 9f * DeviceDpi / 96f;
        var usable = Math.Max(1f, Width - padding * 2f);
        var ratio = Math.Clamp((x - padding) / usable, 0f, 1f);
        var value = PanelPreferenceManager.MinimumOpacity +
                    ratio * (PanelPreferenceManager.MaximumOpacity - PanelPreferenceManager.MinimumOpacity);
        Value = (int)Math.Round(value);
    }
}
