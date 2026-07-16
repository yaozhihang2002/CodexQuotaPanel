using Microsoft.Win32;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace CodexQuotaPanel;

internal sealed record PanelPreferences
{
    public static PanelPreferences Default => new();

    public int OrbOpacityPercent { get; init; } = 100;
    public bool OrbClickThrough { get; init; }
    public int? OrbX { get; init; }
    public int? OrbY { get; init; }
    public bool AlwaysOnTop { get; init; } = true;
    public int StartupViewMode { get; init; }
    public int LastViewMode { get; init; }
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
    public int Language { get; init; } = CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? 0 : 1;
}

internal static class PanelPreferenceManager
{
    private const string PreferencesKey = @"Software\CodexQuotaPanel";
    private const string PreferencesFileName = "preferences.json";
    private const int CurrentPreferencesVersion = 3;
    private const string PreferencesFileSchema = "codex-quota-panel.preferences";
    private const int CurrentPreferencesFileSchemaVersion = 1;
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };
    private static readonly JsonSerializerOptions WriteOptions = new()
    {
        WriteIndented = true
    };
    internal const int MinimumOpacity = 30;
    internal const int MaximumOpacity = 100;
    internal const int MinimumOrbSize = 56;
    internal const int MaximumOrbSize = 192;
    internal const int DefaultOrbSize = 88;
    internal const int MinimumSettingsFontScale = 80;
    internal const int MaximumSettingsFontScale = 150;
    internal const int DefaultSettingsFontScale = 100;
    internal const int DefaultOuterColorArgb = unchecked((int)0xFF6AE4B0);
    internal const int DefaultInnerColorArgb = unchecked((int)0xFF7EC4FF);

    internal static readonly int[] OpacityLevels = [100, 85, 70, 55];
    internal static readonly int[] OrbSizePresets = [64, 88, 128, 192];

    internal static string PreferencesFilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "CodexQuotaPanel",
        PreferencesFileName);

    public static PanelPreferences Default => Normalize(PanelPreferences.Default);

    public static PanelPreferences Load()
    {
        // The per-user file is the authoritative store. Keep the registry as a
        // compatibility fallback for existing installations and installer-set
        // language preferences.
        var filePreferences = LoadFromFile(PreferencesFilePath);
        if (filePreferences is not null) return Normalize(filePreferences);

        // A failed or interrupted primary write can be recovered without
        // discarding any old fields. Restore the validated backup in the new
        // envelope format while deliberately preserving the backup itself.
        var backupPreferences = LoadFromFile(PreferencesFilePath + ".bak");
        if (backupPreferences is not null)
        {
            SaveToFile(backupPreferences, createBackup: false);
            return Normalize(backupPreferences);
        }

        var registryPreferences = Load(Registry.CurrentUser, PreferencesKey);
        SaveToFile(registryPreferences, createBackup: false);
        return registryPreferences;
    }

    internal static PanelPreferences Load(RegistryKey root, string preferencesKey)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentException.ThrowIfNullOrWhiteSpace(preferencesKey);
            using var key = root.OpenSubKey(preferencesKey, writable: false);
            var preferencesVersion = ReadInt(key, "PreferencesVersion", 0);
            return Normalize(new PanelPreferences
            {
                OrbOpacityPercent = ReadInt(key, "OrbOpacityPercent", 100),
                OrbClickThrough = ReadBool(key, "OrbClickThrough", false),
                OrbX = ReadNullableInt(key, "OrbX"),
                OrbY = ReadNullableInt(key, "OrbY"),
                AlwaysOnTop = ReadBool(key, "AlwaysOnTop", true),
                StartupViewMode = ReadInt(key, "StartupViewMode", 0),
                LastViewMode = ReadInt(key, "LastViewMode", 0),
                OrbSize = ReadInt(key, "OrbSize", DefaultOrbSize),
                SettingsFontScalePercent = ReadInt(key, "SettingsFontScalePercent", DefaultSettingsFontScale),
                PositionLocked = ReadBool(key, "PositionLocked", false),
                // v1.6.0 wrote its old opt-out default (true) for many users.
                // Treat that unversioned value as disabled once, while preserving
                // explicit v1.6.1+ choices through the version marker below.
                SnapToEdge = preferencesVersion >= CurrentPreferencesVersion &&
                             ReadBool(key, "SnapToEdge", false),
                OuterWindowMinutes = ReadInt(key, "OuterWindowMinutes", 300),
                InnerWindowMinutes = ReadInt(key, "InnerWindowMinutes", 10080),
                OuterWindowRole = ReadInt(key, "OuterWindowRole", 0),
                InnerWindowRole = ReadInt(key, "InnerWindowRole", 1),
                OuterColorArgb = ReadInt(key, "OuterColorArgb", DefaultOuterColorArgb),
                InnerColorArgb = ReadInt(key, "InnerColorArgb", DefaultInnerColorArgb),
                AlertsEnabled = ReadBool(key, "AlertsEnabled", true),
                WarningThreshold = ReadInt(key, "WarningThreshold", 20),
                CriticalThreshold = ReadInt(key, "CriticalThreshold", 10),
                QuietHoursEnabled = ReadBool(key, "QuietHoursEnabled", false),
                QuietStartMinutes = ReadInt(key, "QuietStartMinutes", ReadInt(key, "QuietStartHour", 23) * 60),
                QuietEndMinutes = ReadInt(key, "QuietEndMinutes", ReadInt(key, "QuietEndHour", 8) * 60),
                AlertSoundEnabled = ReadBool(key, "AlertSoundEnabled", false),
                HoverPreviewEnabled = ReadBool(key, "HoverPreviewEnabled", true),
                TrendRecordingEnabled = ReadBool(key, "TrendRecordingEnabled", true),
                ConsumptionFlameEnabled = ReadBool(key, "ConsumptionFlameEnabled", true),
                ConsumptionFlameStyle = ReadInt(key, "ConsumptionFlameStyle", 1),
                GlobalHotKeyEnabled = ReadBool(key, "GlobalHotKeyEnabled", true),
                CheckForUpdatesOnStartup = ReadBool(key, "CheckForUpdatesOnStartup", false),
                ThemeMode = ReadInt(key, "ThemeMode", 0),
                Language = ReadInt(key, "Language",
                    CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "zh" ? 0 : 1)
            });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or ArgumentException)
        {
            return new PanelPreferences();
        }
    }

    public static void Save(PanelPreferences preferences)
    {
        TrySave(preferences);
    }

    public static bool TrySave(PanelPreferences preferences)
    {
        ArgumentNullException.ThrowIfNull(preferences);
        preferences = Normalize(preferences);

        // A local file remains writable even when a stale registry key has an
        // incompatible owner or ACL. Mirroring to the registry preserves
        // compatibility with older builds.
        var savedToFile = SaveToFile(preferences);
        Save(Registry.CurrentUser, PreferencesKey, preferences);
        return savedToFile;
    }

    internal static void Save(RegistryKey root, string preferencesKey, PanelPreferences preferences)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(root);
            ArgumentException.ThrowIfNullOrWhiteSpace(preferencesKey);
            ArgumentNullException.ThrowIfNull(preferences);
            preferences = Normalize(preferences);
            using var key = root.CreateSubKey(preferencesKey, writable: true);
            WriteInt(key, "PreferencesVersion", CurrentPreferencesVersion);
            WriteInt(key, "OrbOpacityPercent", preferences.OrbOpacityPercent);
            WriteBool(key, "OrbClickThrough", preferences.OrbClickThrough);
            WriteNullableInt(key, "OrbX", preferences.OrbX);
            WriteNullableInt(key, "OrbY", preferences.OrbY);
            WriteBool(key, "AlwaysOnTop", preferences.AlwaysOnTop);
            WriteInt(key, "StartupViewMode", preferences.StartupViewMode);
            WriteInt(key, "LastViewMode", preferences.LastViewMode);
            WriteInt(key, "OrbSize", preferences.OrbSize);
            WriteInt(key, "SettingsFontScalePercent", preferences.SettingsFontScalePercent);
            WriteBool(key, "PositionLocked", preferences.PositionLocked);
            WriteBool(key, "SnapToEdge", preferences.SnapToEdge);
            WriteInt(key, "OuterWindowMinutes", preferences.OuterWindowMinutes);
            WriteInt(key, "InnerWindowMinutes", preferences.InnerWindowMinutes);
            WriteInt(key, "OuterWindowRole", preferences.OuterWindowRole);
            WriteInt(key, "InnerWindowRole", preferences.InnerWindowRole);
            WriteInt(key, "OuterColorArgb", preferences.OuterColorArgb);
            WriteInt(key, "InnerColorArgb", preferences.InnerColorArgb);
            WriteBool(key, "AlertsEnabled", preferences.AlertsEnabled);
            WriteInt(key, "WarningThreshold", preferences.WarningThreshold);
            WriteInt(key, "CriticalThreshold", preferences.CriticalThreshold);
            WriteBool(key, "QuietHoursEnabled", preferences.QuietHoursEnabled);
            WriteInt(key, "QuietStartMinutes", preferences.QuietStartMinutes);
            WriteInt(key, "QuietEndMinutes", preferences.QuietEndMinutes);
            WriteBool(key, "AlertSoundEnabled", preferences.AlertSoundEnabled);
            WriteBool(key, "HoverPreviewEnabled", preferences.HoverPreviewEnabled);
            WriteBool(key, "TrendRecordingEnabled", preferences.TrendRecordingEnabled);
            WriteBool(key, "ConsumptionFlameEnabled", preferences.ConsumptionFlameEnabled);
            WriteInt(key, "ConsumptionFlameStyle", preferences.ConsumptionFlameStyle);
            WriteBool(key, "GlobalHotKeyEnabled", preferences.GlobalHotKeyEnabled);
            WriteBool(key, "CheckForUpdatesOnStartup", preferences.CheckForUpdatesOnStartup);
            WriteInt(key, "ThemeMode", preferences.ThemeMode);
            WriteInt(key, "Language", preferences.Language);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or ArgumentException)
        {
        }
    }

    public static PanelPreferences Reset()
    {
        DeleteAll();
        return Default;
    }

    public static void DeleteAll()
    {
        try
        {
            Registry.CurrentUser.DeleteSubKeyTree(PreferencesKey, throwOnMissingSubKey: false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or ArgumentException)
        {
        }

        try
        {
            File.Delete(PreferencesFilePath);
            File.Delete(PreferencesFilePath + ".bak");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or ArgumentException)
        {
        }
    }

    internal static PanelPreferences? LoadFromFile(string path)
    {
        try
        {
            if (!File.Exists(path)) return null;
            var json = File.ReadAllText(path, Encoding.UTF8);
            using var document = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;

            // v0.2.x and earlier wrote a bare PanelPreferences object. Continue
            // accepting that shape indefinitely so upgrades preserve every old
            // personalization field and only add defaults for newly introduced
            // properties.
            if (!TryGetPropertyIgnoreCase(document.RootElement, "schema", out _))
            {
                if (!ContainsKnownPreferenceProperty(document.RootElement)) return null;
                return JsonSerializer.Deserialize<PanelPreferences>(json, ReadOptions);
            }

            if (!TryGetPropertyIgnoreCase(document.RootElement, "schema", out var schemaElement) ||
                schemaElement.ValueKind != JsonValueKind.String ||
                !string.Equals(schemaElement.GetString(), PreferencesFileSchema, StringComparison.Ordinal) ||
                !TryGetPropertyIgnoreCase(document.RootElement, "schemaVersion", out var versionElement) ||
                versionElement.ValueKind != JsonValueKind.Number ||
                !versionElement.TryGetInt32(out var schemaVersion) ||
                schemaVersion is < 1 or > CurrentPreferencesFileSchemaVersion)
                return null;

            // A short-lived development build used a nested envelope. Continue
            // accepting it so no preview user can lose their choices. Published
            // builds write the compatible root shape below, which older 0.2.x
            // versions can also deserialize without resetting the orb.
            if (TryGetPropertyIgnoreCase(document.RootElement, "preferences", out var nestedPreferences))
            {
                if (nestedPreferences.ValueKind != JsonValueKind.Object) return null;
                return JsonSerializer.Deserialize<PanelPreferences>(nestedPreferences.GetRawText(), ReadOptions);
            }

            return JsonSerializer.Deserialize<PanelPreferences>(json, ReadOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or JsonException or
                                   NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    internal static bool SaveToFile(PanelPreferences preferences, bool createBackup = true)
        => SaveToFile(PreferencesFilePath, preferences, createBackup);

    internal static bool SaveToFile(string path, PanelPreferences preferences, bool createBackup = true)
    {
        try
        {
            preferences = Normalize(preferences);
            // Keep preference properties at the root. System.Text.Json in 0.2.x
            // ignores the two unknown schema fields, so a user can upgrade and
            // still roll back without losing position, size, theme, or colors.
            var root = JsonSerializer.SerializeToNode(preferences, WriteOptions) as JsonObject;
            if (root is null) return false;
            root["Schema"] = PreferencesFileSchema;
            root["SchemaVersion"] = CurrentPreferencesFileSchemaVersion;
            return AtomicJsonFile.TryWrite(
                path,
                root.ToJsonString(WriteOptions),
                createBackup);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or NotSupportedException or
                                   ArgumentException)
        {
            return false;
        }
    }

    private static bool TryGetPropertyIgnoreCase(JsonElement element, string name, out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }
        value = default;
        return false;
    }

    private static bool ContainsKnownPreferenceProperty(JsonElement element) =>
        TryGetPropertyIgnoreCase(element, nameof(PanelPreferences.OrbOpacityPercent), out _) ||
        TryGetPropertyIgnoreCase(element, nameof(PanelPreferences.OrbX), out _) ||
        TryGetPropertyIgnoreCase(element, nameof(PanelPreferences.OrbSize), out _) ||
        TryGetPropertyIgnoreCase(element, nameof(PanelPreferences.ThemeMode), out _) ||
        TryGetPropertyIgnoreCase(element, nameof(PanelPreferences.Language), out _);

    internal static PanelPreferences Normalize(PanelPreferences preferences)
    {
        // Keep the two alert bands distinct even when the registry contains
        // stale or manually edited values. The settings UI enforces the same
        // invariant, so a normalized value can always be reopened and saved.
        var warning = Math.Clamp(preferences.WarningThreshold, 2, 100);
        var critical = Math.Clamp(preferences.CriticalThreshold, 1, warning - 1);
        return preferences with
        {
            OrbOpacityPercent = NormalizeOpacity(preferences.OrbOpacityPercent),
            StartupViewMode = Math.Clamp(preferences.StartupViewMode, 0, 3),
            LastViewMode = Math.Clamp(preferences.LastViewMode, 0, 2),
            OrbSize = NormalizeOrbSize(preferences.OrbSize),
            SettingsFontScalePercent = NormalizeSettingsFontScale(preferences.SettingsFontScalePercent),
            OuterWindowMinutes = NormalizeWindow(preferences.OuterWindowMinutes, 300),
            InnerWindowMinutes = NormalizeWindow(preferences.InnerWindowMinutes, 10080),
            OuterWindowRole = Math.Clamp(preferences.OuterWindowRole, 0, 1),
            InnerWindowRole = Math.Clamp(preferences.InnerWindowRole, 0, 1),
            OuterColorArgb = NormalizeColor(preferences.OuterColorArgb, DefaultOuterColorArgb),
            InnerColorArgb = NormalizeColor(preferences.InnerColorArgb, DefaultInnerColorArgb),
            WarningThreshold = warning,
            CriticalThreshold = critical,
            QuietStartMinutes = Math.Clamp(preferences.QuietStartMinutes, 0, 1439),
            QuietEndMinutes = Math.Clamp(preferences.QuietEndMinutes, 0, 1439),
            ConsumptionFlameStyle = Math.Clamp(preferences.ConsumptionFlameStyle, 0, 2),
            ThemeMode = Math.Clamp(preferences.ThemeMode, 0, 2),
            Language = Math.Clamp(preferences.Language, 0, 1)
        };
    }

    internal static int NormalizeOpacity(int value) => Math.Clamp(value, MinimumOpacity, MaximumOpacity);

    internal static int NormalizeOrbSize(int value) =>
        Math.Clamp(value, MinimumOrbSize, MaximumOrbSize);

    internal static int NormalizeSettingsFontScale(int value) =>
        Math.Clamp(value, MinimumSettingsFontScale, MaximumSettingsFontScale);

    internal static int NormalizeColor(int argb, int fallback)
    {
        var color = Color.FromArgb(argb);
        if (color.A == 0) color = Color.FromArgb(fallback);
        return Color.FromArgb(255, color.R, color.G, color.B).ToArgb();
    }

    private static int NormalizeWindow(int value, int fallback) => value > 0 ? value : fallback;

    private static int ReadInt(RegistryKey? key, string name, int fallback)
    {
        try
        {
            var value = key?.GetValue(name);
            return value is null ? fallback : Convert.ToInt32(value);
        }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
        {
            return fallback;
        }
    }

    private static int? ReadNullableInt(RegistryKey? key, string name)
    {
        var value = key?.GetValue(name);
        if (value is null) return null;
        try { return Convert.ToInt32(value); }
        catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException) { return null; }
    }

    private static bool ReadBool(RegistryKey? key, string name, bool fallback) =>
        ReadInt(key, name, fallback ? 1 : 0) != 0;

    private static void WriteInt(RegistryKey key, string name, int value) =>
        key.SetValue(name, value, RegistryValueKind.DWord);

    private static void WriteBool(RegistryKey key, string name, bool value) => WriteInt(key, name, value ? 1 : 0);

    private static void WriteNullableInt(RegistryKey key, string name, int? value)
    {
        if (value is null) key.DeleteValue(name, throwOnMissingValue: false);
        else WriteInt(key, name, value.Value);
    }
}
