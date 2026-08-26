using System.IO.Pipes;
using System.Text;

namespace CodexQuota.App;

internal sealed class SingleInstanceGate : IDisposable
{
    private readonly Mutex? _mutex;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _lifetime = new();

    private SingleInstanceGate(Mutex? mutex, string pipeName, bool isPrimary)
    {
        _mutex = mutex;
        _pipeName = pipeName;
        IsPrimary = isPrimary;
        if (isPrimary) _ = ListenAsync(_lifetime.Token);
    }

    public bool IsPrimary { get; }
    public event Action? ActivationRequested;

    public static SingleInstanceGate TryCreate(string name)
    {
        var safe = new string(name.Where(char.IsLetterOrDigit).ToArray());
        var mutex = new Mutex(true, name, out var created);
        return new SingleInstanceGate(mutex, safe + "-activate", created);
    }

    public void NotifyPrimary()
    {
        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect(1_500);
            client.Write(Encoding.UTF8.GetBytes("activate"));
        }
        catch { }
    }

    private async Task ListenAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await using var server = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(cancellationToken);
                var buffer = new byte[16];
                var read = await server.ReadAsync(buffer, cancellationToken);
                if (read > 0) ActivationRequested?.Invoke();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { break; }
            catch { await Task.Delay(250, cancellationToken); }
        }
    }

    public void Dispose()
    {
        _lifetime.Cancel();
        if (IsPrimary)
        {
            try { _mutex?.ReleaseMutex(); } catch { }
        }
        _mutex?.Dispose();
        _lifetime.Dispose();
    }
}
