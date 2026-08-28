namespace CodexQuota.Domain;

public static class UsageSummaryCalculator
{
    public static IReadOnlyList<DailyUsageSummary> SummarizeByDay(
        IEnumerable<ObservedUsage> events,
        TimeZoneInfo timeZone)
    {
        ArgumentNullException.ThrowIfNull(events);
        ArgumentNullException.ThrowIfNull(timeZone);
        return events
            .GroupBy(item => DateOnly.FromDateTime(
                TimeZoneInfo.ConvertTime(item.ObservedAt, timeZone).DateTime))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var slices = group
                    .GroupBy(item => (item.Model, item.ServiceTier))
                    .Select(slice => BuildSlice(slice.Key.Model, slice.Key.ServiceTier, slice))
                    .OrderBy(slice => slice.UnpricedEventCount > 0)
                    .ThenByDescending(slice => slice.EstimatedApiUsd)
                    .ThenByDescending(slice => slice.Usage.TotalTokens)
                    .ThenBy(slice => slice.Model, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(slice => slice.ServiceTier, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                return new DailyUsageSummary(
                    group.Key,
                    Sum(group.Select(item => item.Usage)),
                    slices.Sum(slice => slice.EstimatedApiUsd),
                    slices.Sum(slice => slice.UnpricedEventCount),
                    slices);
            })
            .ToArray();
    }

    private static UsageSliceSummary BuildSlice(
        string model,
        string tier,
        IEnumerable<ObservedUsage> events)
    {
        var materialized = events.ToArray();
        var estimates = materialized
            .Select(item => ApiCostEstimator.Estimate(item.Model, item.ServiceTier, item.Usage))
            .ToArray();
        return new UsageSliceSummary(
            model,
            tier,
            Sum(materialized.Select(item => item.Usage)),
            estimates.Where(estimate => estimate.IsPriced).Sum(estimate => estimate.Usd),
            estimates.Count(estimate => !estimate.IsPriced));
    }

    private static TokenUsageBreakdown Sum(IEnumerable<TokenUsageBreakdown> source) =>
        source.Aggregate(TokenUsageBreakdown.Zero, (total, item) => total.Add(item));
}
