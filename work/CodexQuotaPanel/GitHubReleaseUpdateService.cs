using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CodexQuotaPanel;

internal enum UpdateCheckStatus
{
    UpToDate,
    UpdateAvailable,
    Unavailable
}

internal sealed record UpdateCheckResult(
    UpdateCheckStatus Status,
    string CurrentVersion,
    string? LatestTag = null,
    Uri? ReleaseUri = null,
    bool FromCache = false);

internal sealed record UpdateCacheModel(
    string Schema,
    int SchemaVersion,
    DateTimeOffset LastCheckedUtc,
    string? ETag,
    string? LatestTag,
    string? LatestUrl);

internal sealed record GitHubReleaseModel(
    [property: JsonPropertyName("tag_name")] string? TagName,
    [property: JsonPropertyName("html_url")] string? HtmlUrl,
    [property: JsonPropertyName("draft")] bool Draft,
    [property: JsonPropertyName("prerelease")] bool Prerelease);

/// <summary>
/// Checks the public GitHub release feed without credentials. It never downloads
/// or launches release assets; the settings UI may only open a validated GitHub
/// release page after explicit confirmation.
/// </summary>
internal sealed class GitHubReleaseUpdateService : IDisposable
{
    private const string ReleasesEndpoint =
        "https://api.github.com/repos/yaozhihang2002/CodexQuotaPanel/releases?per_page=20";
    private const string CacheSchema = "codex-quota-panel.update-cache";
    private const int CacheSchemaVersion = 1;
    private static readonly TimeSpan AutomaticCheckInterval = TimeSpan.FromHours(24);
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    private readonly HttpClient _client;
    private readonly bool _ownsClient;
    private readonly string _cachePath;

    internal GitHubReleaseUpdateService(HttpMessageHandler? handler = null, string? cachePath = null)
    {
        _client = handler is null ? new HttpClient() : new HttpClient(handler, disposeHandler: true);
        _ownsClient = true;
        _client.Timeout = TimeSpan.FromSeconds(8);
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("CodexQuotaPanel", CurrentVersionText));
        _client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        _cachePath = cachePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "CodexQuotaPanel",
            "update-state.json");
    }

    internal static string CurrentVersionText => ProductVersionInfo.Current;

    internal async Task<UpdateCheckResult> CheckAsync(bool force, CancellationToken cancellationToken)
    {
        var currentText = CurrentVersionText;
        var current = SemanticVersion.TryParse(currentText, out var parsedCurrent)
            ? parsedCurrent
            : new SemanticVersion(0, 0, 0, null);
        var cache = LoadCache();

        if (!force && cache is not null &&
            DateTimeOffset.UtcNow - cache.LastCheckedUtc < AutomaticCheckInterval)
            return ResultFromCache(cache, current, currentText);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesEndpoint);
            if (!string.IsNullOrWhiteSpace(cache?.ETag) &&
                EntityTagHeaderValue.TryParse(cache.ETag, out var entityTag))
                request.Headers.IfNoneMatch.Add(entityTag);

            using var response = await _client.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (response.StatusCode == HttpStatusCode.NotModified && cache is not null)
            {
                cache = cache with { LastCheckedUtc = DateTimeOffset.UtcNow };
                SaveCache(cache);
                return ResultFromCache(cache, current, currentText);
            }

            if (!response.IsSuccessStatusCode)
                return CacheFailure(cache, current, currentText);

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            var releases = await JsonSerializer.DeserializeAsync<List<GitHubReleaseModel>>(
                stream,
                JsonOptions,
                cancellationToken).ConfigureAwait(false) ?? [];

            var latest = releases
                .Where(release => !release.Draft &&
                                  SemanticVersion.TryParse(release.TagName, out _) &&
                                  TryValidateReleaseUri(release.HtmlUrl, out _))
                .Select(release => new
                {
                    Release = release,
                    Version = SemanticVersion.TryParse(release.TagName, out var version) ? version : default
                })
                .OrderByDescending(item => item.Version)
                .FirstOrDefault();

            if (latest is null || !TryValidateReleaseUri(latest.Release.HtmlUrl, out var releaseUri))
                return CacheFailure(cache, current, currentText);

            var newCache = new UpdateCacheModel(
                CacheSchema,
                CacheSchemaVersion,
                DateTimeOffset.UtcNow,
                response.Headers.ETag?.ToString(),
                latest.Release.TagName,
                releaseUri.AbsoluteUri);
            SaveCache(newCache);
            return latest.Version.CompareTo(current) > 0
                ? new UpdateCheckResult(
                    UpdateCheckStatus.UpdateAvailable,
                    currentText,
                    latest.Release.TagName,
                    releaseUri)
                : new UpdateCheckResult(
                    UpdateCheckStatus.UpToDate,
                    currentText,
                    latest.Release.TagName,
                    releaseUri);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return CacheFailure(cache, current, currentText);
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or IOException or
                                   InvalidOperationException or NotSupportedException)
        {
            return CacheFailure(cache, current, currentText);
        }
    }

    private UpdateCheckResult CacheFailure(
        UpdateCacheModel? previous,
        SemanticVersion current,
        string currentText)
    {
        var failedAttempt = previous is null
            ? new UpdateCacheModel(
                CacheSchema,
                CacheSchemaVersion,
                DateTimeOffset.UtcNow,
                null,
                null,
                null)
            : previous with { LastCheckedUtc = DateTimeOffset.UtcNow };
        SaveCache(failedAttempt);

        var cached = ResultFromCache(failedAttempt, current, currentText);
        return cached.Status == UpdateCheckStatus.Unavailable
            ? new UpdateCheckResult(UpdateCheckStatus.Unavailable, currentText, FromCache: previous is not null)
            : cached;
    }

    private UpdateCheckResult ResultFromCache(
        UpdateCacheModel cache,
        SemanticVersion current,
        string currentText)
    {
        if (!SemanticVersion.TryParse(cache.LatestTag, out var latest) ||
            !TryValidateReleaseUri(cache.LatestUrl, out var releaseUri))
            return new UpdateCheckResult(UpdateCheckStatus.Unavailable, currentText, FromCache: true);
        return latest.CompareTo(current) > 0
            ? new UpdateCheckResult(UpdateCheckStatus.UpdateAvailable, currentText, cache.LatestTag, releaseUri, true)
            : new UpdateCheckResult(UpdateCheckStatus.UpToDate, currentText, cache.LatestTag, releaseUri, true);
    }

    private UpdateCacheModel? LoadCache()
    {
        try
        {
            if (!File.Exists(_cachePath)) return null;
            var info = new FileInfo(_cachePath);
            if (info.Length is <= 0 or > 64 * 1024) return null;
            var cache = JsonSerializer.Deserialize<UpdateCacheModel>(File.ReadAllText(_cachePath), JsonOptions);
            if (cache is null ||
                !string.Equals(cache.Schema, CacheSchema, StringComparison.Ordinal) ||
                cache.SchemaVersion != CacheSchemaVersion)
                return null;
            return cache;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                                   System.Security.SecurityException or JsonException or
                                   NotSupportedException or ArgumentException)
        {
            return null;
        }
    }

    private void SaveCache(UpdateCacheModel cache) =>
        AtomicJsonFile.TryWrite(_cachePath, JsonSerializer.Serialize(cache, JsonOptions), createBackup: false);

    internal static bool TryValidateReleaseUri(string? value, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var candidate) ||
            !string.Equals(candidate.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(candidate.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
            !candidate.AbsolutePath.StartsWith(
                "/yaozhihang2002/CodexQuotaPanel/releases/",
                StringComparison.OrdinalIgnoreCase))
            return false;
        uri = candidate;
        return true;
    }

    public void Dispose()
    {
        if (_ownsClient) _client.Dispose();
    }
}

internal readonly record struct SemanticVersion(
    int Major,
    int Minor,
    int Patch,
    string? PreRelease) : IComparable<SemanticVersion>
{
    internal static bool TryParse(string? value, out SemanticVersion version)
    {
        version = default;
        if (string.IsNullOrWhiteSpace(value)) return false;
        var text = value.Trim();
        if (text.StartsWith('v') || text.StartsWith('V')) text = text[1..];
        var metadataIndex = text.IndexOf('+');
        if (metadataIndex >= 0) text = text[..metadataIndex];
        string? preRelease = null;
        var preReleaseIndex = text.IndexOf('-');
        if (preReleaseIndex >= 0)
        {
            preRelease = text[(preReleaseIndex + 1)..];
            text = text[..preReleaseIndex];
            if (string.IsNullOrWhiteSpace(preRelease)) return false;
        }
        var parts = text.Split('.');
        if (parts.Length is < 2 or > 3 ||
            !int.TryParse(parts[0], out var major) || major < 0 ||
            !int.TryParse(parts[1], out var minor) || minor < 0 ||
            (parts.Length == 3 && (!int.TryParse(parts[2], out _) || int.Parse(parts[2]) < 0)))
            return false;
        var patch = parts.Length == 3 ? int.Parse(parts[2]) : 0;
        version = new SemanticVersion(major, minor, patch, preRelease);
        return true;
    }

    public int CompareTo(SemanticVersion other)
    {
        var core = Major.CompareTo(other.Major);
        if (core != 0) return core;
        core = Minor.CompareTo(other.Minor);
        if (core != 0) return core;
        core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;
        if (PreRelease is null && other.PreRelease is null) return 0;
        if (PreRelease is null) return 1;
        if (other.PreRelease is null) return -1;
        return ComparePreRelease(PreRelease, other.PreRelease);
    }

    private static int ComparePreRelease(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        for (var index = 0; index < Math.Max(leftParts.Length, rightParts.Length); index++)
        {
            if (index >= leftParts.Length) return -1;
            if (index >= rightParts.Length) return 1;
            var leftNumeric = int.TryParse(leftParts[index], out var leftNumber);
            var rightNumeric = int.TryParse(rightParts[index], out var rightNumber);
            int comparison;
            if (leftNumeric && rightNumeric) comparison = leftNumber.CompareTo(rightNumber);
            else if (leftNumeric) comparison = -1;
            else if (rightNumeric) comparison = 1;
            else comparison = string.Compare(leftParts[index], rightParts[index], StringComparison.OrdinalIgnoreCase);
            if (comparison != 0) return comparison;
        }
        return 0;
    }
}
