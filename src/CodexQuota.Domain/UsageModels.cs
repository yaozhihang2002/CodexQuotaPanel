namespace CodexQuota.Domain;

public sealed record TokenUsageBreakdown(
    long TotalTokens,
    long InputTokens,
    long CachedInputTokens,
    long OutputTokens,
    long ReasoningOutputTokens,
    long CacheWriteInputTokens = 0)
{
    public static TokenUsageBreakdown Zero { get; } = new(0, 0, 0, 0, 0, 0);

    public TokenUsageBreakdown Add(TokenUsageBreakdown other) => new(
        checked(TotalTokens + other.TotalTokens),
        checked(InputTokens + other.InputTokens),
        checked(CachedInputTokens + other.CachedInputTokens),
        checked(OutputTokens + other.OutputTokens),
        checked(ReasoningOutputTokens + other.ReasoningOutputTokens),
        checked(CacheWriteInputTokens + other.CacheWriteInputTokens));

    public TokenUsageBreakdown PositiveDelta(TokenUsageBreakdown previous) => new(
        Math.Max(0, TotalTokens - previous.TotalTokens),
        Math.Max(0, InputTokens - previous.InputTokens),
        Math.Max(0, CachedInputTokens - previous.CachedInputTokens),
        Math.Max(0, OutputTokens - previous.OutputTokens),
        Math.Max(0, ReasoningOutputTokens - previous.ReasoningOutputTokens),
        Math.Max(0, CacheWriteInputTokens - previous.CacheWriteInputTokens));
}

public sealed record ObservedUsage(
    DateTimeOffset ObservedAt,
    string Model,
    string ServiceTier,
    TokenUsageBreakdown Usage,
    string Fingerprint,
    bool IsServiceTierExplicit)
{
    public long TotalTokens => Usage.TotalTokens;
}

public sealed record DailyUsageSummary(
    DateOnly Day,
    TokenUsageBreakdown Usage,
    decimal EstimatedApiUsd,
    int UnpricedEventCount,
    IReadOnlyList<UsageSliceSummary> Slices);

public sealed record UsageSliceSummary(
    string Model,
    string ServiceTier,
    TokenUsageBreakdown Usage,
    decimal EstimatedApiUsd,
    int UnpricedEventCount);

public sealed record QuotaHistoryPoint(
    DateTimeOffset ObservedAt,
    string WindowId,
    int WindowMinutes,
    double RemainingPercent);

public enum ForecastConfidence
{
    Unavailable,
    Low,
    Medium,
    High
}

public enum ForecastState
{
    InsufficientData,
    Sustainable,
    AtRisk,
    Exhausted
}

public sealed record UsageForecast(
    string WindowId,
    DateTimeOffset? ExhaustsAt,
    double PercentPerHour,
    double ShortPercentPerHour,
    double LongPercentPerHour,
    double? SustainablePercentPerHour,
    ForecastConfidence Confidence,
    ForecastState State,
    int SampleIntervals,
    double ObservedMinutes);
