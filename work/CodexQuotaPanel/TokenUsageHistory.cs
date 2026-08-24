using System.Globalization;
using System.Text.Json;

namespace CodexQuotaPanel;

internal sealed record TokenUsageBreakdown(
    long TotalTokens,
    long InputTokens,
    long CachedInputTokens,
    long OutputTokens,
    long ReasoningOutputTokens)
{
    public static TokenUsageBreakdown Empty { get; } = new(0, 0, 0, 0, 0);

    public TokenUsageBreakdown Add(TokenUsageBreakdown other) => new(
        checked(TotalTokens + other.TotalTokens),
        checked(InputTokens + other.InputTokens),
        checked(CachedInputTokens + other.CachedInputTokens),
        checked(OutputTokens + other.OutputTokens),
        checked(ReasoningOutputTokens + other.ReasoningOutputTokens));
}

internal sealed record TokenUsageSlice(
    string Model,
    string Speed,
    TokenUsageBreakdown Usage,
    decimal EstimatedUsd,
    bool IsPriced)
{
    public string ModelDisplay => ApiCostEstimator.DisplayModel(Model);
    public string SpeedDisplay => ApiCostEstimator.DisplaySpeed(Speed);
}

internal sealed record DailyTokenUsage(
    DateOnly LocalDate,
    TokenUsageBreakdown Usage,
    IReadOnlyList<TokenUsageSlice> Slices)
{
    public DailyTokenUsage(DateOnly localDate, TokenUsageBreakdown usage)
        : this(localDate, usage, [])
    {
    }

    public decimal EstimatedUsd => Slices.Where(slice => slice.IsPriced).Sum(slice => slice.EstimatedUsd);
    public long UnpricedTokens => Slices.Where(slice => !slice.IsPriced).Sum(slice => slice.Usage.TotalTokens);
}

internal sealed record TokenCycleUsage(
    DateTimeOffset StartsAt,
    DateTimeOffset ResetsAt,
    int WindowMinutes,
    IReadOnlyList<DailyTokenUsage> Days,
    DateTimeOffset ScannedAt,
    int SourceFileCount)
{
    public TokenUsageBreakdown Total => Days.Aggregate(
        TokenUsageBreakdown.Empty,
        (sum, day) => sum.Add(day.Usage));

    public decimal EstimatedUsd => Days.Sum(day => day.EstimatedUsd);
    public long UnpricedTokens => Days.Sum(day => day.UnpricedTokens);

    public IReadOnlyList<TokenUsageSlice> Slices => Days
        .SelectMany(day => day.Slices)
        .GroupBy(slice => (slice.Model, slice.Speed, slice.IsPriced))
        .Select(group => new TokenUsageSlice(
            group.Key.Model,
            group.Key.Speed,
            group.Aggregate(TokenUsageBreakdown.Empty, (sum, slice) => sum.Add(slice.Usage)),
            group.Sum(slice => slice.EstimatedUsd),
            group.Key.IsPriced))
        .OrderByDescending(slice => slice.EstimatedUsd)
        .ThenByDescending(slice => slice.Usage.TotalTokens)
        .ToArray();
}

internal sealed record TokenCountSample(
    DateTimeOffset Timestamp,
    long CumulativeTotalTokens,
    TokenUsageBreakdown LastUsage);

internal static class TokenCountParser
{
    public static TokenCountSample? ParseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line) ||
            !line.Contains("\"token_count\"", StringComparison.Ordinal) ||
            !line.Contains("\"total_token_usage\"", StringComparison.Ordinal))
            return null;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!TryGet(root, "payload", out var payload) ||
                !string.Equals(ReadString(payload, "type"), "token_count", StringComparison.Ordinal) ||
                !TryGet(payload, "info", out var info) ||
                !TryGet(info, "total_token_usage", out var totalUsage) ||
                !TryGet(info, "last_token_usage", out var lastUsage))
                return null;

            var timestampText = ReadString(root, "timestamp");
            if (!DateTimeOffset.TryParse(timestampText, CultureInfo.InvariantCulture,
                    DateTimeStyles.AssumeUniversal, out var timestamp))
                return null;

            var cumulative = ReadLong(totalUsage, "total_tokens");
            var lastTotal = ReadLong(lastUsage, "total_tokens");
            if (cumulative is null || lastTotal is null || cumulative < 0 || lastTotal < 0)
                return null;

            return new TokenCountSample(
                timestamp,
                cumulative.Value,
                new TokenUsageBreakdown(
                    lastTotal.Value,
                    NonNegative(ReadLong(lastUsage, "input_tokens")),
                    NonNegative(ReadLong(lastUsage, "cached_input_tokens")),
                    NonNegative(ReadLong(lastUsage, "output_tokens")),
                    NonNegative(ReadLong(lastUsage, "reasoning_output_tokens"))));
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static long NonNegative(long? value) => Math.Max(0, value ?? 0);

    private static string? ReadString(JsonElement root, string name) =>
        TryGet(root, name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static long? ReadLong(JsonElement root, string name)
    {
        if (!TryGet(root, name, out var value)) return null;
        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var number)) return number;
        if (value.ValueKind == JsonValueKind.String &&
            long.TryParse(value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out number))
            return number;
        return null;
    }

    private static bool TryGet(JsonElement root, string name, out JsonElement value)
    {
        if (root.ValueKind == JsonValueKind.Object && root.TryGetProperty(name, out value)) return true;
        value = default;
        return false;
    }
}

internal static class TokenCycleSelector
{
    public static (DateTimeOffset StartsAt, DateTimeOffset ResetsAt, int WindowMinutes)? Select(
        QuotaSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var bucket = snapshot.Buckets
            .Where(item => item.WindowMinutes is > 0 && item.ResetsAt is not null)
            .OrderByDescending(item => item.WindowMinutes)
            .FirstOrDefault();
        if (bucket?.WindowMinutes is not > 0 || bucket.ResetsAt is not { } reset) return null;
        return (reset.AddMinutes(-bucket.WindowMinutes.Value), reset, bucket.WindowMinutes.Value);
    }
}

internal sealed class TokenUsageHistory
{
    private readonly string _codexHome;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly Dictionary<string, CachedTokenFile> _cache = new(StringComparer.OrdinalIgnoreCase);

    public TokenUsageHistory(string? codexHome = null)
    {
        _codexHome = codexHome ?? CodexPaths.Home;
    }

    public async Task<TokenCycleUsage?> ReadCurrentCycleAsync(
        QuotaSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        var cycle = TokenCycleSelector.Select(snapshot);
        if (cycle is null) return null;

        await _scanGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await Task.Run(
                () => ReadCycle(cycle.Value.StartsAt, cycle.Value.ResetsAt,
                    cycle.Value.WindowMinutes, DateTimeOffset.Now, cancellationToken),
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _scanGate.Release();
        }
    }

    internal TokenCycleUsage ReadCycle(
        DateTimeOffset startsAt,
        DateTimeOffset resetsAt,
        int windowMinutes,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        var files = EnumerateCandidateFiles(startsAt).ToArray();
        var activePaths = files.Select(file => file.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in _cache.Keys.Where(path => !activePaths.Contains(path)).ToArray())
            _cache.Remove(stale);

        var events = new List<TokenUsageEvent>();
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            events.AddRange(ReadFile(file, cancellationToken));
        }

        var effectiveEnd = now < resetsAt ? now : resetsAt;
        if (effectiveEnd < startsAt) effectiveEnd = startsAt;
        var eventsByDay = events
            .Where(item => item.Timestamp >= startsAt && item.Timestamp < resetsAt && item.Timestamp <= effectiveEnd)
            .GroupBy(item => DateOnly.FromDateTime(item.Timestamp.ToLocalTime().DateTime))
            .ToDictionary(group => group.Key, group => group.ToArray());

        var firstDay = DateOnly.FromDateTime(startsAt.ToLocalTime().DateTime);
        var lastDay = DateOnly.FromDateTime(effectiveEnd.ToLocalTime().DateTime);
        var days = new List<DailyTokenUsage>();
        for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
        {
            if (!eventsByDay.TryGetValue(day, out var dayEvents))
            {
                days.Add(new DailyTokenUsage(day, TokenUsageBreakdown.Empty, []));
                continue;
            }

            var slices = dayEvents
                .GroupBy(item => (item.Model, item.Speed))
                .Select(group =>
                {
                    var aggregate = group.Aggregate(
                        TokenUsageBreakdown.Empty,
                        (sum, item) => sum.Add(item.Usage));
                    var estimates = group
                        .Select(item => ApiCostEstimator.Estimate(item.Model, item.Speed, item.Usage))
                        .ToArray();
                    return new TokenUsageSlice(
                        group.Key.Model,
                        group.Key.Speed,
                        aggregate,
                        estimates.Where(value => value.IsPriced).Sum(value => value.Usd),
                        estimates.All(value => value.IsPriced));
                })
                .OrderByDescending(slice => slice.EstimatedUsd)
                .ThenByDescending(slice => slice.Usage.TotalTokens)
                .ToArray();
            var aggregate = dayEvents.Aggregate(
                TokenUsageBreakdown.Empty,
                (sum, item) => sum.Add(item.Usage));
            days.Add(new DailyTokenUsage(day, aggregate, slices));
        }

        return new TokenCycleUsage(
            startsAt,
            resetsAt,
            windowMinutes,
            days,
            now,
            files.Length);
    }

    private IEnumerable<FileInfo> EnumerateCandidateFiles(DateTimeOffset startsAt)
    {
        var files = new List<FileInfo>();
        foreach (var folderName in new[] { "sessions", "archived_sessions" })
        {
            var root = Path.Combine(_codexHome, folderName);
            if (!Directory.Exists(root)) continue;
            try
            {
                files.AddRange(new DirectoryInfo(root).EnumerateFiles("*.jsonl", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
                }).Where(file => file.LastWriteTimeUtc >= startsAt.UtcDateTime.AddMinutes(-1)));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
            {
            }
        }

        // A session can move from sessions to archived_sessions. Keep only one
        // physical copy so an archive transition cannot double-count tokens.
        return files
            .GroupBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Length)
                .First())
            .OrderBy(file => file.FullName, StringComparer.OrdinalIgnoreCase);
    }

    private IReadOnlyList<TokenUsageEvent> ReadFile(FileInfo file, CancellationToken cancellationToken)
    {
        file.Refresh();
        var startingLength = file.Length;
        var startingWriteTime = file.LastWriteTimeUtc;
        if (_cache.TryGetValue(file.FullName, out var cached) &&
            cached.Length == startingLength && cached.LastWriteTimeUtc == startingWriteTime)
            return cached.Events;

        var events = new List<TokenUsageEvent>();
        try
        {
            using var stream = new FileStream(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.SequentialScan);
            using var reader = new StreamReader(stream);
            long? previousCumulative = null;
            var model = "unknown";
            var speed = "unknown";
            var hasExplicitSpeed = false;
            string? firstExplicitSpeed = null;
            var lineNumber = 0;
            while (reader.ReadLine() is { } line)
            {
                if ((++lineNumber & 127) == 0) cancellationToken.ThrowIfCancellationRequested();
                TokenLogContextParser.Apply(line, ref model, ref speed, out var speedSpecified);
                if (speedSpecified)
                {
                    hasExplicitSpeed = true;
                    if (firstExplicitSpeed is null &&
                        ApiCostEstimator.NormalizeSpeed(speed) is TokenSpeed.Default or TokenSpeed.Fast)
                        firstExplicitSpeed = ApiCostEstimator.DisplaySpeed(speed);
                }
                var sample = TokenCountParser.ParseLine(line);
                if (sample is null) continue;

                long delta;
                if (previousCumulative is null)
                    delta = sample.LastUsage.TotalTokens > 0
                        ? sample.LastUsage.TotalTokens
                        : sample.CumulativeTotalTokens;
                else if (sample.CumulativeTotalTokens > previousCumulative.Value)
                    delta = sample.CumulativeTotalTokens - previousCumulative.Value;
                else if (sample.CumulativeTotalTokens == previousCumulative.Value)
                    delta = 0;
                else
                    delta = sample.LastUsage.TotalTokens;
                previousCumulative = sample.CumulativeTotalTokens;
                if (delta <= 0) continue;

                events.Add(new TokenUsageEvent(
                    sample.Timestamp,
                    sample.LastUsage with { TotalTokens = delta },
                    ApiCostEstimator.NormalizeModel(model),
                    ApiCostEstimator.DisplaySpeed(speed),
                    hasExplicitSpeed));
            }

            // Older and helper-agent logs often omit service_tier until after
            // their first token event, or omit it for the whole file. Backfill
            // only those genuinely unspecified events: use the first supported
            // explicit tier when one exists, otherwise Codex's normal tier.
            // An explicitly unsupported future tier remains Unknown.
            var missingSpeedFallback = firstExplicitSpeed ?? "Default";
            for (var index = 0; index < events.Count; index++)
            {
                if (!events[index].HasExplicitSpeed)
                    events[index] = events[index] with
                    {
                        Speed = missingSpeedFallback,
                        HasExplicitSpeed = true
                    };
            }

            file.Refresh();
            // Do not cache an unstable read. The next refresh will parse the
            // growing session again instead of treating a partial EOF as final.
            if (file.Length == startingLength && file.LastWriteTimeUtc == startingWriteTime)
                _cache[file.FullName] = new CachedTokenFile(
                    file.Length,
                    file.LastWriteTimeUtc,
                    events.ToArray());
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or JsonException)
        {
        }
        return events;
    }

    private sealed record TokenUsageEvent(
        DateTimeOffset Timestamp,
        TokenUsageBreakdown Usage,
        string Model,
        string Speed,
        bool HasExplicitSpeed);
    private sealed record CachedTokenFile(long Length, DateTime LastWriteTimeUtc, IReadOnlyList<TokenUsageEvent> Events);
}

internal static class TokenLogContextParser
{
    internal static void Apply(
        string line,
        ref string model,
        ref string speed,
        out bool speedSpecified)
    {
        speedSpecified = false;
        if (string.IsNullOrWhiteSpace(line) ||
            (!line.Contains("\"turn_context\"", StringComparison.Ordinal) &&
             !line.Contains("\"thread_settings_applied\"", StringComparison.Ordinal)))
            return;

        try
        {
            using var document = JsonDocument.Parse(line);
            var root = document.RootElement;
            if (!root.TryGetProperty("type", out var rootType) ||
                !root.TryGetProperty("payload", out var payload))
                return;

            JsonElement settings = payload;
            if (rootType.GetString() == "event_msg" &&
                payload.TryGetProperty("type", out var payloadType) &&
                payloadType.GetString() == "thread_settings_applied" &&
                payload.TryGetProperty("thread_settings", out var threadSettings))
                settings = threadSettings;
            else if (rootType.GetString() != "turn_context")
                return;

            if (settings.TryGetProperty("model", out var modelValue) &&
                modelValue.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(modelValue.GetString()))
                model = modelValue.GetString()!;
            if (settings.TryGetProperty("service_tier", out var speedValue) &&
                speedValue.ValueKind == JsonValueKind.String &&
                !string.IsNullOrWhiteSpace(speedValue.GetString()))
            {
                speed = speedValue.GetString()!;
                speedSpecified = true;
            }
        }
        catch (JsonException)
        {
        }
    }
}
