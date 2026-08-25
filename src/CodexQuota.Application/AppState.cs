using CodexQuota.Domain;

namespace CodexQuota.Application;

public enum AppTheme
{
    System,
    Dark,
    Light
}

public enum AppLanguage
{
    SimplifiedChinese,
    English
}

public sealed record AppSettings(
    int SchemaVersion,
    AppTheme Theme,
    AppLanguage Language,
    int OrbSize,
    int OrbOpacityPercent,
    string OrbBackground,
    bool AlwaysOnTop,
    bool ClickThrough,
    bool ReducedMotion)
{
    public const int CurrentSchemaVersion = 1;

    public static AppSettings Default { get; } = new(
        CurrentSchemaVersion,
        AppTheme.System,
        AppLanguage.SimplifiedChinese,
        88,
        100,
        "#161B19",
        true,
        false,
        false);

    public AppSettings Normalize() => this with
    {
        SchemaVersion = CurrentSchemaVersion,
        OrbSize = Math.Clamp(OrbSize, 56, 192),
        OrbOpacityPercent = Math.Clamp(OrbOpacityPercent, 30, 100),
        OrbBackground = NormalizeHexColor(OrbBackground)
    };

    private static string NormalizeHexColor(string? value)
    {
        if (value is { Length: 7 } && value[0] == '#' &&
            value.AsSpan(1).ToString().All(Uri.IsHexDigit))
            return value.ToUpperInvariant();

        return Default.OrbBackground;
    }
}

public sealed record LiveQuotaState(
    OfficialQuotaSnapshot? Snapshot,
    bool IsRefreshing,
    string? Error);

public sealed record UsageHistoryState(
    IReadOnlyList<ObservedUsage> Recent,
    UsageForecast? Forecast);

public sealed record OrbState(
    bool IsVisible,
    bool IsDragging,
    double X,
    double Y);

public sealed record SettingsState(
    AppSettings Persisted,
    AppSettings Effective,
    bool IsEditing,
    bool HasUnsavedChanges);

public sealed record ThemeState(AppTheme Requested, bool IsSystemDark);

public sealed record WindowState(
    bool IsSettingsVisible,
    bool IsDetailsVisible,
    string ActiveSettingsPage);

public sealed record AppState(
    LiveQuotaState LiveQuota,
    UsageHistoryState UsageHistory,
    OrbState Orb,
    SettingsState Settings,
    ThemeState Theme,
    WindowState Windows)
{
    public static AppState Create(AppSettings? settings = null)
    {
        var normalized = (settings ?? AppSettings.Default).Normalize();
        return new AppState(
            new LiveQuotaState(null, false, null),
            new UsageHistoryState([], null),
            new OrbState(true, false, 0d, 0d),
            new SettingsState(normalized, normalized, false, false),
            new ThemeState(normalized.Theme, false),
            new WindowState(false, false, "general"));
    }
}
