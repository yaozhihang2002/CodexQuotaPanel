namespace CodexQuotaPanel;

internal readonly record struct ConsumptionRate(
    double PercentPerHour,
    double Intensity,
    int SampleIntervals)
{
    public static ConsumptionRate Idle => new(0d, 0d, 0);
    public FlameActivityLevel Activity => FlameActivity.Classify(Intensity);
}

internal static class QuotaConsumptionRate
{
    private const int LookbackMinutes = 90;

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
