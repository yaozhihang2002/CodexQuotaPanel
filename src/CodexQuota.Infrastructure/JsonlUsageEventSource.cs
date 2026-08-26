using System.Runtime.CompilerServices;
using System.Collections.Concurrent;
using System.Threading.Channels;
using CodexQuota.Application;
using CodexQuota.Domain;

namespace CodexQuota.Infrastructure;

public sealed class JsonlUsageEventSource : IUsageEventSource
{
    private readonly string _sessionsRoot;
    private readonly Dictionary<string, ObservedUsage> _emitted = new(StringComparer.Ordinal);
    private readonly object _seenGate = new();

    public JsonlUsageEventSource(string? codexHome = null)
    {
        var home = codexHome ?? new CodexHomeResolver().Resolve();
        _sessionsRoot = Path.Combine(home, "sessions");
    }

    public async IAsyncEnumerable<ObservedUsage> WatchAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var paths = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = false
        });

        while (!Directory.Exists(_sessionsRoot))
            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);

        var queued = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        void Queue(string path)
        {
            if (queued.TryAdd(path, 0)) paths.Writer.TryWrite(path);
        }
        foreach (var path in EnumerateSessionFiles()) Queue(path);

        using var watcher = CreateWatcher(Queue);
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
                    queued.TryRemove(path, out _);
                    await foreach (var usage in ReadFileAsync(path, cancellationToken).ConfigureAwait(false))
                    {
                        lock (_seenGate)
                        {
                            if (_emitted.TryGetValue(usage.Fingerprint, out var prior) && prior == usage) continue;
                            _emitted[usage.Fingerprint] = usage;
                        }
                        yield return usage;
                    }
                }
            }
        }
        finally
        {
            try { await polling.ConfigureAwait(false); } catch (OperationCanceledException) { }
        }
    }

    public async IAsyncEnumerable<ObservedUsage> ReadFileAsync(
        string path,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var parsed = new List<ObservedUsage>();
        var pendingTier = new List<int>();
        var model = "unknown";
        var tier = "default";
        var tierExplicit = false;
        TokenUsageBreakdown? previous = null;

        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            yield break;
        }

        await using (stream.ConfigureAwait(false))
        using (var reader = new StreamReader(stream))
        {
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                TokenLogContextParser.Apply(line, ref model, ref tier, out var specifiedNow);
                if (specifiedNow)
                {
                    tierExplicit = true;
                    foreach (var index in pendingTier)
                        parsed[index] = parsed[index] with { ServiceTier = tier };
                    pendingTier.Clear();
                }

                var sample = TokenCountLineParser.Parse(line);
                if (sample is null) continue;
                var usage = TokenUsageNormalizer.Normalize(sample, ref previous);
                if (usage is null) continue;
                parsed.Add(new ObservedUsage(
                    sample.Timestamp,
                    ApiCostEstimator.NormalizeModel(model),
                    tierExplicit ? tier : "default",
                    usage,
                    sample.Fingerprint,
                    tierExplicit));
                if (!tierExplicit) pendingTier.Add(parsed.Count - 1);
            }
        }

        foreach (var usage in parsed)
        {
            cancellationToken.ThrowIfCancellationRequested();
            yield return usage;
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
        return new DirectoryInfo(_sessionsRoot)
            .EnumerateFiles("*.jsonl", options)
            .OrderBy(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .ToArray();
    }

    private FileSystemWatcher? CreateWatcher(Action<string> queue)
    {
        if (!Directory.Exists(_sessionsRoot)) return null;
        var watcher = new FileSystemWatcher(_sessionsRoot, "*.jsonl")
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
            EnableRaisingEvents = true
        };
        FileSystemEventHandler onChange = (_, args) => queue(args.FullPath);
        RenamedEventHandler onRename = (_, args) => queue(args.FullPath);
        watcher.Changed += onChange;
        watcher.Created += onChange;
        watcher.Renamed += onRename;
        return watcher;
    }
}
