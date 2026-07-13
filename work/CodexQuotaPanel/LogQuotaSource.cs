using System.Text.Json;
using System.Text;

namespace CodexQuotaPanel;

internal sealed class LogQuotaSource : IDisposable
{
    private const int TailBytes = 2 * 1024 * 1024;
    private readonly string _sessionsRoot;
    private readonly object _watcherGate = new();
    private readonly object _debounceGate = new();
    private readonly SemaphoreSlim _scanGate = new(1, 1);
    private FileSystemWatcher? _watcher;
    private System.Threading.Timer? _pollTimer;
    private CancellationTokenSource? _debounce;
    private bool _disposed;

    public event Action<QuotaSnapshot>? SnapshotAvailable;
    public event Action<string>? StatusChanged;

    public LogQuotaSource()
    {
        _sessionsRoot = Path.Combine(CodexPaths.Home, "sessions");
    }

    public Task StartAsync()
    {
        EnsureWatcher();
        _pollTimer = new System.Threading.Timer(
            _ => _ = RefreshAsync(),
            null,
            TimeSpan.FromSeconds(30),
            TimeSpan.FromSeconds(30));
        return RefreshAsync();
    }

    public async Task RefreshAsync()
    {
        if (_disposed || !Directory.Exists(_sessionsRoot))
        {
            ResetWatcher();
            StatusChanged?.Invoke("未找到 Codex 会话目录");
            return;
        }

        EnsureWatcher();

        if (!await _scanGate.WaitAsync(0).ConfigureAwait(false))
            return;

        try
        {
            StatusChanged?.Invoke("正在读取本地额度快照");
            var snapshot = await Task.Run(FindLatestSnapshot).ConfigureAwait(false);
            if (snapshot is not null)
            {
                SnapshotAvailable?.Invoke(snapshot);
                StatusChanged?.Invoke("本地会话监听中");
            }
            else
            {
                StatusChanged?.Invoke("等待 Codex 写入额度快照");
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusChanged?.Invoke("本地快照暂不可读");
        }
        finally
        {
            _scanGate.Release();
        }
    }

    private QuotaSnapshot? FindLatestSnapshot()
    {
        IEnumerable<FileInfo> files;
        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                IgnoreInaccessible = true,
                AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
            };
            files = new DirectoryInfo(_sessionsRoot)
                .EnumerateFiles("*.jsonl", options)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .Take(32)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or DirectoryNotFoundException)
        {
            return null;
        }

        QuotaSnapshot? newest = null;
        foreach (var file in files)
        {
            var candidate = ReadLatestSnapshot(file.FullName);
            if (candidate is not null && (newest is null || candidate.ObservedAt > newest.ObservedAt))
                newest = candidate;
        }
        return newest;
    }

    private static QuotaSnapshot? ReadLatestSnapshot(string path)
    {
        byte[]? buffer = null;
        try
        {
            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 64 * 1024,
                FileOptions.SequentialScan);

            var start = Math.Max(0, stream.Length - TailBytes);
            var requested = checked((int)(stream.Length - start));
            stream.Seek(start, SeekOrigin.Begin);
            buffer = GC.AllocateUninitializedArray<byte>(requested);
            var read = 0;
            while (read < requested)
            {
                var count = stream.Read(buffer, read, requested - read);
                if (count == 0) break;
                read += count;
            }

            var lineEnd = read;
            for (var index = read - 1; index >= 0; index--)
            {
                if (buffer[index] != (byte)'\n') continue;
                var parsed = ParseCandidateLine(buffer.AsSpan(index + 1, lineEnd - index - 1));
                if (parsed is not null) return parsed;
                lineEnd = index;
            }

            // Only parse the first buffered segment when it begins at the start of the file;
            // otherwise it may be a partial JSONL record.
            if (start == 0)
            {
                var parsed = ParseCandidateLine(buffer.AsSpan(0, lineEnd));
                if (parsed is not null) return parsed;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
        }
        finally
        {
            if (buffer is not null) Array.Clear(buffer);
        }
        return null;
    }

    private static QuotaSnapshot? ParseCandidateLine(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty && (bytes[^1] == (byte)'\r' || bytes[^1] == (byte)'\n'))
            bytes = bytes[..^1];
        if (bytes.IsEmpty || bytes.IndexOf("\"rate_limits\""u8) < 0) return null;
        return QuotaParser.ParseRolloutLine(Encoding.UTF8.GetString(bytes));
    }

    private void OnFileEvent(object sender, FileSystemEventArgs args)
    {
        if (_disposed || !args.FullPath.EndsWith(".jsonl", StringComparison.OrdinalIgnoreCase))
            return;
        ScheduleFileRead(args.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs args) => OnFileEvent(sender, args);

    private void EnsureWatcher()
    {
        if (_disposed || _watcher is not null || !Directory.Exists(_sessionsRoot)) return;
        lock (_watcherGate)
        {
            if (_disposed || _watcher is not null || !Directory.Exists(_sessionsRoot)) return;
            var watcher = new FileSystemWatcher(_sessionsRoot, "*.jsonl")
            {
                IncludeSubdirectories = true,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName
            };
            watcher.Changed += OnFileEvent;
            watcher.Created += OnFileEvent;
            watcher.Renamed += OnFileRenamed;
            watcher.Error += OnWatcherError;
            watcher.EnableRaisingEvents = true;
            _watcher = watcher;
        }
    }

    private void OnWatcherError(object sender, ErrorEventArgs args)
    {
        ResetWatcher();
        ScheduleFullScan(TimeSpan.FromSeconds(1));
    }

    private void ResetWatcher()
    {
        lock (_watcherGate)
        {
            if (_watcher is null) return;
            _watcher.EnableRaisingEvents = false;
            _watcher.Dispose();
            _watcher = null;
        }
    }

    private void ScheduleFileRead(string path)
    {
        CancellationTokenSource? previous;
        CancellationTokenSource current;
        lock (_debounceGate)
        {
            if (_disposed) return;
            previous = _debounce;
            current = new CancellationTokenSource();
            _debounce = current;
        }
        previous?.Cancel();
        previous?.Dispose();
        var token = current.Token;
        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(250, token).ConfigureAwait(false);
                if (token.IsCancellationRequested) return;
                var snapshot = ReadLatestSnapshot(path);
                if (snapshot is not null)
                {
                    SnapshotAvailable?.Invoke(snapshot);
                    StatusChanged?.Invoke("本地会话监听中");
                }
            }
            catch (OperationCanceledException)
            {
            }
            finally
            {
                lock (_debounceGate)
                {
                    if (ReferenceEquals(_debounce, current)) _debounce = null;
                }
                current.Dispose();
            }
        }, token);
    }

    private void ScheduleFullScan(TimeSpan delay)
    {
        _ = Task.Run(async () =>
        {
            await Task.Delay(delay).ConfigureAwait(false);
            if (_disposed) return;
            await RefreshAsync().ConfigureAwait(false);
        });
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _pollTimer?.Dispose();
        CancellationTokenSource? debounce;
        lock (_debounceGate)
        {
            debounce = _debounce;
            _debounce = null;
        }
        debounce?.Cancel();
        debounce?.Dispose();
        ResetWatcher();
        // The semaphore is intentionally left undisposed: an in-flight read may release it during shutdown.
    }
}
