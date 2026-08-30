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
    private static readonly IReadOnlyDictionary<string, ModelPrice> StandardPrices =
        new Dictionary<string, ModelPrice>(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-5.6-sol"] = new(4.00m, 0.40m, 20.00m, 2m, true, 1.25m),
            ["gpt-5.6-terra"] = new(2.00m, 0.20m, 12.00m, 2m, true, 1.25m),
            ["gpt-5.6-luna"] = new(0.20m, 0.02m, 1.20m, 2m, true, 1.25m),
            ["gpt-5.5"] = new(5.00m, 0.50m, 30.00m, 2m),
            ["gpt-5.4"] = new(2.50m, 0.25m, 15.00m, 2m, true),
            ["gpt-5.4-mini"] = new(0.75m, 0.075m, 4.50m),
            ["gpt-5.3-codex"] = new(1.75m, 0.175m, 14.00m, 2m),
            // Codex reports the reviewer as a workload label rather than its backing model.
            // The current official Codex rate card maps Auto-review to GPT-5.4.
            ["codex-auto-review"] = new(2.50m, 0.25m, 15.00m, 2m, true)
        };

    public static ApiCostEstimate Estimate(string? model, string? serviceTier, TokenUsageBreakdown usage)
    {
        var normalizedModel = NormalizeModel(model);
        var normalizedTier = NormalizeTier(serviceTier);
        if (!StandardPrices.TryGetValue(normalizedModel, out var price) ||
            normalizedTier == ServiceTier.Unknown)
            return ApiCostEstimate.Unpriced(BasisDate, SourceUrl);

        if (normalizedTier == ServiceTier.Fast && price.FastMultiplier is null)
            return ApiCostEstimate.Unpriced(BasisDate, SourceUrl);
        var tierMultiplier = normalizedTier == ServiceTier.Fast ? price.FastMultiplier!.Value : 1m;
        var longContext = price.LongContextSurcharge && usage.InputTokens > LongContextThreshold;
        var inputMultiplier = longContext ? 2m : 1m;
        var outputMultiplier = longContext ? 1.5m : 1m;
        var cached = Math.Min(usage.CachedInputTokens, usage.InputTokens);
        var cacheWrite = Math.Min(usage.CacheWriteInputTokens, Math.Max(0, usage.InputTokens - cached));
        var uncached = Math.Max(0, usage.InputTokens - cached - cacheWrite);

        var scaled =
            uncached * price.Input * tierMultiplier * inputMultiplier +
            cacheWrite * price.Input * price.CacheWriteMultiplier * tierMultiplier * inputMultiplier +
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
        "gpt-5.5" => "gpt-5.5",
        "gpt-5.4" => "gpt-5.4",
        "gpt-5.4-mini" => "gpt-5.4-mini",
        "gpt-5.3-codex" => "gpt-5.3-codex",
        "codex-auto-review" => "codex-auto-review",
        { Length: > 0 } value => value,
        _ => "unknown"
    };

    public static string DisplayModel(string? model) => NormalizeModel(model) switch
    {
        "gpt-5.6-sol" => "GPT-5.6 Sol",
        "gpt-5.6-terra" => "GPT-5.6 Terra",
        "gpt-5.6-luna" => "GPT-5.6 Luna",
        "gpt-5.5" => "GPT-5.5",
        "gpt-5.4" => "GPT-5.4",
        "gpt-5.4-mini" => "GPT-5.4 mini",
        "gpt-5.3-codex" => "GPT-5.3-Codex",
        "codex-auto-review" => "Auto-review",
        "unknown" => "Unknown",
        var value => value
    };

    public static ServiceTier NormalizeTier(string? tier) => tier?.Trim().ToLowerInvariant() switch
    {
        "default" or "standard" => ServiceTier.Default,
        "fast" or "priority" => ServiceTier.Fast,
        _ => ServiceTier.Unknown
    };

    public static string DisplayTier(string? tier) => NormalizeTier(tier) switch
    {
        ServiceTier.Default => "Default",
        ServiceTier.Fast => "Fast",
        _ => "Unknown"
    };

    private sealed record ModelPrice(
        decimal Input,
        decimal CachedInput,
        decimal Output,
        decimal? FastMultiplier = null,
        bool LongContextSurcharge = false,
        decimal CacheWriteMultiplier = 1m);
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
