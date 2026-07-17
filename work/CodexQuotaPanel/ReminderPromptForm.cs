namespace CodexQuotaPanel;

/// <summary>
/// Compact themed acknowledgement window used for actionable reminders. It can
/// be modal for a user-initiated action or modeless for a quota alert.
/// </summary>
internal sealed class ReminderPromptForm : Form
{
    private readonly CheckBox _suppressCheckBox;

    internal bool SuppressChecked => _suppressCheckBox.Checked;
    internal event Action<bool>? SuppressionChanged;

    internal ReminderPromptForm(
        string title,
        string message,
        string suppressText,
        string buttonText,
        int fontScalePercent,
        bool topMost)
    {
        fontScalePercent = PanelPreferenceManager.NormalizeSettingsFontScale(fontScalePercent);
        var scaleExtra = Math.Max(0, fontScalePercent - 100);
        Text = title;
        ClientSize = new Size(440 + scaleExtra * 2, 236 + scaleExtra * 5 / 4);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowIcon = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = UiPalette.Canvas;
        ForeColor = UiPalette.Text;
        Font = UiPalette.Body(9f);
        TopMost = topMost;
        AutoScaleMode = AutoScaleMode.Dpi;

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            BackColor = UiPalette.Canvas,
            Padding = new Padding(24, 20, 24, 18),
            ColumnCount = 1,
            RowCount = 4
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        var heading = new Label
        {
            Text = title,
            AutoSize = true,
            MaximumSize = new Size(ClientSize.Width - 50, 0),
            Font = UiPalette.Display(12.5f, FontStyle.Bold),
            ForeColor = UiPalette.Text,
            Margin = new Padding(0, 0, 0, 8)
        };
        var body = new Label
        {
            Text = message,
            Dock = DockStyle.Fill,
            AutoSize = false,
            Font = UiPalette.Body(9f),
            ForeColor = UiPalette.Muted,
            Margin = new Padding(0, 0, 0, 10)
        };
        _suppressCheckBox = new CheckBox
        {
            Text = suppressText,
            AutoSize = true,
            Font = UiPalette.Body(8.6f),
            ForeColor = UiPalette.Text,
            BackColor = UiPalette.Canvas,
            Margin = new Padding(0, 2, 0, 10)
        };
        _suppressCheckBox.CheckedChanged += (_, _) => SuppressionChanged?.Invoke(_suppressCheckBox.Checked);

        var footer = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            BackColor = UiPalette.Canvas,
            Padding = Padding.Empty,
            Margin = Padding.Empty
        };
        var acknowledge = new ActionButton
        {
            Text = buttonText,
            Primary = true,
            Size = new Size(112, 34),
            DialogResult = DialogResult.OK,
            Margin = Padding.Empty
        };
        footer.Controls.Add(acknowledge);

        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(body, 0, 1);
        root.Controls.Add(_suppressCheckBox, 0, 2);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);
        AcceptButton = acknowledge;
        CancelButton = acknowledge;

        UiPalette.ApplyScaledTypography(this, fontScalePercent);
        NativeTheme.Apply(this);
    }

    internal void PlaceNearNotificationArea()
    {
        var area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Location = new Point(
            Math.Max(area.Left, area.Right - Width - 20),
            Math.Max(area.Top, area.Bottom - Height - 20));
        StartPosition = FormStartPosition.Manual;
    }
}
