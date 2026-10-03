using CodexQuota.Application;
using CodexQuota.Domain;

var original = AppState.Create();
var draft = new SettingsDraftSession(original);
var preview = draft.Preview(settings => settings with
{
    OrbSize = 146,
    Theme = AppTheme.Light,
    OrbOpacityPercent = 62
});

Check.True(preview.Settings.HasUnsavedChanges, "preview dirty flag");
Check.Equal(146, preview.Settings.Effective.OrbSize, "preview orb size");
Check.Equal(AppTheme.Light, preview.Theme.Requested, "preview theme");

var cancelled = draft.Cancel();
Check.Equal(original.Settings.Persisted, cancelled.Settings.Persisted, "cancel persisted settings");
Check.Equal(original.Settings.Effective, cancelled.Settings.Effective, "cancel effective settings");
Check.False(cancelled.Windows.IsSettingsVisible, "cancel closes settings");

var committedDraft = new SettingsDraftSession(original);
committedDraft.Preview(settings => settings with { OrbSize = 300, OrbOpacityPercent = 10 });
var committed = committedDraft.Commit();
Check.Equal(192, committed.Settings.Persisted.OrbSize, "size normalization");
Check.Equal(30, committed.Settings.Persisted.OrbOpacityPercent, "opacity normalization");
Check.True(committed.Settings.IsEditing, "save keeps editor open");
Check.False(committed.Settings.HasUnsavedChanges, "save clears dirty flag");

var now = DateTimeOffset.UtcNow;
var liveQuota = Snapshot(now, 63, "App Server");
var retainedQuota = Snapshot(now.AddMinutes(-1), 64, "App Server");
var localQuota = Snapshot(now.AddMinutes(-2), 81, "Local session");
var liveSelection = QuotaSnapshotContinuity.Select(liveQuota, retainedQuota, localQuota);
Check.Equal(QuotaSnapshotSelectionKind.Live, liveSelection.Kind, "live snapshot wins");
Check.Equal(liveQuota, liveSelection.Snapshot, "live snapshot selected");
var retainedSelection = QuotaSnapshotContinuity.Select(null, retainedQuota, localQuota);
Check.Equal(QuotaSnapshotSelectionKind.Retained, retainedSelection.Kind, "retained snapshot beats fallback");
Check.Equal(retainedQuota, retainedSelection.Snapshot, "retained snapshot stays stable");
Check.False(retainedSelection.IsFresh, "retained snapshot is not recorded again");
var localSelection = QuotaSnapshotContinuity.Select(null, null, localQuota);
Check.Equal(QuotaSnapshotSelectionKind.Local, localSelection.Kind, "local snapshot is first-start fallback");
Check.True(localSelection.IsFresh, "new local fallback may be recorded");
var emptySelection = QuotaSnapshotContinuity.Select(null, null, null);
Check.Equal(QuotaSnapshotSelectionKind.None, emptySelection.Kind, "missing sources stay empty");

Console.WriteLine("Application checks passed: 18");
Check.False(AppSettings.Default.AlertsEnabled, "alerts default off");
Check.False((new AppSettings { SchemaVersion = 6, AlertsEnabled = true }).Normalize().AlertsEnabled, "upgrade disables old default alerts");
var alertSettings = new AppSettings { AlertsEnabled = true };
Check.True(alertSettings.Normalize().AlertsEnabled, "explicit opt-in survives normalization");
var alertSnapshot = new OfficialQuotaSnapshot(now, [new("5h", 300, 15, now.AddHours(2)), new("7d", 10080, 16, now.AddDays(3))]);
var firstAlert = QuotaAlertPolicy.Evaluate(alertSettings, alertSnapshot, now, false);
Check.True(firstAlert.Alert is not null, "first warning appears");
Check.Equal(2, firstAlert.Settings.AlertedUntil.Count, "all low windows remembered");
var restored = System.Text.Json.JsonSerializer.Deserialize<AppSettings>(System.Text.Json.JsonSerializer.Serialize(firstAlert.Settings))!;
Check.True(QuotaAlertPolicy.Evaluate(restored, alertSnapshot, now, false).Alert is null, "restart and close cannot repeat warning");
var criticalSnapshot = alertSnapshot with { Windows = [new("changed-id", 300, 5, now.AddHours(2).AddSeconds(30)), new("7d", 10080, 4, now.AddDays(3))] };
Check.True(QuotaAlertPolicy.Evaluate(restored, criticalSnapshot, now, false).Alert is null, "severity, lowest window and timestamp jitter do not duplicate alerts");
Check.True(QuotaAlertPolicy.Evaluate(alertSettings, alertSnapshot with { IsStale = true }, now, false).Alert is null, "stale snapshot ignored");
Check.True(QuotaAlertPolicy.Evaluate(alertSettings, alertSnapshot, now.AddHours(8), false).Alert is null, "overnight old snapshot ignored");
var nextCycle = alertSnapshot with { ObservedAt = now.AddHours(3), Windows = [new("5h", 300, 8, now.AddHours(7))] };
Check.True(QuotaAlertPolicy.Evaluate(restored, nextCycle, now.AddHours(3), false).Alert is not null, "new reset cycle can warn once");
var whileOpen = QuotaAlertPolicy.Evaluate(restored, nextCycle, now.AddHours(3), true);
Check.True(whileOpen.Alert is null, "never stack windows");
Check.True(QuotaAlertPolicy.Evaluate(whileOpen.Settings, nextCycle, now.AddHours(3), false).Alert is null, "no queued popup after closing");
Check.True(QuotaAlertPolicy.Evaluate(alertSettings, alertSnapshot with { Windows = [new("5h", 300, 1, now.AddSeconds(-1))] }, now, false).Alert is null, "expired reset ignored");
Console.WriteLine("Quota alert checks passed: defaults, upgrade, opt-in, persistence, severity, alternating windows, stale data, reset and no backlog.");

static OfficialQuotaSnapshot Snapshot(DateTimeOffset observedAt, double remaining, string source) =>
    new(observedAt, [new QuotaWindow("7d", 10_080, remaining, observedAt.AddDays(5))], Source: source);

static class Check
{
    public static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
    }

    public static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException($"{name}: expected true");
    }

    public static void False(bool value, string name)
    {
        if (value) throw new InvalidOperationException($"{name}: expected false");
    }
}
