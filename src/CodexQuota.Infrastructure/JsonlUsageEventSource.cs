using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using CodexQuota.Application;
using CodexQuota.Domain;

namespace CodexQuota.Infrastructure;

public sealed class JsonlUsageEventSource : IUsageEventSource
{
    private const int BatchSize = 256;
    // Increment when attribution rules change so the finite index worker
    // replays recent JSONL files and upgrades existing SQLite rows in place.
    private const int CurrentParserVersion = 3;
    private readonly string[] _sessionRoots;
    private readonly string? _cursorStatePath;
    private readonly TimeSpan _lookback;
    private readonly Dictionary<string, FileCursor> _fileCursors = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _stateGate = new();
    private readonly TaskCompletionSource _initialScanCompleted =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task InitialScanCompleted => _initialScanCompleted.Task;

    public static bool HasUsableCursorState(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return false;
        try
        {
            var found = false;
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                CursorEnvelope? item;
                try { item = JsonSerializer.Deserialize<CursorEnvelope>(line); }
                catch (JsonException) { return false; }
                if (item is null || item.ParserVersion != CurrentParserVersion ||
                    string.IsNullOrWhiteSpace(item.Key) || item.Cursor.Length < 0)
                    return false;
                found = true;
            }
            return found;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public JsonlUsageEventSource(
        string? codexHome = null,
        string? cursorStatePath = null,
        TimeSpan? lookback = null)
    {
        var home = codexHome ?? new CodexHomeResolver().Resolve();
        _sessionRoots = [Path.Combine(home, "sessions"), Path.Combine(home, "archived_sessions")];
        _cursorStatePath = cursorStatePath;
        // The longest supported quota window is seven days. One extra day
        // covers clock skew and sessions that remain open across the boundary.
        _lookback = lookback ?? TimeSpan.FromDays(8);
        LoadCursors();
    }

    public async IAsyncEnumerable<ObservedUsage> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var batch in WatchBatchesAsync(cancellationToken).ConfigureAwait(false))
            foreach (var usage in batch)
                yield return usage;
    }

    public async IAsyncEnumerable<IReadOnlyList<ObservedUsage>> ScanExistingBatchesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var paths = EnumerateSessionFiles().ToArray();
        if (paths.Length == 0)
        {
            _initialScanCompleted.TrySetResult();
            yield break;
        }

        foreach (var path in paths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (!HasChanged(path)) continue;
                var batch = new List<ObservedUsage>(BatchSize);
                await foreach (var usage in ReadChangedFileAsync(path, cancellationToken).ConfigureAwait(false))
                {
                    batch.Add(usage);
                    if (batch.Count < BatchSize) continue;
                    yield return batch.ToArray();
                    batch.Clear();
                }
                if (batch.Count > 0) yield return batch.ToArray();
            }
            finally
            {
                if (ReferenceEquals(path, paths[^1]) || string.Equals(path, paths[^1], StringComparison.OrdinalIgnoreCase))
                    _initialScanCompleted.TrySetResult();
            }
        }
    }

    public async IAsyncEnumerable<IReadOnlyList<ObservedUsage>> WatchBatchesAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var paths = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        while (!_sessionRoots.Any(Directory.Exists))
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

        var queued = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        void Queue(string path)
        {
            if (queued.TryAdd(path, 0)) paths.Writer.TryWrite(path);
        }
        var initialPaths = EnumerateSessionFiles().ToArray();
        var initialPending = new HashSet<string>(initialPaths, StringComparer.OrdinalIgnoreCase);
        foreach (var path in initialPaths) Queue(path);
        if (initialPending.Count == 0) _initialScanCompleted.TrySetResult();

        var watchers = CreateWatchers(Queue);
        var polling = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(30));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                foreach (var path in EnumerateSessionFiles()) Queue(path);
        }, cancellationToken);
        try
        {
            while (await paths.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (paths.Reader.TryRead(out var path))
                {
                    try
                    {
                        queued.TryRemove(path, out _);
                        if (!HasChanged(path)) continue;
                        var batch = new List<ObservedUsage>(BatchSize);
                        await foreach (var usage in ReadChangedFileAsync(path, cancellationToken).ConfigureAwait(false))
                        {
                            batch.Add(usage);
                            if (batch.Count < BatchSize) continue;
                            yield return batch.ToArray();
                            batch.Clear();
                        }
                        if (batch.Count > 0) yield return batch.ToArray();
                    }
                    finally
                    {
                        if (initialPending.Remove(path) && initialPending.Count == 0)
                            _initialScanCompleted.TrySetResult();
                    }
                }
            }
        }
        finally
        {
            if (cancellationToken.IsCancellationRequested)
                _initialScanCompleted.TrySetCanceled(cancellationToken);
            foreach (var watcher in watchers) watcher.Dispose();
            try { await polling.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

    public async IAsyncEnumerable<ObservedUsage> ReadFileAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var inferred = await FindFirstExplicitContextAsync(path, 0, "unknown", "default", cancellationToken)
            .ConfigureAwait(false);
        await foreach (var usage in ParseRangeAsync(path, 0, inferred.Model ?? "unknown", inferred.Tier ?? "default", false, null,
                           null, cancellationToken).ConfigureAwait(false))
            yield return usage;
    }

    private async IAsyncEnumerable<ObservedUsage> ReadChangedFileAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        FileCursor? cursor;
        var key = FileKey(path);
        lock (_stateGate) _fileCursors.TryGetValue(key, out cursor);
        var fileLength = SafeLength(path);
        if (fileLength < 0) yield break;

        var start = cursor is not null && cursor.Length <= fileLength ? cursor.Length : 0;
        var model = cursor?.Model ?? "unknown";
        var tier = cursor?.Tier ?? "default";
        var tierExplicit = cursor?.TierExplicit ?? false;
        var previous = cursor?.Previous;

        if (start > 0 && (ApiCostEstimator.NormalizeModel(model) == "unknown" || !tierExplicit))
        {
            var late = await FindFirstExplicitContextAsync(path, start, model, tier, cancellationToken)
                .ConfigureAwait(false);
            if ((ApiCostEstimator.NormalizeModel(model) == "unknown" && late.Model is not null) ||
                (!tierExplicit && late.Tier is not null))
            {
                start = 0;
                model = late.Model ?? model;
                tier = late.Tier ?? tier;
                tierExplicit = false;
                previous = null;
            }
        }
        else if (start == 0)
        {
            var inferred = await FindFirstExplicitContextAsync(path, 0, model, tier, cancellationToken)
                .ConfigureAwait(false);
            model = inferred.Model ?? model;
            tier = inferred.Tier ?? "default";
        }

        FileCursor? completed = null;
        await foreach (var usage in ParseRangeAsync(path, start, model, tier, tierExplicit, previous,
                           state => completed = state, cancellationToken).ConfigureAwait(false))
            yield return usage;
        if (completed is not null)
        {
            completed = completed with { LastWriteTicks = SafeLastWriteTicks(path) };
            lock (_stateGate) _fileCursors[key] = completed;
            await PersistCursorAsync(key, completed, cancellationToken).ConfigureAwait(false);
        }
    }

    private static async IAsyncEnumerable<ObservedUsage> ParseRangeAsync(
        string path,
        long start,
        string initialModel,
        string initialTier,
        bool initialTierExplicit,
        TokenUsageBreakdown? initialPrevious,
        Action<FileCursor>? completed,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var stream = TryOpen(path);
        if (stream is null) yield break;
        await using (stream.ConfigureAwait(false))
        {
            if (start > stream.Length) yield break;
            stream.Position = start;
            using var reader = new StreamReader(stream, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: start == 0, bufferSize: 64 * 1024, leaveOpen: true);
            var model = initialModel;
            var tier = initialTier;
            var tierExplicit = initialTierExplicit;
            var previous = initialPrevious;
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                TokenLogContextParser.Apply(line, ref model, ref tier, out _, out var tierSpecifiedNow);
                if (tierSpecifiedNow) tierExplicit = true;
                var sample = TokenCountLineParser.Parse(line);
                if (sample is null) continue;
                var usage = TokenUsageNormalizer.Normalize(sample, ref previous);
                if (usage is null) continue;
                yield return new ObservedUsage(
                    sample.Timestamp,
                    ApiCostEstimator.NormalizeModel(model),
                    tier,
                    usage,
                    sample.Fingerprint,
                    tierExplicit);
            }
            completed?.Invoke(new FileCursor(stream.Length, 0, model, tier, tierExplicit, previous));
        }
    }

    private static async Task<ContextInference> FindFirstExplicitContextAsync(
        string path,
        long start,
        string initialModel,
        string initialTier,
        CancellationToken cancellationToken)
    {
        var stream = TryOpen(path);
        if (stream is null) return new(null, null);
        await using (stream.ConfigureAwait(false))
        {
            if (start > stream.Length) return new(null, null);
            stream.Position = start;
            using var reader = new StreamReader(stream, Encoding.UTF8,
                detectEncodingFromByteOrderMarks: start == 0, bufferSize: 64 * 1024, leaveOpen: true);
            var model = initialModel;
            var tier = initialTier;
            string? firstModel = null;
            string? firstTier = null;
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                TokenLogContextParser.Apply(line, ref model, ref tier,
                    out var modelSpecifiedNow, out var tierSpecifiedNow);
                if (modelSpecifiedNow && firstModel is null) firstModel = model;
                if (tierSpecifiedNow && firstTier is null) firstTier = tier;
                if (firstModel is not null && firstTier is not null) break;
            }
            return new(firstModel, firstTier);
        }
    }

    private static FileStream? TryOpen(string path)
    {
        try
        {
            return new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return null;
        }
    }

    private static long SafeLength(string path)
    {
        try
        {
            var file = new FileInfo(path);
            return file.Exists ? file.Length : -1;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return -1;
        }
    }

    private IEnumerable<string> EnumerateSessionFiles()
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
        };
        return _sessionRoots.Where(Directory.Exists)
            .SelectMany(root => new DirectoryInfo(root).EnumerateFiles("*.jsonl", options))
            .Where(file => file.LastWriteTimeUtc >= DateTime.UtcNow.Subtract(_lookback))
            .GroupBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Length).First())
            .OrderBy(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .ToArray();
    }

    private IReadOnlyList<FileSystemWatcher> CreateWatchers(Action<string> queue)
    {
        var watchers = new List<FileSystemWatcher>();
        foreach (var root in _sessionRoots.Where(Directory.Exists))
        {
            var watcher = new FileSystemWatcher(root, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
                EnableRaisingEvents = true
            };
            watcher.Changed += (_, args) => queue(args.FullPath);
            watcher.Created += (_, args) => queue(args.FullPath);
            watcher.Renamed += (_, args) => queue(args.FullPath);
            watchers.Add(watcher);
        }
        return watchers;
    }

    private bool HasChanged(string path)
    {
        try
        {
            var file = new FileInfo(path);
            if (!file.Exists) return false;
            var signature = (file.Length, file.LastWriteTimeUtc.Ticks);
            var key = FileKey(path);
            lock (_stateGate)
            {
                return !_fileCursors.TryGetValue(key, out var prior) ||
                       prior.Length != signature.Length || prior.LastWriteTicks != signature.Ticks;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return false;
        }
    }

    private void LoadCursors()
    {
        if (string.IsNullOrWhiteSpace(_cursorStatePath) || !File.Exists(_cursorStatePath)) return;
        try
        {
            foreach (var line in File.ReadLines(_cursorStatePath))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var item = JsonSerializer.Deserialize<CursorEnvelope>(line);
                    if (item is not null && item.ParserVersion == CurrentParserVersion &&
                        !string.IsNullOrWhiteSpace(item.Key))
                        _fileCursors[item.Key] = item.Cursor;
                }
                catch (JsonException) { }
            }
            if (new FileInfo(_cursorStatePath).Length > 4 * 1024 * 1024) CompactCursorState();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private async Task PersistCursorAsync(
        string key,
        FileCursor cursor,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_cursorStatePath)) return;
        try
        {
            var directory = Path.GetDirectoryName(_cursorStatePath);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
            var line = JsonSerializer.Serialize(new CursorEnvelope(key, cursor, CurrentParserVersion)) + Environment.NewLine;
            await File.AppendAllTextAsync(_cursorStatePath, line, Encoding.UTF8, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void CompactCursorState()
    {
        if (string.IsNullOrWhiteSpace(_cursorStatePath)) return;
        var temporary = _cursorStatePath + ".tmp";
        var lines = _fileCursors.Select(pair =>
            JsonSerializer.Serialize(new CursorEnvelope(pair.Key, pair.Value, CurrentParserVersion)));
        File.WriteAllLines(temporary, lines, new UTF8Encoding(false));
        File.Move(temporary, _cursorStatePath, true);
    }

    private static string FileKey(string path) => Path.GetFileName(path);

    private static long SafeLastWriteTicks(string path)
    {
        try { return new FileInfo(path).LastWriteTimeUtc.Ticks; }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException) { return 0; }
    }

    private sealed record CursorEnvelope(string Key, FileCursor Cursor, int ParserVersion = 0);

    private sealed record ContextInference(string? Model, string? Tier);

    private sealed record FileCursor(
        long Length,
        long LastWriteTicks,
        string Model,
        string Tier,
        bool TierExplicit,
        TokenUsageBreakdown? Previous);
}
