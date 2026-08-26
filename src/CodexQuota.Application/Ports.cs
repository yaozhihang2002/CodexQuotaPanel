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
    Task ClearAsync(CancellationToken cancellationToken);
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
    bool SupportsGlobalShortcut { get; }
    bool GetStartWithSystem();
    AppLanguage? GetInitialLanguage();
    void SetStartWithSystem(bool enabled);
    void SetClickThrough(nint nativeWindowHandle, bool enabled);
    void SetWindowTopMost(nint nativeWindowHandle, bool enabled);
    void SetWindowDarkMode(nint nativeWindowHandle, bool enabled);
    IGlobalShortcutRegistration? RegisterRecoveryShortcut(Action callback);
    void PlayAlertSound();
    void OpenUri(Uri uri);
    void RestartApplication();
}

public interface IGlobalShortcutRegistration : IDisposable
{
    bool IsRegistered { get; }
}
