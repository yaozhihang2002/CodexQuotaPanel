using CodexQuota.Domain;

namespace CodexQuota.Application;

public static class QuotaAlertPolicy
{
    // Remember every quota window until its reset, not just the last lowest one.
    // Small reset-time corrections and severity changes do not start a new cycle.
    public static (QuotaWindow? Alert, AppSettings Settings) Evaluate(AppSettings settings,
        OfficialQuotaSnapshot? snapshot, DateTimeOffset now, bool alertOpen)
    {
        if (!settings.AlertsEnabled || snapshot is null || snapshot.IsStale ||
            now - snapshot.ObservedAt > TimeSpan.FromMinutes(2) || snapshot.ObservedAt > now.AddMinutes(1))
            return (null, settings);
        var low = snapshot.VisibleWindows.Where(w => w.ResetsAt > now && double.IsFinite(w.RemainingPercent) &&
            w.ClampedRemainingPercent <= settings.WarningThreshold).OrderBy(w => w.ClampedRemainingPercent).ToArray();
        var receipts = settings.AlertedUntil ?? [];
        var pending = low.Where(w => !receipts.TryGetValue(w.WindowMinutes, out var until) || until <= now).ToArray();
        if (pending.Length == 0) return (null, settings);
        var updated = receipts.Where(p => p.Value > now).ToDictionary(p => p.Key, p => p.Value);
        foreach (var window in low) updated[window.WindowMinutes] = window.ResetsAt!.Value;
        // While a warning remains open, consume new warnings instead of queuing them.
        return (alertOpen ? null : pending[0], settings with { AlertedUntil = updated });
    }
}
