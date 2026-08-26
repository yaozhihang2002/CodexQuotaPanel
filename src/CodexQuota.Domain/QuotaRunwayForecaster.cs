namespace CodexQuota.Domain;

public static class QuotaRunwayForecaster
{
    private const int ShortLookbackMinutes = 90;
    private const int LongLookbackMinutes = 6 * 60;
    private const int MaximumContinuousGapMinutes = 45;
    private const double MinimumMeaningfulRate = 0.05d;

    public static UsageForecast? Evaluate(
        OfficialQuotaSnapshot snapshot,
        IReadOnlyList<QuotaHistoryPoint> history,
        DateTimeOffset? now = null)
    {
        var observed = now ?? snapshot.ObservedAt;
        return snapshot.VisibleWindows
            .Select(window => EvaluateWindow(window, history, observed))
            .Where(result => result is not null)
            .Cast<UsageForecast>()
            .OrderByDescending(result => result.State == ForecastState.Exhausted)
            .ThenByDescending(result => result.State == ForecastState.AtRisk)
            .ThenBy(result => result.ExhaustsAt ?? DateTimeOffset.MaxValue)
            .FirstOrDefault();
    }

    private static UsageForecast? EvaluateWindow(
        QuotaWindow window,
        IReadOnlyList<QuotaHistoryPoint> history,
        DateTimeOffset now)
    {
        if (window.ClampedRemainingPercent <= 0.001d)
            return new UsageForecast(window.Id, now, 0d, 0d, 0d, null,
                ForecastConfidence.High, ForecastState.Exhausted, 0, 0d);

        var points = history
            .Where(point => point.WindowId == window.Id &&
                            point.WindowMinutes == window.WindowMinutes &&
                            point.ObservedAt >= now.AddMinutes(-LongLookbackMinutes) &&
                            point.ObservedAt <= now.AddMinutes(1))
            .OrderBy(point => point.ObservedAt)
            .ToArray();
        if (points.Length < 2) return null;

        var shortRate = EvaluateIdleInclusive(points, now.AddMinutes(-ShortLookbackMinutes));
        var longRate = EvaluateIdleInclusive(points, now.AddMinutes(-LongLookbackMinutes));
        if (shortRate.Intervals < 2 || shortRate.ElapsedMinutes < 10d) return null;

        var longCoverage = Math.Clamp(longRate.ElapsedMinutes / LongLookbackMinutes, 0d, 1d);
        var hasUsefulLongView = longRate.Intervals >= 4 && longRate.ElapsedMinutes >= 120d;
        var longWeight = hasUsefulLongView ? 0.50d + 0.20d * longCoverage : 0d;
        var rate = shortRate.Rate * (1d - longWeight) + longRate.Rate * longWeight;
        if (rate < MinimumMeaningfulRate) return null;

        var spanScore = Math.Clamp(longRate.ElapsedMinutes / 180d, 0d, 1d);
        var sampleScore = Math.Clamp(longRate.Intervals / 8d, 0d, 1d);
        var agreementBase = Math.Max(0.05d, Math.Max(shortRate.Rate, longRate.Rate));
        var agreement = 1d - Math.Clamp(Math.Abs(shortRate.Rate - longRate.Rate) / agreementBase, 0d, 1d);
        var confidenceValue = Math.Clamp(0.25d + 0.35d * spanScore + 0.25d * sampleScore + 0.15d * agreement, 0d, 1d);
        var confidence = confidenceValue switch
        {
            >= 0.80d => ForecastConfidence.High,
            >= 0.55d => ForecastConfidence.Medium,
            _ => ForecastConfidence.Low
        };

        var exhaustsAt = now.AddHours(Math.Min(window.ClampedRemainingPercent / rate, 24d * 45d));
        double? sustainable = null;
        var state = ForecastState.Sustainable;
        if (window.ResetsAt is { } reset && reset > now)
        {
            sustainable = window.ClampedRemainingPercent / Math.Max(1d / 60d, (reset - now).TotalHours);
            var riskMargin = 1.15d + (1d - confidenceValue) * 0.35d;
            state = exhaustsAt < reset && rate > sustainable * riskMargin
                ? ForecastState.AtRisk
                : ForecastState.Sustainable;
        }

        return new UsageForecast(
            window.Id,
            exhaustsAt,
            Math.Round(rate, 2),
            Math.Round(shortRate.Rate, 2),
            Math.Round(longRate.Rate, 2),
            sustainable,
            confidence,
            state,
            longRate.Intervals,
            longRate.ElapsedMinutes);
    }

    private static RateSample EvaluateIdleInclusive(
        IReadOnlyList<QuotaHistoryPoint> points,
        DateTimeOffset cutoff)
    {
        var consumed = 0d;
        var elapsedTotal = 0d;
        var intervals = 0;
        for (var index = 1; index < points.Count; index++)
        {
            var previous = points[index - 1];
            var current = points[index];
            if (current.ObservedAt < cutoff) continue;
            var elapsed = (current.ObservedAt - previous.ObservedAt).TotalMinutes;
            if (elapsed <= 0d || elapsed > MaximumContinuousGapMinutes) continue;
            var drop = previous.RemainingPercent - current.RemainingPercent;
            if (drop < -0.2d || drop > 50d) continue;
            consumed += Math.Max(0d, drop);
            elapsedTotal += elapsed;
            intervals++;
        }

        return intervals == 0 || elapsedTotal <= 0d
            ? new RateSample(0d, 0, 0d)
            : new RateSample(consumed * 60d / elapsedTotal, intervals, elapsedTotal);
    }

    private readonly record struct RateSample(double Rate, int Intervals, double ElapsedMinutes);
}
