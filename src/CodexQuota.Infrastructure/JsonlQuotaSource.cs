using CodexQuota.Application;
using CodexQuota.Domain;
using System.Text;

namespace CodexQuota.Infrastructure;

public sealed class JsonlQuotaSource : IQuotaSource
{
    private const int MaximumFiles = 32;
    private readonly string[] _sessionRoots;

    public JsonlQuotaSource(string? codexHome = null)
    {
        var home = codexHome ?? new CodexHomeResolver().Resolve();
        _sessionRoots = [Path.Combine(home, "sessions"), Path.Combine(home, "archived_sessions")];
    }

    public async Task<OfficialQuotaSnapshot?> ReadAsync(CancellationToken cancellationToken)
    {
        if (!_sessionRoots.Any(Directory.Exists)) return null;
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = true,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
        };
        var files = _sessionRoots.Where(Directory.Exists)
            .SelectMany(root => new DirectoryInfo(root).EnumerateFiles("*.jsonl", options))
            .GroupBy(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.OrderByDescending(file => file.LastWriteTimeUtc)
                .ThenByDescending(file => file.Length).First())
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
                FileOptions.Asynchronous | FileOptions.RandomAccess);
            const int chunkSize = 64 * 1024;
            const int maximumLineBytes = 32 * 1024 * 1024;
            var position = stream.Length;
            byte[] suffix = [];
            while (position > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = (int)Math.Min(chunkSize, position);
                position -= count;
                stream.Position = position;
                var chunk = new byte[count];
                await stream.ReadExactlyAsync(chunk, cancellationToken).ConfigureAwait(false);
                var data = new byte[count + suffix.Length];
                Buffer.BlockCopy(chunk, 0, data, 0, count);
                if (suffix.Length > 0) Buffer.BlockCopy(suffix, 0, data, count, suffix.Length);

                var lineEnd = data.Length;
                for (var index = data.Length - 1; index >= 0; index--)
                {
                    if (data[index] != (byte)'\n') continue;
                    if (lineEnd > index + 1)
                    {
                        var parsed = ParseLine(data.AsSpan(index + 1, lineEnd - index - 1));
                        if (parsed is not null) return parsed;
                    }
                    lineEnd = index;
                }

                if (position == 0 && lineEnd > 0)
                {
                    var parsed = ParseLine(data.AsSpan(0, lineEnd));
                    if (parsed is not null) return parsed;
                }
                if (lineEnd > maximumLineBytes) return null;
                suffix = data.AsSpan(0, lineEnd).ToArray();
            }
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FileNotFoundException)
        {
            return null;
        }
    }

    private static OfficialQuotaSnapshot? ParseLine(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0) return null;
        var line = Encoding.UTF8.GetString(bytes).TrimStart('\uFEFF').TrimEnd('\r');
        return line.Contains("\"rate_limits\"", StringComparison.Ordinal)
            ? QuotaPayloadParser.ParseRolloutLine(line)
            : null;
    }
}
