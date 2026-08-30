using System.Text.Json;
using CodexQuota.Application;
using CodexQuota.Domain;

namespace CodexQuota.Infrastructure;

public sealed class JsonTrustedQuotaSnapshotStore(string path) : ITrustedQuotaSnapshotStore
{
    private const int CurrentSchemaVersion = 1;
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<OfficialQuotaSnapshot?> ReadAsync(CancellationToken cancellationToken)
    {
        var primary = await TryReadAsync(path, cancellationToken).ConfigureAwait(false);
        if (primary is not null) return primary;

        var backup = await TryReadAsync(path + ".bak", cancellationToken).ConfigureAwait(false);
        if (backup is not null) await WriteEnvelopeAsync(backup, rotateBackup: false, cancellationToken).ConfigureAwait(false);
        return backup;
    }

    public async Task WriteAsync(OfficialQuotaSnapshot snapshot, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.VisibleWindows.Count == 0)
            throw new ArgumentException("Trusted quota snapshot must contain a visible window.", nameof(snapshot));

        await WriteEnvelopeAsync(snapshot, rotateBackup: true, cancellationToken).ConfigureAwait(false);
    }

    private async Task WriteEnvelopeAsync(
        OfficialQuotaSnapshot snapshot,
        bool rotateBackup,
        CancellationToken cancellationToken)
    {

        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);
        var temporary = path + ".tmp";
        await using (var stream = new FileStream(temporary, FileMode.Create, FileAccess.Write, FileShare.None,
                         16 * 1024, FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream,
                new TrustedQuotaEnvelope(CurrentSchemaVersion, snapshot), Options, cancellationToken)
                .ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        if (rotateBackup && File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.Move(temporary, path, true);
    }

    private static async Task<OfficialQuotaSnapshot?> TryReadAsync(
        string candidate,
        CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(candidate)) return null;
            await using var stream = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var envelope = await JsonSerializer.DeserializeAsync<TrustedQuotaEnvelope>(stream, Options,
                cancellationToken).ConfigureAwait(false);
            return envelope is { SchemaVersion: CurrentSchemaVersion, Snapshot.VisibleWindows.Count: > 0 }
                ? envelope.Snapshot
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    private sealed record TrustedQuotaEnvelope(int SchemaVersion, OfficialQuotaSnapshot Snapshot);
}
