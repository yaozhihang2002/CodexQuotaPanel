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
