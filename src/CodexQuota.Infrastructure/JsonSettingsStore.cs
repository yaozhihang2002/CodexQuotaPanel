using System.Text.Json;
using CodexQuota.Application;

namespace CodexQuota.Infrastructure;

public sealed class JsonSettingsStore(string path, string? legacyPath = null) : ISettingsStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public async Task<AppSettings?> ReadAsync(CancellationToken cancellationToken)
    {
        var primary = await TryReadCurrentAsync(path, cancellationToken).ConfigureAwait(false);
        if (primary is not null) return primary;

        var backup = await TryReadCurrentAsync(path + ".bak", cancellationToken).ConfigureAwait(false);
        if (backup is not null)
        {
            await WriteAsync(backup, cancellationToken).ConfigureAwait(false);
            return backup;
        }

        var source = legacyPath ?? Path.Combine(Path.GetDirectoryName(path) ?? string.Empty, "preferences.json");
        var migrated = await LegacySettingsMigrator.TryReadAsync(source, cancellationToken).ConfigureAwait(false);
        if (migrated is null) return null;
        await WriteAsync(migrated, cancellationToken).ConfigureAwait(false);
        return migrated;
    }

    public async Task WriteAsync(AppSettings settings, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var normalized = settings.Normalize();
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

        var temporary = path + ".tmp";
        await using (var stream = new FileStream(
                         temporary,
                         FileMode.Create,
                         FileAccess.Write,
                         FileShare.None,
                         16 * 1024,
                         FileOptions.Asynchronous | FileOptions.WriteThrough))
        {
            await JsonSerializer.SerializeAsync(stream, normalized, Options, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        if (File.Exists(path)) File.Copy(path, path + ".bak", true);
        File.Move(temporary, path, true);
    }

    private static async Task<AppSettings?> TryReadCurrentAsync(string candidate, CancellationToken cancellationToken)
    {
        try
        {
            if (!File.Exists(candidate)) return null;
            await using var stream = new FileStream(candidate, FileMode.Open, FileAccess.Read, FileShare.Read,
                16 * 1024, FileOptions.Asynchronous | FileOptions.SequentialScan);
            var settings = await JsonSerializer.DeserializeAsync<AppSettings>(stream, Options, cancellationToken)
                .ConfigureAwait(false);
            return settings?.Normalize();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }
}
