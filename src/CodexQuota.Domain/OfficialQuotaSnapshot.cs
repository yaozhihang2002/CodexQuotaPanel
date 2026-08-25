namespace CodexQuota.Domain;

public sealed record QuotaWindow(
    string Id,
    int WindowMinutes,
    double RemainingPercent,
    DateTimeOffset? ResetsAt)
{
    public double ClampedRemainingPercent => Math.Clamp(RemainingPercent, 0d, 100d);
}

public sealed record OfficialQuotaSnapshot(
    DateTimeOffset ObservedAt,
    IReadOnlyList<QuotaWindow> Windows,
    bool IsStale = false)
{
    public IReadOnlyList<QuotaWindow> VisibleWindows => Windows
        .Where(window => window.WindowMinutes > 0)
        .OrderBy(window => window.WindowMinutes)
        .ToArray();
}
