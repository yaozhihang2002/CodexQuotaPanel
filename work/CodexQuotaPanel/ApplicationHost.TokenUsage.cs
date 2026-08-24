namespace CodexQuotaPanel;

internal sealed partial class QuotaApplicationContext
{
    private readonly TokenUsageHistory _tokenUsageHistory = new();
    private int _tokenUsageRefreshGeneration;

    private void QueueTokenUsageRefresh(QuotaSnapshot snapshot)
    {
        var generation = Interlocked.Increment(ref _tokenUsageRefreshGeneration);
        _ = RefreshTokenUsageAsync(snapshot, generation);
    }

    private async Task RefreshTokenUsageAsync(QuotaSnapshot snapshot, int generation)
    {
        try
        {
            var usage = await _tokenUsageHistory.ReadCurrentCycleAsync(snapshot, _lifetime.Token)
                .ConfigureAwait(false);
            if (generation != Volatile.Read(ref _tokenUsageRefreshGeneration)) return;
            SafeUi(() => _form.SetTokenCycleUsage(usage));
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
        }
    }
}
