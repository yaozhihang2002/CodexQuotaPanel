using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;

namespace CodexQuotaPanel;

internal sealed class DailyTokenUsageControl : Control
{
    private readonly ToolTip _toolTip;
    private readonly List<RectangleF> _barHitBounds = [];
    private TokenCycleUsage? _usage;
    private int _hoveredIndex = -1;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    internal TokenCycleUsage? Usage => _usage;

    internal string? HoveredText => _hoveredIndex >= 0 && _usage is not null && _hoveredIndex < _usage.Days.Count
        ? FormatDayDetails(_usage.Days[_hoveredIndex])
        : null;

    public DailyTokenUsageControl()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        Height = 96;
        TabStop = true;
        AccessibleRole = AccessibleRole.Chart;
        _toolTip = new ToolTip
        {
            InitialDelay = 140,
            ReshowDelay = 60,
            AutoPopDelay = 12000,
            ShowAlways = true
        };
        ApplyLanguage();
    }

    public void SetUsage(TokenCycleUsage? usage)
    {
        _usage = usage;
        _hoveredIndex = -1;
        _toolTip.Hide(this);
        UpdateAccessibility();
        Invalidate();
    }

    public void ApplyLanguage()
    {
        UpdateAccessibility();
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

        using var titleFont = UiPalette.Display(9.2f, FontStyle.Bold);
        using var valueFont = UiPalette.Mono(7.8f, FontStyle.Bold);
        using var dayFont = UiPalette.Mono(6.4f, FontStyle.Bold);
        using var textBrush = new SolidBrush(UiPalette.Text);
        using var mutedBrush = new SolidBrush(UiPalette.Muted);
        using var accentBrush = new SolidBrush(UiPalette.Mint);

        var title = L10n.Pick("本周期 Token", "Cycle tokens");
        e.Graphics.DrawString(title, titleFont, textBrush, S(12), S(7));
        var totalText = _usage is null ? "—" : FormatCompact(_usage.Total.TotalTokens);
        var totalSize = e.Graphics.MeasureString(totalText, valueFont);
        e.Graphics.DrawString(totalText, valueFont, accentBrush, Width - totalSize.Width - S(12), S(9));

        _barHitBounds.Clear();
        if (_usage is null)
        {
            e.Graphics.DrawString(
                L10n.Pick("等待可识别的重置周期", "Waiting for a reset cycle"),
                valueFont, mutedBrush, S(12), S(43));
            return;
        }
        if (_usage.Days.Count == 0)
        {
            e.Graphics.DrawString(
                L10n.Pick("本周期暂无本机会话记录", "No local session usage in this cycle"),
                valueFont, mutedBrush, S(12), S(43));
            return;
        }

        var chart = new RectangleF(S(12), S(29), Width - S(24), S(39));
        using var baselinePen = new Pen(Color.FromArgb(90, UiPalette.Border), Math.Max(1f, scale));
        e.Graphics.DrawLine(baselinePen, chart.Left, chart.Bottom, chart.Right, chart.Bottom);
        var gap = S(4);
        var count = _usage.Days.Count;
        var slotWidth = chart.Width / count;
        var barWidth = Math.Max(S(5), Math.Min(S(22), slotWidth - gap));
        var maximum = Math.Max(1L, _usage.Days.Max(day => day.Usage.TotalTokens));
        var today = DateOnly.FromDateTime(DateTime.Now);

        for (var index = 0; index < count; index++)
        {
            var day = _usage.Days[index];
            var ratio = day.Usage.TotalTokens / (double)maximum;
            var height = day.Usage.TotalTokens == 0 ? S(2) : Math.Max(S(4), chart.Height * (float)ratio);
            var centerX = chart.Left + slotWidth * (index + 0.5f);
            var bar = new RectangleF(centerX - barWidth / 2f, chart.Bottom - height, barWidth, height);
            var hit = new RectangleF(centerX - slotWidth / 2f, chart.Top, slotWidth, chart.Height + S(18));
            _barHitBounds.Add(hit);

            var highlighted = index == _hoveredIndex || day.LocalDate == today;
            using var barPath = UiPalette.RoundedRect(bar, Math.Min(S(3), bar.Width / 2f));
            using var fill = new SolidBrush(highlighted
                ? UiPalette.Mint
                : Color.FromArgb(150, UiPalette.Mix(UiPalette.Mint, UiPalette.Sky, index / (float)Math.Max(1, count - 1))));
            e.Graphics.FillPath(fill, barPath);

            var dayText = day.LocalDate.Day.ToString(CultureInfo.InvariantCulture);
            var daySize = e.Graphics.MeasureString(dayText, dayFont);
            e.Graphics.DrawString(dayText, dayFont,
                index == _hoveredIndex ? accentBrush : mutedBrush,
                centerX - daySize.Width / 2f, S(71));

            if (index == _hoveredIndex)
            {
                using var focus = new Pen(Color.FromArgb(130, UiPalette.Mint), Math.Max(1f, scale));
                e.Graphics.DrawRectangle(focus, Rectangle.Round(hit));
            }
        }

        var hint = L10n.Pick("悬停查看每日明细 · 仅本机", "Hover for daily details · local only");
        using var hintFont = UiPalette.Body(5.9f);
        var hintSize = e.Graphics.MeasureString(hint, hintFont);
        e.Graphics.DrawString(hint, hintFont, mutedBrush,
            Math.Max(S(12), Width - hintSize.Width - S(12)), S(83));
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        var index = _barHitBounds.FindIndex(bounds => bounds.Contains(e.Location));
        SelectDay(index, e.Location);
    }

    protected override void OnMouseLeave(EventArgs e)
    {
        SelectDay(-1, Point.Empty);
        base.OnMouseLeave(e);
    }

    protected override void OnGotFocus(EventArgs e)
    {
        base.OnGotFocus(e);
        if (_usage?.Days.Count > 0 && _hoveredIndex < 0)
            SelectDay(_usage.Days.Count - 1, new Point(Width / 2, Height / 2));
    }

    protected override void OnLostFocus(EventArgs e)
    {
        SelectDay(-1, Point.Empty);
        base.OnLostFocus(e);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (_usage?.Days.Count is not > 0)
        {
            base.OnKeyDown(e);
            return;
        }

        if (e.KeyCode is Keys.Left or Keys.Right or Keys.Home or Keys.End)
        {
            var next = e.KeyCode switch
            {
                Keys.Home => 0,
                Keys.End => _usage.Days.Count - 1,
                Keys.Left => Math.Max(0, (_hoveredIndex < 0 ? _usage.Days.Count : _hoveredIndex) - 1),
                _ => Math.Min(_usage.Days.Count - 1, _hoveredIndex + 1)
            };
            SelectDay(next, new Point(Width / 2, Height / 2));
            e.Handled = true;
            return;
        }
        base.OnKeyDown(e);
    }

    internal string? ShowDayForTest(int index)
    {
        if (_usage?.Days.Count is not > 0) return null;
        SelectDay(Math.Clamp(index, 0, _usage.Days.Count - 1), new Point(Width / 2, Height / 2));
        return HoveredText;
    }

    internal static string FormatDayDetails(DailyTokenUsage day)
    {
        var usage = day.Usage;
        return L10n.Pick(
            $"{day.LocalDate.Month}月{day.LocalDate.Day}日\n总计 {usage.TotalTokens:N0} token\n输入 {usage.InputTokens:N0}（缓存 {usage.CachedInputTokens:N0}）\n输出 {usage.OutputTokens:N0}（推理 {usage.ReasoningOutputTokens:N0}）",
            $"{day.LocalDate:MMM d}\nTotal {usage.TotalTokens:N0} tokens\nInput {usage.InputTokens:N0} (cached {usage.CachedInputTokens:N0})\nOutput {usage.OutputTokens:N0} (reasoning {usage.ReasoningOutputTokens:N0})");
    }

    internal static string FormatCompact(long tokens) => tokens switch
    {
        >= 1_000_000 => $"{tokens / 1_000_000d:0.##}M",
        >= 1_000 => $"{tokens / 1_000d:0.#}K",
        _ => tokens.ToString("N0", CultureInfo.CurrentCulture)
    };

    private void SelectDay(int index, Point location)
    {
        if (index == _hoveredIndex) return;
        _hoveredIndex = index;
        Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
        AccessibleDescription = HoveredText;
        _toolTip.Hide(this);
        if (HoveredText is { } details && IsHandleCreated)
            _toolTip.Show(details, this,
                Math.Clamp(location.X + 10, 4, Math.Max(4, Width - 16)),
                Math.Clamp(location.Y + 12, 4, Math.Max(4, Height - 12)),
                12000);
        Invalidate();
    }

    private void UpdateAccessibility()
    {
        AccessibleName = _usage is null
            ? L10n.Pick("本周期 Token，等待重置周期", "Cycle tokens, waiting for reset cycle")
            : L10n.Pick(
                $"本周期 Token，共 {_usage.Total.TotalTokens:N0}",
                $"Cycle tokens, {_usage.Total.TotalTokens:N0} total");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _toolTip.Dispose();
        base.Dispose(disposing);
    }
}
