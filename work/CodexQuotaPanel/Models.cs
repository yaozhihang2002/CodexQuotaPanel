using System.Globalization;
using System.Text.Json;

namespace CodexQuotaPanel;

internal sealed record LimitBucket(
    double UsedPercent,
    int? WindowMinutes,
    DateTimeOffset? ResetsAt)
{
    public double RemainingPercent => Math.Clamp(100d - UsedPercent, 0d, 100d);
}

internal sealed record CreditInfo(
    bool? HasCredits,
    bool? Unlimited,
    string? Balance);

internal sealed record RateLimitResetCreditInfo(
    string Id,
    string? Title,
    string? Description,
    string Status,
    DateTimeOffset? ExpiresAt);

internal sealed record RateLimitResetCreditsInfo(
    long AvailableCount,
    IReadOnlyList<RateLimitResetCreditInfo>? Credits)
{
    public RateLimitResetCreditInfo? SoonestExpiring => Credits?
        .Where(credit => string.Equals(credit.Status, "available", StringComparison.OrdinalIgnoreCase) &&
                         credit.ExpiresAt is not null &&
                         credit.ExpiresAt.Value > DateTimeOffset.Now)
        .OrderBy(credit => credit.ExpiresAt)
        .FirstOrDefault();
}

internal sealed record QuotaSnapshot(
    string? LimitId,
    string? LimitName,
    LimitBucket? Primary,
    LimitBucket? Secondary,
    CreditInfo? Credits,
    string? PlanType,
    string? ReachedType,
    DateTimeOffset ObservedAt,
    string Source,
    int AdditionalLimitCount = 0,
    RateLimitResetCreditsInfo? ResetCredits = null)
{
    public IEnumerable<LimitBucket> Buckets
    {
        get
        {
            if (Primary is not null) yield return Primary;
            if (Secondary is not null) yield return Secondary;
        }
    }

    public double RemainingPercent => Buckets.Any()
        ? Buckets.Min(bucket => bucket.RemainingPercent)
        : 100d;

    public bool IsBlocked => RemainingPercent <= 0.001d || ReachedType is not null;
}

internal static class QuotaParser
{
    public static QuotaSnapshot? ParseRolloutLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.Contains("\"rate_limits\"", StringComparison.Ordinal))
            return null;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!TryGet(root, "payload", out var payload) || payload.ValueKind != JsonValueKind.Object)
                return null;
            if (!TryGet(payload, "rate_limits", out var limits) || limits.ValueKind != JsonValueKind.Object)
                return null;

            var observedAt = ReadTimestamp(root, "timestamp");
            if (observedAt is null) return null;
            return ParseSnapshot(limits, observedAt.Value, "本地会话", false, 0);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static QuotaSnapshot? ParseAppServerResult(JsonElement result)
    {
        if (result.ValueKind != JsonValueKind.Object)
            return null;

        var candidates = new List<(string? Id, JsonElement Value)>();
        var hasByIdEntries = false;

        if (TryGetAny(result, out var byId, "rateLimitsByLimitId", "rate_limits_by_limit_id") &&
            byId.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in byId.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Object)
                {
                    candidates.Add((property.Name, property.Value));
                    hasByIdEntries = true;
                }
            }
        }

        if (!hasByIdEntries && TryGetAny(result, out var single, "rateLimits", "rate_limits") &&
            single.ValueKind == JsonValueKind.Object)
        {
            candidates.Add((ReadString(single, "limitId", "limit_id"), single));
        }

        if (candidates.Count == 0 &&
            (HasAny(result, "primary", "primary_window") || HasAny(result, "secondary", "secondary_window")))
        {
            candidates.Add((ReadString(result, "limitId", "limit_id"), result));
        }

        if (candidates.Count == 0)
            return null;

        var chosen = candidates.FirstOrDefault(item =>
            string.Equals(item.Id, "codex", StringComparison.OrdinalIgnoreCase));
        if (chosen.Value.ValueKind == JsonValueKind.Undefined)
            chosen = candidates.OrderBy(item => MinimumRemaining(item.Value)).First();

        return ParseSnapshot(
            chosen.Value,
            DateTimeOffset.Now,
            "App Server",
            true,
            Math.Max(candidates.Count - 1, 0),
            chosen.Id,
            ReadResetCredits(result));
    }

    private static double MinimumRemaining(JsonElement value)
    {
        var parsed = ParseSnapshot(value, DateTimeOffset.Now, "App Server", true, 0);
        return parsed?.RemainingPercent ?? 100d;
    }

    private static QuotaSnapshot? ParseSnapshot(
        JsonElement limits,
        DateTimeOffset observedAt,
        string source,
        bool camelCase,
        int additionalLimitCount,
        string? fallbackId = null,
        RateLimitResetCreditsInfo? resetCredits = null)
    {
        var primary = ReadBucket(limits, camelCase ? new[] { "primary", "primaryWindow", "primary_window" } : new[] { "primary", "primary_window" }, camelCase);
        var secondary = ReadBucket(limits, camelCase ? new[] { "secondary", "secondaryWindow", "secondary_window" } : new[] { "secondary", "secondary_window" }, camelCase);
        if (primary is null && secondary is null)
            return null;

        var creditInfo = ReadCredits(limits);
        return new QuotaSnapshot(
            ReadString(limits, "limitId", "limit_id") ?? fallbackId,
            ReadString(limits, "limitName", "limit_name"),
            primary,
            secondary,
            creditInfo,
            ReadString(limits, "planType", "plan_type"),
            ReadReachedType(limits),
            observedAt,
            source,
            additionalLimitCount,
            resetCredits);
    }

    private static RateLimitResetCreditsInfo? ReadResetCredits(JsonElement root)
    {
        if (!TryGetAny(root, out var summary, "rateLimitResetCredits", "rate_limit_reset_credits") ||
            summary.ValueKind != JsonValueKind.Object)
            return null;

        var availableCount = ReadLong(summary, "availableCount", "available_count");
        if (availableCount is null) return null;
        if (!TryGet(summary, "credits", out var creditsElement) || creditsElement.ValueKind == JsonValueKind.Null)
            return new RateLimitResetCreditsInfo(Math.Max(0, availableCount.Value), null);
        if (creditsElement.ValueKind != JsonValueKind.Array)
            return new RateLimitResetCreditsInfo(Math.Max(0, availableCount.Value), null);

        var credits = new List<RateLimitResetCreditInfo>();
        foreach (var value in creditsElement.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Object) continue;
            var id = ReadString(value, "id");
            var status = ReadString(value, "status");
            if (string.IsNullOrWhiteSpace(id) || string.IsNullOrWhiteSpace(status)) continue;

            DateTimeOffset? expiresAt = null;
            var expiresAtSeconds = ReadLong(value, "expiresAt", "expires_at");
            if (expiresAtSeconds is > 0)
            {
                try { expiresAt = DateTimeOffset.FromUnixTimeSeconds(expiresAtSeconds.Value); }
                catch (ArgumentOutOfRangeException) { }
            }

            credits.Add(new RateLimitResetCreditInfo(
                id,
                ReadString(value, "title"),
                ReadString(value, "description"),
                status,
                expiresAt));
        }

        return new RateLimitResetCreditsInfo(Math.Max(0, availableCount.Value), credits);
    }

    private static LimitBucket? ReadBucket(JsonElement root, IEnumerable<string> names, bool camelCase)
    {
        JsonElement bucket = default;
        var found = names.Any(name => TryGet(root, name, out bucket));
        if (!found || bucket.ValueKind != JsonValueKind.Object)
            return null;

        var used = ReadDouble(bucket, camelCase ? new[] { "usedPercent", "used_percent" } : new[] { "used_percent", "usedPercent" });
        if (used is null)
            return null;

        var minutes = ReadInt(bucket,
            camelCase
                ? new[] { "windowDurationMins", "windowMinutes", "window_minutes" }
                : new[] { "window_minutes", "windowDurationMins" });
        var resetSeconds = ReadLong(bucket, camelCase ? new[] { "resetsAt", "resets_at" } : new[] { "resets_at", "resetsAt" });
        DateTimeOffset? resetsAt = null;
        if (resetSeconds is > 0)
        {
            try { resetsAt = DateTimeOffset.FromUnixTimeSeconds(resetSeconds.Value); }
            catch (ArgumentOutOfRangeException) { }
        }

        return new LimitBucket(Math.Clamp(used.Value, 0d, 100d), minutes, resetsAt);
    }

    private static CreditInfo? ReadCredits(JsonElement root)
    {
        if (!TryGetAny(root, out var credits, "credits", "creditDetails", "credit_details") ||
            credits.ValueKind != JsonValueKind.Object)
            return null;

        var balance = ReadScalarAsString(credits, "balance");
        var hasCredits = ReadBool(credits, "hasCredits", "has_credits");
        var unlimited = ReadBool(credits, "unlimited");
        return balance is null && hasCredits is null && unlimited is null
            ? null
            : new CreditInfo(hasCredits, unlimited, balance);
    }

    private static string? ReadReachedType(JsonElement root)
    {
        if (!TryGetAny(root, out var value, "rateLimitReachedType", "rate_limit_reached_type"))
            return null;
        if (value.ValueKind == JsonValueKind.String)
            return value.GetString();
        if (value.ValueKind == JsonValueKind.Object)
            return ReadString(value, "type");
        return null;
    }

    private static DateTimeOffset? ReadTimestamp(JsonElement root, params string[] names)
    {
        var text = ReadString(root, names);
        return DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed
            : null;
    }

    private static string? ReadString(JsonElement root, params string[] names)
    {
        if (!TryGetAny(root, out var value, names) || value.ValueKind != JsonValueKind.String)
            return null;
        return value.GetString();
    }

    private static string? ReadScalarAsString(JsonElement root, params string[] names)
    {
        if (!TryGetAny(root, out var value, names))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static double? ReadDouble(JsonElement root, params string[] names)
    {
        if (!TryGetAny(root, out var value, names))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String &&
            double.TryParse(value.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out number))
            return number;
        return null;
    }

    private static int? ReadInt(JsonElement root, params string[] names)
    {
        var value = ReadLong(root, names);
        return value is >= int.MinValue and <= int.MaxValue ? (int)value.Value : null;
    }

    private static long? ReadLong(JsonElement root, params string[] names)
    {
        if (!TryGetAny(root, out var value, names))
            return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number))
            return number;
        if (value.ValueKind == JsonValueKind.String &&
            long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            return number;
        return null;
    }

    private static bool? ReadBool(JsonElement root, params string[] names)
    {
        if (!TryGetAny(root, out var value, names))
            return null;
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => null
        };
    }

    private static bool HasAny(JsonElement root, params string[] names) =>
        names.Any(name => TryGet(root, name, out _));

    private static bool TryGetAny(JsonElement root, out JsonElement value, params string[] names)
    {
        foreach (var name in names)
        {
            if (TryGet(root, name, out value))
                return true;
        }
        value = default;
        return false;
    }

    private static bool TryGet(JsonElement root, string name, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out value))
            return true;
        value = default;
        return false;
    }
}
