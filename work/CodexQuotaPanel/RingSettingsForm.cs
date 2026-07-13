using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CodexQuotaPanel;

internal sealed class RingSettingsForm : Form
{
    private readonly ComboBox _outerCombo;
    private readonly ComboBox _innerCombo;
    private readonly RingColorButton _outerColorButton;
    private readonly RingColorButton _innerColorButton;
    private readonly QuotaOrbControl _preview;
    private readonly QuotaSnapshot? _snapshot;
    private bool _syncing;

    public event Action<RingDisplayConfiguration>? PreviewChanged;

    public RingDisplayConfiguration SelectedConfiguration => new(
        Selected(_outerCombo),
        Selected(_innerCombo),
        _outerColorButton.SelectedColor,
        _innerColorButton.SelectedColor);

    public RingSettingsForm(QuotaSnapshot? snapshot, RingDisplayConfiguration configuration)
    {
        _snapshot = snapshot;
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(480, 390);
        MinimumSize = new Size(400, 340);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = UiPalette.Canvas;
        ForeColor = UiPalette.Text;
        Font = UiPalette.Body(8.5f);
        DoubleBuffered = true;
        Text = L10n.Pick("环形显示设置", "Ring display settings");
        AccessibleName = L10n.Pick("额度悬浮球环形显示设置", "Quota orb ring display settings");

        _preview = new QuotaOrbControl
        {
            Size = new Size(88, 88),
            Anchor = AnchorStyles.None,
            TabStop = false,
            Margin = new Padding(8)
        };
        _preview.ConfigureRings(configuration);
        if (snapshot is not null) _preview.SetSnapshot(snapshot, live: true);

        _outerCombo = MakeCombo();
        _innerCombo = MakeCombo();
        _outerColorButton = MakeColorButton(configuration.OuterColor,
            L10n.Pick("设置外环颜色", "Set outer ring color"));
        _innerColorButton = MakeColorButton(configuration.InnerColor,
            L10n.Pick("设置内环颜色", "Set inner ring color"));

        var options = RingWindowCatalog.GetOptions(
            snapshot,
            configuration.Outer,
            configuration.Inner,
            new RingWindowSelection(300, RingWindowRole.Primary),
            new RingWindowSelection(10080, RingWindowRole.Secondary));
        foreach (var option in options)
        {
            _outerCombo.Items.Add(option);
            _innerCombo.Items.Add(option);
        }
        SelectOption(_outerCombo, configuration.Outer);
        SelectOption(_innerCombo, configuration.Inner);

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
            Padding = new Padding(18, 8, 18, 8),
            Margin = Padding.Empty,
            TabStop = false
        };
        root.Controls.Add(contentHost, 0, 1);
        contentHost.Controls.Add(BuildContent());

        var resetButton = MakeActionButton(
            L10n.Pick("恢复默认", "Restore defaults"),
            L10n.Pick("恢复默认双环设置", "Restore default ring settings"),
            minimumWidth: 116);
        resetButton.Click += (_, _) => RestoreDefaults();
        var cancelButton = MakeActionButton(
            L10n.Pick("取消", "Cancel"),
            L10n.Pick("取消双环设置", "Cancel ring settings"));
        cancelButton.Click += (_, _) => CancelAndClose();
        var applyButton = MakeActionButton(
            L10n.Pick("应用", "Apply"),
            L10n.Pick("应用双环设置", "Apply ring settings"),
            primary: true);
        applyButton.Click += (_, _) => ApplyAndClose();
        root.Controls.Add(BuildFooter(resetButton, cancelButton, applyButton), 0, 2);
        AcceptButton = applyButton;
        CancelButton = cancelButton;

        _outerColorButton.Click += (_, _) => ChooseColor(_outerColorButton);
        _innerColorButton.Click += (_, _) => ChooseColor(_innerColorButton);
        _outerCombo.SelectedIndexChanged += (_, _) => UpdatePreview();
        _innerCombo.SelectedIndexChanged += (_, _) => UpdatePreview();
        Shown += (_, _) => PositionNearCursor();
        MouseDown += DragWindow;
        UpdateRegion();
    }

    internal void SavePreview(string path)
    {
        CreateControl();
        foreach (Control child in Controls) child.CreateControl();
        PerformLayout();
        using var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
        DrawToBitmap(bitmap, new Rectangle(Point.Empty, ClientSize));
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    internal void RestoreDefaultsForTest() => RestoreDefaults();

    internal void SetColorsForTest(Color outer, Color inner)
    {
        _outerColorButton.SelectedColor = Color.FromArgb(255, outer);
        _innerColorButton.SelectedColor = Color.FromArgb(255, inner);
        UpdatePreview();
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
        using var gradient = new LinearGradientBrush(ClientRectangle,
            UiPalette.SurfaceRaised, UiPalette.Canvas, LinearGradientMode.Vertical);
        e.Graphics.FillRectangle(gradient, ClientRectangle);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(UiPalette.Border, 1);
        using var path = UiPalette.RoundedRect(
            new RectangleF(0.5f, 0.5f, ClientSize.Width - 1, ClientSize.Height - 1),
            Math.Max(8f, 14f * DeviceDpi / 96f));
        e.Graphics.DrawPath(pen, path);
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

        var title = MakeWrappingLabel(L10n.Pick("双环显示", "Dual-ring display"),
            UiPalette.Display(12f, FontStyle.Bold), UiPalette.Text);
        title.Margin = Padding.Empty;
        title.MouseDown += DragWindow;
        header.Controls.Add(title, 0, 0);

        var subtitle = MakeWrappingLabel(L10n.RingSettingsEyebrow,
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
            ColumnCount = 2,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var previewHost = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = UiPalette.Surface,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(10),
            Margin = new Padding(0, 0, 16, 0)
        };
        previewHost.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        previewHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewHost.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        previewHost.Controls.Add(_preview, 0, 0);
        var previewLabel = MakeWrappingLabel(L10n.Pick("实时预览", "Live preview"),
            UiPalette.Body(7.4f), UiPalette.Muted);
        previewLabel.TextAlign = ContentAlignment.MiddleCenter;
        previewLabel.Margin = new Padding(0, 2, 0, 0);
        previewHost.Controls.Add(previewLabel, 0, 1);
        content.Controls.Add(previewHost, 0, 0);

        var editor = new TableLayoutPanel
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
        editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (var row = 0; row < editor.RowCount; row++)
            editor.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        editor.Controls.Add(BuildRingRow(L10n.Pick("外环", "Outer ring"), _outerCombo, _outerColorButton), 0, 0);
        editor.Controls.Add(BuildRingRow(L10n.Pick("内环", "Inner ring"), _innerCombo, _innerColorButton), 0, 1);
        var hint = MakeWrappingLabel(L10n.AvailableAgainHint, UiPalette.Body(7.3f), UiPalette.Muted);
        hint.Margin = new Padding(0, 8, 0, 0);
        editor.Controls.Add(hint, 0, 2);
        content.Controls.Add(editor, 1, 0);
        return content;
    }

    private static Control BuildRingRow(string captionText, ComboBox combo, RingColorButton colorButton)
    {
        var group = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 2,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 12),
            Padding = Padding.Empty
        };
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        group.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        group.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        group.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var caption = MakeWrappingLabel(captionText, UiPalette.Body(8f, FontStyle.Bold), UiPalette.Muted);
        caption.Margin = new Padding(0, 0, 0, 5);
        group.Controls.Add(caption, 0, 0);
        group.SetColumnSpan(caption, 2);
        combo.Dock = DockStyle.Top;
        combo.Margin = new Padding(0, 0, 8, 0);
        colorButton.Margin = Padding.Empty;
        group.Controls.Add(combo, 0, 1);
        group.Controls.Add(colorButton, 1, 1);
        return group;
    }

    private static Control BuildFooter(Control resetButton, Control cancelButton, Control applyButton)
    {
        var footer = new TableLayoutPanel
        {
            Dock = DockStyle.Bottom,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = UiPalette.Surface,
            ColumnCount = 4,
            RowCount = 1,
            Padding = new Padding(18, 10, 18, 12),
            Margin = Padding.Empty
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        footer.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        resetButton.Margin = Padding.Empty;
        cancelButton.Margin = new Padding(0, 0, 8, 0);
        applyButton.Margin = Padding.Empty;
        footer.Controls.Add(resetButton, 0, 0);
        footer.Controls.Add(new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty }, 1, 0);
        footer.Controls.Add(cancelButton, 2, 0);
        footer.Controls.Add(applyButton, 3, 0);
        return footer;
    }

    private ComboBox MakeCombo()
    {
        var combo = new ComboBox
        {
            MinimumSize = new Size(150, 32),
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            FlatStyle = FlatStyle.Flat,
            BackColor = UiPalette.SurfaceRaised,
            ForeColor = UiPalette.Text,
            Font = UiPalette.Body(8f)
        };
        combo.ItemHeight = Math.Max(22, combo.Font.Height + 6);
        combo.DrawItem += (_, e) =>
        {
            e.DrawBackground();
            if (e.Index < 0 || combo.Items[e.Index] is not RingWindowOption option) return;
            var selected = (e.State & DrawItemState.Selected) != 0;
            using var background = new SolidBrush(selected ? UiPalette.SurfaceRaised : UiPalette.Surface);
            e.Graphics.FillRectangle(background, e.Bounds);
            var color = option.Available ? UiPalette.Text : UiPalette.Muted;
            TextRenderer.DrawText(e.Graphics, option.Label, combo.Font, e.Bounds, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        };
        return combo;
    }

    private static RingColorButton MakeColorButton(Color color, string accessibleName) => new()
    {
        Size = new Size(80, 32),
        MinimumSize = new Size(80, 32),
        SelectedColor = color,
        AccessibleName = accessibleName
    };

    private void ChooseColor(RingColorButton button)
    {
        using var picker = new ColorDialog
        {
            Color = button.SelectedColor,
            FullOpen = true,
            AnyColor = true,
            SolidColorOnly = true
        };
        if (picker.ShowDialog(this) != DialogResult.OK) return;
        button.SelectedColor = Color.FromArgb(255, picker.Color);
        UpdatePreview();
    }

    private void RestoreDefaults()
    {
        _syncing = true;
        SelectOption(_outerCombo, new RingWindowSelection(300, RingWindowRole.Primary));
        SelectOption(_innerCombo, new RingWindowSelection(10080, RingWindowRole.Secondary));
        _outerColorButton.SelectedColor = UiPalette.Mint;
        _innerColorButton.SelectedColor = UiPalette.Sky;
        _syncing = false;
        UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_syncing || _outerCombo.SelectedItem is null || _innerCombo.SelectedItem is null) return;
        var configuration = SelectedConfiguration;
        _preview.ConfigureRings(configuration);
        if (_snapshot is not null) _preview.SetSnapshot(_snapshot, live: true);
        PreviewChanged?.Invoke(configuration);
    }

    private void ApplyAndClose()
    {
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
        var above = Cursor.Position.Y - Height - 14;
        var y = above >= area.Top + 8
            ? above
            : Math.Clamp(Cursor.Position.Y + 14, area.Top + 8, area.Bottom - Height - 8);
        Location = new Point(
            Math.Clamp(Cursor.Position.X - Width / 2, area.Left + 8, area.Right - Width - 8),
            y);
    }

    private void ConstrainTo(Rectangle area)
    {
        var maximum = new Size(Math.Max(300, area.Width - 16), Math.Max(260, area.Height - 16));
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

    private static RingWindowSelection Selected(ComboBox combo) =>
        ((RingWindowOption)combo.SelectedItem!).Selection;

    private static void SelectOption(ComboBox combo, RingWindowSelection selection)
    {
        for (var index = 0; index < combo.Items.Count; index++)
        {
            if (combo.Items[index] is RingWindowOption option && option.Selection == selection)
            {
                combo.SelectedIndex = index;
                return;
            }
        }
        if (combo.Items.Count > 0) combo.SelectedIndex = 0;
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

    private static ActionButton MakeActionButton(
        string text,
        string accessibleName,
        bool primary = false,
        int minimumWidth = 82) => new()
    {
        Text = text,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowOnly,
        MinimumSize = new Size(minimumWidth, 34),
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

internal sealed class RingColorButton : Button
{
    private Color _selectedColor = UiPalette.Mint;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color SelectedColor
    {
        get => _selectedColor;
        set { _selectedColor = Color.FromArgb(255, value); Invalidate(); }
    }

    public RingColorButton()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderSize = 0;
        Cursor = Cursors.Hand;
        TabStop = true;
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var path = UiPalette.RoundedRect(
            new RectangleF(0.5f, 0.5f, Width - 1, Height - 1),
            Math.Max(5f, 7f * DeviceDpi / 96f));
        using var fill = new SolidBrush(_selectedColor);
        using var border = new Pen(Color.FromArgb(110, Color.White), 1);
        e.Graphics.FillPath(fill, path);
        e.Graphics.DrawPath(border, path);
        var luminance = 0.299 * _selectedColor.R + 0.587 * _selectedColor.G + 0.114 * _selectedColor.B;
        var textColor = luminance > 145 ? UiPalette.Canvas : Color.White;
        using var font = UiPalette.Mono(6f, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, $"#{_selectedColor.R:X2}{_selectedColor.G:X2}{_selectedColor.B:X2}",
            font, ClientRectangle, textColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
            TextFormatFlags.EndEllipsis);
    }
}
