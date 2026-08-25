namespace CodexQuotaPanel;

/// <summary>
/// Estimates the API-equivalent USD cost of local Codex token records from a
/// dated snapshot of published OpenAI API prices. This is not a subscription
/// invoice and is not an official conversion of Codex quota percentages.
/// </summary>
internal static class ApiCostEstimator
{
    internal const string BasisDate = "2026-08-24";
    internal const string SourceUrl = "https://developers.openai.com/api/docs/models/compare";
    internal const string FastSourceUrl = "https://openai.com/api-fast-mode/";
    private const decimal TokensPerMillion = 1_000_000m;
    private const long LongContextThreshold = 272_000;
    private const decimal CacheWriteInputMultiplier = 1.25m;

    private static readonly IReadOnlyDictionary<string, Price> DefaultPrices =
        new Dictionary<string, Price>(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-5.6-sol"] = new(4.00m, 0.40m, 20.00m),
            ["gpt-5.6-terra"] = new(2.00m, 0.20m, 12.00m),
            ["gpt-5.6-luna"] = new(0.20m, 0.02m, 1.20m)
        };

    internal static ApiCostEstimate Estimate(
        string model,
        string speed,
        TokenUsageBreakdown usage)
    {
        var normalizedModel = NormalizeModel(model);
        var normalizedSpeed = NormalizeSpeed(speed);
        if (!DefaultPrices.TryGetValue(normalizedModel, out var price) ||
            normalizedSpeed == TokenSpeed.Unknown)
            return new ApiCostEstimate(0m, false);

        // OpenAI documents Fast as the API priority tier at twice Standard
        // price. Keep the multiplier explicit so the basis is easy to update.
        var speedMultiplier = normalizedSpeed == TokenSpeed.Fast ? 2m : 1m;
        var inputMultiplier = usage.InputTokens > LongContextThreshold ? 2m : 1m;
        var outputMultiplier = usage.InputTokens > LongContextThreshold ? 1.5m : 1m;
        var cached = Math.Min(usage.CachedInputTokens, usage.InputTokens);
        var cacheWrite = Math.Min(usage.CacheWriteInputTokens, Math.Max(0, usage.InputTokens - cached));
        var uncached = Math.Max(0, usage.InputTokens - cached - cacheWrite);
        var usdPerMillion =
            uncached * price.Input * speedMultiplier * inputMultiplier +
            cacheWrite * price.Input * CacheWriteInputMultiplier * speedMultiplier * inputMultiplier +
            cached * price.CachedInput * speedMultiplier * inputMultiplier +
            usage.OutputTokens * price.Output * speedMultiplier * outputMultiplier;
        return new ApiCostEstimate(usdPerMillion / TokensPerMillion, true);
    }

    internal static string NormalizeModel(string? model) => model?.Trim().ToLowerInvariant() switch
    {
        "gpt-5.6" => "gpt-5.6-sol",
        "gpt-5.6-sol" => "gpt-5.6-sol",
        "gpt-5.6-terra" => "gpt-5.6-terra",
        "gpt-5.6-luna" => "gpt-5.6-luna",
        { Length: > 0 } value => value,
        _ => "unknown"
    };

    internal static string DisplayModel(string? model) => NormalizeModel(model) switch
    {
        "gpt-5.6-sol" => "GPT-5.6 Sol",
        "gpt-5.6-terra" => "GPT-5.6 Terra",
        "gpt-5.6-luna" => "GPT-5.6 Luna",
        "codex-auto-review" => "Auto-review",
        "unknown" => "Unknown",
        var value => value
    };

    internal static TokenSpeed NormalizeSpeed(string? speed) => speed?.Trim().ToLowerInvariant() switch
    {
        "default" => TokenSpeed.Default,
        "fast" or "priority" => TokenSpeed.Fast,
        _ => TokenSpeed.Unknown
    };

    internal static string DisplaySpeed(string? speed) => NormalizeSpeed(speed) switch
    {
        TokenSpeed.Default => "Default",
        TokenSpeed.Fast => "Fast",
        _ => "Unknown"
    };

    private sealed record Price(decimal Input, decimal CachedInput, decimal Output);
}

internal enum TokenSpeed
{
    Unknown,
    Default,
    Fast
}

internal readonly record struct ApiCostEstimate(decimal Usd, bool IsPriced);
