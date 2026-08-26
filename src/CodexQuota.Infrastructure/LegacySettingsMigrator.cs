using System.Globalization;
using System.Text.Json;
using CodexQuota.Application;

namespace CodexQuota.Infrastructure;

internal static class LegacySettingsMigrator
{
    public static async Task<AppSettings?> TryReadAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(path)) return null;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
            var root = document.RootElement;
            if (TryGet(root, "preferences", out var nested) && nested.ValueKind == JsonValueKind.Object) root = nested;
            if (!ContainsAnyKnownField(root)) return null;

            var defaults = AppSettings.Default;
            return (defaults with
            {
                Theme = ReadInt(root, "ThemeMode") switch { 1 => AppTheme.Dark, 2 => AppTheme.Light, _ => AppTheme.System },
                Language = ReadInt(root, "Language") == 1 ? AppLanguage.English : AppLanguage.SimplifiedChinese,
                StartupView = EnumValue(ReadInt(root, "StartupViewMode"), defaults.StartupView),
                LastView = EnumValue(ReadInt(root, "LastViewMode"), defaults.LastView),
                OrbSize = ReadInt(root, "OrbSize") ?? defaults.OrbSize,
                OrbOpacityPercent = ReadInt(root, "OrbOpacityPercent") ?? defaults.OrbOpacityPercent,
                OrbBackground = ReadArgb(root, "OrbBackgroundColorArgb") ?? defaults.OrbBackground,
                OuterRingColor = ReadArgb(root, "OuterColorArgb") ?? defaults.OuterRingColor,
                InnerRingColor = ReadArgb(root, "InnerColorArgb") ?? defaults.InnerRingColor,
                OuterWindowMinutes = ReadInt(root, "OuterWindowMinutes") ?? defaults.OuterWindowMinutes,
                InnerWindowMinutes = ReadInt(root, "InnerWindowMinutes") ?? defaults.InnerWindowMinutes,
                InterfaceScalePercent = ReadInt(root, "SettingsFontScalePercent") ?? defaults.InterfaceScalePercent,
                AlwaysOnTop = ReadBool(root, "AlwaysOnTop") ?? defaults.AlwaysOnTop,
                ClickThrough = ReadBool(root, "OrbClickThrough") ?? defaults.ClickThrough,
                ShowClickThroughReminder = ReadBool(root, "ShowClickThroughReminder") ?? defaults.ShowClickThroughReminder,
                PositionLocked = ReadBool(root, "PositionLocked") ?? defaults.PositionLocked,
                SnapToEdge = ReadBool(root, "SnapToEdge") ?? defaults.SnapToEdge,
                HoverPreviewEnabled = ReadBool(root, "HoverPreviewEnabled") ?? defaults.HoverPreviewEnabled,
                GlobalRecoveryShortcutEnabled = ReadBool(root, "GlobalHotKeyEnabled") ?? defaults.GlobalRecoveryShortcutEnabled,
                ConsumptionFeedbackEnabled = ReadBool(root, "ConsumptionFlameEnabled") ?? defaults.ConsumptionFeedbackEnabled,
                ConsumptionFeedbackStyle = EnumValue(ReadInt(root, "ConsumptionFlameStyle"), defaults.ConsumptionFeedbackStyle),
                AlertsEnabled = ReadBool(root, "AlertsEnabled") ?? defaults.AlertsEnabled,
                WarningThreshold = ReadInt(root, "WarningThreshold") ?? defaults.WarningThreshold,
                CriticalThreshold = ReadInt(root, "CriticalThreshold") ?? defaults.CriticalThreshold,
                QuietHoursEnabled = ReadBool(root, "QuietHoursEnabled") ?? defaults.QuietHoursEnabled,
                QuietStartMinutes = ReadInt(root, "QuietStartMinutes") ?? defaults.QuietStartMinutes,
                QuietEndMinutes = ReadInt(root, "QuietEndMinutes") ?? defaults.QuietEndMinutes,
                AlertSoundEnabled = ReadBool(root, "AlertSoundEnabled") ?? defaults.AlertSoundEnabled,
                TrendRecordingEnabled = ReadBool(root, "TrendRecordingEnabled") ?? defaults.TrendRecordingEnabled,
                CheckForUpdatesOnStartup = ReadBool(root, "CheckForUpdatesOnStartup") ?? defaults.CheckForUpdatesOnStartup,
                OrbX = ReadDouble(root, "OrbX"),
                OrbY = ReadDouble(root, "OrbY")
            }).Normalize();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool ContainsAnyKnownField(JsonElement root) =>
        TryGet(root, "OrbSize", out _) || TryGet(root, "OrbX", out _) || TryGet(root, "ThemeMode", out _);

    private static T EnumValue<T>(int? value, T fallback) where T : struct, Enum =>
        value is int number && Enum.IsDefined(typeof(T), number) ? (T)Enum.ToObject(typeof(T), number) : fallback;

    private static string? ReadArgb(JsonElement root, string name)
    {
        var value = ReadInt64(root, name);
        if (value is null) return null;
        var argb = unchecked((uint)value.Value);
        return $"#{(argb >> 16) & 0xFF:X2}{(argb >> 8) & 0xFF:X2}{argb & 0xFF:X2}";
    }

    private static int? ReadInt(JsonElement root, string name)
    {
        var value = ReadInt64(root, name);
        return value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;
    }

    private static long? ReadInt64(JsonElement root, string name)
    {
        if (!TryGet(root, name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        return value.ValueKind == JsonValueKind.String &&
               long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static double? ReadDouble(JsonElement root, string name)
    {
        if (!TryGet(root, name, out var value)) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;
    }

    private static bool? ReadBool(JsonElement root, string name)
    {
        if (!TryGet(root, name, out var value)) return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number when value.TryGetInt32(out var number) => number != 0,
            _ => null
        };
    }

    private static bool TryGet(JsonElement root, string name, out JsonElement value)
    {
        foreach (var property in root.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
            value = property.Value;
            return true;
        }
        value = default;
        return false;
    }
}
