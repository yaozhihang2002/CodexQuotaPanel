namespace CodexQuotaPanel;

internal sealed record TokenUsageBreakdown(
    long TotalTokens,
    long InputTokens,
    long CachedInputTokens,
    long OutputTokens,
    long ReasoningOutputTokens,
    long CacheWriteInputTokens = 0)
{
    public static TokenUsageBreakdown Empty { get; } = new(0, 0, 0, 0, 0, 0);

    public TokenUsageBreakdown Add(TokenUsageBreakdown other) => new(
        checked(TotalTokens + other.TotalTokens),
        checked(InputTokens + other.InputTokens),
        checked(CachedInputTokens + other.CachedInputTokens),
        checked(OutputTokens + other.OutputTokens),
        checked(ReasoningOutputTokens + other.ReasoningOutputTokens),
        checked(CacheWriteInputTokens + other.CacheWriteInputTokens));

    internal TokenUsageBreakdown PositiveDelta(TokenUsageBreakdown previous) => new(
        Math.Max(0, TotalTokens - previous.TotalTokens),
        Math.Max(0, InputTokens - previous.InputTokens),
        Math.Max(0, CachedInputTokens - previous.CachedInputTokens),
        Math.Max(0, OutputTokens - previous.OutputTokens),
        Math.Max(0, ReasoningOutputTokens - previous.ReasoningOutputTokens),
        Math.Max(0, CacheWriteInputTokens - previous.CacheWriteInputTokens));
}

internal sealed record TokenUsageSlice(
    string Model,
    string Speed,
    TokenUsageBreakdown Usage,
    decimal EstimatedUsd,
    bool IsPriced)
{
    public string ModelDisplay => ApiCostEstimator.DisplayModel(Model);
    public string SpeedDisplay => ApiCostEstimator.DisplaySpeed(Speed);
}

internal sealed record DailyTokenUsage(
    DateOnly LocalDate,
    TokenUsageBreakdown Usage,
    IReadOnlyList<TokenUsageSlice> Slices)
{
    public DailyTokenUsage(DateOnly localDate, TokenUsageBreakdown usage)
        : this(localDate, usage, [])
    {
    }

    public decimal EstimatedUsd => Slices.Where(slice => slice.IsPriced).Sum(slice => slice.EstimatedUsd);
    public long UnpricedTokens => Slices.Where(slice => !slice.IsPriced).Sum(slice => slice.Usage.TotalTokens);
}

internal sealed record TokenUsageDiagnostics(
    int ParsedFileCount,
    int CachedFileCount,
    int IncrementalFileCount,
    int ParsedEventCount,
    int MalformedTokenLineCount,
    int DuplicateEventCount,
    int FallbackEventCount,
    long AttributedTokens,
    long TotalTokens)
{
    internal static TokenUsageDiagnostics Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);
    internal double AttributionCoverage => TotalTokens <= 0
        ? 1d
        : Math.Clamp(AttributedTokens / (double)TotalTokens, 0d, 1d);
    internal bool IsPartial => MalformedTokenLineCount > 0;
}

internal sealed record TokenCycleUsage(
    DateTimeOffset StartsAt,
    DateTimeOffset ResetsAt,
    int WindowMinutes,
    IReadOnlyList<DailyTokenUsage> Days,
    DateTimeOffset ScannedAt,
    int SourceFileCount,
    TokenUsageDiagnostics? Diagnostics = null)
{
    public TokenUsageBreakdown Total => Days.Aggregate(
        TokenUsageBreakdown.Empty,
        (sum, day) => sum.Add(day.Usage));

    public decimal EstimatedUsd => Days.Sum(day => day.EstimatedUsd);
    public long UnpricedTokens => Days.Sum(day => day.UnpricedTokens);
    public TokenUsageDiagnostics Health => Diagnostics ?? TokenUsageDiagnostics.Empty;

    public IReadOnlyList<TokenUsageSlice> Slices => Days
        .SelectMany(day => day.Slices)
        .GroupBy(slice => (slice.Model, slice.Speed, slice.IsPriced))
        .Select(group => new TokenUsageSlice(
            group.Key.Model,
            group.Key.Speed,
            group.Aggregate(TokenUsageBreakdown.Empty, (sum, slice) => sum.Add(slice.Usage)),
            group.Sum(slice => slice.EstimatedUsd),
            group.Key.IsPriced))
        .OrderByDescending(slice => slice.EstimatedUsd)
        .ThenByDescending(slice => slice.Usage.TotalTokens)
        .ToArray();
}

internal sealed record TokenCountSample(
    DateTimeOffset Timestamp,
    TokenUsageBreakdown? CumulativeUsage,
    TokenUsageBreakdown? LastUsage,
    string Fingerprint);

internal sealed record TokenUsageEvent(
    DateTimeOffset Timestamp,
    TokenUsageBreakdown Usage,
    string Model,
    string Speed,
    bool HasExplicitSpeed,
    string Fingerprint);
