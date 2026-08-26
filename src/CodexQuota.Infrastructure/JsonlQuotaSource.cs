using CodexQuota.Application;
using CodexQuota.Domain;

namespace CodexQuota.Infrastructure;

public sealed class JsonlQuotaSource : IQuotaSource
{
    private const int MaximumFiles = 32;
    private readonly string _sessionsRoot;

    public JsonlQuotaSource(string? codexHome = null)
    {
        var home = codexHome ?? new CodexHomeResolver().Resolve();
        _sessionsRoot = Path.Combine(home, "sessions");
    }

    public async Task<OfficialQuotaSnapshot?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!Directory.Exists(_sessionsRoot)) return null;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
        };
        var files = new DirectoryInfo(_sessionsRoot)
            .EnumerateFiles("*.jsonl", options)
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Take(MaximumFiles)
            .ToArray();

        OfficialQuotaSnapshot? newest = null;
        foreach (var file in files)
        {
            var candidate = await ReadLatestAsync(file.FullName, cancellationToken).ConfigureAwait(false);
            if (candidate is not null && (newest is null || candidate.ObservedAt > newest.ObservedAt))
                newest = candidate;
        }
        return newest;
    }

    private static async Task<OfficialQuotaSnapshot?> ReadLatestAsync(
        string path,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete, 64 * 1024,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            using var reader = new StreamReader(stream);
            OfficialQuotaSnapshot? latest = null;
            while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
            {
                var parsed = QuotaPayloadParser.ParseRolloutLine(line);
                if (parsed is not null) latest = parsed;
            }
            return latest;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return null;
        }
    }
}
