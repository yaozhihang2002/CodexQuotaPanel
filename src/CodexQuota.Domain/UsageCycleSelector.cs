namespace CodexQuota.Domain;

public static class UsageCycleSelector
{
    /// <summary>
    /// Selects the exact quota cycle as a half-open interval: [start, end).
    /// The local calendar date is only applied later when the selected events
    /// are grouped for display.
    /// </summary>
    public static IReadOnlyList<ObservedUsage> Select(
        IEnumerable<ObservedUsage> usage,
        DateTimeOffset? cycleStart,
        DateTimeOffset? cycleEnd)
    {
        ArgumentNullException.ThrowIfNull(usage);
        return usage
            .Where(item => cycleStart is null || item.ObservedAt >= cycleStart)
            .Where(item => cycleEnd is null || item.ObservedAt < cycleEnd)
            .ToArray();
    }
}
