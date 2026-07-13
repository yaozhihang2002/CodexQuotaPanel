namespace CodexQuotaPanel;

internal sealed class UiAnimationTimer : IDisposable
{
    private readonly Control _dispatcher;
    private readonly Action _tick;
    private readonly int _intervalMilliseconds;
    private readonly System.Threading.Timer _timer;
    private int _running;
    private int _queued;
    private int _disposed;

    public UiAnimationTimer(Control dispatcher, int intervalMilliseconds, Action tick)
    {
        _dispatcher = dispatcher;
        _tick = tick;
        _intervalMilliseconds = Math.Max(1, intervalMilliseconds);
        _timer = new System.Threading.Timer(OnPulse, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        Volatile.Write(ref _running, 1);
        _timer.Change(_intervalMilliseconds, _intervalMilliseconds);
    }

    public void Stop()
    {
        Volatile.Write(ref _running, 0);
        _timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private void OnPulse(object? state)
    {
        if (Volatile.Read(ref _running) == 0 || Volatile.Read(ref _disposed) != 0 ||
            Interlocked.CompareExchange(ref _queued, 1, 0) != 0)
            return;

        try
        {
            _dispatcher.BeginInvoke((Action)(() =>
            {
                Interlocked.Exchange(ref _queued, 0);
                if (Volatile.Read(ref _running) != 0 && Volatile.Read(ref _disposed) == 0)
                    _tick();
            }));
        }
        catch (InvalidOperationException)
        {
            Interlocked.Exchange(ref _queued, 0);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        Volatile.Write(ref _running, 0);
        _timer.Dispose();
    }
}
