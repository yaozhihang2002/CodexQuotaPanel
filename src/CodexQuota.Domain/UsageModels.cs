namespace CodexQuota.Domain;

public sealed record ObservedUsage(
    DateTimeOffset ObservedAt,
    string Model,
    string ServiceTier,
    long InputTokens,
    long CachedInputTokens,
    long OutputTokens,
    long ReasoningTokens)
{
    public long TotalTokens => checked(InputTokens + OutputTokens);
}

public enum ForecastConfidence
{
    Unavailable,
    Low,
    Medium,
    High
}

public sealed record UsageForecast(
    string WindowId,
    DateTimeOffset? ExhaustsAt,
    double PercentPerHour,
    double? SustainablePercentPerHour,
    ForecastConfidence Confidence);
