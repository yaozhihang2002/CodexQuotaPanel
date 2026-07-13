using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace CodexQuotaPanel;

internal sealed record AlertSettingsValues(
    bool Enabled,
    int WarningThreshold,
    int CriticalThreshold,
    bool QuietHoursEnabled,
    int QuietStartMinutes,
    int QuietEndMinutes);

internal sealed record TimeOption(int Minutes, string Label)
{
    public override string ToString() => Label;
}

internal sealed class AlertSettingsForm : Form
{
    private readonly CheckBox _enabled;
    private readonly NumericUpDown _warning;
    private readonly NumericUpDown _critical;
    private readonly CheckBox _quietEnabled;
    private readonly ComboBox _quietStart;
    private readonly ComboBox _quietEnd;
    private readonly Label _errorLabel;
    private readonly ActionButton _applyButton;

    public AlertSettingsValues SelectedValues => new(
        _enabled.Checked,
        (int)_warning.Value,
        (int)_critical.Value,
        _quietEnabled.Checked,
        ((TimeOption)_quietStart.SelectedItem!).Minutes,
        ((TimeOption)_quietEnd.SelectedItem!).Minutes);

    public AlertSettingsForm(PanelPreferences preferences)
    {
        preferences = PanelPreferenceManager.Normalize(preferences);
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(430, 430);
        MinimumSize = new Size(360, 380);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        BackColor = UiPalette.Canvas;
        ForeColor = UiPalette.Text;
        Font = UiPalette.Body(8.5f);
        DoubleBuffered = true;
        Text = L10n.Pick("额度提醒设置", "Quota alert settings");
        AccessibleName = L10n.Pick("Codex 额度提醒设置", "Codex quota alert settings");

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

        var header = BuildHeader();
        root.Controls.Add(header, 0, 0);

        var contentHost = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.Transparent,
            Padding = new Padding(20, 4, 20, 8),
            Margin = Padding.Empty,
            TabStop = false
        };
        root.Controls.Add(contentHost, 0, 1);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 6,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        for (var row = 0; row < content.RowCount; row++)
            content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        contentHost.Controls.Add(content);

        _enabled = MakeCheckBox(L10n.Pick("启用额度提醒", "Enable quota alerts"));
        _enabled.Checked = preferences.AlertsEnabled;
        content.Controls.Add(_enabled, 0, 0);

        _warning = MakeNumeric(preferences.WarningThreshold, 2, 100);
        _critical = MakeNumeric(preferences.CriticalThreshold, 1, 99);
        content.Controls.Add(BuildThresholdCard(), 0, 1);

        _quietEnabled = MakeCheckBox(L10n.Pick("免打扰时段", "Quiet hours"));
        _quietEnabled.Checked = preferences.QuietHoursEnabled;
        _quietEnabled.Margin = new Padding(0, 8, 0, 5);
        content.Controls.Add(_quietEnabled, 0, 2);

        _quietStart = MakeTimeCombo();
        _quietEnd = MakeTimeCombo();
        FillTimes(_quietStart, preferences.QuietStartMinutes);
        FillTimes(_quietEnd, preferences.QuietEndMinutes);
        content.Controls.Add(BuildTimeRow(), 0, 3);

        var quietHint = MakeWrappingLabel(
            L10n.Pick("免打扰期间不弹出；结束后在下一次额度更新时重新判断",
                "No popups during quiet hours; the next update evaluates again"),
            UiPalette.Body(7.3f), UiPalette.Muted);
        quietHint.Margin = new Padding(0, 8, 0, 4);
        content.Controls.Add(quietHint, 0, 4);

        _errorLabel = MakeWrappingLabel(string.Empty, UiPalette.Body(7.3f, FontStyle.Bold), UiPalette.Coral);
        _errorLabel.MinimumSize = new Size(0, 22);
        _errorLabel.Margin = new Padding(0, 2, 0, 4);
        content.Controls.Add(_errorLabel, 0, 5);

        var cancelButton = MakeActionButton(
            L10n.Pick("取消", "Cancel"),
            L10n.Pick("取消提醒设置", "Cancel alert settings"));
        cancelButton.Click += (_, _) => CancelAndClose();

        _applyButton = MakeActionButton(
            L10n.Pick("保存", "Save"),
            L10n.Pick("保存提醒设置", "Save alert settings"),
            primary: true);
        _applyButton.Click += (_, _) => ApplyAndClose();
        root.Controls.Add(BuildFooter(cancelButton, _applyButton), 0, 2);
        AcceptButton = _applyButton;
        CancelButton = cancelButton;

        _enabled.CheckedChanged += (_, _) => UpdateEnabledState();
        _quietEnabled.CheckedChanged += (_, _) => UpdateEnabledState();
        _warning.ValueChanged += (_, _) => ValidateInputs();
        _critical.ValueChanged += (_, _) => ValidateInputs();
        _quietStart.SelectedIndexChanged += (_, _) => ValidateInputs();
        _quietEnd.SelectedIndexChanged += (_, _) => ValidateInputs();
        Shown += (_, _) => PositionNearCursor();
        MouseDown += DragWindow;
        UpdateEnabledState();
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

    internal bool InputsValid => _applyButton.Enabled;

    internal void SetThresholdsForTest(int warning, int critical)
    {
        _warning.Value = Math.Clamp(warning, (int)_warning.Minimum, (int)_warning.Maximum);
        _critical.Value = Math.Clamp(critical, (int)_critical.Minimum, (int)_critical.Maximum);
        ValidateInputs();
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

        var title = MakeWrappingLabel(L10n.Pick("额度提醒", "Quota alerts"),
            UiPalette.Display(12f, FontStyle.Bold), UiPalette.Text);
        title.Margin = Padding.Empty;
        title.MouseDown += DragWindow;
        header.Controls.Add(title, 0, 0);

        var subtitle = MakeWrappingLabel(L10n.AlertSettingsEyebrow,
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

    private Control BuildThresholdCard()
    {
        var card = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = UiPalette.Surface,
            ColumnCount = 2,
            RowCount = 1,
            Padding = new Padding(12, 10, 12, 10),
            Margin = new Padding(0, 5, 0, 4)
        };
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        card.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        card.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        card.Controls.Add(BuildThresholdGroup(
            L10n.Pick("警告阈值", "Warning threshold"), UiPalette.Amber, _warning), 0, 0);
        card.Controls.Add(BuildThresholdGroup(
            L10n.Pick("严重阈值", "Critical threshold"), UiPalette.Coral, _critical), 1, 0);
        return card;
    }

    private static Control BuildThresholdGroup(string text, Color color, NumericUpDown input)
    {
        var group = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(4)
        };
        group.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
        group.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        group.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var caption = MakeWrappingLabel("●  " + text, UiPalette.Body(8f, FontStyle.Bold), color);
        caption.Margin = new Padding(0, 0, 0, 6);
        group.Controls.Add(caption, 0, 0);

        var valueRow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            BackColor = Color.Transparent,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        input.Margin = Padding.Empty;
        valueRow.Controls.Add(input);
        var percent = MakeWrappingLabel("%", UiPalette.Mono(8f, FontStyle.Bold), UiPalette.Muted);
        percent.Margin = new Padding(5, 5, 0, 0);
        valueRow.Controls.Add(percent);
        group.Controls.Add(valueRow, 0, 1);
        return group;
    }

    private Control BuildTimeRow()
    {
        var row = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.Transparent,
            ColumnCount = 3,
            RowCount = 1,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        row.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
        row.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _quietStart.Dock = DockStyle.Top;
        _quietEnd.Dock = DockStyle.Top;
        _quietStart.Margin = new Padding(0, 0, 8, 0);
        _quietEnd.Margin = new Padding(8, 0, 0, 0);
        row.Controls.Add(_quietStart, 0, 0);
        var separator = MakeWrappingLabel(L10n.Pick("至", "to"),
            UiPalette.Body(8f, FontStyle.Bold), UiPalette.Muted);
        separator.Margin = new Padding(0, 7, 0, 0);
        row.Controls.Add(separator, 1, 0);
        row.Controls.Add(_quietEnd, 2, 0);
        return row;
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

    private void UpdateEnabledState()
    {
        var enabled = _enabled.Checked;
        _warning.Enabled = enabled;
        _critical.Enabled = enabled;
        _quietEnabled.Enabled = enabled;
        _quietStart.Enabled = enabled && _quietEnabled.Checked;
        _quietEnd.Enabled = enabled && _quietEnabled.Checked;
        ValidateInputs();
    }

    private void ValidateInputs()
    {
        var error = string.Empty;
        if (_enabled.Checked && _critical.Value >= _warning.Value)
            error = L10n.Pick("严重阈值必须低于警告阈值", "Critical threshold must be below warning");
        else if (_enabled.Checked && _quietEnabled.Checked &&
                 SelectedMinutes(_quietStart) == SelectedMinutes(_quietEnd))
            error = L10n.Pick("免打扰开始和结束时间不能相同", "Quiet-hours start and end cannot match");
        _errorLabel.Text = error;
        _applyButton.Enabled = string.IsNullOrEmpty(error);
        _errorLabel.Parent?.PerformLayout();
    }

    private void ApplyAndClose()
    {
        ValidateInputs();
        if (!_applyButton.Enabled) return;
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
        var maximum = new Size(Math.Max(260, area.Width - 16), Math.Max(260, area.Height - 16));
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

    private static CheckBox MakeCheckBox(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Top,
        AutoSize = true,
        MinimumSize = new Size(0, 28),
        Font = UiPalette.Body(8.4f, FontStyle.Bold),
        ForeColor = UiPalette.Text,
        BackColor = Color.Transparent,
        FlatStyle = FlatStyle.Flat,
        Cursor = Cursors.Hand,
        Margin = new Padding(0, 3, 0, 3)
    };

    private static NumericUpDown MakeNumeric(int value, int minimum, int maximum) => new()
    {
        Size = new Size(92, 30),
        Minimum = minimum,
        Maximum = maximum,
        Value = Math.Clamp(value, minimum, maximum),
        BackColor = UiPalette.SurfaceRaised,
        ForeColor = UiPalette.Text,
        Font = UiPalette.Mono(8.4f, FontStyle.Bold),
        TextAlign = HorizontalAlignment.Right,
        BorderStyle = BorderStyle.FixedSingle
    };

    private static ComboBox MakeTimeCombo() => new()
    {
        MinimumSize = new Size(100, 31),
        DropDownStyle = ComboBoxStyle.DropDownList,
        FlatStyle = FlatStyle.Flat,
        BackColor = UiPalette.SurfaceRaised,
        ForeColor = UiPalette.Text,
        Font = UiPalette.Mono(8.2f, FontStyle.Bold),
        MaxDropDownItems = 10
    };

    private static void FillTimes(ComboBox combo, int selectedMinutes)
    {
        selectedMinutes = Math.Clamp(selectedMinutes, 0, 1439);
        for (var minutes = 0; minutes < 1440; minutes += 15)
            combo.Items.Add(new TimeOption(minutes, $"{minutes / 60:00}:{minutes % 60:00}"));
        var rounded = (int)Math.Round(selectedMinutes / 15d) * 15 % 1440;
        combo.SelectedIndex = rounded / 15;
    }

    private static int SelectedMinutes(ComboBox combo) =>
        combo.SelectedItem is TimeOption option ? option.Minutes : 0;

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

/// <summary>
/// A WinForms label whose preferred height is measured against the width of its
/// table-layout cell.  The stock AutoSize label reports a single-line width,
/// which is the main source of localized text collisions in percentage columns.
/// </summary>
internal sealed class ResponsiveTextLabel : Label
{
    private const TextFormatFlags MeasureFlags =
        TextFormatFlags.WordBreak | TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;

    public override Size GetPreferredSize(Size proposedSize)
    {
        var availableWidth = proposedSize.Width;
        if (availableWidth <= 1 || availableWidth >= int.MaxValue / 2)
        {
            var parentWidth = Parent?.DisplayRectangle.Width ?? 320;
            availableWidth = Math.Max(48, parentWidth - Margin.Horizontal);
        }

        var measured = TextRenderer.MeasureText(
            string.IsNullOrEmpty(Text) ? " " : Text,
            Font,
            new Size(Math.Max(1, availableWidth), int.MaxValue),
            MeasureFlags);
        return new Size(Math.Min(availableWidth, Math.Max(1, measured.Width)),
            Math.Max(Font.Height + 2, measured.Height + 2));
    }

    protected override void OnTextChanged(EventArgs e)
    {
        base.OnTextChanged(e);
        Parent?.PerformLayout(this, nameof(Text));
    }

    protected override void OnFontChanged(EventArgs e)
    {
        base.OnFontChanged(e);
        Parent?.PerformLayout(this, nameof(Font));
    }
}
