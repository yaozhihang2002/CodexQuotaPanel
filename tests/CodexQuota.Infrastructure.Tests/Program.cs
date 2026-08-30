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

    var trustedQuotaPath = Path.Combine(root, "trusted-quota.json");
    var trustedQuotaStore = new JsonTrustedQuotaSnapshotStore(trustedQuotaPath);
    var trustedQuota = new OfficialQuotaSnapshot(DateTimeOffset.UtcNow,
        [new QuotaWindow("7d", 10_080, 13d, DateTimeOffset.UtcNow.AddDays(5))],
        Source: "App Server", PlanType: "pro");
    await trustedQuotaStore.WriteAsync(trustedQuota, CancellationToken.None);
    Check.SnapshotEqual(trustedQuota, await trustedQuotaStore.ReadAsync(CancellationToken.None),
        "trusted quota cache round trip");
    await trustedQuotaStore.WriteAsync(trustedQuota with
    {
        ObservedAt = trustedQuota.ObservedAt.AddMinutes(1),
        Windows = [trustedQuota.Windows[0] with { RemainingPercent = 12d }]
    }, CancellationToken.None);
    await File.WriteAllTextAsync(trustedQuotaPath, "{broken", CancellationToken.None);
    Check.SnapshotEqual(trustedQuota, await trustedQuotaStore.ReadAsync(CancellationToken.None),
        "trusted quota cache backup recovery");
    await File.WriteAllTextAsync(trustedQuotaPath, "{broken-again", CancellationToken.None);
    Check.SnapshotEqual(trustedQuota, await trustedQuotaStore.ReadAsync(CancellationToken.None),
        "trusted quota backup remains valid after recovery");

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
    var legacyState = Path.Combine(streamingRoot, "legacy-state.jsonl");
    await File.WriteAllTextAsync(emptyState, string.Empty);
    await File.WriteAllTextAsync(corruptState, "{broken");
    await File.WriteAllTextAsync(legacyState,
        """{"Key":"legacy.jsonl","Cursor":{"Length":1,"LastWriteTicks":0,"Model":"unknown","Tier":"default","TierExplicit":false,"Previous":null}}""");
    Check.True(!JsonlUsageEventSource.HasUsableCursorState(emptyState),
        "empty cursor state is rejected");
    Check.True(!JsonlUsageEventSource.HasUsableCursorState(corruptState),
        "corrupt cursor state is rejected");
    Check.True(!JsonlUsageEventSource.HasUsableCursorState(legacyState),
        "older parser cursor triggers one-time reindex");
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

    var lateModelRoot = Path.Combine(root, "late-model");
    var lateModelSessions = Path.Combine(lateModelRoot, "sessions");
    Directory.CreateDirectory(lateModelSessions);
    var lateModelTranscript = Path.Combine(lateModelSessions, "late-model.jsonl");
    await File.WriteAllLinesAsync(lateModelTranscript,
    [
        TokenLineWithLimitName("2026-08-26T05:01:00Z", "model-a", 100, 80, 20,
            "GPT-5.3-Codex-Spark"),
        """{"timestamp":"2026-08-26T05:02:00Z","type":"turn_context","payload":{"model":"gpt-5.6-sol"}}""",
        TokenLineWithLimitName("2026-08-26T05:03:00Z", "model-b", 150, 120, 30,
            "GPT-5.3-Codex-Spark")
    ]);
    var lateModelEvents = new List<ObservedUsage>();
    await foreach (var item in new JsonlUsageEventSource(lateModelRoot).ReadFileAsync(lateModelTranscript))
        lateModelEvents.Add(item);
    Check.Equal(2, lateModelEvents.Count, "late model event count");
    Check.True(lateModelEvents.All(item => item.Model == "gpt-5.6-sol"),
        "later explicit model backfills earlier token events");

    var lateModelStreamingRoot = Path.Combine(root, "late-model-streaming");
    var lateModelStreamingSessions = Path.Combine(lateModelStreamingRoot, "sessions");
    Directory.CreateDirectory(lateModelStreamingSessions);
    var lateModelStreamingTranscript = Path.Combine(lateModelStreamingSessions, "late-model-streaming.jsonl");
    await File.WriteAllLinesAsync(lateModelStreamingTranscript,
    [
        TokenLine("2026-08-26T06:01:00Z", "stream-model-a", 100, 80, 20),
        TokenLine("2026-08-26T06:02:00Z", "stream-model-b", 150, 120, 30)
    ]);
    var lateModelStreamingState = Path.Combine(lateModelStreamingRoot, "usage-file-state.jsonl");
    var lateModelStreamingSource = new JsonlUsageEventSource(lateModelStreamingRoot, lateModelStreamingState);
    var unknownModelBatches = await CollectBatchesAsync(lateModelStreamingSource, 2);
    Check.True(unknownModelBatches.SelectMany(batch => batch).All(item => item.Model == "unknown"),
        "model remains unknown until evidence appears");
    await File.AppendAllTextAsync(lateModelStreamingTranscript, Environment.NewLine +
        """{"timestamp":"2026-08-26T06:03:00Z","type":"turn_context","payload":{"model":"gpt-5.6-terra"}}""" +
        Environment.NewLine + TokenLine("2026-08-26T06:04:00Z", "stream-model-c", 200, 160, 40));
    var replayedModelBatches = await CollectBatchesAsync(lateModelStreamingSource, 3);
    var replayedModelEvents = replayedModelBatches.SelectMany(batch => batch).ToArray();
    Check.Equal(3, replayedModelEvents.Length, "late model triggers bounded replay");
    Check.True(replayedModelEvents.All(item => item.Model == "gpt-5.6-terra"),
        "late model replay upgrades prior unknown events");

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

    var soonExpiry = DateTimeOffset.UtcNow.AddDays(1).ToUnixTimeSeconds();
    var lateExpiry = DateTimeOffset.UtcNow.AddDays(10).ToUnixTimeSeconds();
    using var appServerDocument = JsonDocument.Parse(JsonSerializer.Serialize(new
    {
        rateLimits = new
        {
            limitId = "codex", planType = "pro",
            secondary = new { usedPercent = 12, windowDurationMins = 10_080, resetsAt = 1_788_050_820 }
        },
        rateLimitResetCredits = new
        {
            availableCount = 2,
            credits = new[]
            {
                new { id = "late", status = "available", title = "Full reset", expiresAt = lateExpiry },
                new { id = "soon", status = "available", title = "Full reset", expiresAt = soonExpiry }
            }
        }
    }));
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

    var knownDuplicate = new ObservedUsage(
        new DateTimeOffset(2026, 8, 26, 7, 0, 0, TimeSpan.Zero),
        "gpt-5.6-sol", "priority", events[0].Usage, "merge-known-first", true);
    await store.AppendUsageAsync(knownDuplicate, CancellationToken.None);
    await store.AppendUsageAsync(knownDuplicate with
    {
        Model = "unknown", ServiceTier = "default", IsServiceTierExplicit = false
    }, CancellationToken.None);
    var merged = (await store.ReadUsageAsync(DateTimeOffset.MinValue, CancellationToken.None))
        .Single(item => item.Fingerprint == knownDuplicate.Fingerprint);
    Check.Equal("gpt-5.6-sol", merged.Model, "SQLite duplicate cannot downgrade a known model");
    Check.Equal("priority", merged.ServiceTier, "SQLite duplicate cannot downgrade an explicit tier");
    Check.True(merged.IsServiceTierExplicit, "SQLite duplicate preserves explicit tier provenance");

    var lateKnown = knownDuplicate with
    {
        Fingerprint = "merge-known-last", Model = "unknown", ServiceTier = "default",
        IsServiceTierExplicit = false
    };
    await store.AppendUsageAsync(lateKnown, CancellationToken.None);
    await store.AppendUsageAsync(lateKnown with
    {
        Model = "gpt-5.6-terra", ServiceTier = "priority", IsServiceTierExplicit = true
    }, CancellationToken.None);
    merged = (await store.ReadUsageAsync(DateTimeOffset.MinValue, CancellationToken.None))
        .Single(item => item.Fingerprint == lateKnown.Fingerprint);
    Check.Equal("gpt-5.6-terra", merged.Model, "SQLite duplicate upgrades an unknown model");
    Check.Equal("priority", merged.ServiceTier, "SQLite duplicate upgrades an inferred tier");
    Check.True(merged.IsServiceTierExplicit, "SQLite tier upgrade records explicit provenance");
    await store.ClearAsync(CancellationToken.None);
    Check.Equal(0, (await store.ReadQuotaAsync(DateTimeOffset.MinValue, CancellationToken.None)).Count, "SQLite quota clear");
    Check.Equal(0, (await store.ReadUsageAsync(DateTimeOffset.MinValue, CancellationToken.None)).Count, "SQLite usage clear");
}
finally
{
    if (Directory.Exists(root)) Directory.Delete(root, true);
}

Console.WriteLine("Infrastructure checks passed: 59");

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

static string TokenLineWithLimitName(
    string timestamp,
    string turn,
    long total,
    long input,
    long output,
    string limitName)
{
    using var document = JsonDocument.Parse(TokenLine(timestamp, turn, total, input, output));
    var root = document.RootElement;
    return JsonSerializer.Serialize(new
    {
        timestamp = root.GetProperty("timestamp").GetString(),
        type = "event_msg",
        payload = new
        {
            type = "token_count",
            turn_id = turn,
            rate_limits = new { limit_name = limitName },
            info = root.GetProperty("payload").GetProperty("info")
        }
    });
}

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
    public static void SnapshotEqual(
        OfficialQuotaSnapshot expected,
        OfficialQuotaSnapshot? actual,
        string name)
    {
        if (actual is null || expected.ObservedAt != actual.ObservedAt ||
            expected.IsStale != actual.IsStale || expected.Source != actual.Source ||
            expected.PlanType != actual.PlanType || expected.Windows.Count != actual.Windows.Count ||
            !expected.Windows.SequenceEqual(actual.Windows))
            throw new InvalidOperationException($"{name}: snapshot fields differ");
    }

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
