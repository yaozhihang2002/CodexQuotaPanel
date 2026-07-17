using System.Reflection;
using System.Text;
using System.Text.Json;

namespace CodexQuotaPanel;

internal enum SettingsTransferFailure
{
    None,
    Io,
    TooLarge,
    InvalidFormat,
    UnsupportedVersion
}

internal sealed record PortableSettingsEnvelope(
    string Schema,
    int SchemaVersion,
    string ExportedByVersion,
    PortablePanelPreferences Preferences);

internal sealed record PortablePanelPreferences
{
    public int OrbOpacityPercent { get; init; } = 100;
    public bool OrbClickThrough { get; init; }
    public bool ShowClickThroughReminder { get; init; } = true;
    public bool AlwaysOnTop { get; init; } = true;
    public int StartupViewMode { get; init; }
    public int OrbSize { get; init; } = PanelPreferenceManager.DefaultOrbSize;
    public int SettingsFontScalePercent { get; init; } = PanelPreferenceManager.DefaultSettingsFontScale;
    public bool PositionLocked { get; init; }
    public bool SnapToEdge { get; init; }
    public int OuterWindowMinutes { get; init; } = 300;
    public int InnerWindowMinutes { get; init; } = 10080;
    public int OuterWindowRole { get; init; }
    public int InnerWindowRole { get; init; } = 1;
    public int OuterColorArgb { get; init; } = PanelPreferenceManager.DefaultOuterColorArgb;
    public int InnerColorArgb { get; init; } = PanelPreferenceManager.DefaultInnerColorArgb;
    public bool AlertsEnabled { get; init; } = true;
    public int WarningThreshold { get; init; } = 20;
    public int CriticalThreshold { get; init; } = 10;
    public bool QuietHoursEnabled { get; init; }
    public int QuietStartMinutes { get; init; } = 1380;
    public int QuietEndMinutes { get; init; } = 480;
    public bool AlertSoundEnabled { get; init; }
    public bool HoverPreviewEnabled { get; init; } = true;
    public bool TrendRecordingEnabled { get; init; } = true;
    public bool ConsumptionFlameEnabled { get; init; } = true;
    public int ConsumptionFlameStyle { get; init; } = 1;
    public bool GlobalHotKeyEnabled { get; init; } = true;
    public bool CheckForUpdatesOnStartup { get; init; }
    public int ThemeMode { get; init; }
    public int Language { get; init; }
}

/// <summary>
/// Imports and exports only portable, non-account settings. Device coordinates,
/// last window state, startup registration, history, paths, and quota data are
/// deliberately outside this format.
/// </summary>
internal static class SettingsTransferService
{
    private const string Schema = "codex-quota-panel.settings";
    private const int CurrentSchemaVersion = 1;
    private const long MaximumImportBytes = 128 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    internal static bool TryExport(
        string path,
        PanelPreferences preferences,
        out SettingsTransferFailure failure)
    {
        failure = SettingsTransferFailure.None;
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            ArgumentNullException.ThrowIfNull(preferences);
            var portable = MakePortable(preferences);
            var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "unknown";
            var envelope = new PortableSettingsEnvelope(Schema, CurrentSchemaVersion, version, portable);
            if (AtomicJsonFile.TryWrite(path, JsonSerializer.Serialize(envelope, JsonOptions), createBackup: false))
                return true;
            failure = SettingsTransferFailure.Io;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or ArgumentException or
                                   NotSupportedException)
        {
            failure = SettingsTransferFailure.Io;
            return false;
        }
    }

    internal static bool TryImport(
        string path,
        PanelPreferences currentDevicePreferences,
        out PanelPreferences imported,
        out SettingsTransferFailure failure)
    {
        imported = currentDevicePreferences;
        failure = SettingsTransferFailure.None;
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(path);
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                failure = SettingsTransferFailure.Io;
                return false;
            }
            if (info.Length is <= 0 or > MaximumImportBytes)
            {
                failure = info.Length > MaximumImportBytes
                    ? SettingsTransferFailure.TooLarge
                    : SettingsTransferFailure.InvalidFormat;
                return false;
            }

            var json = File.ReadAllText(path, Encoding.UTF8);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 32
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                failure = SettingsTransferFailure.InvalidFormat;
                return false;
            }

            var envelope = JsonSerializer.Deserialize<PortableSettingsEnvelope>(json, JsonOptions);
            if (envelope is null || !string.Equals(envelope.Schema, Schema, StringComparison.Ordinal))
            {
                failure = SettingsTransferFailure.InvalidFormat;
                return false;
            }
            if (envelope.SchemaVersion is < 1 or > CurrentSchemaVersion)
            {
                failure = SettingsTransferFailure.UnsupportedVersion;
                return false;
            }
            if (envelope.Preferences is null)
            {
                failure = SettingsTransferFailure.InvalidFormat;
                return false;
            }

            var portable = envelope.Preferences;
            imported = PanelPreferenceManager.Normalize(new PanelPreferences
            {
                OrbOpacityPercent = portable.OrbOpacityPercent,
                OrbClickThrough = portable.OrbClickThrough,
                ShowClickThroughReminder = portable.ShowClickThroughReminder,
                OrbX = currentDevicePreferences.OrbX,
                OrbY = currentDevicePreferences.OrbY,
                AlwaysOnTop = portable.AlwaysOnTop,
                StartupViewMode = portable.StartupViewMode,
                LastViewMode = currentDevicePreferences.LastViewMode,
                OrbSize = portable.OrbSize,
                SettingsFontScalePercent = portable.SettingsFontScalePercent,
                PositionLocked = portable.PositionLocked,
                SnapToEdge = portable.SnapToEdge,
                OuterWindowMinutes = portable.OuterWindowMinutes,
                InnerWindowMinutes = portable.InnerWindowMinutes,
                OuterWindowRole = portable.OuterWindowRole,
                InnerWindowRole = portable.InnerWindowRole,
                OuterColorArgb = portable.OuterColorArgb,
                InnerColorArgb = portable.InnerColorArgb,
                AlertsEnabled = portable.AlertsEnabled,
                WarningThreshold = portable.WarningThreshold,
                CriticalThreshold = portable.CriticalThreshold,
                QuietHoursEnabled = portable.QuietHoursEnabled,
                QuietStartMinutes = portable.QuietStartMinutes,
                QuietEndMinutes = portable.QuietEndMinutes,
                AlertSoundEnabled = portable.AlertSoundEnabled,
                HoverPreviewEnabled = portable.HoverPreviewEnabled,
                TrendRecordingEnabled = portable.TrendRecordingEnabled,
                ConsumptionFlameEnabled = portable.ConsumptionFlameEnabled,
                ConsumptionFlameStyle = portable.ConsumptionFlameStyle,
                GlobalHotKeyEnabled = portable.GlobalHotKeyEnabled,
                CheckForUpdatesOnStartup = portable.CheckForUpdatesOnStartup,
                ThemeMode = portable.ThemeMode,
                Language = portable.Language
            });
            return true;
        }
        catch (JsonException)
        {
            failure = SettingsTransferFailure.InvalidFormat;
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or ArgumentException or
                                   NotSupportedException)
        {
            failure = SettingsTransferFailure.Io;
            return false;
        }
    }

    internal static PortablePanelPreferences MakePortable(PanelPreferences preferences)
    {
        preferences = PanelPreferenceManager.Normalize(preferences);
        return new PortablePanelPreferences
        {
            OrbOpacityPercent = preferences.OrbOpacityPercent,
            OrbClickThrough = preferences.OrbClickThrough,
            ShowClickThroughReminder = preferences.ShowClickThroughReminder,
            AlwaysOnTop = preferences.AlwaysOnTop,
            StartupViewMode = preferences.StartupViewMode,
            OrbSize = preferences.OrbSize,
            SettingsFontScalePercent = preferences.SettingsFontScalePercent,
            PositionLocked = preferences.PositionLocked,
            SnapToEdge = preferences.SnapToEdge,
            OuterWindowMinutes = preferences.OuterWindowMinutes,
            InnerWindowMinutes = preferences.InnerWindowMinutes,
            OuterWindowRole = preferences.OuterWindowRole,
            InnerWindowRole = preferences.InnerWindowRole,
            OuterColorArgb = preferences.OuterColorArgb,
            InnerColorArgb = preferences.InnerColorArgb,
            AlertsEnabled = preferences.AlertsEnabled,
            WarningThreshold = preferences.WarningThreshold,
            CriticalThreshold = preferences.CriticalThreshold,
            QuietHoursEnabled = preferences.QuietHoursEnabled,
            QuietStartMinutes = preferences.QuietStartMinutes,
            QuietEndMinutes = preferences.QuietEndMinutes,
            AlertSoundEnabled = preferences.AlertSoundEnabled,
            HoverPreviewEnabled = preferences.HoverPreviewEnabled,
            TrendRecordingEnabled = preferences.TrendRecordingEnabled,
            ConsumptionFlameEnabled = preferences.ConsumptionFlameEnabled,
            ConsumptionFlameStyle = preferences.ConsumptionFlameStyle,
            GlobalHotKeyEnabled = preferences.GlobalHotKeyEnabled,
            CheckForUpdatesOnStartup = preferences.CheckForUpdatesOnStartup,
            ThemeMode = preferences.ThemeMode,
            Language = preferences.Language
        };
    }
}
