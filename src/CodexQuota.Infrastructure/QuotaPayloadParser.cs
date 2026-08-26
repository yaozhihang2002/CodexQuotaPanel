using System.Globalization;
using System.Text.Json;
using CodexQuota.Domain;

namespace CodexQuota.Infrastructure;

public static class QuotaPayloadParser
{
    public static OfficialQuotaSnapshot? ParseRolloutLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.Contains("\"rate_limits\"", StringComparison.Ordinal))
            return null;
        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!TryGet(root, "payload", out var payload) || !TryGet(payload, "rate_limits", out var limits))
                return null;
            if (!DateTimeOffset.TryParse(ReadString(root, "timestamp"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var observedAt))
                return null;
            return ParseLimits(limits, observedAt, "Local session");
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static OfficialQuotaSnapshot? ParseAppServerResult(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object) return null;
        var candidates = new List<(string Id, JsonElement Value)>();
        if (TryGetAny(result, out var byId, "rateLimitsByLimitId", "rate_limits_by_limit_id") &&
            byId.ValueKind == JsonValueKind.Object)
        {
            candidates.AddRange(byId.EnumerateObject()
                .Where(property => property.Value.ValueKind == JsonValueKind.Object)
                .Select(property => (property.Name, property.Value)));
        }
        if (candidates.Count == 0 && TryGetAny(result, out var single, "rateLimits", "rate_limits"))
            candidates.Add((ReadString(single, "limitId", "limit_id") ?? "codex", single));
        if (candidates.Count == 0) return null;
        var selected = candidates.FirstOrDefault(item => item.Id.Equals("codex", StringComparison.OrdinalIgnoreCase));
        if (selected.Value.ValueKind == JsonValueKind.Undefined) selected = candidates[0];
        return ParseLimits(selected.Value, DateTimeOffset.UtcNow, "App Server",
            ReadString(selected.Value, "planType", "plan_type"), ReadResetCredits(result));
    }

    private static OfficialQuotaSnapshot? ParseLimits(
        JsonElement limits,
        DateTimeOffset observedAt,
        string source,
        string? planType = null,
        IReadOnlyList<ResetCredit>? resetCredits = null)
    {
        var windows = new List<QuotaWindow>(2);
        AddWindow(windows, limits, "primary", "primaryWindow", "primary_window");
        AddWindow(windows, limits, "secondary", "secondaryWindow", "secondary_window");
        return windows.Count == 0 ? null : new OfficialQuotaSnapshot(
            observedAt, windows, false, source, planType, resetCredits);
    }

    private static IReadOnlyList<ResetCredit>? ReadResetCredits(JsonElement root)
    {
        if (!TryGetAny(root, out var summary, "rateLimitResetCredits", "rate_limit_reset_credits") ||
            summary.ValueKind != JsonValueKind.Object ||
            !TryGet(summary, "credits", out var values) || values.ValueKind != JsonValueKind.Array)
            return null;

        var credits = new List<ResetCredit>();
        foreach (var value in values.EnumerateArray())
        {
            var id = ReadString(value, "id");
            var status = ReadString(value, "status");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(status)) continue;
            DateTimeOffset? expiresAt = null;
            var seconds = ReadLong(value, "expiresAt", "expires_at");
            if (seconds is > 0)
            {
                try { expiresAt = DateTimeOffset.FromUnixTimeSeconds(seconds.Value); }
                catch (ArgumentOutOfRangeException) { }
            }
            credits.Add(new ResetCredit(id, status, expiresAt,
                ReadString(value, "title"), ReadString(value, "description")));
        }
        return credits;
    }

    private static void AddWindow(List<QuotaWindow> windows, JsonElement root, params string[] names)
    {
        if (!TryGetAny(root, out var value, names) || value.ValueKind != JsonValueKind.Object) return;
        var used = ReadDouble(value, "usedPercent", "used_percent");
        var minutes = ReadLong(value, "windowDurationMins", "windowMinutes", "window_minutes");
        if (used is null || minutes is not > 0 or > int.MaxValue) return;
        DateTimeOffset? reset = null;
        var resetSeconds = ReadLong(value, "resetsAt", "resets_at");
        if (resetSeconds is > 0)
        {
            try { reset = DateTimeOffset.FromUnixTimeSeconds(resetSeconds.Value); }
            catch (ArgumentOutOfRangeException) { }
        }
        var id = minutes.Value switch
        {
            300 => "5h",
            10_080 => "7d",
            _ => $"{minutes.Value}m"
        };
        windows.Add(new QuotaWindow(id, (int)minutes.Value, 100d - Math.Clamp(used.Value, 0d, 100d), reset));
    }

    private static string? ReadString(JsonElement root, params string[] names) =>
        TryGetAny(root, out var value, names) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? ReadLong(JsonElement root, params string[] names)
    {
        if (!TryGetAny(root, out var value, names)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        return value.ValueKind == JsonValueKind.String &&
               long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static double? ReadDouble(JsonElement root, params string[] names)
    {
        if (!TryGetAny(root, out var value, names)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number)) return number;
        return value.ValueKind == JsonValueKind.String &&
               double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static bool TryGetAny(JsonElement root, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
            if (TryGet(root, name, out value)) return true;
        value = default;
        return false;
    }

    private static bool TryGet(JsonElement root, string name, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out value)) return true;
        value = default;
        return false;
    }
}
