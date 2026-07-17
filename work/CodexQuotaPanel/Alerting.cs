using Microsoft.Win32;
using System.Text.Json;

namespace CodexQuotaPanel;

internal enum QuotaAlertLevel
{
    None = 0,
    Warning = 1,
    Critical = 2
}

internal sealed record QuotaAlertDecision(
    QuotaAlertLevel Level,
    double RemainingPercent,
    int? WindowMinutes,
    DateTimeOffset? ResetsAt,
    bool IsBlocked,
    string BucketKey,
    string CycleKey);

internal sealed record AlertStateEntry(string BucketKey, string CycleKey, int Level);

internal sealed class AlertDedupState
{
    private readonly List<AlertStateEntry> _entries;

    public bool Dirty { get; private set; }

    public AlertDedupState(IEnumerable<AlertStateEntry>? entries = null)
    {
        _entries = entries?
            .Where(entry => !string.IsNullOrWhiteSpace(entry.BucketKey) && !string.IsNullOrWhiteSpace(entry.CycleKey))
            .TakeLast(8)
            .ToList() ?? [];
    }

    public bool TryMarkNotified(string bucketKey, string cycleKey, QuotaAlertLevel level)
    {
        var index = _entries.FindIndex(entry => entry.BucketKey == bucketKey);
        if (index >= 0)
        {
            var existing = _entries[index];
            if (existing.CycleKey == cycleKey && existing.Level >= (int)level) return false;
            _entries[index] = new AlertStateEntry(bucketKey, cycleKey, (int)level);
        }
        else
        {
            _entries.Add(new AlertStateEntry(bucketKey, cycleKey, (int)level));
            while (_entries.Count > 8) _entries.RemoveAt(0);
        }
        Dirty = true;
        return true;
    }

    public void Reset(string bucketKey)
    {
        if (_entries.RemoveAll(entry => entry.BucketKey == bucketKey) <= 0) return;
        Dirty = true;
    }

    public void SuppressCycle(string bucketKey, string cycleKey)
    {
        var index = _entries.FindIndex(entry => entry.BucketKey == bucketKey);
        var suppressed = new AlertStateEntry(bucketKey, cycleKey, (int)QuotaAlertLevel.Critical);
        if (index >= 0)
        {
            if (_entries[index] == suppressed) return;
            _entries[index] = suppressed;
        }
        else
        {
            _entries.Add(suppressed);
            while (_entries.Count > 8) _entries.RemoveAt(0);
        }
        Dirty = true;
    }

    public IReadOnlyList<AlertStateEntry> Snapshot() => _entries.ToArray();

    public void MarkClean() => Dirty = false;
}

internal static class QuotaAlertEvaluator
{
    public static QuotaAlertDecision? Evaluate(
        QuotaSnapshot snapshot,
        PanelPreferences preferences,
        DateTimeOffset now,
        AlertDedupState state)
    {
        preferences = PanelPreferenceManager.Normalize(preferences);
        var tightest = snapshot.Buckets.OrderBy(bucket => bucket.RemainingPercent).FirstOrDefault();
        if (tightest is null) return null;

        var bucketKey = $"{snapshot.LimitId ?? "codex"}:{tightest.WindowMinutes?.ToString() ?? "unknown"}";
        var remaining = tightest.RemainingPercent;
        var level = snapshot.IsBlocked || remaining <= preferences.CriticalThreshold
            ? QuotaAlertLevel.Critical
            : remaining <= preferences.WarningThreshold
                ? QuotaAlertLevel.Warning
                : QuotaAlertLevel.None;

        if (level == QuotaAlertLevel.None)
        {
            state.Reset(bucketKey);
            return null;
        }
        if (!preferences.AlertsEnabled || IsQuietTime(now, preferences)) return null;

        // A reset timestamp is the natural notification cycle. If the source
        // omits it, use a coarse six-hour bucket so a permanently low quota is
        // not silenced forever while still avoiding repeated popups.
        var cycleKey = tightest.ResetsAt?.ToUnixTimeSeconds().ToString() ??
            $"unknown:{now.ToUniversalTime().ToUnixTimeSeconds() / 21600}";
        if (!state.TryMarkNotified(bucketKey, cycleKey, level)) return null;
        return new QuotaAlertDecision(
            level,
            remaining,
            tightest.WindowMinutes,
            tightest.ResetsAt,
            snapshot.IsBlocked,
            bucketKey,
            cycleKey);
    }

    internal static bool IsQuietTime(DateTimeOffset now, PanelPreferences preferences)
    {
        if (!preferences.QuietHoursEnabled) return false;
        var local = now.ToLocalTime();
        var minute = local.Hour * 60 + local.Minute;
        var start = preferences.QuietStartMinutes;
        var end = preferences.QuietEndMinutes;
        // Equal endpoints are invalid in the UI. Treat corrupted/migrated
        // preferences as disabled instead of making alerts silent all day.
        if (start == end) return false;
        return start < end
            ? minute >= start && minute < end
            : minute >= start || minute < end;
    }
}

internal static class AlertStateStore
{
    private const string PreferencesKey = @"Software\CodexQuotaPanel";
    private const string ValueName = "AlertDedupState";

    public static AlertDedupState Load()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PreferencesKey, writable: false);
            var json = key?.GetValue(ValueName) as string;
            if (string.IsNullOrWhiteSpace(json)) return new AlertDedupState();
            var entries = JsonSerializer.Deserialize<List<AlertStateEntry>>(json);
            return new AlertDedupState(entries);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or ArgumentException)
        {
            return new AlertDedupState();
        }
    }

    public static void Save(AlertDedupState state)
    {
        if (!state.Dirty) return;
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(PreferencesKey, writable: true);
            key.SetValue(ValueName, JsonSerializer.Serialize(state.Snapshot()), RegistryValueKind.String);
            state.MarkClean();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or ArgumentException)
        {
        }
    }
}
