using System.Net;
using System.Net.Http.Headers;
using System.Text;
using CodexQuota.Domain;

namespace CodexQuota.Infrastructure;

public enum PricingUpdateStatus { Updated, Current, NotPublished, Outdated, Unavailable }

public sealed class PricingCatalogUpdater(string cachePath, HttpClient client)
{
    public const string FeedUrl = "https://api.github.com/repos/yaozhihang2002/CodexQuotaPanel/contents/pricing/catalog-v1.json?ref=main";
    public const string RawFeedUrl = "https://raw.githubusercontent.com/yaozhihang2002/CodexQuotaPanel/main/pricing/catalog-v1.json";
    public const int MaximumBytes = 128 * 1024;
    private static readonly string[] RequiredModels = ["gpt-6-astra", "gpt-6.1-sol", "gpt-6-sol", "gpt-6-luna", "gpt-5.6-sol",
        "gpt-5.6-terra", "gpt-5.6-luna", "gpt-5.5", "gpt-5.4", "gpt-5.4-mini", "gpt-5.3-codex", "codex-auto-review"];
    private readonly SemaphoreSlim _gate = new(1, 1);
    public DateTimeOffset LastAttempt { get; private set; }

    public async Task LoadAsync(CancellationToken token)
    {
        try
        {
            if (File.Exists(cachePath) && new FileInfo(cachePath).Length <= MaximumBytes)
                ApiCostEstimator.ApplyCatalog(ParseComplete(await File.ReadAllTextAsync(cachePath, token)));
            if (File.Exists(cachePath + ".checked") && DateTimeOffset.TryParse(
                    await File.ReadAllTextAsync(cachePath + ".checked", token), out var checkedAt))
                LastAttempt = checkedAt > DateTimeOffset.UtcNow ? DateTimeOffset.MinValue : checkedAt;
        }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException) { }
    }

    public async Task<PricingUpdateStatus> UpdateAsync(CancellationToken token)
    {
        // A manual sync may arrive during the silent startup check. Queue it
        // instead of reporting a false network failure to the user.
        await _gate.WaitAsync(token);
        var temp = cachePath + ".tmp";
        try
        {
            LastAttempt = DateTimeOffset.UtcNow;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(cachePath))!);
            await File.WriteAllTextAsync(cachePath + ".checked", LastAttempt.ToString("O"), token);
            var missing = 0;
            foreach (var url in new[] { FeedUrl, RawFeedUrl })
            {
                string json;
                try { json = await DownloadAsync(url, token); }
                catch (HttpRequestException e) when (e.StatusCode == HttpStatusCode.NotFound) { missing++; continue; }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException) { continue; }

                var catalog = ParseComplete(json);
                if (ApiCostEstimator.IsOlderCatalog(catalog)) return PricingUpdateStatus.Outdated;
                if (ApiCostEstimator.IsCurrentCatalog(catalog)) return PricingUpdateStatus.Current;
                // Content changes at the same revision are valid: maintainers can edit
                // the GitHub table directly without changing the application version.
                await File.WriteAllTextAsync(temp, json, token);
                File.Move(temp, cachePath, true);
                ApiCostEstimator.ApplyCatalog(catalog);
                return PricingUpdateStatus.Updated;
            }
            return missing == 2 ? PricingUpdateStatus.NotPublished : PricingUpdateStatus.Unavailable;
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is IOException or InvalidDataException or UnauthorizedAccessException or HttpRequestException or
            System.Text.Json.JsonException or ArgumentException or OperationCanceledException)
        { return PricingUpdateStatus.Unavailable; }
        finally
        {
            try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            _gate.Release();
        }
    }

    private async Task<string> DownloadAsync(string url, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(5));
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.UserAgent.ParseAdd("CodexQuotaPanel/0.8.0");
        request.Headers.Accept.ParseAdd("application/vnd.github.raw+json");
        request.Headers.CacheControl = new CacheControlHeaderValue { NoCache = true };
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > MaximumBytes) throw new InvalidDataException("Oversize pricing feed.");
        await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token);
        using var buffer = new MemoryStream();
        var chunk = new byte[4096];
        int count;
        while ((count = await stream.ReadAsync(chunk, deadline.Token)) != 0)
        {
            if (buffer.Length + count > MaximumBytes) throw new InvalidDataException("Oversize pricing feed.");
            buffer.Write(chunk, 0, count);
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static PricingCatalog ParseComplete(string json)
    {
        var catalog = PricingCatalog.Parse(json);
        if (RequiredModels.Any(model => !catalog.Models.ContainsKey(model)))
            throw new InvalidDataException("Incomplete pricing feed.");
        return catalog;
    }
}
