using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Globalization;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.Win32;

namespace CodexQuotaPanel;

internal static class UiPalette
{
    private const float MinimumDisplaySize = 8f;
    private const float MinimumBodySize = 7f;
    private const float MinimumCompactSize = 5.5f;

    private static readonly Lazy<HashSet<string>> InstalledFontNames = new(() =>
    {
        using var fonts = new InstalledFontCollection();
        return fonts.Families
            .Select(family => family.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    });

    private sealed record TypographyBaseline(float SizeInPoints, FontStyle Style, bool Monospace);
    private static readonly ConditionalWeakTable<Control, TypographyBaseline> TypographyBaselines = new();

    internal sealed record Colors(
        Color Canvas,
        Color Surface,
        Color SurfaceRaised,
        Color Border,
        Color Track,
        Color Text,
        Color Muted,
        Color Faint,
        Color Mint,
        Color Sky,
        Color Amber,
        Color Coral);

    private static readonly Colors DarkColors = new(
        Color.FromArgb(20, 22, 21), Color.FromArgb(29, 32, 30), Color.FromArgb(36, 39, 37),
        Color.FromArgb(52, 56, 52), Color.FromArgb(54, 58, 55), Color.FromArgb(239, 235, 220),
        Color.FromArgb(151, 153, 143), Color.FromArgb(102, 105, 99), Color.FromArgb(106, 228, 176),
        Color.FromArgb(126, 196, 255), Color.FromArgb(244, 185, 88), Color.FromArgb(255, 107, 102));

    private static readonly Colors LightColors = new(
        Color.FromArgb(247, 248, 245), Color.FromArgb(255, 255, 252), Color.FromArgb(235, 238, 233),
        Color.FromArgb(207, 213, 205), Color.FromArgb(200, 207, 199), Color.FromArgb(31, 36, 33),
        Color.FromArgb(82, 91, 85), Color.FromArgb(112, 121, 114), Color.FromArgb(20, 145, 105),
        Color.FromArgb(42, 121, 184), Color.FromArgb(181, 111, 12), Color.FromArgb(207, 68, 65));

    private static Colors _colors = DarkColors;

    public static Color Canvas => _colors.Canvas;
    public static Color Surface => _colors.Surface;
    public static Color SurfaceRaised => _colors.SurfaceRaised;
    public static Color Border => _colors.Border;
    public static Color Track => _colors.Track;
    public static Color Text => _colors.Text;
    public static Color Muted => _colors.Muted;
    public static Color Faint => _colors.Faint;
    public static Color Mint => _colors.Mint;
    public static Color Sky => _colors.Sky;
    public static Color Amber => _colors.Amber;
    public static Color Coral => _colors.Coral;
    internal static Colors CurrentColors => _colors;

    internal static Colors ResolveColors(int themeMode) => themeMode switch
    {
        1 => DarkColors,
        2 => LightColors,
        _ => SystemUsesLightTheme() ? LightColors : DarkColors
    };

    public static void SetTheme(int themeMode) => _colors = ResolveColors(themeMode);

    public static void ApplyTheme(Control root, Colors previousColors)
    {
        ArgumentNullException.ThrowIfNull(root);
        ApplyThemeRecursive(root, previousColors);
        root.Invalidate(true);
    }

    private static void ApplyThemeRecursive(Control control, Colors previous)
    {
        control.BackColor = TranslateThemeColor(control.BackColor, previous);
        control.ForeColor = TranslateThemeColor(control.ForeColor, previous);
        foreach (Control child in control.Controls)
            ApplyThemeRecursive(child, previous);
    }

    private static Color TranslateThemeColor(Color color, Colors previous)
    {
        if (color.ToArgb() == previous.Canvas.ToArgb()) return Canvas;
        if (color.ToArgb() == previous.Surface.ToArgb()) return Surface;
        if (color.ToArgb() == previous.SurfaceRaised.ToArgb()) return SurfaceRaised;
        if (color.ToArgb() == previous.Border.ToArgb()) return Border;
        if (color.ToArgb() == previous.Track.ToArgb()) return Track;
        if (color.ToArgb() == previous.Text.ToArgb()) return Text;
        if (color.ToArgb() == previous.Muted.ToArgb()) return Muted;
        if (color.ToArgb() == previous.Faint.ToArgb()) return Faint;
        if (color.ToArgb() == previous.Mint.ToArgb()) return Mint;
        if (color.ToArgb() == previous.Sky.ToArgb()) return Sky;
        if (color.ToArgb() == previous.Amber.ToArgb()) return Amber;
        if (color.ToArgb() == previous.Coral.ToArgb()) return Coral;
        return color;
    }

    private static bool SystemUsesLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                writable: false);
            return key?.GetValue("AppsUseLightTheme") is int value && value != 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or ArgumentException)
        {
            return false;
        }
    }

    public static Color ForRemaining(double remaining) =>
        remaining <= 20 ? Coral : remaining <= 45 ? Amber : Mint;

    // Display and body text intentionally share the platform's natural-width UI face.
    // This keeps Chinese text readable and avoids the cramped metrics of condensed fonts.
    public static Font Display(float size, FontStyle style = FontStyle.Regular) =>
        CreateFont(CurrentUiFontName(), Math.Max(size, MinimumDisplaySize), style);

    public static Font Body(float size, FontStyle style = FontStyle.Regular) =>
        CreateFont(CurrentUiFontName(), Math.Max(size, MinimumBodySize), style);

    public static Font Mono(float size, FontStyle style = FontStyle.Regular) =>
        CreateFont(MonospaceFontName(), Math.Max(size, MinimumCompactSize), style);

    /// <summary>
    /// Reapplies the current language's UI font to an existing control tree.
    /// Call this after <see cref="L10n.SetLanguage(AppLanguage)"/> so an open window
    /// updates immediately without replacing icon or emoji fonts.
    /// </summary>
    public static void ApplyTypography(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        ApplyTypographyRecursive(root);
        root.PerformLayout();
        root.Invalidate(true);
    }

    /// <summary>
    /// Applies a non-compounding user scale to settings surfaces. Each control's
    /// original point size is remembered, so repeated previews always scale from
    /// 100% instead of growing or shrinking cumulatively.
    /// </summary>
    public static void ApplyScaledTypography(Control root, int scalePercent)
    {
        ArgumentNullException.ThrowIfNull(root);
        scalePercent = PanelPreferenceManager.NormalizeSettingsFontScale(scalePercent);
        ApplyScaledTypographyRecursive(root, scalePercent / 100f);
        root.PerformLayout();
        root.Invalidate(true);
    }

    private static void ApplyScaledTypographyRecursive(Control control, float scale)
    {
        var current = control.Font;
        var hasBaseline = TypographyBaselines.TryGetValue(control, out var existingBaseline);
        var inheritsParentFont = !hasBaseline && control.Parent is not null &&
                                 current is not null && control.Parent.Font.Equals(current);
        if (!inheritsParentFont && current is not null && !IsIconOrEmojiFont(current.Name))
        {
            var baseline = hasBaseline
                ? existingBaseline!
                : TypographyBaselines.GetValue(control, item =>
                    new TypographyBaseline(item.Font.SizeInPoints, item.Font.Style,
                        IsMonospaceFont(item.Font.Name)));
            var scaledSize = baseline.SizeInPoints * scale;
            control.Font = baseline.Monospace
                ? CreateFont(MonospaceFontName(), Math.Max(scaledSize, MinimumCompactSize), baseline.Style)
                : CreateFont(CurrentUiFontName(), Math.Max(scaledSize, MinimumBodySize), baseline.Style);
        }

        foreach (Control child in control.Controls)
            ApplyScaledTypographyRecursive(child, scale);
    }

    private static void ApplyTypographyRecursive(Control control)
    {
        var current = control.Font;
        if (current is not null && !IsIconOrEmojiFont(current.Name))
        {
            control.Font = IsMonospaceFont(current.Name)
                ? Mono(current.SizeInPoints, current.Style)
                : Body(current.SizeInPoints, current.Style);
        }

        foreach (Control child in control.Controls)
            ApplyTypographyRecursive(child);
    }

    private static bool IsIconOrEmojiFont(string fontName) =>
        fontName.Equals("Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase) ||
        fontName.Equals("Segoe UI Emoji", StringComparison.OrdinalIgnoreCase);

    private static bool IsMonospaceFont(string fontName) =>
        fontName.Equals("Consolas", StringComparison.OrdinalIgnoreCase) ||
        fontName.Equals("Courier New", StringComparison.OrdinalIgnoreCase);

    private static string CurrentUiFontName() => L10n.IsChinese
        ? FirstInstalled("Microsoft YaHei UI", "Microsoft YaHei", "Segoe UI")
        : FirstInstalled("Segoe UI Variable Text", "Segoe UI", "Tahoma");

    private static string MonospaceFontName() => FirstInstalled("Consolas", "Courier New");

    private static string FirstInstalled(params string[] candidates)
    {
        foreach (var candidate in candidates)
        {
            if (InstalledFontNames.Value.Contains(candidate))
                return candidate;
        }

        return SystemFonts.MessageBoxFont?.FontFamily.Name ?? FontFamily.GenericSansSerif.Name;
    }

    private static Font CreateFont(string fontName, float size, FontStyle style)
    {
        try
        {
            return new Font(fontName, size, style, GraphicsUnit.Point);
        }
        catch (ArgumentException)
        {
            var fallbackFamily = SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;
            return new Font(fallbackFamily, size, style, GraphicsUnit.Point);
        }
    }

    public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class QuotaOrbControl : Control
{
    private const double FlameIdleEpsilon = 0.005d;
    private readonly System.Windows.Forms.Timer _flameTimer;
    private QuotaSnapshot? _snapshot;
    private LimitBucket? _outerBucket;
    private LimitBucket? _innerBucket;
    private RingDisplayConfiguration _configuration = new(
        new RingWindowSelection(300, RingWindowRole.Primary),
        new RingWindowSelection(10080, RingWindowRole.Secondary),
        UiPalette.Mint,
        UiPalette.Sky);
    private bool _live;
    private bool _flameAnimationEnabled = true;
    private int _flameStyle = 1;
    private double _flameIntensity;
    private double _targetFlameIntensity;
    private double _flamePhase;

    internal string OuterLabel => RingWindowCatalog.FormatShort(_configuration.Outer.WindowMinutes);
    internal string InnerLabel => RingWindowCatalog.FormatShort(_configuration.Inner.WindowMinutes);
    internal Color OuterColor => _configuration.OuterColor;
    internal Color InnerColor => _configuration.InnerColor;
    internal bool OuterAvailable => _outerBucket is not null;
    internal bool InnerAvailable => _innerBucket is not null;
    internal double ConsumptionIntensity => _targetFlameIntensity;
    internal bool FlameAnimationEnabled => _flameAnimationEnabled;
    internal int FlameStyle => _flameStyle;
    internal bool FlameTimerRunning => _flameTimer.Enabled;

    public QuotaOrbControl()
    {
        SetStyle(ControlStyles.SupportsTransparentBackColor |
                 ControlStyles.OptimizedDoubleBuffer |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.UserPaint |
                 ControlStyles.Selectable, true);
        BackColor = Color.Transparent;
        Size = new Size(88, 88);
        Cursor = Cursors.Hand;
        TabStop = true;
        AccessibleRole = AccessibleRole.PushButton;
        AccessibleName = L10n.Pick("Codex 额度悬浮球，单击展开详情", "Codex quota orb, click to open details");

        _flameTimer = new System.Windows.Forms.Timer { Interval = 50 };
        _flameTimer.Tick += (_, _) =>
        {
            _flameIntensity += (_targetFlameIntensity - _flameIntensity) * 0.18d;
            if (_targetFlameIntensity <= FlameIdleEpsilon && _flameIntensity <= FlameIdleEpsilon)
            {
                _flameIntensity = 0d;
                _flamePhase = 0d;
                _flameTimer.Stop();
            }
            else
            {
                _flamePhase += 0.07d + _flameIntensity * 0.24d;
            }
            Invalidate();
        };
    }

    public void SetSnapshot(QuotaSnapshot snapshot, bool live)
    {
        _snapshot = snapshot;
        _live = live;
        UpdateBuckets();
    }

    public void ConfigureRings(RingDisplayConfiguration configuration)
    {
        _configuration = configuration with
        {
            OuterColor = Color.FromArgb(255, configuration.OuterColor),
            InnerColor = Color.FromArgb(255, configuration.InnerColor)
        };
        UpdateBuckets();
    }

    private void UpdateBuckets()
    {
        _outerBucket = RingWindowCatalog.FindBucket(_snapshot, _configuration.Outer);
        _innerBucket = RingWindowCatalog.FindBucket(_snapshot, _configuration.Inner);
        var outerText = _outerBucket is null ? L10n.TemporarilyUnavailable :
            L10n.Pick($"剩余 {Math.Round(_outerBucket.RemainingPercent):0}%", $"{Math.Round(_outerBucket.RemainingPercent):0}% remaining");
        var innerText = _innerBucket is null ? L10n.TemporarilyUnavailable :
            L10n.Pick($"剩余 {Math.Round(_innerBucket.RemainingPercent):0}%", $"{Math.Round(_innerBucket.RemainingPercent):0}% remaining");
        AccessibleName = L10n.Pick(
            $"Codex 额度悬浮球，{RingWindowCatalog.FormatLong(_configuration.Outer.WindowMinutes)}{outerText}，" +
            $"{RingWindowCatalog.FormatLong(_configuration.Inner.WindowMinutes)}{innerText}，单击展开详情",
            $"Codex quota orb, {RingWindowCatalog.FormatLong(_configuration.Outer.WindowMinutes)} {outerText}, " +
            $"{RingWindowCatalog.FormatLong(_configuration.Inner.WindowMinutes)} {innerText}, click to open details");
        Invalidate();
    }

    public void SetConnectionState(bool live)
    {
        _live = live;
        Invalidate();
    }

    public void SetConsumptionIntensity(double intensity)
    {
        _targetFlameIntensity = Math.Clamp(intensity, 0d, 1d);
        if (!_flameTimer.Enabled)
            _flameIntensity = _targetFlameIntensity;
        if (_targetFlameIntensity <= FlameIdleEpsilon && _flameIntensity <= FlameIdleEpsilon)
        {
            _flameIntensity = 0d;
            _flamePhase = 0d;
        }
        UpdateFlameTimer();
        Invalidate();
    }

    public void SetFlameAnimationEnabled(bool enabled)
    {
        _flameAnimationEnabled = enabled;
        UpdateFlameTimer();
        Invalidate();
    }

    public void SetFlameStyle(int value)
    {
        _flameStyle = Math.Clamp(value, 0, 2);
        Invalidate();
    }

    internal void SetFlamePhaseForTest(double phase)
    {
        _flameTimer.Stop();
        _flamePhase = phase;
        Invalidate();
        Update();
    }

    protected override void OnVisibleChanged(EventArgs e)
    {
        base.OnVisibleChanged(e);
        UpdateFlameTimer();
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        UpdateFlameTimer();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        DrawOrb(e.Graphics);
    }

    internal Bitmap RenderTransparentPreview()
    {
        var bitmap = new Bitmap(Math.Max(1, Width), Math.Max(1, Height),
            System.Drawing.Imaging.PixelFormat.Format32bppPArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.Transparent);
        DrawOrb(graphics);
        return bitmap;
    }

    private void DrawOrb(Graphics graphics)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var scale = Math.Max(0.5f, Math.Min(Width, Height) / 88f);
        using var shell = UiPalette.RoundedRect(new RectangleF(1.5f, 1.5f, Width - 3, Height - 3), (Width - 3) / 2f);
        using var background = new LinearGradientBrush(
            ClientRectangle,
            UiPalette.SurfaceRaised,
            UiPalette.Canvas,
            LinearGradientMode.ForwardDiagonal);
        using var border = new Pen(UiPalette.Border, 1);
        graphics.FillPath(background, shell);
        graphics.DrawPath(border, shell);

        var outerBounds = new RectangleF(8 * scale, 8 * scale, Width - 16 * scale, Height - 16 * scale);
        DrawArc(graphics, outerBounds, 7 * scale,
            _outerBucket?.RemainingPercent, _configuration.OuterColor);
        DrawArc(graphics, new RectangleF(19 * scale, 19 * scale, Width - 38 * scale, Height - 38 * scale), 4.5f * scale,
            _innerBucket?.RemainingPercent, _configuration.InnerColor);

        using var labelFont = UiPalette.Mono(6.4f * scale, FontStyle.Bold);
        var outerText = $"{OuterLabel} {FormatPercent(_outerBucket)}";
        var innerText = $"{InnerLabel} {FormatPercent(_innerBucket)}";
        TextRenderer.DrawText(graphics, outerText, labelFont, ScaleRectangle(new RectangleF(22, 29, 44, 14), scale), _configuration.OuterColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(graphics, innerText, labelFont, ScaleRectangle(new RectangleF(22, 43, 44, 14), scale), _configuration.InnerColor,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        if (_flameAnimationEnabled)
        {
            switch (_flameStyle)
            {
                case 0:
                    DrawMinimalEmber(graphics, scale);
                    break;
                case 2:
                    DrawPixelFlame(graphics, scale);
                    break;
                default:
                    DrawFluidFlame(graphics, scale);
                    break;
            }
        }

        var statusCenter = ArcEndpoint(outerBounds, _outerBucket?.RemainingPercent);
        var statusDiameter = 5f * scale;
        using var statusBorder = new SolidBrush(UiPalette.Canvas);
        graphics.FillEllipse(statusBorder,
            statusCenter.X - statusDiameter * 0.72f,
            statusCenter.Y - statusDiameter * 0.72f,
            statusDiameter * 1.44f,
            statusDiameter * 1.44f);
        using var statusBrush = new SolidBrush(_live ? UiPalette.Mint : UiPalette.Amber);
        graphics.FillEllipse(statusBrush,
            statusCenter.X - statusDiameter / 2f,
            statusCenter.Y - statusDiameter / 2f,
            statusDiameter,
            statusDiameter);
    }

    private void DrawFluidFlame(Graphics graphics, float scale)
    {
        var intensity = (float)Math.Clamp(_flameIntensity, 0d, 1d);
        var pulse = (float)Math.Sin(_flamePhase);
        var sway = pulse * (0.35f + intensity * 1.15f) * scale;
        var width = (5.8f + intensity * 5.4f) * scale;
        var height = (7.2f + intensity * 9.8f + Math.Abs(pulse) * intensity * 1.8f) * scale;
        var centerX = Width / 2f + sway;
        var baseY = Math.Min(Height - 8f * scale, 76f * scale);
        var topY = baseY - height;

        var cool = Color.FromArgb(92, 190, 255);
        var warm = Color.FromArgb(255, 183, 76);
        var hot = Color.FromArgb(255, 91, 72);
        var flameColor = intensity < 0.58f
            ? Blend(cool, warm, intensity / 0.58f)
            : Blend(warm, hot, (intensity - 0.58f) / 0.42f);

        using var outer = CreateFlamePath(centerX, baseY, width, height, sway * 0.45f);
        // A restrained halo softens the tiny flame silhouette without turning
        // it into a blurry badge at the smallest supported orb size.
        using (var halo = new Pen(Color.FromArgb(34 + (int)(intensity * 24f), flameColor), 1.8f * scale)
               {
                   LineJoin = LineJoin.Round
               })
            graphics.DrawPath(halo, outer);
        using var outerBrush = new LinearGradientBrush(
            new RectangleF(centerX - width, topY, width * 2f, height),
            Color.FromArgb(235, Blend(Color.White, flameColor, 0.48f)),
            Color.FromArgb(225, flameColor),
            LinearGradientMode.Vertical);
        graphics.FillPath(outerBrush, outer);

        var innerIntensity = Math.Clamp((intensity - 0.12f) / 0.88f, 0f, 1f);
        if (innerIntensity > 0f)
        {
            var innerHeight = height * (0.46f + innerIntensity * 0.12f);
            var innerWidth = width * 0.48f;
            using var inner = CreateFlamePath(
                centerX - sway * 0.2f,
                baseY - 0.7f * scale,
                innerWidth,
                innerHeight,
                -sway * 0.18f);
            using var innerBrush = new SolidBrush(Color.FromArgb(
                205,
                Blend(Color.FromArgb(208, 246, 255), Color.FromArgb(255, 241, 174), innerIntensity)));
            graphics.FillPath(innerBrush, inner);
        }

        if (intensity > 0.68f)
            DrawFluidEmbers(graphics, centerX, topY, width, height, sway, scale, intensity, flameColor);
    }

    private void DrawFluidEmbers(
        Graphics graphics,
        float centerX,
        float topY,
        float flameWidth,
        float flameHeight,
        float sway,
        float scale,
        float intensity,
        Color flameColor)
    {
        // Staggered, tapered embers replace the old isolated circular spark.
        // Their short lifetime and opposing drift keep the motion organic while
        // remaining legible on a 56 px orb.
        ReadOnlySpan<float> phaseOffsets = stackalloc float[] { 0.08f, 0.47f, 0.79f };
        ReadOnlySpan<float> sideOffsets = stackalloc float[] { 0.62f, -0.42f, 0.88f };

        for (var index = 0; index < phaseOffsets.Length; index++)
        {
            var life = (float)((_flamePhase * (0.205d + index * 0.018d) + phaseOffsets[index]) % 1d);
            var envelope = MathF.Sin(life * MathF.PI);
            if (envelope < 0.12f) continue;

            var direction = MathF.Sign(sideOffsets[index]);
            var drift = direction * life * (1.3f + index * 0.35f) * scale;
            var x = centerX + flameWidth * sideOffsets[index] - sway * 0.32f + drift;
            var y = topY + flameHeight * (0.56f - life * 0.62f);
            var emberHeight = (1.25f + intensity * 1.05f - life * 0.34f) * scale;
            var emberWidth = Math.Max(0.34f * scale, emberHeight * 0.3f);
            var alpha = Math.Clamp((int)(168f * envelope * (1f - life * 0.35f)), 0, 168);

            using var trail = new Pen(Color.FromArgb(alpha / 4, flameColor), Math.Max(0.42f, 0.34f * scale))
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            graphics.DrawLine(trail,
                x + direction * 0.2f * scale,
                y - emberHeight * 0.15f,
                x - direction * 0.45f * scale,
                y - emberHeight * 1.25f);

            using var glow = new SolidBrush(Color.FromArgb(alpha / 5, flameColor));
            graphics.FillEllipse(glow,
                x - emberWidth * 1.25f,
                y - emberHeight * 0.9f,
                emberWidth * 2.5f,
                emberHeight * 2.5f);

            using var ember = CreateFlamePath(x, y + emberHeight * 0.45f, emberWidth, emberHeight, drift * 0.12f);
            using var emberBrush = new SolidBrush(Color.FromArgb(
                alpha,
                Blend(Color.FromArgb(255, 231, 164), flameColor, 0.58f)));
            graphics.FillPath(emberBrush, ember);
        }
    }

    private void DrawMinimalEmber(Graphics graphics, float scale)
    {
        var intensity = (float)Math.Clamp(_flameIntensity, 0d, 1d);
        var breath = 0.5f + 0.5f * (float)Math.Sin(_flamePhase * 0.72d);
        var drift = (float)Math.Sin(_flamePhase * 0.43d + 0.8d) * 0.42f * scale;
        var color = FlameColor(intensity);
        var emberWidth = (5.2f + intensity * 3.3f + breath * 0.45f) * scale;
        var emberHeight = (3.3f + intensity * 2.2f + breath * 0.35f) * scale;
        var centerX = Width / 2f + drift;
        var baseY = Math.Min(Height - 8.5f * scale, 76f * scale);
        var centerY = baseY - emberHeight * 0.45f;

        using (var wideGlow = new SolidBrush(Color.FromArgb(16 + (int)(intensity * 20f), color)))
            graphics.FillEllipse(wideGlow,
                centerX - emberWidth * 1.35f,
                centerY - emberHeight * 1.5f,
                emberWidth * 2.7f,
                emberHeight * 3f);
        using (var closeGlow = new SolidBrush(Color.FromArgb(42 + (int)(intensity * 42f), color)))
            graphics.FillEllipse(closeGlow,
                centerX - emberWidth * 0.82f,
                centerY - emberHeight * 0.92f,
                emberWidth * 1.64f,
                emberHeight * 1.84f);

        using var ember = CreateEmberPath(centerX, centerY, emberWidth, emberHeight);
        using (var rim = new Pen(Color.FromArgb(165, Blend(UiPalette.Canvas, color, 0.7f)), Math.Max(0.7f, 0.8f * scale))
               {
                   LineJoin = LineJoin.Round
               })
            graphics.DrawPath(rim, ember);
        using (var fill = new LinearGradientBrush(
                   new RectangleF(centerX - emberWidth / 2f, centerY - emberHeight / 2f, emberWidth, emberHeight),
                   Blend(Color.White, color, 0.5f),
                   Blend(color, UiPalette.Canvas, 0.2f),
                   LinearGradientMode.Vertical))
            graphics.FillPath(fill, ember);

        using (var heatLine = new Pen(
                   Color.FromArgb(205, Blend(Color.White, color, 0.36f)),
                   Math.Max(0.8f, 1.05f * scale))
               {
                   StartCap = LineCap.Round,
                   EndCap = LineCap.Round
               })
            graphics.DrawLine(heatLine,
                centerX - emberWidth * 0.22f,
                centerY - emberHeight * 0.04f,
                centerX + emberWidth * (0.13f + breath * 0.08f),
                centerY - emberHeight * 0.2f);

        if (intensity <= 0.58f) return;
        var wispLife = 0.5f + 0.5f * (float)Math.Sin(_flamePhase * 0.8d + 1.1d);
        var wispHeight = (2.1f + intensity * 2.6f + wispLife * 1.2f) * scale;
        using var wisp = CreateFlamePath(
            centerX + emberWidth * 0.16f,
            centerY - emberHeight * 0.35f,
            Math.Max(0.75f * scale, emberWidth * 0.16f),
            wispHeight,
            -drift * 0.7f);
        using var wispBrush = new SolidBrush(Color.FromArgb(
            55 + (int)(intensity * 55f),
            Blend(Color.White, color, 0.55f)));
        graphics.FillPath(wispBrush, wisp);
    }

    private void DrawPixelFlame(Graphics graphics, float scale)
    {
        var intensity = (float)Math.Clamp(_flameIntensity, 0d, 1d);
        var frame = (int)Math.Floor((_flamePhase * 1.65d) % 4d);
        var color = FlameColor(intensity);
        var cell = Math.Max(2, (int)Math.Round((2.35f + intensity * 0.55f) * scale));
        var originX = (int)Math.Round(Width / 2f - cell / 2f);
        var baseY = (int)Math.Round(Math.Min(Height - 7f * scale, 77f * scale));
        var state = graphics.Save();
        try
        {
            graphics.SmoothingMode = SmoothingMode.None;
            graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
            graphics.PixelOffsetMode = PixelOffsetMode.Half;

            using var outer = new SolidBrush(Color.FromArgb(245, Blend(color, UiPalette.Canvas, 0.18f)));
            using var middle = new SolidBrush(Color.FromArgb(250, color));
            using var core = new SolidBrush(Color.FromArgb(255,
                Blend(Color.FromArgb(211, 247, 255), Color.FromArgb(255, 238, 130), intensity)));

            Span<Point> outerCells = stackalloc Point[]
            {
                new(-2, 0), new(-1, 0), new(0, 0), new(1, 0), new(2, 0),
                new(-2, 1), new(-1, 1), new(0, 1), new(1, 1), new(2, 1),
                new(-1, 2), new(0, 2), new(1, 2),
                new(frame is 0 or 3 ? -1 : 0, 3),
                new(frame is 0 or 1 ? 0 : 1, 4)
            };
            DrawPixelCells(graphics, outer, originX, baseY, cell, outerCells);

            Span<Point> middleCells = stackalloc Point[]
            {
                new(-1, 0), new(0, 0), new(1, 0),
                new(-1, 1), new(0, 1), new(1, 1),
                new(0, 2),
                new(frame is 1 or 2 ? 0 : -1, 3)
            };
            DrawPixelCells(graphics, middle, originX, baseY, cell, middleCells);

            Span<Point> coreCells = stackalloc Point[]
            {
                new(0, 0), new(0, 1), new(frame == 2 ? 1 : 0, 2)
            };
            DrawPixelCells(graphics, core, originX, baseY, cell, coreCells);

            if (intensity > 0.52f)
            {
                var emberX = frame is 0 or 1 ? 2 : -2;
                FillPixelCell(graphics, middle, originX, baseY, cell, emberX, 5);
                if (intensity > 0.82f && frame is 1 or 3)
                    FillPixelCell(graphics, outer, originX, baseY, cell, -emberX, 4);
            }
        }
        finally
        {
            graphics.Restore(state);
        }
    }

    private static GraphicsPath CreateEmberPath(float centerX, float centerY, float width, float height)
    {
        var path = new GraphicsPath();
        path.StartFigure();
        path.AddBezier(
            centerX - width / 2f, centerY,
            centerX - width * 0.42f, centerY - height * 0.58f,
            centerX + width * 0.28f, centerY - height * 0.62f,
            centerX + width / 2f, centerY - height * 0.08f);
        path.AddBezier(
            centerX + width / 2f, centerY - height * 0.08f,
            centerX + width * 0.34f, centerY + height * 0.48f,
            centerX - width * 0.34f, centerY + height * 0.45f,
            centerX - width / 2f, centerY);
        path.CloseFigure();
        return path;
    }

    private static void DrawPixelCells(
        Graphics graphics,
        Brush brush,
        int originX,
        int baseY,
        int cell,
        ReadOnlySpan<Point> cells)
    {
        foreach (var point in cells)
            FillPixelCell(graphics, brush, originX, baseY, cell, point.X, point.Y);
    }

    private static void FillPixelCell(
        Graphics graphics,
        Brush brush,
        int originX,
        int baseY,
        int cell,
        int x,
        int y) =>
        graphics.FillRectangle(brush, originX + x * cell, baseY - (y + 1) * cell, cell, cell);

    private static Color FlameColor(float intensity)
    {
        var cool = Color.FromArgb(92, 190, 255);
        var warm = Color.FromArgb(255, 183, 76);
        var hot = Color.FromArgb(255, 91, 72);
        return intensity < 0.58f
            ? Blend(cool, warm, intensity / 0.58f)
            : Blend(warm, hot, (intensity - 0.58f) / 0.42f);
    }

    private static GraphicsPath CreateFlamePath(float centerX, float baseY, float width, float height, float sway)
    {
        var topY = baseY - height;
        var path = new GraphicsPath();
        path.StartFigure();
        path.AddBezier(
            new PointF(centerX - width / 2f, baseY),
            new PointF(centerX - width * 0.76f, baseY - height * 0.33f),
            new PointF(centerX - width * 0.18f + sway, topY + height * 0.32f),
            new PointF(centerX + sway, topY));
        path.AddBezier(
            new PointF(centerX + sway, topY),
            new PointF(centerX + width * 0.16f + sway, topY + height * 0.26f),
            new PointF(centerX + width * 0.78f, baseY - height * 0.36f),
            new PointF(centerX + width / 2f, baseY));
        path.AddBezier(
            new PointF(centerX + width / 2f, baseY),
            new PointF(centerX + width * 0.18f, baseY + height * 0.08f),
            new PointF(centerX - width * 0.18f, baseY + height * 0.08f),
            new PointF(centerX - width / 2f, baseY));
        path.CloseFigure();
        return path;
    }

    private static Color Blend(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(from.A + (to.A - from.A) * amount),
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }

    private void UpdateFlameTimer()
    {
        var hasMotion = _targetFlameIntensity > FlameIdleEpsilon || _flameIntensity > FlameIdleEpsilon;
        if (_flameAnimationEnabled && hasMotion && Visible && IsHandleCreated && !DesignMode)
            _flameTimer.Start();
        else
            _flameTimer.Stop();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _flameTimer.Dispose();
        base.Dispose(disposing);
    }

    private static void DrawArc(Graphics graphics, RectangleF bounds, float width, double? remaining, Color baseColor)
    {
        const float start = -220;
        const float sweep = 260;
        using var track = new Pen(Color.FromArgb(54, 60, 56), width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawArc(track, bounds, start, sweep);
        if (remaining is null or <= 0) return;
        using var value = new Pen(baseColor, width)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round
        };
        graphics.DrawArc(value, bounds, start, sweep * (float)(Math.Clamp(remaining.Value, 0, 100) / 100d));
    }

    private static PointF ArcEndpoint(RectangleF bounds, double? remaining)
    {
        const float start = -220f;
        const float sweep = 260f;
        var progress = Math.Clamp(remaining ?? 0d, 0d, 100d) / 100d;
        var angle = (start + sweep * progress) * Math.PI / 180d;
        return new PointF(
            bounds.Left + bounds.Width / 2f + bounds.Width / 2f * (float)Math.Cos(angle),
            bounds.Top + bounds.Height / 2f + bounds.Height / 2f * (float)Math.Sin(angle));
    }

    private static string FormatPercent(LimitBucket? bucket) => bucket is null
        ? "—"
        : $"{Math.Round(bucket.RemainingPercent):0}";

    private static Rectangle ScaleRectangle(RectangleF rectangle, float scale) => Rectangle.Round(new RectangleF(
        rectangle.X * scale,
        rectangle.Y * scale,
        rectangle.Width * scale,
        rectangle.Height * scale));
}

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
        var bounds = new RectangleF(10, 10, Width - 20, Height - 20);
        using var track = new Pen(UiPalette.Track, 8) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        using var value = new Pen(UiPalette.ForRemaining(Remaining), 8) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        e.Graphics.DrawArc(track, bounds, -225, 270);
        e.Graphics.DrawArc(value, bounds, -225, (float)(270 * Remaining / 100d));

        var percent = $"{Math.Round(Remaining):0}%";
        using var numberFont = UiPalette.Display(25, FontStyle.Bold);
        using var labelFont = UiPalette.Body(8.3f, FontStyle.Bold);
        using var textBrush = new SolidBrush(UiPalette.Text);
        using var mutedBrush = new SolidBrush(UiPalette.Muted);
        var numberSize = e.Graphics.MeasureString(percent, numberFont);
        var numberY = (Height - numberSize.Height) / 2f - 5;
        e.Graphics.DrawString(percent, numberFont, textBrush, (Width - numberSize.Width) / 2f, numberY);
        var label = L10n.Remaining;
        var labelSize = e.Graphics.MeasureString(label, labelFont);
        e.Graphics.DrawString(label, labelFont, mutedBrush, (Width - labelSize.Width) / 2f, numberY + numberSize.Height - 1);
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
        using var cardPath = UiPalette.RoundedRect(new RectangleF(0.5f, 0.5f, Width - 1, Height - 1), 10);
        using var cardBrush = new SolidBrush(UiPalette.Surface);
        using var borderPen = new Pen(UiPalette.Border, 1);
        e.Graphics.FillPath(cardBrush, cardPath);
        e.Graphics.DrawPath(borderPen, cardPath);

        using var labelFont = UiPalette.Display(11f, FontStyle.Bold);
        using var valueFont = UiPalette.Mono(9.5f, FontStyle.Bold);
        using var detailFont = UiPalette.Body(8.2f);
        using var textBrush = new SolidBrush(UiPalette.Text);
        using var mutedBrush = new SolidBrush(UiPalette.Muted);

        e.Graphics.DrawString(_label, labelFont, textBrush, 12, 7);
        var remainingText = _bucket is null ? "—" : L10n.Pick(
            $"{Math.Round(_bucket.RemainingPercent):0}% 剩余",
            $"{Math.Round(_bucket.RemainingPercent):0}% left");
        var remainingSize = e.Graphics.MeasureString(remainingText, valueFont);
        var color = _bucket is null ? UiPalette.Muted : UiPalette.ForRemaining(_bucket.RemainingPercent);
        using var valueBrush = new SolidBrush(color);
        e.Graphics.DrawString(remainingText, valueFont, valueBrush, Width - remainingSize.Width - 12, 8);

        var cutoffMinute = DateTimeOffset.Now.ToUniversalTime().ToUnixTimeSeconds() / 60 - 24 * 60;
        var trend = _bucket?.WindowMinutes is > 0
            ? _history.Where(point => point.Slot == HistorySlot && point.WindowMinutes == _bucket.WindowMinutes.Value)
                .Where(point => point.UtcMinute >= cutoffMinute)
                .OrderBy(point => point.UtcMinute)
                .ToArray()
            : [];
        if (trend.Length >= 2)
        {
            DrawTrend(e.Graphics, new RectangleF(12, 27, Width - 24, 16), trend, _trendColor);
        }
        else
        {
            var trackRect = new RectangleF(12, 30, Width - 24, 5);
            using var trackPath = UiPalette.RoundedRect(trackRect, 2.5f);
            using var trackBrush = new SolidBrush(UiPalette.Track);
            e.Graphics.FillPath(trackBrush, trackPath);
            if (_bucket is not null && _bucket.RemainingPercent > 0)
            {
                var fillWidth = Math.Max(6, trackRect.Width * (float)(_bucket.RemainingPercent / 100d));
                using var fillPath = UiPalette.RoundedRect(new RectangleF(trackRect.X, trackRect.Y, fillWidth, trackRect.Height), 2.5f);
                using var fillBrush = new SolidBrush(color);
                e.Graphics.FillPath(fillBrush, fillPath);
            }
        }

        var detail = _bucket is null ? L10n.WaitingSnapshot : FormatReset(_bucket.ResetsAt);
        e.Graphics.DrawString(detail, detailFont, mutedBrush, 12, 48);
        using var trendFont = UiPalette.Mono(6.8f, FontStyle.Bold);
        var trendLabel = trend.Length >= 2 ? L10n.Trend24Hours : L10n.TrendAccumulating;
        var trendSize = e.Graphics.MeasureString(trendLabel, trendFont);
        e.Graphics.DrawString(trendLabel, trendFont, mutedBrush, Width - trendSize.Width - 12, 50);
    }

    private static void DrawTrend(Graphics graphics, RectangleF bounds, IReadOnlyList<QuotaHistoryPoint> points, Color color)
    {
        using var background = new SolidBrush(Color.FromArgb(55, UiPalette.Track));
        using var baseline = new Pen(Color.FromArgb(70, UiPalette.Border), 1);
        graphics.FillRectangle(background, bounds);
        graphics.DrawLine(baseline, bounds.Left, bounds.Top + bounds.Height / 2f, bounds.Right, bounds.Top + bounds.Height / 2f);

        var nowMinute = DateTimeOffset.Now.ToUniversalTime().ToUnixTimeSeconds() / 60;
        var cutoff = nowMinute - 24 * 60;
        using var line = new Pen(color, 1.6f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
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
        graphics.FillEllipse(dot, lastPoint.Value.X - 2.2f, lastPoint.Value.Y - 2.2f, 4.4f, 4.4f);
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
            ? (_pressed ? Color.FromArgb(196, 193, 181) : _hovered ? Color.FromArgb(222, 219, 205) : UiPalette.Text)
            : (_pressed ? UiPalette.Track : _hovered ? UiPalette.SurfaceRaised : UiPalette.Surface);
        using var fill = new SolidBrush(background);
        e.Graphics.FillPath(fill, path);
        if (!Primary)
        {
            using var border = new Pen(UiPalette.Border, 1);
            e.Graphics.DrawPath(border, path);
        }

        var foreground = Primary ? UiPalette.Canvas : UiPalette.Text;
        TextRenderer.DrawText(e.Graphics, Text, Font, ClientRectangle, foreground,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
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
