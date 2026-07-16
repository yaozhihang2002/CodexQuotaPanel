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
            var points = group.OrderBy(point => point.UtcMinute).ToArray();
            if (points.Length < 2)
                continue;

            var weightedRate = 0d;
            var weightTotal = 0d;
            var intervals = 0;
            var groupNewestMinute = long.MinValue;
            for (var index = 1; index < points.Length; index++)
            {
                var previous = points[index - 1];
                var current = points[index];
                var elapsedMinutes = current.UtcMinute - previous.UtcMinute;
                if (elapsedMinutes <= 0 || elapsedMinutes > LookbackMinutes)
                    continue;

                // Remaining quota rises on a reset. Ignore that edge instead of
                // rendering a reset as an extreme burst of consumption.
                var drop = previous.RemainingPercent - current.RemainingPercent;
                if (drop <= 0d || drop > 50d)
                    continue;

                var rate = drop * 60d / elapsedMinutes;
                var age = Math.Max(0d, currentMinute - current.UtcMinute);
                var recencyWeight = 0.25d + 0.75d * (1d - Math.Min(age, LookbackMinutes) / LookbackMinutes);
                weightedRate += rate * recencyWeight;
                weightTotal += recencyWeight;
                intervals++;
                groupNewestMinute = Math.Max(groupNewestMinute, current.UtcMinute);
            }

            if (intervals == 0 || weightTotal <= 0d)
                continue;

            var groupRate = weightedRate / weightTotal;
            if (groupRate > bestRate)
            {
                bestRate = groupRate;
                bestIntervals = intervals;
                bestNewestMinute = groupNewestMinute;
            }
        }

        if (bestIntervals == 0)
            return ConsumptionRate.Idle;

        // Ease into the lively range.  FlameActivity maps the normalized value
        // to frozen/cool/warm/hot/inferno presentation states without coupling
        // the history estimator to any one visual style.
        var intensity = 1d - Math.Exp(-bestRate / 7d);
        if (bestNewestMinute != long.MinValue)
        {
            var staleMinutes = Math.Max(0d, currentMinute - bestNewestMinute);
            var freshness = 1d - Math.Clamp((staleMinutes - 20d) / 70d, 0d, 1d);
            intensity *= freshness;
        }

        return new ConsumptionRate(
            Math.Round(bestRate, 2),
            Math.Clamp(intensity, 0d, 1d),
            bestIntervals);
    }
}
