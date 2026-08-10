namespace CodexQuotaPanel;

internal readonly record struct ConsumptionRate(
    double PercentPerHour,
    double Intensity,
    int SampleIntervals)
{
    public static ConsumptionRate Idle => new(0d, 0d, 0);
    public FlameActivityLevel Activity => FlameActivity.Classify(Intensity);
}

internal readonly record struct RunwayRateEstimate(
    double PercentPerHour,
    double ShortPercentPerHour,
    double LongPercentPerHour,
    double Confidence,
    int SampleIntervals,
    double ObservedMinutes)
{
    public static RunwayRateEstimate Empty => new(0d, 0d, 0d, 0d, 0, 0d);
}

internal static class QuotaConsumptionRate
{
    private const int LookbackMinutes = 90;
    private const int RunwayLongLookbackMinutes = 6 * 60;
    private const int MaximumContinuousGapMinutes = 45;

    /// <summary>
    /// Estimates recent quota burn without treating a quota reset as usage.
    /// The result is normalized for animation only; it is never persisted.
    /// </summary>
    internal static ConsumptionRate Evaluate(
        IReadOnlyList<QuotaHistoryPoint> history,
        DateTimeOffset? now = null)
    {
        if (history.Count < 2)
            return ConsumptionRate.Idle;

        var currentMinute = (now ?? DateTimeOffset.UtcNow).ToUniversalTime().ToUnixTimeSeconds() / 60;
        var cutoff = currentMinute - LookbackMinutes;
        var bestRate = 0d;
        var bestIntervals = 0;
        var bestNewestMinute = long.MinValue;

        foreach (var group in history
                     .Where(point => point.UtcMinute >= cutoff && point.UtcMinute <= currentMinute + 1)
                     .GroupBy(point => (point.Slot, point.WindowMinutes)))
        {
            var (groupRate, intervals, groupNewestMinute) = EvaluatePoints(group, currentMinute);
            if (intervals == 0) continue;
            if (groupRate > bestRate)
            {
                bestRate = groupRate;
                bestIntervals = intervals;
                bestNewestMinute = groupNewestMinute;
            }
        }

        if (bestIntervals == 0)
            return ConsumptionRate.Idle;

        return CreateRate(bestRate, bestIntervals, bestNewestMinute, currentMinute);
    }

    /// <summary>
    /// Returns the recent burn rate for one concrete quota window. Forecasting
    /// must never borrow a faster rate from the other ring.
    /// </summary>
    internal static ConsumptionRate EvaluateWindow(
        IReadOnlyList<QuotaHistoryPoint> history,
        int slot,
        int windowMinutes,
        DateTimeOffset? now = null)
    {
        var currentMinute = (now ?? DateTimeOffset.UtcNow).ToUniversalTime().ToUnixTimeSeconds() / 60;
        var cutoff = currentMinute - LookbackMinutes;
        var points = history.Where(point =>
            point.Slot == slot &&
            point.WindowMinutes == windowMinutes &&
            point.UtcMinute >= cutoff &&
            point.UtcMinute <= currentMinute + 1);
        var (rate, intervals, newestMinute) = EvaluatePoints(points, currentMinute);
        if (intervals == 0) return ConsumptionRate.Idle;
        return CreateRate(rate, intervals, newestMinute, currentMinute);
    }

    /// <summary>
    /// Estimates a conservative runway rate. Unlike the flame calculation,
    /// unchanged samples count as idle time. A short average remains responsive,
    /// while a sufficiently observed six-hour average prevents one burst from
    /// dominating the remaining-time estimate.
    /// </summary>
    internal static RunwayRateEstimate EvaluateRunwayWindow(
        IReadOnlyList<QuotaHistoryPoint> history,
        int slot,
        int windowMinutes,
        DateTimeOffset? now = null)
    {
        var currentMinute = (now ?? DateTimeOffset.UtcNow).ToUniversalTime().ToUnixTimeSeconds() / 60;
        var points = history
            .Where(point =>
                point.Slot == slot &&
                point.WindowMinutes == windowMinutes &&
                point.UtcMinute >= currentMinute - RunwayLongLookbackMinutes &&
                point.UtcMinute <= currentMinute + 1)
            .OrderBy(point => point.UtcMinute)
            .ToArray();
        if (points.Length < 2) return RunwayRateEstimate.Empty;

        var shortRate = EvaluateIdleInclusive(points, currentMinute - LookbackMinutes);
        var longRate = EvaluateIdleInclusive(points, currentMinute - RunwayLongLookbackMinutes);
        if (shortRate.Intervals == 0 || shortRate.ElapsedMinutes < 10d)
            return RunwayRateEstimate.Empty;

        var longCoverage = Math.Clamp(longRate.ElapsedMinutes / RunwayLongLookbackMinutes, 0d, 1d);
        var hasUsefulLongView = longRate.Intervals >= 4 && longRate.ElapsedMinutes >= 120d;
        var longWeight = hasUsefulLongView ? 0.50d + 0.20d * longCoverage : 0d;
        var blendedRate = shortRate.Rate * (1d - longWeight) + longRate.Rate * longWeight;

        var spanScore = Math.Clamp(longRate.ElapsedMinutes / 180d, 0d, 1d);
        var sampleScore = Math.Clamp(longRate.Intervals / 8d, 0d, 1d);
        var agreementBase = Math.Max(0.05d, Math.Max(shortRate.Rate, longRate.Rate));
        var agreementScore = 1d - Math.Clamp(Math.Abs(shortRate.Rate - longRate.Rate) / agreementBase, 0d, 1d);
        var confidence = 0.25d + 0.35d * spanScore + 0.25d * sampleScore + 0.15d * agreementScore;

        return new RunwayRateEstimate(
            Math.Round(blendedRate, 2),
            Math.Round(shortRate.Rate, 2),
            Math.Round(longRate.Rate, 2),
            Math.Clamp(confidence, 0d, 1d),
            longRate.Intervals,
            longRate.ElapsedMinutes);
    }

    private static (double Rate, int Intervals, double ElapsedMinutes) EvaluateIdleInclusive(
        IReadOnlyList<QuotaHistoryPoint> points,
        long cutoffMinute)
    {
        var consumed = 0d;
        var elapsedTotal = 0d;
        var intervals = 0;
        for (var index = 1; index < points.Count; index++)
        {
            var previous = points[index - 1];
            var current = points[index];
            if (current.UtcMinute < cutoffMinute) continue;

            var elapsed = current.UtcMinute - previous.UtcMinute;
            if (elapsed <= 0 || elapsed > MaximumContinuousGapMinutes) continue;

            var drop = previous.RemainingPercent - current.RemainingPercent;
            // A rising balance is a reset or correction. Do not count that edge,
            // and do not use its elapsed time as artificial idle time.
            if (drop < -0.2d || drop > 50d) continue;

            consumed += Math.Max(0d, drop);
            elapsedTotal += elapsed;
            intervals++;
        }

        return intervals == 0 || elapsedTotal <= 0d
            ? (0d, 0, 0d)
            : (consumed * 60d / elapsedTotal, intervals, elapsedTotal);
    }

    private static (double Rate, int Intervals, long NewestMinute) EvaluatePoints(
        IEnumerable<QuotaHistoryPoint> source,
        long currentMinute)
    {
        var points = source.OrderBy(point => point.UtcMinute).ToArray();
        if (points.Length < 2) return (0d, 0, long.MinValue);

        var weightedRate = 0d;
        var weightTotal = 0d;
        var intervals = 0;
        var newestMinute = long.MinValue;
        for (var index = 1; index < points.Length; index++)
        {
            var previous = points[index - 1];
            var current = points[index];
            var elapsedMinutes = current.UtcMinute - previous.UtcMinute;
            if (elapsedMinutes <= 0 || elapsedMinutes > LookbackMinutes) continue;

            // Remaining quota rises on a reset. Ignore that edge instead of
            // forecasting an extreme burst of consumption.
            var drop = previous.RemainingPercent - current.RemainingPercent;
            if (drop <= 0d || drop > 50d) continue;

            var rate = drop * 60d / elapsedMinutes;
            var age = Math.Max(0d, currentMinute - current.UtcMinute);
            var recencyWeight = 0.25d + 0.75d * (1d - Math.Min(age, LookbackMinutes) / LookbackMinutes);
            weightedRate += rate * recencyWeight;
            weightTotal += recencyWeight;
            intervals++;
            newestMinute = Math.Max(newestMinute, current.UtcMinute);
        }

        return intervals == 0 || weightTotal <= 0d
            ? (0d, 0, long.MinValue)
            : (weightedRate / weightTotal, intervals, newestMinute);
    }

    private static ConsumptionRate CreateRate(
        double rate,
        int intervals,
        long newestMinute,
        long currentMinute)
    {
        var intensity = 1d - Math.Exp(-rate / 7d);
        if (newestMinute != long.MinValue)
        {
            var staleMinutes = Math.Max(0d, currentMinute - newestMinute);
            var freshness = 1d - Math.Clamp((staleMinutes - 20d) / 70d, 0d, 1d);
            intensity *= freshness;
        }

        return new ConsumptionRate(
            Math.Round(rate, 2),
            Math.Clamp(intensity, 0d, 1d),
            intervals);
    }
}
