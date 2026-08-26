namespace CodexQuota.Domain;

public sealed record QuotaWindow(
    string Id,
    int WindowMinutes,
    double RemainingPercent,
    DateTimeOffset? ResetsAt)
{
    public double ClampedRemainingPercent => Math.Clamp(RemainingPercent, 0d, 100d);
}

public sealed record ResetCredit(
    string Id,
    string Status,
    DateTimeOffset? ExpiresAt,
    string? Title = null,
    string? Description = null);

public sealed record OfficialQuotaSnapshot(
    DateTimeOffset ObservedAt,
    IReadOnlyList<QuotaWindow> Windows,
    bool IsStale = false,
    string Source = "Local session",
    string? PlanType = null,
    IReadOnlyList<ResetCredit>? ResetCredits = null)
{
    public IReadOnlyList<QuotaWindow> VisibleWindows => Windows
        .Where(window => window.WindowMinutes > 0)
        .OrderBy(window => window.WindowMinutes)
        .ToArray();

    public ResetCredit? SoonestAvailableResetCredit => ResetCredits?
        .Where(credit => credit.Status.Equals("available", StringComparison.OrdinalIgnoreCase) &&
                         credit.ExpiresAt > ObservedAt)
        .OrderBy(credit => credit.ExpiresAt)
        .FirstOrDefault();
}
