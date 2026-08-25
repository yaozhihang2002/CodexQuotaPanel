using CodexQuota.Application;

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

Console.WriteLine("Application checks passed: 10");

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
