using System.Drawing.Drawing2D;
using System.Globalization;

namespace CodexQuotaPanel;

internal sealed class TokenUsageDetailsForm : Form
{
    private readonly Panel _viewport;
    private readonly TokenUsageDetailsView _view;

    internal TokenCycleUsage Usage => _view.Usage;

    internal TokenUsageDetailsForm(TokenCycleUsage usage)
    {
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(620, 590);
        MinimumSize = new Size(520, 440);
        MaximumSize = new Size(900, 900);
        StartPosition = FormStartPosition.Manual;
        FormBorderStyle = FormBorderStyle.Sizable;
        BackColor = UiPalette.Canvas;
        ForeColor = UiPalette.Text;
        Text = L10n.Pick("Codex 使用明细", "Codex usage details");
        ShowInTaskbar = false;
        KeyPreview = true;
        DoubleBuffered = true;

        _viewport = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = UiPalette.Canvas,
            Padding = Padding.Empty
        };
        _view = new TokenUsageDetailsView(usage)
        {
            Location = Point.Empty,
            Width = _viewport.ClientSize.Width
        };
        _viewport.Controls.Add(_view);
        Controls.Add(_viewport);
        _viewport.ClientSizeChanged += (_, _) => ResizeContent();
        KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) Close();
        };
        Shown += (_, _) => ResizeContent();
    }

    internal void SetUsage(TokenCycleUsage usage)
    {
        _view.SetUsage(usage);
        ResizeContent();
    }

    internal void CenterOnWorkingArea(Rectangle workingArea)
    {
        // Create the hidden native handle first so PerMonitorV2 can settle the
        // target DPI before the final center point is calculated. Doing the
        // calculation twice accounts for a hidden WM_DPICHANGED size update.
        _ = Handle;
        Location = DisplayPlacement.CenterInArea(Size, workingArea);
        Location = DisplayPlacement.CenterInArea(Size, workingArea);
    }

    internal void ApplyLanguage()
    {
        Text = L10n.Pick("Codex 使用明细", "Codex usage details");
        _view.Invalidate();
    }

    internal void ApplyTheme(UiPalette.Colors previousColors)
    {
        BackColor = UiPalette.Canvas;
        ForeColor = UiPalette.Text;
        _viewport.BackColor = UiPalette.Canvas;
        _view.BackColor = UiPalette.Canvas;
        _view.ForeColor = UiPalette.Text;
        _view.Invalidate();
    }

    internal Bitmap CaptureContentForTest()
    {
        var bitmap = new Bitmap(Math.Max(1, _view.Width), Math.Max(1, _view.Height));
        _view.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        return bitmap;
    }

    private void ResizeContent()
    {
        var scrollbarAllowance = _view.Height > _viewport.ClientSize.Height
            ? SystemInformation.VerticalScrollBarWidth
            : 0;
        _view.Width = Math.Max(420, _viewport.ClientSize.Width - scrollbarAllowance);
    }
}

internal sealed class TokenUsageDetailsView : Control
{
    private const int SidePadding = 22;
    private TokenCycleUsage _usage;

    internal TokenCycleUsage Usage => _usage;

    internal TokenUsageDetailsView(TokenCycleUsage usage)
    {
        _usage = usage;
        BackColor = UiPalette.Canvas;
        ForeColor = UiPalette.Text;
        SetStyle(ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.ResizeRedraw |
                 ControlStyles.UserPaint, true);
        AccessibleRole = AccessibleRole.Pane;
        UpdateHeight();
    }

    internal void SetUsage(TokenCycleUsage usage)
    {
        _usage = usage;
        UpdateHeight();
        Invalidate();
    }

    protected override void OnSizeChanged(EventArgs e)
    {
        base.OnSizeChanged(e);
        UpdateHeight();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
        var scale = Math.Max(0.5f, DeviceDpi / 96f);
        float S(float value) => value * scale;
        var left = S(SidePadding);
        var width = Math.Max(S(300), ClientSize.Width - S(SidePadding * 2));

        using var titleFont = UiPalette.Display(16f, FontStyle.Bold);
        using var sectionFont = UiPalette.Display(10f, FontStyle.Bold);
        using var bodyFont = UiPalette.Body(8.4f);
        using var smallFont = UiPalette.Body(7.1f);
        using var monoFont = UiPalette.Mono(7.3f, FontStyle.Bold);
        using var textBrush = new SolidBrush(UiPalette.Text);
        using var mutedBrush = new SolidBrush(UiPalette.Muted);
        using var faintBrush = new SolidBrush(UiPalette.Faint);
        using var accentBrush = new SolidBrush(UiPalette.Mint);
        using var borderPen = new Pen(UiPalette.Border, Math.Max(1f, scale));

        var y = S(18);
        e.Graphics.DrawString(L10n.Pick("使用明细", "Usage details"), titleFont, textBrush, left, y);
        y += S(31);
        var cycleText = L10n.Pick(
            $"当前重置周期 · {_usage.StartsAt.ToLocalTime():M月d日 HH:mm} – {_usage.ResetsAt.ToLocalTime():M月d日 HH:mm}",
            $"Current reset cycle · {_usage.StartsAt.ToLocalTime():MMM d HH:mm} – {_usage.ResetsAt.ToLocalTime():MMM d HH:mm}");
        e.Graphics.DrawString(cycleText, bodyFont, mutedBrush, left, y);
        y += S(29);

        var summaryHeight = S(82);
        DrawCard(e.Graphics, new RectangleF(left, y, width, summaryHeight), borderPen);
        e.Graphics.DrawString(L10n.Pick("本周期 API 估算", "Cycle API estimate"), sectionFont, textBrush,
            left + S(14), y + S(11));
        var totalCost = _usage.EstimatedUsd > 0
            ? DailyTokenUsageControl.FormatUsd(_usage.EstimatedUsd)
            : "—";
        var totalSize = e.Graphics.MeasureString(totalCost, sectionFont);
        e.Graphics.DrawString(totalCost, sectionFont, accentBrush,
            left + width - totalSize.Width - S(14), y + S(11));
        var rawSummary = L10n.Pick(
            $"原始 Token {_usage.Total.TotalTokens:N0}",
            $"Raw tokens {_usage.Total.TotalTokens:N0}");
        if (_usage.Total.CacheWriteInputTokens > 0)
            rawSummary += L10n.Pick(
                $" · 缓存写入 {_usage.Total.CacheWriteInputTokens:N0}",
                $" · cache write {_usage.Total.CacheWriteInputTokens:N0}");
        e.Graphics.DrawString(rawSummary, monoFont, mutedBrush, left + S(14), y + S(39));
        var basis = L10n.Pick(
            $"公开 API 价格估算 · 基准 {ApiCostEstimator.BasisDate} · 非订阅账单或额度换算",
            $"Published API price estimate · basis {ApiCostEstimator.BasisDate} · not a subscription bill or quota conversion");
        e.Graphics.DrawString(basis, smallFont, faintBrush, left + S(14), y + S(59));
        y += summaryHeight + S(16);

        e.Graphics.DrawString(L10n.Pick("模型与速率汇总", "Model and speed summary"), sectionFont, textBrush, left, y);
        y += S(27);
        y = DrawSlices(e.Graphics, _usage.Slices, left, y, width, bodyFont, smallFont, monoFont,
            textBrush, mutedBrush, accentBrush, borderPen, showEmpty: true);
        y += S(20);

        e.Graphics.DrawString(L10n.Pick("每日使用", "Daily usage"), sectionFont, textBrush, left, y);
        y += S(28);
        foreach (var day in _usage.Days.OrderByDescending(item => item.LocalDate))
        {
            var dayHeight = DayLogicalHeight(day) * scale;
            DrawCard(e.Graphics, new RectangleF(left, y, width, dayHeight), borderPen);
            var dateText = L10n.Pick(
                $"{day.LocalDate.Month}月{day.LocalDate.Day}日",
                day.LocalDate.ToString("MMM d", CultureInfo.CurrentCulture));
            e.Graphics.DrawString(dateText, sectionFont, textBrush, left + S(14), y + S(10));
            var dayCost = day.EstimatedUsd > 0
                ? DailyTokenUsageControl.FormatUsd(day.EstimatedUsd)
                : day.Usage.TotalTokens > 0 ? DailyTokenUsageControl.NoPublicRateLabel : "$0.0000";
            var dayCostSize = e.Graphics.MeasureString(dayCost, monoFont);
            e.Graphics.DrawString(dayCost, monoFont,
                day.EstimatedUsd > 0 ? accentBrush : mutedBrush,
                left + width - dayCostSize.Width - S(14), y + S(12));

            var rowY = y + S(38);
            if (day.Slices.Count == 0)
            {
                e.Graphics.DrawString(L10n.Pick("无本机会话记录", "No local session records"),
                    bodyFont, mutedBrush, left + S(14), rowY);
            }
            else
            {
                foreach (var slice in day.Slices)
                {
                    DrawSliceRow(e.Graphics, slice, left + S(14), rowY, width - S(28),
                        bodyFont, smallFont, monoFont, textBrush, mutedBrush, accentBrush);
                    rowY += S(29);
                }
            }
            y += dayHeight + S(10);
        }

        y += S(8);
        e.Graphics.DrawString(L10n.Pick("数据质量", "Data quality"), sectionFont, textBrush, left, y);
        y += S(27);
        var healthHeight = S(58);
        DrawCard(e.Graphics, new RectangleF(left, y, width, healthHeight), borderPen);
        var health = _usage.Health;
        var quality = health.IsPartial
            ? L10n.Pick("部分记录无法解析", "Some records could not be parsed")
            : L10n.Pick("记录完整", "Records complete");
        var healthLine = L10n.Pick(
            $"{quality} · 归因 {health.AttributionCoverage:P0} · 文件 {health.ParsedFileCount}",
            $"{quality} · attributed {health.AttributionCoverage:P0} · files {health.ParsedFileCount}");
        e.Graphics.DrawString(healthLine, bodyFont, health.IsPartial ? mutedBrush : accentBrush,
            left + S(14), y + S(10));
        var cacheLine = L10n.Pick(
            $"缓存命中 {health.CachedFileCount} · 增量读取 {health.IncrementalFileCount} · 去重 {health.DuplicateEventCount}",
            $"cache hits {health.CachedFileCount} · incremental {health.IncrementalFileCount} · deduplicated {health.DuplicateEventCount}");
        e.Graphics.DrawString(cacheLine, smallFont, faintBrush, left + S(14), y + S(34));
    }

    private float DrawSlices(
        Graphics graphics,
        IReadOnlyList<TokenUsageSlice> slices,
        float left,
        float top,
        float width,
        Font bodyFont,
        Font smallFont,
        Font monoFont,
        Brush textBrush,
        Brush mutedBrush,
        Brush accentBrush,
        Pen borderPen,
        bool showEmpty)
    {
        var scale = Math.Max(0.5f, DeviceDpi / 96f);
        float S(float value) => value * scale;
        var rowCount = Math.Max(showEmpty ? 1 : 0, slices.Count);
        var height = S(18 + rowCount * 31);
        DrawCard(graphics, new RectangleF(left, top, width, height), borderPen);
        var y = top + S(9);
        if (slices.Count == 0)
        {
            graphics.DrawString(L10n.Pick("暂无可归类记录", "No attributable records"),
                bodyFont, mutedBrush, left + S(14), y);
        }
        else
        {
            foreach (var slice in slices)
            {
                DrawSliceRow(graphics, slice, left + S(14), y, width - S(28),
                    bodyFont, smallFont, monoFont, textBrush, mutedBrush, accentBrush);
                y += S(31);
            }
        }
        return top + height;
    }

    private void DrawSliceRow(
        Graphics graphics,
        TokenUsageSlice slice,
        float left,
        float top,
        float width,
        Font bodyFont,
        Font smallFont,
        Font monoFont,
        Brush textBrush,
        Brush mutedBrush,
        Brush accentBrush)
    {
        var scale = Math.Max(0.5f, DeviceDpi / 96f);
        float S(float value) => value * scale;
        var name = $"{slice.ModelDisplay} · {slice.SpeedDisplay}";
        graphics.DrawString(name, bodyFont, textBrush, left, top);
        var raw = $"{slice.Usage.TotalTokens:N0} raw";
        if (slice.Usage.CacheWriteInputTokens > 0)
            raw += $" · cw {slice.Usage.CacheWriteInputTokens:N0}";
        graphics.DrawString(raw, smallFont, mutedBrush, left, top + S(15));
        var value = slice.IsPriced
            ? DailyTokenUsageControl.FormatUsd(slice.EstimatedUsd)
            : DailyTokenUsageControl.NoPublicRateLabel;
        var size = graphics.MeasureString(value, monoFont);
        graphics.DrawString(value, monoFont, slice.IsPriced ? accentBrush : mutedBrush,
            left + width - size.Width, top + S(4));
    }

    private static void DrawCard(Graphics graphics, RectangleF bounds, Pen borderPen)
    {
        using var path = UiPalette.RoundedRect(bounds, 10f * graphics.DpiX / 96f);
        using var brush = new SolidBrush(UiPalette.Surface);
        graphics.FillPath(brush, path);
        graphics.DrawPath(borderPen, path);
    }

    private void UpdateHeight()
    {
        var logical = 18 + 31 + 29 + 82 + 16 + 27 + 18 + Math.Max(1, _usage.Slices.Count) * 31 +
                      20 + 28 + _usage.Days.Sum(day => DayLogicalHeight(day) + 10) + 8 + 27 + 58 + 18;
        Height = Math.Max(420, (int)Math.Ceiling(logical * Math.Max(0.5f, DeviceDpi / 96f)));
    }

    private static int DayLogicalHeight(DailyTokenUsage day) =>
        48 + Math.Max(1, day.Slices.Count) * 29;
}
