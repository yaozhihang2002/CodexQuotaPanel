using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;

namespace CodexQuotaPanel;

internal sealed class AppServerQuotaSource : IAsyncDisposable
{
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _process;
    private long _requestId;
    private Task? _readerTask;
    private Task? _errorTask;
    private Task? _pollTask;
    private EventHandler? _processExitedHandler;
    private volatile bool _initialized;
    private volatile bool _disposed;
    private int _disposeState;
    private int _disconnectRaised;

    public event Action<QuotaSnapshot>? SnapshotAvailable;
    public event Action<string>? StatusChanged;
    public event Action? Disconnected;

    public async Task<bool> TryStartAsync(CancellationToken cancellationToken = default)
    {
        if (_disposed) return false;
        try
        {
            // Native process startup and redirected-pipe setup can block
            // synchronously on a broken CLI installation, so keep the entire
            // startup path off the caller thread and enforce a hard timeout.
            var startTask = Task.Run(() => StartCoreAsync(cancellationToken), CancellationToken.None);
            return await startTask
                .WaitAsync(TimeSpan.FromSeconds(15), cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException)
        {
            _initialized = false;
            await StopProcessAsync().ConfigureAwait(false);
            StatusChanged?.Invoke("App Server 不可用，使用本地快照");
            return false;
        }
    }

    private async Task<bool> StartCoreAsync(CancellationToken cancellationToken)
    {
        if (_disposed) return false;

        try
        {
            StatusChanged?.Invoke("正在连接 Codex App Server");
            var executable = ResolveCodexExecutable();

            var startInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = CodexPaths.UserHome
            };
            startInfo.ArgumentList.Add("app-server");
            startInfo.ArgumentList.Add("--listen");
            startInfo.ArgumentList.Add("stdio://");
            startInfo.Environment["RUST_LOG"] = "error";
            startInfo.Environment["CODEX_HOME"] = CodexPaths.Home;

            _process = new Process { StartInfo = startInfo, EnableRaisingEvents = true };
            Interlocked.Exchange(ref _disconnectRaised, 0);
            _processExitedHandler = (_, _) =>
            {
                _initialized = false;
                MarkDisconnected();
                FailPending(new IOException("Codex app-server exited."));
            };
            _process.Exited += _processExitedHandler;

            if (!_process.Start())
                return false;

            _readerTask = Task.Run(() => ReadLoopAsync(_lifetime.Token));
            _errorTask = Task.Run(() => DrainErrorAsync(_lifetime.Token));

            using var startTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token, cancellationToken, startTimeout.Token);
            await SendRequestAsync(
                "initialize",
                new
                {
                    clientInfo = new
                    {
                        name = "codex_quota_panel",
                        title = "Codex Quota Panel",
                        version = "1.8.6"
                    }
                },
                linked.Token).ConfigureAwait(false);
            await SendNotificationAsync("initialized", null, linked.Token).ConfigureAwait(false);
            _initialized = true;

            if (!await FetchSnapshotAsync(linked.Token).ConfigureAwait(false))
                throw new JsonException("App Server returned no usable rate-limit snapshot.");
            _pollTask = Task.Run(() => PollLoopAsync(_lifetime.Token));
            StatusChanged?.Invoke("App Server 实时连接");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or OperationCanceledException or TimeoutException or JsonException or System.ComponentModel.Win32Exception)
        {
            _initialized = false;
            await StopProcessAsync().ConfigureAwait(false);
            StatusChanged?.Invoke("App Server 不可用，使用本地快照");
            return false;
        }
    }

    public Task RefreshAsync() => RefreshAsync(_lifetime.Token);

    private async Task RefreshAsync(CancellationToken cancellationToken)
    {
        if (!_initialized || _disposed) return;
        if (!await _refreshGate.WaitAsync(0, cancellationToken).ConfigureAwait(false)) return;

        try
        {
            await FetchSnapshotAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or TimeoutException or JsonException or InvalidOperationException)
        {
            StatusChanged?.Invoke("App Server 刷新失败，保留本地快照");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<bool> FetchSnapshotAsync(CancellationToken cancellationToken)
    {
        var result = await SendRequestAsync("account/rateLimits/read", null, cancellationToken).ConfigureAwait(false);
        var snapshot = QuotaParser.ParseAppServerResult(result);
        if (snapshot is null) return false;
        SnapshotAvailable?.Invoke(snapshot);
        StatusChanged?.Invoke("App Server 实时连接");
        return true;
    }

    private async Task<JsonElement> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var process = _process;
        if (process is null || process.HasExited)
            throw new IOException("Codex app-server is not running.");

        var id = Interlocked.Increment(ref _requestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion))
            throw new InvalidOperationException("Duplicate JSON-RPC request id.");

        try
        {
            var message = parameters is null
                ? JsonSerializer.Serialize(new { method, id })
                : JsonSerializer.Serialize(new { method, id, @params = parameters });
            await WriteLineAsync(message, cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(12), cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _pending.TryRemove(id, out _);
        }
    }

    private Task SendNotificationAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var message = parameters is null
            ? JsonSerializer.Serialize(new { method })
            : JsonSerializer.Serialize(new { method, @params = parameters });
        return WriteLineAsync(message, cancellationToken);
    }

    private async Task WriteLineAsync(string line, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var writer = _process?.StandardInput ?? throw new IOException("App-server stdin is unavailable.");
            await writer.WriteLineAsync(line.AsMemory(), cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken)
                .ConfigureAwait(false);
            await writer.FlushAsync(cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken)
                .ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        var reader = _process?.StandardOutput;
        if (reader is null) return;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null) break;
                if (string.IsNullOrWhiteSpace(line)) continue;

                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (root.ValueKind != JsonValueKind.Object) continue;
                    if (TryReadId(root, out var id) && _pending.TryGetValue(id, out var completion))
                    {
                        if (root.TryGetProperty("error", out var error))
                        {
                            completion.TrySetException(new IOException(error.GetRawText()));
                        }
                        else if (root.TryGetProperty("result", out var result))
                        {
                            completion.TrySetResult(result.Clone());
                        }
                        continue;
                    }

                    if (root.TryGetProperty("method", out var methodValue) &&
                        methodValue.ValueKind == JsonValueKind.String &&
                        string.Equals(methodValue.GetString(), "account/rateLimits/updated", StringComparison.Ordinal))
                    {
                        _ = Task.Run(RefreshAsync, cancellationToken);
                    }
                }
                catch (JsonException)
                {
                }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
        finally
        {
            var wasInitialized = _initialized;
            _initialized = false;
            if (wasInitialized) MarkDisconnected();
        }
    }

    private async Task DrainErrorAsync(CancellationToken cancellationToken)
    {
        var reader = _process?.StandardError;
        if (reader is null) return;
        try
        {
            while (!cancellationToken.IsCancellationRequested && await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null)
            {
                // Intentionally discard diagnostic output: it can include local paths.
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
        }
    }

    private async Task PollLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(60));
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false))
                await RefreshAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private static bool TryReadId(JsonElement root, out long id)
    {
        id = default;
        if (!root.TryGetProperty("id", out var value)) return false;
        if (value.ValueKind == JsonValueKind.Number) return value.TryGetInt64(out id);
        return value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out id);
    }

    private static string ResolveCodexExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return configured;

        var localRoots = new[]
        {
            Environment.GetEnvironmentVariable("LOCALAPPDATA"),
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
        }
        .Where(path => !string.IsNullOrWhiteSpace(path))
        .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var localAppData in localRoots)
        {
            var binRoot = Path.Combine(localAppData!, "OpenAI", "Codex", "bin");
            if (Directory.Exists(binRoot))
            {
                try
                {
                    var local = new DirectoryInfo(binRoot)
                        .EnumerateFiles("codex.exe", SearchOption.AllDirectories)
                        .OrderByDescending(file => file.LastWriteTimeUtc)
                        .FirstOrDefault();
                    if (local is not null) return local.FullName;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                }
            }
        }

        return "codex";
    }

    private void FailPending(Exception exception)
    {
        foreach (var completion in _pending.Values)
            completion.TrySetException(exception);
    }

    private void MarkDisconnected()
    {
        if (_disposed || Interlocked.Exchange(ref _disconnectRaised, 1) != 0) return;
        StatusChanged?.Invoke("App Server 已断开，使用本地快照");
        Disconnected?.Invoke();
    }

    private Task StopProcessAsync()
    {
        var process = _process;
        if (process is null) return Task.CompletedTask;
        _process = null;
        _processExitedHandler = null;
        // All Process APIs run off the startup/fallback path. On some Codex
        // builds a child can close stdio while its process handle is still in
        // transition, and even HasExited/Kill/Dispose may briefly block.
        _ = Task.Run(() =>
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: false);
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            try { process.Dispose(); } catch { }
        });
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;
        _disposed = true;
        _ = _lifetime.CancelAsync();
        FailPending(new OperationCanceledException());
        await StopProcessAsync().ConfigureAwait(false);
        if (_readerTask is not null)
            await AwaitBackgroundTaskAsync(_readerTask).ConfigureAwait(false);
        if (_errorTask is not null)
            await AwaitBackgroundTaskAsync(_errorTask).ConfigureAwait(false);
        if (_pollTask is not null)
            await AwaitBackgroundTaskAsync(_pollTask).ConfigureAwait(false);
        // Gates and CTS are intentionally not disposed: a late notification refresh may still
        // unwind through its finally block after the child process has been stopped.
    }

    private static async Task AwaitBackgroundTaskAsync(Task task)
    {
        try { await task.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
        catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException or ObjectDisposedException) { }
    }
}
