using System.Drawing.Drawing2D;

namespace CodexQuotaPanel;

internal sealed class HoverPeekForm : Form
{
    private const int WsExTransparent = 0x00000020;
    private const int WsExToolWindow = 0x00000080;
    private const int WsExNoActivate = 0x08000000;
    private QuotaSnapshot? _snapshot;
    private RingDisplayConfiguration _configuration = new(
        new RingWindowSelection(300, RingWindowRole.Primary),
        new RingWindowSelection(10080, RingWindowRole.Secondary),
        UiPalette.Mint,
        UiPalette.Sky);

    public HoverPeekForm()
    {
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoScaleMode = AutoScaleMode.Dpi;
        // Manual painting below uses the same 96-DPI logical grid as the rest of
        // the application.  A little extra breathing room prevents localized
        // countdown text from crowding the footer at larger text/DPI settings.
        ClientSize = new Size(336, 124);
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        BackColor = UiPalette.Canvas;
        ForeColor = UiPalette.Text;
        DoubleBuffered = true;
        Opacity = 0.97d;
        Text = L10n.Pick("Codex 额度速览", "Codex quota preview");
        AccessibleName = L10n.Pick("Codex 额度悬停速览", "Codex quota hover preview");
        UpdateRegion();
    }

    internal bool UsesPassiveWindowStyles
    {
        get
        {
            var required = WsExTransparent | WsExToolWindow | WsExNoActivate;
            return (CreateParams.ExStyle & required) == required;
        }
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExTransparent | WsExToolWindow | WsExNoActivate;
            return parameters;
        }
    }

    public void SetData(QuotaSnapshot snapshot, RingDisplayConfiguration configuration)
    {
        _snapshot = snapshot;
        _configuration = configuration;
        Invalidate();
    }

    public void ApplyLanguage()
    {
        Text = L10n.Pick("Codex 额度速览", "Codex quota preview");
        AccessibleName = L10n.Pick("Codex 额度悬停速览", "Codex quota hover preview");
        Invalidate();
    }

    public void ShowNear(Rectangle orbBounds, bool topMost)
    {
        TopMost = topMost;
        var screen = Screen.FromRectangle(orbBounds);
        var area = screen.WorkingArea;
        var gap = Math.Max(8, (int)Math.Round(10 * DeviceDpi / 96d));
        var preferLeft = orbBounds.Left > area.Left + area.Width / 2;
        var x = preferLeft ? orbBounds.Left - Width - gap : orbBounds.Right + gap;
        var y = orbBounds.Top + (orbBounds.Height - Height) / 2;
        x = Math.Clamp(x, area.Left + 4, Math.Max(area.Left + 4, area.Right - Width - 4));
        y = Math.Clamp(y, area.Top + 4, Math.Max(area.Top + 4, area.Bottom - Height - 4));
        Location = new Point(x, y);
        if (!Visible) Show();
        Invalidate();
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
        var scale = DeviceDpi / 96f;
        using var border = new Pen(UiPalette.Border, 1);
        using var path = UiPalette.RoundedRect(
            new RectangleF(0.5f, 0.5f, ClientSize.Width - 1, ClientSize.Height - 1),
            Math.Max(7f, 11f * scale));
        e.Graphics.DrawPath(border, path);

        if (_snapshot is null)
        {
            using var waitingFont = UiPalette.Body(9.5f, FontStyle.Bold);
            TextRenderer.DrawText(e.Graphics, L10n.WaitingData, waitingFont, ClientRectangle, UiPalette.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            return;
        }

        DrawLine(e.Graphics, 12, _configuration.Outer,
            RingWindowCatalog.FindBucket(_snapshot, _configuration.Outer), _configuration.OuterColor, scale);
        DrawLine(e.Graphics, 47, _configuration.Inner,
            RingWindowCatalog.FindBucket(_snapshot, _configuration.Inner), _configuration.InnerColor, scale);

        var age = DateTimeOffset.Now - _snapshot.ObservedAt;
        var ageText = L10n.FormatAge(_snapshot.ObservedAt);
        var source = string.Equals(_snapshot.Source, "App Server", StringComparison.Ordinal) ? L10n.LiveRpc : L10n.LocalShort;
        using var footerFont = UiPalette.Mono(7.6f, FontStyle.Bold);
        TextRenderer.DrawText(e.Graphics, L10n.Pick($"更新 {ageText} · {source}", $"Updated {ageText} · {source}"), footerFont,
            ScaleRectangle(14, 97, 308, 18, scale), age.TotalMinutes > 15 ? UiPalette.Amber : UiPalette.Faint,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding |
            TextFormatFlags.EndEllipsis);
    }

    protected override void OnResize(EventArgs e)
    {
        base.OnResize(e);
        UpdateRegion();
    }

    private static void DrawLine(
        Graphics graphics,
        int y,
        RingWindowSelection selection,
        LimitBucket? bucket,
        Color color,
        float scale)
    {
        using var labelFont = UiPalette.Mono(9.2f, FontStyle.Bold);
        using var detailFont = UiPalette.Body(10.2f, FontStyle.Bold);
        var label = RingWindowCatalog.FormatShort(selection.WindowMinutes);
        TextRenderer.DrawText(graphics, label, labelFont, ScaleRectangle(14, y, 48, 24, scale), color,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        var detail = bucket is null
            ? L10n.TemporarilyUnavailable
            : $"{Math.Round(bucket.RemainingPercent):0}% · {FormatCountdown(bucket.ResetsAt)}";
        TextRenderer.DrawText(graphics, detail, detailFont, ScaleRectangle(72, y, 250, 28, scale),
            bucket is null ? UiPalette.Muted : UiPalette.Text,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    private static Rectangle ScaleRectangle(int x, int y, int width, int height, float scale) =>
        Rectangle.Round(new RectangleF(x * scale, y * scale, width * scale, height * scale));

    private static string FormatCountdown(DateTimeOffset? reset)
    {
        if (reset is null) return L10n.ResetUnknown;
        var remaining = reset.Value - DateTimeOffset.Now;
        if (remaining <= TimeSpan.Zero) return L10n.WaitingRefresh;
        if (remaining.TotalDays >= 1) return L10n.Pick(
            $"{(int)remaining.TotalDays}天 {remaining.Hours:00}:{remaining.Minutes:00} 后重置",
            $"resets in {(int)remaining.TotalDays}d {remaining.Hours:00}:{remaining.Minutes:00}");
        return L10n.Pick(
            $"{(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00} 后重置",
            $"resets in {(int)remaining.TotalHours:00}:{remaining.Minutes:00}:{remaining.Seconds:00}");
    }

    private void UpdateRegion()
    {
        if (ClientSize.Width <= 0 || ClientSize.Height <= 0) return;
        using var path = UiPalette.RoundedRect(new RectangleF(ClientRectangle.X, ClientRectangle.Y,
            ClientRectangle.Width, ClientRectangle.Height), Math.Max(7f, 11f * DeviceDpi / 96f));
        Region?.Dispose();
        Region = new Region(path);
    }
}
