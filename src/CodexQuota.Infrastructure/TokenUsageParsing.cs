using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CodexQuota.Domain;

namespace CodexQuota.Infrastructure;

public sealed record TokenCountSample(
    DateTimeOffset Timestamp,
    TokenUsageBreakdown? CumulativeUsage,
    TokenUsageBreakdown? LastUsage,
    string Fingerprint);

public static class TokenCountLineParser
{
    public static TokenCountSample? Parse(string line)
    {
        if (string.IsNullOrWhiteSpace(line) || !line.Contains("\"token_count\"", StringComparison.Ordinal))
            return null;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!TryGet(root, "payload", out var payload) ||
                !string.Equals(ReadString(payload, "type"), "token_count", StringComparison.Ordinal) ||
                !TryGet(payload, "info", out var info))
                return null;

            if (!DateTimeOffset.TryParse(ReadString(root, "timestamp"), CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var timestamp))
                return null;

            var cumulative = TryGet(info, "total_token_usage", out var total) ? ReadUsage(total) : null;
            var last = TryGet(info, "last_token_usage", out var recent) ? ReadUsage(recent) : null;
            if (cumulative is null && last is null) return null;

            var turnId = ReadString(payload, "turn_id");
            var identity = string.IsNullOrWhiteSpace(turnId)
                ? line
                : $"{timestamp.UtcTicks}|{turnId}|{last?.TotalTokens}|{cumulative?.TotalTokens}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
            return new TokenCountSample(timestamp, cumulative, last, Convert.ToHexString(hash.AsSpan(0, 12)));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static TokenUsageBreakdown? ReadUsage(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object) return null;
        var input = NonNegative(ReadLong(value, "input_tokens"));
        var hasInputDetails = TryGet(value, "input_tokens_details", out var inputDetails);
        var cached = NonNegative(ReadLong(value, "cached_input_tokens") ??
                                 (hasInputDetails ? ReadLong(inputDetails, "cached_tokens") : null));
        var output = NonNegative(ReadLong(value, "output_tokens"));
        var hasOutputDetails = TryGet(value, "output_tokens_details", out var outputDetails);
        var reasoning = NonNegative(ReadLong(value, "reasoning_output_tokens") ??
                                    (hasOutputDetails ? ReadLong(outputDetails, "reasoning_tokens") : null));
        var cacheWrite = NonNegative(ReadLong(value, "cache_write_input_tokens") ??
                                     ReadLong(value, "cache_write_tokens") ??
                                     (hasInputDetails ? ReadLong(inputDetails, "cache_write_tokens") : null));
        var total = Math.Max(0, ReadLong(value, "total_tokens") ?? checked(input + output));
        return total == 0 && input == 0 && cached == 0 && output == 0 && reasoning == 0 && cacheWrite == 0
            ? null
            : new TokenUsageBreakdown(total, input, cached, output, reasoning, cacheWrite);
    }

    private static long NonNegative(long? value) => Math.Max(0, value ?? 0);

    private static string? ReadString(JsonElement root, string name) =>
        TryGet(root, name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static long? ReadLong(JsonElement root, string name)
    {
        if (!TryGet(root, name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        return value.ValueKind == JsonValueKind.String &&
               long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number)
            ? number
            : null;
    }

    private static bool TryGet(JsonElement root, string name, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out value)) return true;
        value = default;
        return false;
    }
}

public static class TokenUsageNormalizer
{
    public static TokenUsageBreakdown? Normalize(
        TokenCountSample sample,
        ref TokenUsageBreakdown? previousCumulative)
    {
        if (sample.CumulativeUsage is not { } cumulative)
            return Positive(sample.LastUsage);

        TokenUsageBreakdown? delta;
        if (previousCumulative is null || cumulative.TotalTokens < previousCumulative.TotalTokens)
            delta = cumulative;
        else if (cumulative.TotalTokens == previousCumulative.TotalTokens)
            delta = null;
        else
            delta = cumulative.PositiveDelta(previousCumulative);

        previousCumulative = cumulative;
        return delta is null ? null : Positive(sample.LastUsage) ?? Positive(delta);
    }

    private static TokenUsageBreakdown? Positive(TokenUsageBreakdown? usage) =>
        usage is { TotalTokens: > 0 } ? usage : null;
}

public static class TokenLogContextParser
{
    public static void Apply(string line, ref string model, ref string tier, out bool tierSpecified)
    {
        tierSpecified = false;
        if (string.IsNullOrWhiteSpace(line) ||
            (!line.Contains("\"turn_context\"", StringComparison.Ordinal) &&
             !line.Contains("\"thread_settings_applied\"", StringComparison.Ordinal)))
            return;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var type) || !root.TryGetProperty("payload", out var payload))
                return;

            var settings = payload;
            if (type.GetString() == "event_msg" &&
                payload.TryGetProperty("type", out var payloadType) &&
                payloadType.GetString() == "thread_settings_applied" &&
                payload.TryGetProperty("thread_settings", out var threadSettings))
                settings = threadSettings;
            else if (type.GetString() != "turn_context")
                return;

            if (settings.TryGetProperty("model", out var modelValue) &&
                modelValue.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(modelValue.GetString()))
                model = modelValue.GetString()!;
            if (settings.TryGetProperty("service_tier", out var tierValue) &&
                tierValue.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(tierValue.GetString()))
            {
                tier = tierValue.GetString()!;
                tierSpecified = true;
            }
        }
        catch (JsonException)
        {
        }
    }
}
