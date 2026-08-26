namespace CodexQuota.Domain;

/// <summary>
/// Estimates API-equivalent USD from a dated public API price snapshot.
/// It is never a subscription invoice or a Codex quota conversion.
/// </summary>
public static class ApiCostEstimator
{
    public const string BasisDate = "2026-08-26";
    public const string SourceUrl = "https://platform.openai.com/pricing";
    private const decimal TokensPerMillion = 1_000_000m;
    private const long LongContextThreshold = 272_000;
    private const decimal CacheWriteMultiplier = 1.25m;

    private static readonly IReadOnlyDictionary<string, ModelPrice> StandardPrices =
        new Dictionary<string, ModelPrice>(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-5.6-sol"] = new(4.00m, 0.40m, 20.00m),
            ["gpt-5.6-terra"] = new(2.00m, 0.20m, 12.00m),
            ["gpt-5.6-luna"] = new(0.20m, 0.02m, 1.20m)
        };

    public static ApiCostEstimate Estimate(string? model, string? serviceTier, TokenUsageBreakdown usage)
    {
        var normalizedModel = NormalizeModel(model);
        var normalizedTier = NormalizeTier(serviceTier);
        if (!StandardPrices.TryGetValue(normalizedModel, out var price) ||
            normalizedTier == ServiceTier.Unknown)
            return ApiCostEstimate.Unpriced(BasisDate, SourceUrl);

        var tierMultiplier = normalizedTier == ServiceTier.Fast ? 2m : 1m;
        var inputMultiplier = usage.InputTokens > LongContextThreshold ? 2m : 1m;
        var outputMultiplier = usage.InputTokens > LongContextThreshold ? 1.5m : 1m;
        var cached = Math.Min(usage.CachedInputTokens, usage.InputTokens);
        var cacheWrite = Math.Min(usage.CacheWriteInputTokens, Math.Max(0, usage.InputTokens - cached));
        var uncached = Math.Max(0, usage.InputTokens - cached - cacheWrite);

        var scaled =
            uncached * price.Input * tierMultiplier * inputMultiplier +
            cacheWrite * price.Input * CacheWriteMultiplier * tierMultiplier * inputMultiplier +
            cached * price.CachedInput * tierMultiplier * inputMultiplier +
            usage.OutputTokens * price.Output * tierMultiplier * outputMultiplier;
        return new ApiCostEstimate(scaled / TokensPerMillion, true, BasisDate, SourceUrl);
    }

    public static string NormalizeModel(string? model) => model?.Trim().ToLowerInvariant() switch
    {
        "gpt-5.6" => "gpt-5.6-sol",
        "gpt-5.6-sol" => "gpt-5.6-sol",
        "gpt-5.6-terra" => "gpt-5.6-terra",
        "gpt-5.6-luna" => "gpt-5.6-luna",
        { Length: > 0 } value => value,
        _ => "unknown"
    };

    public static ServiceTier NormalizeTier(string? tier) => tier?.Trim().ToLowerInvariant() switch
    {
        "default" or "standard" => ServiceTier.Default,
        "fast" or "priority" => ServiceTier.Fast,
        _ => ServiceTier.Unknown
    };

    private sealed record ModelPrice(decimal Input, decimal CachedInput, decimal Output);
}

public enum ServiceTier
{
    Unknown,
    Default,
    Fast
}

public readonly record struct ApiCostEstimate(
    decimal Usd,
    bool IsPriced,
    string BasisDate,
    string SourceUrl)
{
    public static ApiCostEstimate Unpriced(string basisDate, string sourceUrl) =>
        new(0m, false, basisDate, sourceUrl);
}
