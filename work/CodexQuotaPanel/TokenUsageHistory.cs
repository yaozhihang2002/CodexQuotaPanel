using System.Security.Cryptography;
using System.Text.Json;

namespace CodexQuotaPanel;

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

internal sealed record CachedTokenFile(
    long Length,
    long LastWriteTimeUtcTicks,
    int PrefixLength,
    string PrefixHash,
    string Model,
    string Speed,
    bool HasExplicitSpeed,
    string? FirstExplicitSpeed,
    TokenUsageBreakdown? PreviousCumulative,
    IReadOnlyList<TokenUsageEvent> Events,
    int ParsedTokenLineCount,
    int MalformedTokenLineCount,
    int DuplicateTokenLineCount);

internal sealed record TokenUsageCacheDocument(
    string Schema,
    int SchemaVersion,
    IReadOnlyDictionary<string, CachedTokenFile> Files);

internal sealed class TokenUsageHistory
{
    private const string CacheSchema = "codex-quota-panel.token-usage-cache";
    private const int CacheSchemaVersion = 2;
    private static readonly JsonSerializerOptions CacheJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly string _codexHome;
    private readonly string? _diskCachePath;
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private readonly Dictionary<string, CachedTokenFile> _cache = new(StringComparer.OrdinalIgnoreCase);
    private bool _cacheDirty;
    private bool _diskCacheLoaded;

    public TokenUsageHistory(string? codexHome = null, string? cachePath = null)
    {
        _codexHome = codexHome ?? CodexPaths.Home;
        _diskCachePath = cachePath ?? (codexHome is null
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CodexQuotaPanel",
                "token-usage-cache-v2.json")
            : null);
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
        LoadDiskCache();
        var files = EnumerateCandidateFiles(startsAt).ToArray();
        var activePaths = files.Select(file => file.FullName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var stale in _cache.Keys.Where(path => !activePaths.Contains(path)).ToArray())
        {
            _cache.Remove(stale);
            _cacheDirty = true;
        }

        var events = new List<TokenUsageEvent>();
        var fingerprints = new HashSet<string>(StringComparer.Ordinal);
        var cachedFiles = 0;
        var incrementalFiles = 0;
        var parsedEvents = 0;
        var malformedLines = 0;
        var duplicateEvents = 0;
        var fallbackEvents = 0;
        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var result = ReadFile(file, cancellationToken);
            if (result.FromCache) cachedFiles++;
            if (result.Incremental) incrementalFiles++;
            parsedEvents += result.ParsedTokenLineCount;
            malformedLines += result.MalformedTokenLineCount;
            duplicateEvents += result.DuplicateTokenLineCount;
            fallbackEvents += result.Events.Count(item => !item.HasExplicitSpeed);
            foreach (var item in result.Events)
            {
                if (fingerprints.Add(item.Fingerprint)) events.Add(item);
                else duplicateEvents++;
            }
        }
        SaveDiskCache();

        var effectiveEnd = now < resetsAt ? now : resetsAt;
        if (effectiveEnd < startsAt) effectiveEnd = startsAt;
        var eligibleEvents = events
            .Where(item => item.Timestamp >= startsAt && item.Timestamp < resetsAt && item.Timestamp <= effectiveEnd)
            .ToArray();
        var eventsByDay = eligibleEvents
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

        var totalTokens = eligibleEvents.Sum(item => item.Usage.TotalTokens);
        var attributedTokens = eligibleEvents
            .Where(item => item.Model != "unknown" &&
                           ApiCostEstimator.NormalizeSpeed(item.Speed) != TokenSpeed.Unknown)
            .Sum(item => item.Usage.TotalTokens);
        var diagnostics = new TokenUsageDiagnostics(
            files.Length,
            cachedFiles,
            incrementalFiles,
            parsedEvents,
            malformedLines,
            duplicateEvents,
            fallbackEvents,
            attributedTokens,
            totalTokens);
        return new TokenCycleUsage(
            startsAt,
            resetsAt,
            windowMinutes,
            days,
            now,
            files.Length,
            diagnostics);
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

    private FileReadResult ReadFile(FileInfo file, CancellationToken cancellationToken)
    {
        file.Refresh();
        var startingLength = file.Length;
        var startingWriteTimeTicks = file.LastWriteTimeUtc.Ticks;
        if (_cache.TryGetValue(file.FullName, out var cached) &&
            cached.Length == startingLength && cached.LastWriteTimeUtcTicks == startingWriteTimeTicks)
            return ToResult(cached, fromCache: true, incremental: false);

        var incremental = cached is not null &&
                          startingLength > cached.Length &&
                          cached.Length > 0 &&
                          PrefixMatches(file.FullName, cached);
        var events = incremental ? cached!.Events.ToList() : [];
        var previousCumulative = incremental ? cached!.PreviousCumulative : null;
        var model = incremental ? cached!.Model : "unknown";
        var speed = incremental ? cached!.Speed : "unknown";
        var hasExplicitSpeed = incremental && cached!.HasExplicitSpeed;
        var firstExplicitSpeed = incremental ? cached!.FirstExplicitSpeed : null;
        var parsedTokenLines = incremental ? cached!.ParsedTokenLineCount : 0;
        var malformedTokenLines = incremental ? cached!.MalformedTokenLineCount : 0;
        var duplicateTokenLines = incremental ? cached!.DuplicateTokenLineCount : 0;
        var seenFingerprints = events.Select(item => item.Fingerprint).ToHashSet(StringComparer.Ordinal);

        try
        {
            using var stream = new FileStream(
                file.FullName,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                64 * 1024,
                FileOptions.SequentialScan);
            if (incremental) stream.Seek(cached!.Length, SeekOrigin.Begin);
            using var reader = new StreamReader(stream);
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

                var looksLikeToken = TokenCountParser.LooksLikeTokenLine(line);
                var sample = TokenCountParser.ParseLine(line);
                if (sample is null)
                {
                    if (looksLikeToken) malformedTokenLines++;
                    continue;
                }
                parsedTokenLines++;
                if (!seenFingerprints.Add(sample.Fingerprint))
                {
                    duplicateTokenLines++;
                    continue;
                }

                var delta = TokenUsageNormalizer.Normalize(sample, ref previousCumulative);
                if (delta is null)
                {
                    duplicateTokenLines++;
                    continue;
                }
                events.Add(new TokenUsageEvent(
                    sample.Timestamp,
                    delta,
                    ApiCostEstimator.NormalizeModel(model),
                    ApiCostEstimator.DisplaySpeed(speed),
                    hasExplicitSpeed,
                    sample.Fingerprint));
            }

            file.Refresh();
            if (file.Length == startingLength &&
                file.LastWriteTimeUtc.Ticks == startingWriteTimeTicks &&
                EndsWithNewline(file.FullName, startingLength))
            {
                var prefixLength = (int)Math.Min(4096, startingLength);
                var updated = new CachedTokenFile(
                    startingLength,
                    startingWriteTimeTicks,
                    prefixLength,
                    ComputePrefixHash(file.FullName, prefixLength),
                    model,
                    speed,
                    hasExplicitSpeed,
                    firstExplicitSpeed,
                    previousCumulative,
                    events.ToArray(),
                    parsedTokenLines,
                    malformedTokenLines,
                    duplicateTokenLines);
                _cache[file.FullName] = updated;
                _cacheDirty = true;
                return ToResult(updated, fromCache: false, incremental);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or JsonException)
        {
        }

        var fallback = new CachedTokenFile(
            startingLength,
            startingWriteTimeTicks,
            0,
            string.Empty,
            model,
            speed,
            hasExplicitSpeed,
            firstExplicitSpeed,
            previousCumulative,
            events.ToArray(),
            parsedTokenLines,
            malformedTokenLines,
            duplicateTokenLines);
        return ToResult(fallback, fromCache: false, incremental);
    }

    private static FileReadResult ToResult(CachedTokenFile cached, bool fromCache, bool incremental)
    {
        var fallbackSpeed = cached.FirstExplicitSpeed ?? "Default";
        var events = cached.Events
            .Select(item => item.HasExplicitSpeed
                ? item
                : item with { Speed = fallbackSpeed })
            .ToArray();
        return new FileReadResult(
            events,
            fromCache,
            incremental,
            cached.ParsedTokenLineCount,
            cached.MalformedTokenLineCount,
            cached.DuplicateTokenLineCount);
    }

    private void LoadDiskCache()
    {
        if (_diskCacheLoaded) return;
        _diskCacheLoaded = true;
        if (string.IsNullOrWhiteSpace(_diskCachePath)) return;
        try
        {
            var info = new FileInfo(_diskCachePath);
            if (!info.Exists || info.Length is <= 0 or > 32 * 1024 * 1024) return;
            var document = JsonSerializer.Deserialize<TokenUsageCacheDocument>(
                File.ReadAllText(_diskCachePath), CacheJsonOptions);
            if (document is null || document.Schema != CacheSchema || document.SchemaVersion != CacheSchemaVersion)
                return;
            var homePrefix = Path.GetFullPath(_codexHome).TrimEnd(Path.DirectorySeparatorChar) +
                             Path.DirectorySeparatorChar;
            foreach (var pair in document.Files)
            {
                var fullPath = Path.GetFullPath(pair.Key);
                if (fullPath.StartsWith(homePrefix, StringComparison.OrdinalIgnoreCase))
                    _cache[fullPath] = pair.Value;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or JsonException or
                                   NotSupportedException or ArgumentException)
        {
            _cache.Clear();
        }
    }

    private void SaveDiskCache()
    {
        if (!_cacheDirty || string.IsNullOrWhiteSpace(_diskCachePath)) return;
        var document = new TokenUsageCacheDocument(CacheSchema, CacheSchemaVersion, _cache);
        if (AtomicJsonFile.TryWrite(
                _diskCachePath,
                JsonSerializer.Serialize(document, CacheJsonOptions),
                createBackup: false))
            _cacheDirty = false;
    }

    private static bool PrefixMatches(string path, CachedTokenFile cached) =>
        cached.PrefixLength > 0 &&
        string.Equals(
            ComputePrefixHash(path, cached.PrefixLength),
            cached.PrefixHash,
            StringComparison.Ordinal);

    private static string ComputePrefixHash(string path, int length)
    {
        if (length <= 0) return string.Empty;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        var buffer = new byte[length];
        var read = 0;
        while (read < buffer.Length)
        {
            var count = stream.Read(buffer, read, buffer.Length - read);
            if (count <= 0) break;
            read += count;
        }
        return Convert.ToHexString(SHA256.HashData(buffer.AsSpan(0, read)));
    }

    private static bool EndsWithNewline(string path, long length)
    {
        if (length == 0) return true;
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.ReadWrite | FileShare.Delete);
        stream.Seek(-1, SeekOrigin.End);
        var value = stream.ReadByte();
        return value is '\n' or '\r';
    }

    private sealed record FileReadResult(
        IReadOnlyList<TokenUsageEvent> Events,
        bool FromCache,
        bool Incremental,
        int ParsedTokenLineCount,
        int MalformedTokenLineCount,
        int DuplicateTokenLineCount);
}
