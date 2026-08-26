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
    await settings.WriteAsync(AppSettings.Default with
    {
        OrbSize = 140,
        Theme = AppTheme.Light,
        OrbX = -640,
        OrbY = 318,
        OrbDisplayId = "-1920,0,1920,1080",
        DashboardX = -1120,
        DashboardY = 140,
        DashboardDisplayId = "-1920,0,1920,1080"
    }, CancellationToken.None);
    var readSettings = await settings.ReadAsync(CancellationToken.None);
    Check.Equal(140, readSettings?.OrbSize, "settings round trip size");
    Check.Equal(AppTheme.Light, readSettings?.Theme, "settings round trip theme");
    Check.Equal(-640d, readSettings?.OrbX, "settings round trip negative display X");
    Check.Equal(318d, readSettings?.OrbY, "settings round trip Y");
    Check.Equal("-1920,0,1920,1080", readSettings?.OrbDisplayId, "settings round trip display identity");
    Check.Equal(-1120d, readSettings?.DashboardX, "settings round trip dashboard X");
    Check.Equal(140d, readSettings?.DashboardY, "settings round trip dashboard Y");
    Check.Equal("-1920,0,1920,1080", readSettings?.DashboardDisplayId,
        "settings round trip dashboard display identity");

    await settings.WriteAsync(readSettings! with { OrbSize = 128 }, CancellationToken.None);
    await File.WriteAllTextAsync(settingsPath, "{broken", CancellationToken.None);
    var recoveredSettings = await settings.ReadAsync(CancellationToken.None);
    Check.Equal(140, recoveredSettings?.OrbSize, "settings backup recovery");

    var legacyPath = Path.Combine(root, "preferences.json");
    var migratedPath = Path.Combine(root, "settings-migrated.json");
    await File.WriteAllTextAsync(legacyPath,
        """{"Schema":"codex-quota-panel.preferences","SchemaVersion":1,"OrbSize":146,"OrbOpacityPercent":62,"OrbBackgroundColorArgb":-16777216,"OuterColorArgb":-9763664,"InnerColorArgb":-8469249,"ThemeMode":2,"Language":1,"OrbX":420,"OrbY":220,"ConsumptionFlameStyle":2}""");
    var migrationStore = new JsonSettingsStore(migratedPath, legacyPath);
    var migrated = await migrationStore.ReadAsync(CancellationToken.None);
    Check.Equal(146, migrated?.OrbSize, "legacy size migration");
    Check.Equal(62, migrated?.OrbOpacityPercent, "legacy opacity migration");
    Check.Equal(AppTheme.Light, migrated?.Theme, "legacy theme migration");
    Check.Equal(AppLanguage.English, migrated?.Language, "legacy language migration");
    Check.Equal(ConsumptionFeedbackStyle.Pixel, migrated?.ConsumptionFeedbackStyle,
        "legacy flame migration");
    Check.True(File.Exists(migratedPath), "migrated settings persisted");

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

    var streamingRoot = Path.Combine(root, "streaming");
    var streamingSessions = Path.Combine(streamingRoot, "sessions");
    Directory.CreateDirectory(streamingSessions);
    var streamingTranscript = Path.Combine(streamingSessions, "large.jsonl");
    var streamingLines = new List<string>
    {
        """{"timestamp":"2026-08-26T00:00:00Z","type":"event_msg","payload":{"type":"thread_settings_applied","thread_settings":{"model":"gpt-5.6-sol","service_tier":"default"}}}"""
    };
    var streamingStart = new DateTimeOffset(2026, 8, 26, 0, 0, 0, TimeSpan.Zero);
    for (var index = 0; index < 600; index++)
        streamingLines.Add(TokenLine(streamingStart.AddSeconds(index + 1).ToString("O"), $"stream-{index}",
            (index + 1) * 50L, (index + 1) * 40L, (index + 1) * 10L));
    await File.WriteAllLinesAsync(streamingTranscript, streamingLines);
    var streamingState = Path.Combine(streamingRoot, "usage-file-state.jsonl");
    var emptyState = Path.Combine(streamingRoot, "empty-state.jsonl");
    var corruptState = Path.Combine(streamingRoot, "corrupt-state.jsonl");
    await File.WriteAllTextAsync(emptyState, string.Empty);
    await File.WriteAllTextAsync(corruptState, "{broken");
    Check.True(!JsonlUsageEventSource.HasUsableCursorState(emptyState),
        "empty cursor state is rejected");
    Check.True(!JsonlUsageEventSource.HasUsableCursorState(corruptState),
        "corrupt cursor state is rejected");
    var streamingSource = new JsonlUsageEventSource(streamingRoot, streamingState);
    var initialBatches = await CollectBatchesAsync(streamingSource, 600);
    Check.Equal(600, initialBatches.Sum(batch => batch.Count), "streaming initial event count");
    Check.True(initialBatches.All(batch => batch.Count <= 256), "streaming batches stay bounded");
    Check.True(streamingSource.InitialScanCompleted.IsCompletedSuccessfully,
        "streaming initial scan completion signal");
    await File.AppendAllTextAsync(streamingTranscript, Environment.NewLine +
        TokenLine(streamingStart.AddSeconds(601).ToString("O"), "stream-600", 30_050, 24_040, 6_010));
    var restartedStreamingSource = new JsonlUsageEventSource(streamingRoot, streamingState);
    var appendedBatches = await CollectBatchesAsync(restartedStreamingSource, 1);
    Check.Equal(1, appendedBatches.Sum(batch => batch.Count), "streaming restart reads only new event");
    var streamingStateText = await File.ReadAllTextAsync(streamingState);
    Check.True(JsonlUsageEventSource.HasUsableCursorState(streamingState),
        "persisted cursor state is usable");
    Check.True(!streamingStateText.Contains(root, StringComparison.OrdinalIgnoreCase),
        "streaming cursor cache excludes personal paths");

    var lateTierRoot = Path.Combine(root, "late-tier");
    var lateTierSessions = Path.Combine(lateTierRoot, "sessions");
    Directory.CreateDirectory(lateTierSessions);
    var lateTierTranscript = Path.Combine(lateTierSessions, "late.jsonl");
    await File.WriteAllLinesAsync(lateTierTranscript,
    [
        """{"timestamp":"2026-08-26T03:00:00Z","type":"turn_context","payload":{"model":"gpt-5.6-terra"}}""",
        TokenLine("2026-08-26T03:01:00Z", "late-a", 100, 80, 20),
        TokenLine("2026-08-26T03:02:00Z", "late-b", 150, 120, 30)
    ]);
    var lateTierSource = new JsonlUsageEventSource(lateTierRoot);
    var defaultTierBatch = await CollectBatchesAsync(lateTierSource, 2);
    Check.True(defaultTierBatch.SelectMany(batch => batch).All(item => item.ServiceTier == "default"),
        "streaming missing tier defaults safely");
    await File.AppendAllTextAsync(lateTierTranscript, Environment.NewLine +
        """{"timestamp":"2026-08-26T03:03:00Z","type":"event_msg","payload":{"type":"thread_settings_applied","thread_settings":{"model":"gpt-5.6-terra","service_tier":"fast"}}}""" +
        Environment.NewLine + TokenLine("2026-08-26T03:04:00Z", "late-c", 200, 160, 40));
    var backfilledBatches = await CollectBatchesAsync(lateTierSource, 3);
    var backfilledEvents = backfilledBatches.SelectMany(batch => batch).ToArray();
    Check.Equal(3, backfilledEvents.Length, "streaming late tier triggers bounded replay");
    Check.True(backfilledEvents.Take(2).All(item => item.ServiceTier == "fast" && !item.IsServiceTierExplicit),
        "streaming late tier backfills prior events");
    var finiteEvents = new List<ObservedUsage>();
    await foreach (var batch in new JsonlUsageEventSource(lateTierRoot)
                       .ScanExistingBatchesAsync(CancellationToken.None))
        finiteEvents.AddRange(batch);
    Check.Equal(3, finiteEvents.Count, "finite index worker scan completes");

    var quotaSource = new JsonlQuotaSource(root);
    var quota = await quotaSource.ReadAsync(CancellationToken.None);
    Check.Equal(1, quota?.VisibleWindows.Count, "single window detection");
    Check.Equal(62d, quota?.VisibleWindows[0].RemainingPercent, "quota remaining");

    var archived = Path.Combine(root, "archived_sessions");
    Directory.CreateDirectory(archived);
    var archivedTranscript = Path.Combine(archived, "archived.jsonl");
    await File.WriteAllTextAsync(archivedTranscript,
        """{"timestamp":"2026-08-26T02:04:00Z","type":"event_msg","payload":{"rate_limits":{"primary":{"used_percent":41,"window_minutes":10080,"resets_at":1788050820}}}}""");
    File.SetLastWriteTimeUtc(archivedTranscript, DateTime.UtcNow.AddMinutes(1));
    quota = await quotaSource.ReadAsync(CancellationToken.None);
    Check.Equal(59d, quota?.VisibleWindows[0].RemainingPercent, "archived session quota detection");

    var reverseTranscript = Path.Combine(archived, "reverse.jsonl");
    await File.WriteAllLinesAsync(reverseTranscript,
    [
        new string('x', 180_000),
        """{"timestamp":"2026-08-26T04:00:00Z","type":"event_msg","payload":{"rate_limits":{"primary":{"used_percent":45,"window_minutes":10080,"resets_at":1788050820}}}}""",
        """{"timestamp":"2026-08-26T04:05:00Z","type":"event_msg","payload":{"rate_limits":{"primary":{"used_percent":47,"window_minutes":10080,"resets_at":1788050820}}}}"""
    ]);
    File.SetLastWriteTimeUtc(reverseTranscript, DateTime.UtcNow.AddMinutes(2));
    quota = await quotaSource.ReadAsync(CancellationToken.None);
    Check.Equal(53d, quota?.VisibleWindows[0].RemainingPercent, "reverse quota scan returns newest event");

    using var appServerDocument = JsonDocument.Parse("""
        {"rateLimits":{"limitId":"codex","planType":"pro","secondary":{"usedPercent":12,"windowDurationMins":10080,"resetsAt":1788050820}},
         "rateLimitResetCredits":{"availableCount":2,"credits":[
           {"id":"late","status":"available","title":"Full reset","expiresAt":1789000000},
           {"id":"soon","status":"available","title":"Full reset","expiresAt":1788100000}]}}
        """);
    var appServerQuota = QuotaPayloadParser.ParseAppServerResult(appServerDocument.RootElement);
    Check.Equal("App Server", appServerQuota?.Source, "app-server source metadata");
    Check.Equal("pro", appServerQuota?.PlanType, "app-server plan metadata");
    Check.Equal("soon", appServerQuota?.SoonestAvailableResetCredit?.Id, "soonest reset credit");

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
    await store.ClearAsync(CancellationToken.None);
    Check.Equal(0, (await store.ReadQuotaAsync(DateTimeOffset.MinValue, CancellationToken.None)).Count, "SQLite quota clear");
    Check.Equal(0, (await store.ReadUsageAsync(DateTimeOffset.MinValue, CancellationToken.None)).Count, "SQLite usage clear");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}

Console.WriteLine("Infrastructure checks passed: 43");

if (args.Contains("--live", StringComparer.OrdinalIgnoreCase))
{
    var live = await new CodexAppServerQuotaSource().ReadAsync(CancellationToken.None);
    Check.True(live is { VisibleWindows.Count: > 0 }, "live app-server quota snapshot");
    Console.WriteLine("Live windows: " + string.Join(", ", live!.VisibleWindows.Select(window =>
        $"{window.WindowMinutes}m={window.ClampedRemainingPercent:0.##}%")));
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

static async Task<IReadOnlyList<IReadOnlyList<ObservedUsage>>> CollectBatchesAsync(
    JsonlUsageEventSource source,
    int expectedCount)
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    var result = new List<IReadOnlyList<ObservedUsage>>();
    var count = 0;
    await foreach (var batch in source.WatchBatchesAsync(timeout.Token))
    {
        result.Add(batch);
        count += batch.Count;
        if (count < expectedCount) continue;
        timeout.Cancel();
        break;
    }
    return result;
}

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
