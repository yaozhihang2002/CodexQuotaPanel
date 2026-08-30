namespace CodexQuota.Domain;

public static class QuotaHistoryContinuity
{
    private static readonly TimeSpan MaximumTransientGap = TimeSpan.FromMinutes(3);
    private static readonly TimeSpan MaximumTransientRunDuration = TimeSpan.FromMinutes(3);
    private const double SameLevelTolerancePercent = 2d;
    private const double MinimumExcursionPercent = 10d;

    public static IReadOnlyList<QuotaHistoryPoint> RemoveTransientSourceSpikes(
        IReadOnlyList<QuotaHistoryPoint> points)
    {
        if (points.Count < 3) return points;

        var filtered = new List<QuotaHistoryPoint>(points.Count);
        foreach (var group in points
                     .GroupBy(point => (point.WindowId, point.WindowMinutes)))
        {
            var ordered = group.OrderBy(point => point.ObservedAt).ToArray();
            if (ordered.Length < 3)
            {
                filtered.AddRange(ordered);
                continue;
            }

            var start = 0;
            while (start < ordered.Length)
            {
                var end = start;
                while (end + 1 < ordered.Length &&
                       Math.Abs(ordered[end + 1].RemainingPercent - ordered[start].RemainingPercent) <=
                       SameLevelTolerancePercent)
                    end++;

                var previous = start > 0 ? ordered[start - 1] : null;
                var next = end + 1 < ordered.Length ? ordered[end + 1] : null;
                var shortRun = ordered[end].ObservedAt - ordered[start].ObservedAt <= MaximumTransientRunDuration;
                var tightlyBounded = previous is not null && next is not null &&
                                     ordered[start].ObservedAt - previous.ObservedAt <= MaximumTransientGap &&
                                     next.ObservedAt - ordered[end].ObservedAt <= MaximumTransientGap;
                var neighborsAgree = previous is not null && next is not null &&
                                     Math.Abs(previous.RemainingPercent - next.RemainingPercent) <=
                                     SameLevelTolerancePercent;
                var neighborAverage = previous is not null && next is not null
                    ? (previous.RemainingPercent + next.RemainingPercent) / 2d
                    : ordered[start].RemainingPercent;
                var upwardExcursion = ordered[start].RemainingPercent - neighborAverage >=
                                      MinimumExcursionPercent;
                if (!(shortRun && tightlyBounded && neighborsAgree && upwardExcursion))
                    for (var index = start; index <= end; index++) filtered.Add(ordered[index]);

                start = end + 1;
            }
        }

        return filtered.OrderBy(point => point.ObservedAt).ThenBy(point => point.WindowMinutes).ToArray();
    }
}
