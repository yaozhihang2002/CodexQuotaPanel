namespace CodexQuotaPanel;

internal enum QuotaRunwayState
{
    InsufficientData,
    Sustainable,
    AtRisk,
    Exhausted
}

internal sealed record QuotaRunwayForecast(
    int Slot,
    int WindowMinutes,
    double RemainingPercent,
    double PercentPerHour,
    double ShortPercentPerHour,
    double LongPercentPerHour,
    double Confidence,
    int SampleIntervals,
    double ObservedMinutes,
    DateTimeOffset? ResetsAt,
    DateTimeOffset? ExhaustsAt,
    double? SustainablePercentPerHour,
    QuotaRunwayState State)
{
    internal TimeSpan? EstimatedRunway => ExhaustsAt is { } value
        ? value - DateTimeOffset.Now
        : null;
}

internal static class QuotaRunwayForecaster
{
    private const int MinimumIntervals = 2;
    private const double MinimumMeaningfulRate = 0.05d;
    private const double MinimumConfidence = 0.45d;

    internal static QuotaRunwayForecast? Evaluate(
        QuotaSnapshot snapshot,
        IReadOnlyList<QuotaHistoryPoint> history,
        DateTimeOffset? now = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var observed = now ?? DateTimeOffset.Now;
        var candidates = new List<QuotaRunwayForecast>(2);
        AddCandidate(candidates, snapshot.Primary, 0, history, observed);
        AddCandidate(candidates, snapshot.Secondary, 1, history, observed);
        if (candidates.Count == 0) return null;

        // Prefer a window forecast to run out before reset. Otherwise show the
        // shortest trustworthy runway, which is the most useful glance result.
        return candidates
            .OrderByDescending(candidate => candidate.State == QuotaRunwayState.Exhausted)
            .ThenByDescending(candidate => candidate.State == QuotaRunwayState.AtRisk)
            .ThenBy(candidate => candidate.ExhaustsAt ?? DateTimeOffset.MaxValue)
            .ThenBy(candidate => candidate.RemainingPercent)
            .First();
    }

    private static void AddCandidate(
        ICollection<QuotaRunwayForecast> candidates,
        LimitBucket? bucket,
        int slot,
        IReadOnlyList<QuotaHistoryPoint> history,
        DateTimeOffset now)
    {
        if (bucket?.WindowMinutes is not > 0) return;
        if (bucket.RemainingPercent <= 0.001d)
        {
            candidates.Add(new QuotaRunwayForecast(
                slot, bucket.WindowMinutes.Value, 0d, 0d, 0d, 0d, 1d, 0, 0d,
                bucket.ResetsAt, now, null, QuotaRunwayState.Exhausted));
            return;
        }

        var rate = QuotaConsumptionRate.EvaluateRunwayWindow(history, slot, bucket.WindowMinutes.Value, now);
        if (rate.SampleIntervals < MinimumIntervals ||
            rate.Confidence < MinimumConfidence ||
            rate.PercentPerHour < MinimumMeaningfulRate)
            return;

        var runwayHours = bucket.RemainingPercent / rate.PercentPerHour;
        if (!double.IsFinite(runwayHours) || runwayHours <= 0d) return;
        var exhaustsAt = now.AddHours(Math.Min(runwayHours, 24d * 45d));
        double? sustainableRate = null;
        var state = QuotaRunwayState.Sustainable;
        if (bucket.ResetsAt is { } reset && reset > now)
        {
            var resetHours = Math.Max(1d / 60d, (reset - now).TotalHours);
            sustainableRate = bucket.RemainingPercent / resetHours;
            // Require a wider safety margin when confidence is lower. A short
            // burst should not immediately turn a healthy balance into a warning.
            var riskMargin = 1.15d + (1d - rate.Confidence) * 0.35d;
            state = exhaustsAt < reset && rate.PercentPerHour > sustainableRate.Value * riskMargin
                ? QuotaRunwayState.AtRisk
                : QuotaRunwayState.Sustainable;
        }

        candidates.Add(new QuotaRunwayForecast(
            slot,
            bucket.WindowMinutes.Value,
            bucket.RemainingPercent,
            rate.PercentPerHour,
            rate.ShortPercentPerHour,
            rate.LongPercentPerHour,
            rate.Confidence,
            rate.SampleIntervals,
            rate.ObservedMinutes,
            bucket.ResetsAt,
            exhaustsAt,
            sustainableRate,
            state));
    }
}
