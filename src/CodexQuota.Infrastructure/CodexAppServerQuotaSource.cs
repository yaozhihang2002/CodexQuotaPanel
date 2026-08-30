using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using CodexQuota.Application;
using CodexQuota.Domain;

namespace CodexQuota.Infrastructure;

/// <summary>
/// Keeps one Codex app-server process alive and reuses its JSON-RPC connection.
/// The adapter stays isolated because this local surface is not a public API.
/// </summary>
public sealed class CodexAppServerQuotaSource : IQuotaSource, IAsyncDisposable
{
    private readonly string _codexHome;
    private readonly string _executable;
    private readonly ConcurrentDictionary<long, TaskCompletionSource<JsonElement>> _pending = new();
    private readonly SemaphoreSlim _connectGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly CancellationTokenSource _lifetime = new();
    private Process? _process;
    private Task? _readerTask;
    private Task? _errorTask;
    private long _requestId;
    private volatile bool _initialized;
    private int _disposeState;

    public CodexAppServerQuotaSource(string? codexHome = null, string? executable = null)
    {
        _codexHome = codexHome ?? new CodexHomeResolver().Resolve();
        _executable = executable ?? ResolveExecutable();
    }

    public async Task<OfficialQuotaSnapshot?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false)) return null;
        try
        {
            var result = await SendRequestAsync("account/rateLimits/read", null, cancellationToken)
                .ConfigureAwait(false);
            return QuotaPayloadParser.ParseAppServerResult(result);
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or
                                   OperationCanceledException or TimeoutException or ObjectDisposedException)
        {
            await ResetConnectionAsync().ConfigureAwait(false);
            return null;
        }
    }

    private async Task<bool> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_initialized && _process is { HasExited: false }) return true;
        await _connectGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_initialized && _process is { HasExited: false }) return true;
            await ResetConnectionAsync().ConfigureAwait(false);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            _process = StartProcess();
            _readerTask = Task.Run(() => ReadLoopAsync(_process, _lifetime.Token), CancellationToken.None);
            _errorTask = Task.Run(() => DrainErrorAsync(_process, _lifetime.Token), CancellationToken.None);
            await SendRequestAsync("initialize", new
            {
                clientInfo = new { name = "codex_quota_panel_vnext", title = "Codex Quota Panel", version = "0.6.4" }
            }, timeout.Token).ConfigureAwait(false);
            await SendNotificationAsync("initialized", timeout.Token).ConfigureAwait(false);
            _initialized = true;
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidOperationException or
                                   OperationCanceledException or TimeoutException or
                                   System.ComponentModel.Win32Exception or ObjectDisposedException)
        {
            await ResetConnectionAsync().ConfigureAwait(false);
            return false;
        }
        finally { _connectGate.Release(); }
    }

    private Process StartProcess()
    {
        var info = new ProcessStartInfo
        {
            FileName = _executable,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
        };
        info.ArgumentList.Add("app-server");
        info.ArgumentList.Add("--listen");
        info.ArgumentList.Add("stdio://");
        info.Environment["CODEX_HOME"] = _codexHome;
        info.Environment["RUST_LOG"] = "error";
        var process = new Process { StartInfo = info };
        if (!process.Start()) throw new InvalidOperationException("Codex app-server did not start.");
        return process;
    }

    private async Task<JsonElement> SendRequestAsync(string method, object? parameters, CancellationToken cancellationToken)
    {
        var process = _process;
        if (process is null || process.HasExited) throw new IOException("Codex app-server is unavailable.");
        var id = Interlocked.Increment(ref _requestId);
        var completion = new TaskCompletionSource<JsonElement>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(id, completion)) throw new InvalidOperationException("Duplicate JSON-RPC id.");
        try
        {
            var message = parameters is null
                ? JsonSerializer.Serialize(new { method, id })
                : JsonSerializer.Serialize(new { method, id, @params = parameters });
            await WriteAsync(process, message, cancellationToken).ConfigureAwait(false);
            return await completion.Task.WaitAsync(TimeSpan.FromSeconds(12), cancellationToken).ConfigureAwait(false);
        }
        finally { _pending.TryRemove(id, out _); }
    }

    private Task SendNotificationAsync(string method, CancellationToken cancellationToken)
    {
        var process = _process ?? throw new IOException("Codex app-server is unavailable.");
        return WriteAsync(process, JsonSerializer.Serialize(new { method }), cancellationToken);
    }

    private async Task WriteAsync(Process process, string line, CancellationToken cancellationToken)
    {
        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await process.StandardInput.WriteLineAsync(line.AsMemory(), cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
            await process.StandardInput.FlushAsync(cancellationToken)
                .WaitAsync(TimeSpan.FromSeconds(3), cancellationToken).ConfigureAwait(false);
        }
        finally { _writeGate.Release(); }
    }

    private async Task ReadLoopAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    using var document = JsonDocument.Parse(line);
                    var root = document.RootElement;
                    if (!TryReadId(root, out var id) || !_pending.TryGetValue(id, out var completion)) continue;
                    if (root.TryGetProperty("error", out var error))
                        completion.TrySetException(new IOException(error.GetRawText()));
                    else if (root.TryGetProperty("result", out var result))
                        completion.TrySetResult(result.Clone());
                }
                catch (JsonException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
        finally
        {
            _initialized = false;
            foreach (var completion in _pending.Values)
                completion.TrySetException(new IOException("Codex app-server disconnected."));
        }
    }

    private static async Task DrainErrorAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (await process.StandardError.ReadLineAsync(cancellationToken).ConfigureAwait(false) is not null) { }
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { }
    }

    private async Task ResetConnectionAsync()
    {
        _initialized = false;
        var process = Interlocked.Exchange(ref _process, null);
        if (process is not null)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: false); }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
            try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (Exception ex) when (ex is InvalidOperationException or TimeoutException) { }
            process.Dispose();
        }
        foreach (var completion in _pending.Values)
            completion.TrySetException(new IOException("Codex app-server connection reset."));
        _pending.Clear();
    }

    private static bool TryReadId(JsonElement root, out long id)
    {
        id = default;
        if (!root.TryGetProperty("id", out var value)) return false;
        return value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out id) ||
               value.ValueKind == JsonValueKind.String && long.TryParse(value.GetString(), out id);
    }

    private static string ResolveExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_CLI_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        if (OperatingSystem.IsWindows())
        {
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var root = Path.Combine(local, "OpenAI", "Codex", "bin");
            if (Directory.Exists(root))
            {
                try
                {
                    var candidate = new DirectoryInfo(root).EnumerateFiles("codex.exe", SearchOption.AllDirectories)
                        .OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault();
                    if (candidate is not null) return candidate.FullName;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        if (OperatingSystem.IsMacOS())
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (var app in new[] { "/Applications/Codex.app", Path.Combine(home, "Applications", "Codex.app") })
            {
                var contents = Path.Combine(app, "Contents");
                foreach (var candidate in new[]
                         {
                             Path.Combine(contents, "Resources", "codex"),
                             Path.Combine(contents, "Resources", "bin", "codex")
                         })
                    if (File.Exists(candidate)) return candidate;
                if (!Directory.Exists(contents)) continue;
                try
                {
                    var discovered = new DirectoryInfo(Path.Combine(contents, "Resources"))
                        .EnumerateFiles("codex", SearchOption.AllDirectories)
                        .OrderByDescending(file => file.LastWriteTimeUtc).FirstOrDefault();
                    if (discovered is not null) return discovered.FullName;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        return OperatingSystem.IsWindows() ? "codex.exe" : "codex";
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeState, 1) != 0) return;
        _lifetime.Cancel();
        await ResetConnectionAsync().ConfigureAwait(false);
        foreach (var task in new[] { _readerTask, _errorTask }.Where(task => task is not null))
            try { await task!.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or TimeoutException or IOException) { }
        _lifetime.Dispose();
        _connectGate.Dispose();
        _writeGate.Dispose();
    }
}
