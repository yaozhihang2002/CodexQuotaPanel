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
    private static readonly ConditionalWeakTable<Control, ScaledFontLease> ScaledFontLeases = new();
    private static readonly ConditionalWeakTable<Control, RetiredFontPool> RetiredFontPools = new();

    private sealed class ScaledFontLease
    {
        private Font? _font;

        internal ScaledFontLease(Control owner) => owner.Disposed += OnOwnerDisposed;

        internal Font? Replace(Control owner, Font font)
        {
            // Control.Font ignores an assignment when the new Font is value-
            // equal to the current one. Treat that as a no-op: otherwise the
            // lease would remember the unused new instance and retire the font
            // that the control is still actively painting with.
            if (owner.Font is { } current && current.Equals(font))
            {
                font.Dispose();
                return null;
            }

            var previous = _font;
            owner.Font = font;
            if (!ReferenceEquals(owner.Font, font))
            {
                font.Dispose();
                return null;
            }
            _font = font;
            return previous;
        }

        private void OnOwnerDisposed(object? sender, EventArgs e)
        {
            _font?.Dispose();
            _font = null;
        }
    }

    private sealed class RetiredFontPool : IDisposable
    {
        private readonly List<Font> _fonts = [];
        private readonly System.Windows.Forms.Timer _timer = new() { Interval = 750 };

        internal RetiredFontPool(Control owner)
        {
            _timer.Tick += (_, _) => Drain();
            owner.Disposed += (_, _) => Dispose();
        }

        internal void Retire(IEnumerable<Font> fonts)
        {
            _fonts.AddRange(fonts);
            _timer.Stop();
            _timer.Start();
        }

        private void Drain()
        {
            _timer.Stop();
            foreach (var font in _fonts) font.Dispose();
            _fonts.Clear();
        }

        public void Dispose()
        {
            _timer.Stop();
            _timer.Dispose();
            foreach (var font in _fonts) font.Dispose();
            _fonts.Clear();
        }
    }

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

    // Neutral surfaces keep the Windows UI familiar while the mint accent
    // remains recognisably Codex.  Muted/faint and semantic colours retain at
    // least normal-text contrast on their corresponding canvas and surface.
    private static readonly Colors DarkColors = new(
        Color.FromArgb(16, 19, 18), Color.FromArgb(24, 28, 26), Color.FromArgb(34, 40, 37),
        Color.FromArgb(55, 65, 59), Color.FromArgb(62, 73, 67), Color.FromArgb(242, 245, 243),
        Color.FromArgb(168, 177, 172), Color.FromArgb(135, 145, 139), Color.FromArgb(88, 214, 166),
        Color.FromArgb(118, 191, 242), Color.FromArgb(234, 180, 91), Color.FromArgb(240, 107, 103));

    private static readonly Colors LightColors = new(
        // Soft mineral neutrals avoid the clinical glare of pure white while
        // preserving clear separation between canvas, cards and selections.
        Color.FromArgb(239, 243, 241), Color.FromArgb(249, 251, 250), Color.FromArgb(231, 238, 234),
        Color.FromArgb(193, 206, 199), Color.FromArgb(213, 223, 218), Color.FromArgb(24, 32, 28),
        Color.FromArgb(84, 98, 91), Color.FromArgb(103, 116, 109), Color.FromArgb(8, 123, 88),
        Color.FromArgb(40, 111, 168), Color.FromArgb(154, 95, 0), Color.FromArgb(184, 63, 59));

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
    // The orb is a desktop instrument rather than part of the settings canvas.
    // Keep its default shell consistently dark in both application themes so
    // the coloured quota rings retain the same contrast over any wallpaper.
    internal static Color DefaultOrbBackground => Color.Black;

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

    public static Color Mix(Color from, Color to, float amount)
    {
        amount = Math.Clamp(amount, 0f, 1f);
        return Color.FromArgb(
            (int)Math.Round(from.A + (to.A - from.A) * amount),
            (int)Math.Round(from.R + (to.R - from.R) * amount),
            (int)Math.Round(from.G + (to.G - from.G) * amount),
            (int)Math.Round(from.B + (to.B - from.B) * amount));
    }

    // Display and body text intentionally share the platform's natural-width UI face.
    // This keeps Chinese text readable and avoids the cramped metrics of condensed fonts.
    public static Font Display(float size, FontStyle style = FontStyle.Regular) =>
        CreateFont(CurrentUiFontName(), Math.Max(size, MinimumDisplaySize), style);

    public static Font Body(float size, FontStyle style = FontStyle.Regular) =>
        CreateFont(CurrentUiFontName(), Math.Max(size, MinimumBodySize), style);

    internal static Font LatinBody(float size, FontStyle style = FontStyle.Regular) =>
        CreateFont(
            FirstInstalled("Segoe UI Variable Text", "Segoe UI", "Tahoma"),
            Math.Max(size, MinimumBodySize),
            style);

    public static Font Mono(float size, FontStyle style = FontStyle.Regular) =>
        CreateFont(MonospaceFontName(), Math.Max(size, MinimumCompactSize), style);

    /// <summary>
    /// Creates a monospace font whose size is already expressed in device
    /// pixels. This is used by manually scaled drawing surfaces so Windows does
    /// not apply the monitor DPI a second time to an already scaled size.
    /// </summary>
    internal static Font MonoPixels(float size, FontStyle style = FontStyle.Regular) =>
        CreateFont(
            MonospaceFontName(),
            Math.Max(size, MinimumCompactSize * 96f / 72f),
            style,
            GraphicsUnit.Pixel);

    /// <summary>
    /// Reapplies the current language's UI font to an existing control tree.
    /// Call this after <see cref="L10n.SetLanguage(AppLanguage)"/> so an open window
    /// updates immediately without replacing icon or emoji fonts.
    /// </summary>
    public static void ApplyTypography(Control root)
    {
        ArgumentNullException.ThrowIfNull(root);
        var retiredFonts = new List<Font>();
        ApplyTypographyRecursive(root, retiredFonts);
        root.PerformLayout();
        RetireFonts(root, retiredFonts);
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
        var controls = EnumerateControlTree(root).ToArray();
        var retiredFonts = new List<Font>();
        foreach (var control in controls) control.SuspendLayout();
        try
        {
            ApplyScaledTypographyRecursive(root, scalePercent / 100f, retiredFonts);
        }
        finally
        {
            for (var index = controls.Length - 1; index >= 0; index--)
                controls[index].ResumeLayout(performLayout: false);
        }
        root.PerformLayout();
        RetireFonts(root, retiredFonts);
        root.Invalidate(true);
    }

    private static IEnumerable<Control> EnumerateControlTree(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
            foreach (var descendant in EnumerateControlTree(child))
                yield return descendant;
    }

    private static void ApplyScaledTypographyRecursive(Control control, float scale, List<Font> retiredFonts)
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
            var font = baseline.Monospace
                ? CreateFont(MonospaceFontName(), Math.Max(scaledSize, MinimumCompactSize), baseline.Style)
                : CreateFont(CurrentUiFontName(), Math.Max(scaledSize, MinimumBodySize), baseline.Style);
            AssignManagedFont(control, font, retiredFonts);
        }

        foreach (Control child in control.Controls)
            ApplyScaledTypographyRecursive(child, scale, retiredFonts);
    }

    private static void ApplyTypographyRecursive(Control control, List<Font> retiredFonts)
    {
        var current = control.Font;
        if (current is not null && !IsIconOrEmojiFont(current.Name))
        {
            var font = IsMonospaceFont(current.Name)
                ? Mono(current.SizeInPoints, current.Style)
                : Body(current.SizeInPoints, current.Style);
            AssignManagedFont(control, font, retiredFonts);
        }

        foreach (Control child in control.Controls)
            ApplyTypographyRecursive(child, retiredFonts);
    }

    private static bool IsIconOrEmojiFont(string fontName) =>
        fontName.Equals("Segoe MDL2 Assets", StringComparison.OrdinalIgnoreCase) ||
        fontName.Equals("Segoe UI Emoji", StringComparison.OrdinalIgnoreCase);

    private static bool IsMonospaceFont(string fontName) =>
        fontName.Equals("Consolas", StringComparison.OrdinalIgnoreCase) ||
        fontName.Equals("Courier New", StringComparison.OrdinalIgnoreCase);

    private static void AssignManagedFont(Control control, Font font, List<Font> retiredFonts)
    {
        var previous = ScaledFontLeases.GetValue(control, owner => new ScaledFontLease(owner))
            .Replace(control, font);
        if (previous is not null) retiredFonts.Add(previous);
    }

    private static void RetireFonts(Control root, List<Font> fonts)
    {
        if (fonts.Count == 0) return;
        RetiredFontPools.GetValue(root, owner => new RetiredFontPool(owner)).Retire(fonts);
    }

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

    private static Font CreateFont(
        string fontName,
        float size,
        FontStyle style,
        GraphicsUnit unit = GraphicsUnit.Point)
    {
        try
        {
            return new Font(fontName, size, style, unit);
        }
        catch (ArgumentException)
        {
            var fallbackFamily = SystemFonts.MessageBoxFont?.FontFamily ?? FontFamily.GenericSansSerif;
            return new Font(fallbackFamily, size, style, unit);
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
