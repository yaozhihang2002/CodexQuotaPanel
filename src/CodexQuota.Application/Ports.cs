using CodexQuota.Domain;

namespace CodexQuota.Application;

public interface IQuotaSource
{
    Task<OfficialQuotaSnapshot?> ReadAsync(CancellationToken cancellationToken);
}

public interface IUsageEventSource
{
    IAsyncEnumerable<ObservedUsage> WatchAsync(CancellationToken cancellationToken);
}

public interface IUsageHistoryStore
{
    Task InitializeAsync(CancellationToken cancellationToken);
    Task AppendQuotaAsync(OfficialQuotaSnapshot snapshot, CancellationToken cancellationToken);
    Task AppendUsageAsync(ObservedUsage usage, CancellationToken cancellationToken);
    Task<IReadOnlyList<QuotaHistoryPoint>> ReadQuotaAsync(DateTimeOffset since, CancellationToken cancellationToken);
    Task<IReadOnlyList<ObservedUsage>> ReadUsageAsync(DateTimeOffset since, CancellationToken cancellationToken);
}

public interface ISettingsStore
{
    Task<AppSettings?> ReadAsync(CancellationToken cancellationToken);
    Task WriteAsync(AppSettings settings, CancellationToken cancellationToken);
}

public interface IPlatformShell
{
    string PlatformName { get; }
    bool SupportsClickThrough { get; }
    bool SupportsMenuBarOrTray { get; }
}
