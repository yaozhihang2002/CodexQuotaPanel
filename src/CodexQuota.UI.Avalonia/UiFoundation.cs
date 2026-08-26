using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using CodexQuota.Application;
using CodexQuota.Domain;

namespace CodexQuota.UI.Avalonia;

public enum QuotaConnectionState
{
    Connecting,
    Live,
    LocalFallback,
    Stale,
    Offline
}

public sealed record QuotaPresentation(
    OfficialQuotaSnapshot? Snapshot,
    IReadOnlyList<QuotaHistoryPoint> History,
    IReadOnlyList<ObservedUsage> Usage,
    UsageForecast? Forecast,
    bool IsRefreshing,
    string? Error,
    DateTimeOffset UpdatedAt)
{
    public QuotaConnectionState ConnectionState { get; init; } = QuotaConnectionState.Connecting;
    public string? ConnectionDetail { get; init; }

    public static QuotaPresentation Empty { get; } = new(null, [], [], null, false, null, DateTimeOffset.MinValue)
    {
        ConnectionState = QuotaConnectionState.Connecting
    };
}

public sealed record UiPalette(
    IBrush Canvas, IBrush Header, IBrush Sidebar, IBrush Surface, IBrush SurfaceRaised,
    IBrush Border, IBrush Active, IBrush TextPrimary, IBrush TextSecondary, IBrush TextMuted,
    IBrush Mint, IBrush Blue, IBrush Amber, IBrush Red, IBrush ButtonText)
{
    public static UiPalette For(AppTheme theme, bool systemDark = true)
    {
        var dark = theme == AppTheme.Dark || theme == AppTheme.System && systemDark;
        return dark
            ? new UiPalette(B("#0D1210"), B("#121916"), B("#101613"), B("#151C19"), B("#1A231F"),
                B("#2B3933"), B("#20362D"), B("#F2F4EF"), B("#BDCAC4"), B("#8FA198"),
                B("#57D9AA"), B("#72BFF2"), B("#E9B94F"), B("#FF746F"), B("#0B1611"))
            : new UiPalette(B("#F2F5F1"), B("#E9F0EB"), B("#EDF2EE"), B("#FAFBF9"), B("#FFFFFF"),
                B("#CAD6CF"), B("#DDECE4"), B("#15211B"), B("#475B51"), B("#667A70"),
                B("#168A67"), B("#247EAC"), B("#976611"), B("#C44742"), B("#FFFFFF"));
    }

    public static IBrush B(string value) => new SolidColorBrush(Color.Parse(value));
}

public static class UiElements
{
    public static readonly FontFamily AppFont = new(
        "Segoe UI Variable, Segoe UI, PingFang SC, Microsoft YaHei UI, SF Pro Text, sans-serif");
    public static double ScaleFactor { get; set; } = 1d;

    public static TextBlock Text(string text, double size, FontWeight weight, IBrush brush,
        TextWrapping wrapping = TextWrapping.Wrap) => new()
    {
        Text = text,
        FontFamily = AppFont,
        FontSize = size * ScaleFactor,
        FontWeight = weight,
        Foreground = brush,
        TextWrapping = wrapping,
        LineHeight = Math.Ceiling(size * ScaleFactor * 1.42),
        VerticalAlignment = VerticalAlignment.Center
    };

    public static Button Button(string text, UiPalette palette, bool primary = false)
    {
        var normalBackground = primary ? palette.Mint : palette.SurfaceRaised;
        var normalBorder = primary ? palette.Mint : palette.Border;
        var foreground = primary ? palette.ButtonText : palette.TextPrimary;
        var hoverBackground = primary
            ? Mix(normalBackground, IsBright(normalBackground) ? Colors.Black : Colors.White, .055)
            : Mix(normalBackground, palette.TextPrimary, .055);
        var pressedBackground = primary
            ? Mix(normalBackground, Colors.Black, .095)
            : Mix(normalBackground, palette.TextPrimary, .095);
        var button = new Button
        {
            Content = text,
            FontFamily = AppFont,
            FontSize = 13 * ScaleFactor,
            FontWeight = FontWeight.SemiBold,
            Foreground = foreground,
            Background = normalBackground,
            BorderBrush = normalBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16, 9),
            MinHeight = 38
        };

        // Fluent's default pointer resources can replace a custom mint button
        // with a near-black fill. Keep every state in the same colour family.
        button.Resources["ButtonBackgroundPointerOver"] = hoverBackground;
        button.Resources["ButtonBorderBrushPointerOver"] = Mix(normalBorder, foreground, .08);
        button.Resources["ButtonForegroundPointerOver"] = foreground;
        button.Resources["ButtonBackgroundPressed"] = pressedBackground;
        button.Resources["ButtonBorderBrushPressed"] = Mix(normalBorder, foreground, .12);
        button.Resources["ButtonForegroundPressed"] = foreground;
        return button;
    }

    private static IBrush Mix(IBrush brush, Color target, double amount)
    {
        var source = brush is SolidColorBrush solid ? solid.Color : Colors.Transparent;
        amount = Math.Clamp(amount, 0, 1);
        return new SolidColorBrush(Color.FromArgb(
            (byte)Math.Round(source.A + (target.A - source.A) * amount),
            (byte)Math.Round(source.R + (target.R - source.R) * amount),
            (byte)Math.Round(source.G + (target.G - source.G) * amount),
            (byte)Math.Round(source.B + (target.B - source.B) * amount)));
    }

    private static IBrush Mix(IBrush brush, IBrush target, double amount) =>
        Mix(brush, target is SolidColorBrush solid ? solid.Color : Colors.Transparent, amount);

    private static bool IsBright(IBrush brush) => brush is SolidColorBrush solid &&
        (solid.Color.R * .299 + solid.Color.G * .587 + solid.Color.B * .114) >= 150;

    public static Border Card(Control child, UiPalette palette, Thickness? padding = null) => new()
    {
        Background = palette.Surface,
        BorderBrush = palette.Border,
        BorderThickness = new Thickness(1),
        CornerRadius = new CornerRadius(12),
        Padding = padding ?? new Thickness(18, 15),
        Child = child
    };

    public static string WindowLabel(QuotaWindow window, AppLanguage language)
    {
        if (window.WindowMinutes == 300) return language == AppLanguage.SimplifiedChinese ? "5 小时窗口" : "5-hour window";
        if (window.WindowMinutes == 10_080) return language == AppLanguage.SimplifiedChinese ? "7 天窗口" : "7-day window";
        var span = TimeSpan.FromMinutes(window.WindowMinutes);
        return span.TotalDays >= 1
            ? (language == AppLanguage.SimplifiedChinese ? $"{span.TotalDays:0.#} 天窗口" : $"{span.TotalDays:0.#}-day window")
            : (language == AppLanguage.SimplifiedChinese ? $"{span.TotalHours:0.#} 小时窗口" : $"{span.TotalHours:0.#}-hour window");
    }

    public static string ShortWindowLabel(int minutes)
    {
        if (minutes % 1_440 == 0) return $"{minutes / 1_440}D";
        if (minutes % 60 == 0) return $"{minutes / 60}H";
        return $"{minutes}M";
    }

    public static string RemainingTime(DateTimeOffset? reset, AppLanguage language)
    {
        if (reset is null) return language == AppLanguage.SimplifiedChinese ? "重置时间未知" : "Reset time unavailable";
        var span = reset.Value - DateTimeOffset.Now;
        if (span <= TimeSpan.Zero) return language == AppLanguage.SimplifiedChinese ? "正在等待重置" : "Waiting for reset";
        return language == AppLanguage.SimplifiedChinese
            ? $"{Math.Max(0, (int)span.TotalDays)} 天 {span.Hours:D2} 小时 {span.Minutes:D2} 分后"
            : $"in {Math.Max(0, (int)span.TotalDays)}d {span.Hours:D2}h {span.Minutes:D2}m";
    }

    public static (QuotaWindow? Outer, QuotaWindow? Inner) SelectRingWindows(
        IReadOnlyList<QuotaWindow> windows,
        AppSettings settings)
    {
        if (windows.Count == 0) return (null, null);
        if (windows.Count == 1) return (windows[0], null);
        var outer = windows.FirstOrDefault(window => window.WindowMinutes == settings.OuterWindowMinutes) ?? windows[0];
        var inner = windows.FirstOrDefault(window => window.WindowMinutes == settings.InnerWindowMinutes &&
                                                     !ReferenceEquals(window, outer)) ??
                    windows.First(window => !ReferenceEquals(window, outer));
        return (outer, inner);
    }
}
