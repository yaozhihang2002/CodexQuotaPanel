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

public enum StartupViewMode
{
    RestorePrevious,
    Orb,
    Details,
    TrayOnly
}

public enum ConsumptionFeedbackStyle
{
    Ember,
    Fluid,
    Pixel
}

public sealed record AppSettings
{
    public const int CurrentSchemaVersion = 4;

    public int SchemaVersion { get; init; } = CurrentSchemaVersion;
    public AppTheme Theme { get; init; } = AppTheme.System;
    public AppLanguage Language { get; init; } = AppLanguage.SimplifiedChinese;
    public StartupViewMode StartupView { get; init; } = StartupViewMode.RestorePrevious;
    public StartupViewMode LastView { get; init; } = StartupViewMode.Orb;
    public bool StartWithSystem { get; init; }
    public int OrbSize { get; init; } = 88;
    public int OrbOpacityPercent { get; init; } = 100;
    public string OrbBackground { get; init; } = "#000000";
    public string OuterRingColor { get; init; } = "#6AE4B0";
    public string InnerRingColor { get; init; } = "#7EC4FF";
    public int OuterWindowMinutes { get; init; } = 300;
    public int InnerWindowMinutes { get; init; } = 10_080;
    public int InterfaceScalePercent { get; init; } = 100;
    public bool AlwaysOnTop { get; init; } = true;
    public bool ClickThrough { get; init; }
    public bool ShowClickThroughReminder { get; init; } = true;
    public bool PositionLocked { get; init; }
    public bool SnapToEdge { get; init; }
    public bool HoverPreviewEnabled { get; init; } = true;
    public bool GlobalRecoveryShortcutEnabled { get; init; } = true;
    public bool ReducedMotion { get; init; }
    public bool ConsumptionFeedbackEnabled { get; init; } = true;
    public ConsumptionFeedbackStyle ConsumptionFeedbackStyle { get; init; } = ConsumptionFeedbackStyle.Fluid;
    public bool AlertsEnabled { get; init; } = true;
    public int WarningThreshold { get; init; } = 20;
    public int CriticalThreshold { get; init; } = 10;
    public bool QuietHoursEnabled { get; init; }
    public int QuietStartMinutes { get; init; } = 23 * 60;
    public int QuietEndMinutes { get; init; } = 8 * 60;
    public bool AlertSoundEnabled { get; init; }
    public bool TrendRecordingEnabled { get; init; } = true;
    public bool CheckForUpdatesOnStartup { get; init; }
    public string? DismissedAlertCycleKey { get; init; }
    public string? LastWarningCycleKey { get; init; }
    public string? LastCriticalCycleKey { get; init; }
    public double? OrbX { get; init; }
    public double? OrbY { get; init; }
    public string? OrbDisplayId { get; init; }
    public double? DashboardX { get; init; }
    public double? DashboardY { get; init; }
    public string? DashboardDisplayId { get; init; }

    public static AppSettings Default { get; } = new();

    public AppSettings Normalize()
    {
        var warning = Math.Clamp(WarningThreshold, 2, 100);
        return this with
        {
            SchemaVersion = CurrentSchemaVersion,
            OrbSize = Math.Clamp(OrbSize, 56, 192),
            OrbOpacityPercent = Math.Clamp(OrbOpacityPercent, 30, 100),
            InterfaceScalePercent = Math.Clamp(InterfaceScalePercent, 80, 150),
            OrbBackground = NormalizeHexColor(OrbBackground, Default.OrbBackground),
            OuterRingColor = NormalizeHexColor(OuterRingColor, Default.OuterRingColor),
            InnerRingColor = NormalizeHexColor(InnerRingColor, Default.InnerRingColor),
            OuterWindowMinutes = Math.Max(1, OuterWindowMinutes),
            InnerWindowMinutes = Math.Max(1, InnerWindowMinutes),
            WarningThreshold = warning,
            CriticalThreshold = Math.Clamp(CriticalThreshold, 1, warning - 1),
            QuietStartMinutes = Math.Clamp(QuietStartMinutes, 0, 1439),
            QuietEndMinutes = Math.Clamp(QuietEndMinutes, 0, 1439)
        };
    }

    private static string NormalizeHexColor(string? value, string fallback)
    {
        if (value is { Length: 7 } && value[0] == '#' &&
            value.AsSpan(1).ToString().All(Uri.IsHexDigit))
            return value.ToUpperInvariant();

        return fallback;
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
