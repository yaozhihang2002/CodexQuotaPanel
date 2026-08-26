using System.Diagnostics;
using CodexQuota.Infrastructure;

namespace CodexQuota.App;

internal static class UsageIndexWorker
{
    private const string DataRootVariable = "CODEXQUOTA_INDEX_DATA_ROOT";

    public static async Task<int> RunAsync(CancellationToken cancellationToken = default)
    {
        var configuredRoot = Environment.GetEnvironmentVariable(DataRootVariable);
        if (string.IsNullOrWhiteSpace(configuredRoot)) return 2;
        try
        {
            var dataRoot = Path.GetFullPath(configuredRoot);
            Directory.CreateDirectory(dataRoot);
            var history = new SqliteUsageHistoryStore(Path.Combine(dataRoot, "history-vnext.db"));
            await history.InitializeAsync(cancellationToken).ConfigureAwait(false);
            var source = new JsonlUsageEventSource(
                cursorStatePath: Path.Combine(dataRoot, "usage-file-state.jsonl"));
            await foreach (var batch in source.ScanExistingBatchesAsync(cancellationToken).ConfigureAwait(false))
                await history.AppendUsageBatchAsync(batch, cancellationToken).ConfigureAwait(false);
            return 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return 3;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            return 1;
        }
    }

    public static async Task<bool> RunChildAsync(string dataRoot, CancellationToken cancellationToken)
    {
        var executable = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executable) || !File.Exists(executable)) return false;
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = executable,
                UseShellExecute = false,
                CreateNoWindow = true
            }
        };
        process.StartInfo.ArgumentList.Add("--index-usage");
        process.StartInfo.Environment[DataRootVariable] = Path.GetFullPath(dataRoot);
        if (!process.Start()) return false;
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
            return process.ExitCode == 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
    }
}
