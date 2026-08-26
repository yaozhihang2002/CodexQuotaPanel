namespace CodexQuota.App;

internal sealed class LegacyExitSignal : IDisposable
{
    private readonly EventWaitHandle? _signal;
    private readonly CancellationTokenSource _lifetime = new();

    private LegacyExitSignal(EventWaitHandle? signal)
    {
        _signal = signal;
        if (signal is not null) _ = WaitAsync();
    }

    public event Action? ExitRequested;

    public static LegacyExitSignal Create()
    {
        if (!OperatingSystem.IsWindows()) return new LegacyExitSignal(null);
        try { return new LegacyExitSignal(new EventWaitHandle(false, EventResetMode.AutoReset,
            @"Local\CodexQuotaPanel.Exit.v1")); }
        catch { return new LegacyExitSignal(null); }
    }

    private async Task WaitAsync()
    {
        while (!_lifetime.IsCancellationRequested && _signal is not null)
        {
            var index = await Task.Run(() => WaitHandle.WaitAny([_signal, _lifetime.Token.WaitHandle]));
            if (index != 0 || _lifetime.IsCancellationRequested) break;
            ExitRequested?.Invoke();
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        _signal?.Dispose();
        _lifetime.Dispose();
    }
}
