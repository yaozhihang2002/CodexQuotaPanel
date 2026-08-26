using CodexQuota.Application;
using CodexQuota.Domain;
using CodexQuota.Infrastructure;
using System.Text.Json;

var overrideResolver = new CodexHomeResolver(
    key => key == "CODEX_HOME" ? Path.Combine(Path.GetTempPath(), "custom-codex") : null,
    () => Path.Combine(Path.GetTempPath(), "home"));
Check.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "custom-codex")),
    overrideResolver.Resolve(), "CODEX_HOME override");

var fallbackResolver = new CodexHomeResolver(_ => null, () => Path.Combine(Path.GetTempPath(), "home"));
Check.Equal(Path.GetFullPath(Path.Combine(Path.GetTempPath(), "home", ".codex")),
    fallbackResolver.Resolve(), "home fallback");

var root = Path.Combine(Path.GetTempPath(), "CodexQuotaPanel-vnext-tests", Guid.NewGuid().ToString("N"));
var settingsPath = Path.Combine(root, "settings.json");
var sessions = Path.Combine(root, "sessions", "2026", "08", "26");
var transcript = Path.Combine(sessions, "sample.jsonl");
var database = Path.Combine(root, "history.db");
Directory.CreateDirectory(sessions);

try
{
    var settings = new JsonSettingsStore(settingsPath);
    await settings.WriteAsync(AppSettings.Default with { OrbSize = 140, Theme = AppTheme.Light }, CancellationToken.None);
    var readSettings = await settings.ReadAsync(CancellationToken.None);
    Check.Equal(140, readSettings?.OrbSize, "settings round trip size");
    Check.Equal(AppTheme.Light, readSettings?.Theme, "settings round trip theme");

    var lines = new[]
    {
        """{"timestamp":"2026-08-26T01:00:00Z","type":"turn_context","payload":{"model":"gpt-5.6-terra"}}""",
        TokenLine("2026-08-26T01:01:00Z", "turn-a", 100, 80, 20),
        """{"timestamp":"2026-08-26T01:02:00Z","type":"event_msg","payload":{"type":"thread_settings_applied","thread_settings":{"model":"gpt-5.6-terra","service_tier":"fast"}}}""",
        TokenLine("2026-08-26T01:03:00Z", "turn-b", 150, 120, 30),
        """{"timestamp":"2026-08-26T01:04:00Z","type":"event_msg","payload":{"rate_limits":{"primary":{"used_percent":38,"window_minutes":10080,"resets_at":1788050820}}}}"""
    };
    await File.WriteAllLinesAsync(transcript, lines);

    var usageSource = new JsonlUsageEventSource(root);
    var events = new List<ObservedUsage>();
    await foreach (var item in usageSource.ReadFileAsync(transcript)) events.Add(item);
    Check.Equal(2, events.Count, "usage event count");
    Check.Equal("gpt-5.6-terra", events[0].Model, "model context");
    Check.Equal("fast", events[0].ServiceTier, "later explicit tier backfill");
    Check.True(!events[0].IsServiceTierExplicit, "backfilled event remains marked inferred");
    Check.Equal(50L, events[1].Usage.TotalTokens, "cumulative normalization");

    var quotaSource = new JsonlQuotaSource(root);
    var quota = await quotaSource.ReadAsync(CancellationToken.None);
    Check.Equal(1, quota?.VisibleWindows.Count, "single window detection");
    Check.Equal(62d, quota?.VisibleWindows[0].RemainingPercent, "quota remaining");

    var store = new SqliteUsageHistoryStore(database);
    await store.InitializeAsync(CancellationToken.None);
    await store.AppendQuotaAsync(quota!, CancellationToken.None);
    await store.AppendUsageAsync(events[0], CancellationToken.None);
    await store.AppendUsageAsync(events[0] with { IsServiceTierExplicit = true }, CancellationToken.None);
    var quotaHistory = await store.ReadQuotaAsync(new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);
    var usageHistory = await store.ReadUsageAsync(new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero), CancellationToken.None);
    Check.Equal(1, quotaHistory.Count, "SQLite quota round trip");
    Check.Equal(1, usageHistory.Count, "SQLite fingerprint deduplication");
    Check.True(usageHistory[0].IsServiceTierExplicit, "SQLite inference upgrade");
    Check.Equal(events[0] with { IsServiceTierExplicit = true }, usageHistory[0], "SQLite usage round trip");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}

Console.WriteLine("Infrastructure checks passed: 16");

if (args.Contains("--live", StringComparer.OrdinalIgnoreCase))
{
    var live = await new CodexAppServerQuotaSource().ReadAsync(CancellationToken.None);
    Check.True(live is { VisibleWindows.Count: > 0 }, "live app-server quota snapshot");
    Console.WriteLine("Live app-server check passed");
}
else
{
    Console.WriteLine("Live app-server check skipped; pass --live to enable");
}

static string TokenLine(string timestamp, string turn, long total, long input, long output) =>
    JsonSerializer.Serialize(new
    {
        timestamp,
        type = "event_msg",
        payload = new
        {
            type = "token_count",
            turn_id = turn,
            info = new
            {
                total_token_usage = new
                {
                    total_tokens = total,
                    input_tokens = input,
                    cached_input_tokens = 10,
                    output_tokens = output,
                    reasoning_output_tokens = 5
                },
                last_token_usage = new
                {
                    total_tokens = total == 100 ? 100 : 50,
                    input_tokens = total == 100 ? 80 : 40,
                    cached_input_tokens = 5,
                    output_tokens = total == 100 ? 20 : 10,
                    reasoning_output_tokens = 2
                }
            }
        }
    });

static class Check
{
    public static void Equal(double expected, double? actual, string name)
    {
        if (actual is null || Math.Abs(expected - actual.Value) > 0.000_001d)
            throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
    }

    public static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
    }

    public static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException($"{name}: expected true");
    }
}
