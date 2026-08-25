using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace CodexQuotaPanel;

internal static class TokenCountParser
{
    internal static bool LooksLikeTokenLine(string line) =>
        !string.IsNullOrWhiteSpace(line) &&
        line.Contains("\"token_count\"", StringComparison.Ordinal);

    public static TokenCountSample? ParseLine(string line)
    {
        if (!LooksLikeTokenLine(line)) return null;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!TryGet(root, "payload", out var payload) ||
                !string.Equals(ReadString(payload, "type"), "token_count", StringComparison.Ordinal) ||
                !TryGet(payload, "info", out var info))
                return null;

            var timestampText = ReadString(root, "timestamp");
            if (!DateTimeOffset.TryParse(timestampText, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var timestamp))
                return null;

            var cumulative = TryGet(info, "total_token_usage", out var totalUsage)
                ? ReadUsage(totalUsage)
                : null;
            var last = TryGet(info, "last_token_usage", out var lastUsage)
                ? ReadUsage(lastUsage)
                : null;
            if (cumulative is null && last is null) return null;

            var turnId = ReadString(payload, "turn_id");
            var identityText = string.IsNullOrWhiteSpace(turnId)
                ? line
                : $"{timestamp.UtcTicks}|{turnId}|{last?.TotalTokens}|{cumulative?.TotalTokens}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identityText));
            return new TokenCountSample(timestamp, cumulative, last, Convert.ToHexString(hash.AsSpan(0, 12)));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TokenUsageBreakdown? ReadUsage(JsonElement usage)
    {
        if (usage.ValueKind != JsonValueKind.Object) return null;
        var input = NonNegative(ReadLong(usage, "input_tokens"));
        var hasInputDetails = TryGet(usage, "input_tokens_details", out var inputDetails);
        var cached = NonNegative(ReadLong(usage, "cached_input_tokens") ??
                                 (hasInputDetails ? ReadLong(inputDetails, "cached_tokens") : null));
        var output = NonNegative(ReadLong(usage, "output_tokens"));
        var hasOutputDetails = TryGet(usage, "output_tokens_details", out var outputDetails);
        var reasoning = NonNegative(ReadLong(usage, "reasoning_output_tokens") ??
                                    (hasOutputDetails ? ReadLong(outputDetails, "reasoning_tokens") : null));
        var cacheWrite = NonNegative(ReadLong(usage, "cache_write_input_tokens") ??
                                     ReadLong(usage, "cache_write_tokens") ??
                                     (hasInputDetails ? ReadLong(inputDetails, "cache_write_tokens") : null));
        var totalValue = ReadLong(usage, "total_tokens");
        var total = Math.Max(0, totalValue ?? checked(input + output));
        if (total == 0 && input == 0 && cached == 0 && output == 0 && reasoning == 0 && cacheWrite == 0)
            return null;
        return new TokenUsageBreakdown(total, input, cached, output, reasoning, cacheWrite);
    }

    private static long NonNegative(long? value) => Math.Max(0, value ?? 0);

    private static string? ReadString(JsonElement root, string name) =>
        TryGet(root, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? ReadLong(JsonElement root, string name)
    {
        if (!TryGet(root, name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String &&
            long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            return number;
        return null;
    }

    private static bool TryGet(JsonElement root, string name, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out value)) return true;
        value = default;
        return false;
    }
}

internal static class TokenUsageNormalizer
{
    internal static TokenUsageBreakdown? Normalize(
        TokenCountSample sample,
        ref TokenUsageBreakdown? previousCumulative)
    {
        var cumulative = sample.CumulativeUsage;
        if (cumulative is null)
            return Positive(sample.LastUsage);

        TokenUsageBreakdown? cumulativeDelta = null;
        var repeatedSnapshot = false;
        if (previousCumulative is null)
        {
            cumulativeDelta = cumulative;
        }
        else if (cumulative.TotalTokens > previousCumulative.TotalTokens)
        {
            cumulativeDelta = cumulative.PositiveDelta(previousCumulative);
        }
        else if (cumulative.TotalTokens == previousCumulative.TotalTokens)
        {
            repeatedSnapshot = true;
        }
        else
        {
            // The counter restarted inside the same transcript. Treat the new
            // snapshot as a fresh baseline instead of subtracting into negatives.
            cumulativeDelta = cumulative;
        }
        previousCumulative = cumulative;

        if (repeatedSnapshot) return null;
        return Positive(sample.LastUsage) ?? Positive(cumulativeDelta);
    }

    private static TokenUsageBreakdown? Positive(TokenUsageBreakdown? usage) =>
        usage is { TotalTokens: > 0 } ? usage : null;
}

internal static class TokenLogContextParser
{
    internal static void Apply(
        string line,
        ref string model,
        ref string speed,
        out bool speedSpecified)
    {
        speedSpecified = false;
        if (string.IsNullOrWhiteSpace(line) ||
            (!line.Contains("\"turn_context\"", StringComparison.Ordinal) &&
             !line.Contains("\"thread_settings_applied\"", StringComparison.Ordinal)))
            return;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var rootType) ||
                !root.TryGetProperty("payload", out var payload))
                return;

            JsonElement settings = payload;
            if (rootType.GetString() == "event_msg" &&
                payload.TryGetProperty("type", out var payloadType) &&
                payloadType.GetString() == "thread_settings_applied" &&
                payload.TryGetProperty("thread_settings", out var threadSettings))
                settings = threadSettings;
            else if (rootType.GetString() != "turn_context")
                return;

            if (settings.TryGetProperty("model", out var modelValue) &&
                modelValue.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(modelValue.GetString()))
                model = modelValue.GetString()!;
            if (settings.TryGetProperty("service_tier", out var speedValue) &&
                speedValue.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(speedValue.GetString()))
            {
                speed = speedValue.GetString()!;
                speedSpecified = true;
            }
        }
        catch (JsonException)
        {
        }
    }
}
