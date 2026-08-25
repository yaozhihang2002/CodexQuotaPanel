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
