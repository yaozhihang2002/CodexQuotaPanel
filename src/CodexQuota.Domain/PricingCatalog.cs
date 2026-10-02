using System.Collections.Frozen;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexQuota.Domain;

public sealed record ModelRate(string Model, string DisplayName, decimal Input, decimal CachedInput,
    decimal Output, string BasisDate, decimal? FastMultiplier = null, decimal CacheWriteMultiplier = 1m,
    long? LongContextThreshold = null, decimal LongInputMultiplier = 1m, decimal LongOutputMultiplier = 1m);

public sealed record PricingDocument(int SchemaVersion, long Revision, string CheckedAt, ModelRate[] Models);

/// <summary>Validated, immutable data only. No URLs, scripts or executable rules from the feed.</summary>
public sealed class PricingCatalog
{
    public const long BuiltInRevision = 2026093001;
    public long Revision { get; }
    public string CheckedAt { get; }
    public FrozenDictionary<string, ModelRate> Models { get; }
    public static JsonSerializerOptions JsonOptions { get; } = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public PricingCatalog(PricingDocument document)
    {
        if (document.SchemaVersion != 1 || document.Revision < 1 ||
            !ValidDate(document.CheckedAt) || document.Models is not { Length: > 0 and <= 256 })
            throw new InvalidDataException("Unsupported pricing catalog.");
        var models = new Dictionary<string, ModelRate>(StringComparer.OrdinalIgnoreCase);
        foreach (var rate in document.Models)
        {
            if (rate is null || rate.Model is null ||
                !Regex.IsMatch(rate.Model, "^[a-z0-9][a-z0-9.-]{0,79}$", RegexOptions.CultureInvariant) ||
                string.IsNullOrWhiteSpace(rate.DisplayName) || rate.DisplayName.Length > 80 ||
                rate.DisplayName.Any(char.IsControl) || !ValidDate(rate.BasisDate) ||
                string.CompareOrdinal(rate.BasisDate, document.CheckedAt) > 0 ||
                !Rate(rate.Input) || !Rate(rate.CachedInput) || !Rate(rate.Output) ||
                rate.CachedInput > rate.Input || !Multiplier(rate.CacheWriteMultiplier) ||
                rate.FastMultiplier is { } fast && !Multiplier(fast) ||
                rate.LongContextThreshold is { } threshold && (threshold < 1 || threshold > 100_000_000) ||
                !Multiplier(rate.LongInputMultiplier) || !Multiplier(rate.LongOutputMultiplier) ||
                !models.TryAdd(rate.Model, rate))
                throw new InvalidDataException("Invalid or duplicate model rate.");
        }
        Revision = document.Revision;
        CheckedAt = document.CheckedAt;
        Models = models.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }

    public static PricingCatalog Parse(string json) => new(JsonSerializer.Deserialize<PricingDocument>(json, JsonOptions)
        ?? throw new InvalidDataException("Empty pricing catalog."));
    private static bool Rate(decimal value) => value is >= 0 and <= 10_000;
    private static bool Multiplier(decimal value) => value is >= 1 and <= 100;
    private static bool ValidDate(string? date) => DateOnly.TryParseExact(date, "yyyy-MM-dd",
        CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}
