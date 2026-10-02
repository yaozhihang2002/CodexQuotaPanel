namespace CodexQuota.Domain;

public static class QuotaResetDetector
{
    // Countdown expiry alone is not evidence of a reset. Require a newer live
    // observation, an elapsed old boundary, a new cycle, and recovered quota.
    public static IReadOnlyList<QuotaWindow> Detect(OfficialQuotaSnapshot? previous,
        OfficialQuotaSnapshot current)
    {
        if (previous is null || previous.IsStale || current.IsStale ||
            current.ObservedAt <= previous.ObservedAt || previous.PlanType != current.PlanType)
            return [];
        return current.VisibleWindows.Where(next => previous.VisibleWindows.Any(old =>
            old.Id == next.Id && old.WindowMinutes == next.WindowMinutes &&
            old.ResetsAt is { } boundary && next.ResetsAt is { } newBoundary &&
            current.ObservedAt >= boundary &&
            newBoundary > current.ObservedAt && newBoundary > boundary &&
            next.ClampedRemainingPercent > old.ClampedRemainingPercent + 1)).ToArray();
    }
}
