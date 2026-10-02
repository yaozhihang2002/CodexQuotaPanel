namespace CodexQuota.Domain;

/// <summary>
/// Estimates API-equivalent USD from a dated public API price snapshot.
/// It is never a subscription invoice or a Codex quota conversion.
/// </summary>
public static class ApiCostEstimator
{
    public const string BasisDate = "2026-09-30";
    private const string LegacyBasisDate = "2026-09-07";
    public const string SourceUrl = "https://developers.openai.com/api/docs/pricing";
    private static PricingCatalog? _catalog;
    private static readonly object CatalogGate = new();
    public static long CatalogRevision => Volatile.Read(ref _catalog)?.Revision ?? PricingCatalog.BuiltInRevision;
    public static string CurrentBasisDate => Volatile.Read(ref _catalog)?.CheckedAt ?? BasisDate;

    public static bool ApplyCatalog(PricingCatalog catalog)
    {
        lock (CatalogGate)
        {
            if (IsOlderCatalog(catalog) || IsCurrentCatalog(catalog)) return false;
            Volatile.Write(ref _catalog, catalog);
            return true;
        }
    }

    public static bool IsOlderCatalog(PricingCatalog catalog) => catalog.Revision < CatalogRevision ||
        string.CompareOrdinal(catalog.CheckedAt, CurrentBasisDate) < 0;

    public static bool IsCurrentCatalog(PricingCatalog catalog)
    {
        var current = Volatile.Read(ref _catalog);
        if (catalog.Revision != (current?.Revision ?? PricingCatalog.BuiltInRevision) ||
            catalog.CheckedAt != (current?.CheckedAt ?? BasisDate)) return false;
        if (current is not null)
            return catalog.Models.Count == current.Models.Count && catalog.Models.All(pair =>
                current.Models.TryGetValue(pair.Key, out var rate) && pair.Value == rate);
        return catalog.Models.Count == StandardPrices.Count && StandardPrices.All(pair =>
        {
            var p = pair.Value;
            return catalog.Models.TryGetValue(pair.Key, out var rate) && rate == new ModelRate(pair.Key,
                BuiltInDisplayModel(pair.Key), p.Input, p.CachedInput, p.Output, p.BasisDate,
                p.FastMultiplier, p.CacheWriteMultiplier, p.LongContextSurcharge ? p.LongThreshold : null,
                p.LongContextSurcharge ? p.LongInputMultiplier : 1m, p.LongContextSurcharge ? p.LongOutputMultiplier : 1m);
        });
    }
    private const decimal TokensPerMillion = 1_000_000m;
    private const long LongContextThreshold = 272_000;
    private static readonly IReadOnlyDictionary<string, ModelPrice> StandardPrices =
        new Dictionary<string, ModelPrice>(StringComparer.OrdinalIgnoreCase)
        {
            ["gpt-6-astra"] = new(10.00m, 1.00m, 50.00m, 2m, true, 1.25m),
            ["gpt-6.1-sol"] = new(2.00m, 0.10m, 10.00m, 2m, true, 1.25m),
            ["gpt-6-sol"] = new(2.00m, 0.20m, 10.00m, 2m, true, 1.25m, BasisDate),
            ["gpt-6-luna"] = new(0.10m, 0.01m, 0.50m, 2m, true, 1.25m, BasisDate),
            ["gpt-5.6-sol"] = new(4.00m, 0.40m, 20.00m, 2m, true, 1.25m),
            ["gpt-5.6-terra"] = new(2.00m, 0.20m, 12.00m, 2m, true, 1.25m),
            ["gpt-5.6-luna"] = new(0.20m, 0.02m, 1.20m, 2m, true, 1.25m),
            ["gpt-5.5"] = new(5.00m, 0.50m, 30.00m, 2m, true),
            ["gpt-5.4"] = new(2.50m, 0.25m, 15.00m, 2m, true),
            ["gpt-5.4-mini"] = new(0.75m, 0.075m, 4.50m),
            ["gpt-5.3-codex"] = new(1.75m, 0.175m, 14.00m, 2m),
            // Codex reports the reviewer as a workload label rather than its backing model.
            // Retain the previously verified Auto-review to GPT-5.4 mapping;
            // the model API prices above were rechecked separately on 2026-09-30.
            ["codex-auto-review"] = new(2.50m, 0.25m, 15.00m, 2m, true, 1m, LegacyBasisDate)
        };

    public static ApiCostEstimate Estimate(string? model, string? serviceTier, TokenUsageBreakdown usage)
    {
        var normalizedModel = NormalizeModel(model);
        var normalizedTier = NormalizeTier(serviceTier);
        var catalog = Volatile.Read(ref _catalog);
        ModelPrice? price;
        if (catalog is not null)
            price = catalog.Models.TryGetValue(normalizedModel, out var rate)
                ? new ModelPrice(rate.Input, rate.CachedInput, rate.Output, rate.FastMultiplier,
                    rate.LongContextThreshold is not null, rate.CacheWriteMultiplier, rate.BasisDate,
                    rate.LongContextThreshold ?? LongContextThreshold, rate.LongInputMultiplier, rate.LongOutputMultiplier)
                : null;
        else StandardPrices.TryGetValue(normalizedModel, out price);
        if (price is null ||
            normalizedTier == ServiceTier.Unknown)
            return ApiCostEstimate.Unpriced(catalog?.CheckedAt ?? BasisDate, SourceUrl);

        if (normalizedTier == ServiceTier.Fast && price.FastMultiplier is null)
            return ApiCostEstimate.Unpriced(price.BasisDate, SourceUrl);
        var tierMultiplier = normalizedTier == ServiceTier.Fast ? price.FastMultiplier!.Value : 1m;
        var longContext = price.LongContextSurcharge && usage.InputTokens > price.LongThreshold;
        var inputMultiplier = longContext ? price.LongInputMultiplier : 1m;
        var outputMultiplier = longContext ? price.LongOutputMultiplier : 1m;
        var cached = Math.Min(usage.CachedInputTokens, usage.InputTokens);
        var cacheWrite = Math.Min(usage.CacheWriteInputTokens, Math.Max(0, usage.InputTokens - cached));
        var uncached = Math.Max(0, usage.InputTokens - cached - cacheWrite);

        var scaled =
            uncached * price.Input * tierMultiplier * inputMultiplier +
            cacheWrite * price.Input * price.CacheWriteMultiplier * tierMultiplier * inputMultiplier +
            cached * price.CachedInput * tierMultiplier * inputMultiplier +
            usage.OutputTokens * price.Output * tierMultiplier * outputMultiplier;
        return new ApiCostEstimate(scaled / TokensPerMillion, true, price.BasisDate, SourceUrl);
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

    public static string DisplayModel(string? model) =>
        Volatile.Read(ref _catalog)?.Models.TryGetValue(NormalizeModel(model), out var rate) == true
            ? rate.DisplayName : BuiltInDisplayModel(model);

    private static string BuiltInDisplayModel(string? model) => NormalizeModel(model) switch
    {
        "gpt-6-astra" => "GPT-6 Astra",
        "gpt-6.1-sol" => "GPT-6.1 Sol",
        "gpt-6-sol" => "GPT-6 Sol",
        "gpt-6-luna" => "GPT-6 Luna",
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
        decimal CacheWriteMultiplier = 1m,
        string BasisDate = ApiCostEstimator.BasisDate,
        long LongThreshold = LongContextThreshold,
        decimal LongInputMultiplier = 2m,
        decimal LongOutputMultiplier = 1.5m);
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
