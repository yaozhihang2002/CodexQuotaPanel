namespace CodexQuotaPanel;

internal sealed class QuotaCoordinator : IAsyncDisposable
{
    private readonly LogQuotaSource _logSource = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly object _stateGate = new();
    private AppServerQuotaSource? _appServer;
    private QuotaSnapshot? _latestRpc;
    private QuotaSnapshot? _latestLog;
    private Task? _connectionLoop;
    private bool _disposed;

    public event Action<QuotaSnapshot>? SnapshotChanged;
    public event Action<string>? StatusChanged;

    public QuotaCoordinator()
    {
        _logSource.SnapshotAvailable += OnLogSnapshot;
        _logSource.StatusChanged += status =>
        {
            lock (_stateGate)
            {
                if (_latestRpc is null) StatusChanged?.Invoke(status);
            }
        };
    }

    public async Task StartAsync()
    {
        await _logSource.StartAsync().ConfigureAwait(false);
        _connectionLoop = Task.Run(() => ConnectAppServerAsync(_lifetime.Token));
    }

    public async Task RefreshAsync()
    {
        var tasks = new List<Task> { _logSource.RefreshAsync() };
        AppServerQuotaSource? appServer;
        lock (_stateGate) appServer = _appServer;
        if (appServer is not null) tasks.Add(appServer.RefreshAsync());
        await Task.WhenAll(tasks).ConfigureAwait(false);
    }

    private async Task ConnectAppServerAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && !_disposed)
        {
            var source = new AppServerQuotaSource();
            var disconnected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            source.SnapshotAvailable += OnRpcSnapshot;
            source.StatusChanged += status => StatusChanged?.Invoke(status);
            source.Disconnected += () => disconnected.TrySetResult();
            lock (_stateGate) _appServer = source;

            var started = await source.TryStartAsync(cancellationToken).ConfigureAwait(false);
            if (started)
            {
                try { await disconnected.Task.WaitAsync(cancellationToken).ConfigureAwait(false); }
                catch (OperationCanceledException) { }
                QuotaSnapshot? fallback;
                lock (_stateGate)
                {
                    _latestRpc = null;
                    fallback = _latestLog;
                }
                if (fallback is not null) SnapshotChanged?.Invoke(fallback);
            }

            await source.DisposeAsync().ConfigureAwait(false);
            lock (_stateGate)
            {
                if (ReferenceEquals(_appServer, source)) _appServer = null;
            }

            if (cancellationToken.IsCancellationRequested || _disposed) return;

            var retryDelay = started ? TimeSpan.FromSeconds(15) : TimeSpan.FromMinutes(5);
            try { await Task.Delay(retryDelay, cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; }
        }
    }

    private void OnLogSnapshot(QuotaSnapshot snapshot)
    {
        var shouldPublish = false;
        lock (_stateGate)
        {
            _latestLog = snapshot;
            shouldPublish = _latestRpc is null;
        }
        if (shouldPublish) SnapshotChanged?.Invoke(snapshot);
    }

    private void OnRpcSnapshot(QuotaSnapshot snapshot)
    {
        lock (_stateGate) _latestRpc = snapshot;
        SnapshotChanged?.Invoke(snapshot);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _lifetime.Cancel();
        _logSource.Dispose();
        if (_connectionLoop is not null)
        {
            try { await _connectionLoop.ConfigureAwait(false); }
            catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException) { }
        }
        else
        {
            AppServerQuotaSource? appServer;
            lock (_stateGate) appServer = _appServer;
            if (appServer is not null) await appServer.DisposeAsync().ConfigureAwait(false);
        }
        _lifetime.Dispose();
    }
}
