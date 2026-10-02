using System.Net;
using System.Text.Json;
using CodexQuota.Application;
using CodexQuota.Domain;
using CodexQuota.Infrastructure;

internal static class ProductivityChecks
{
    public static async Task RunAsync(string root)
    {
        var token = CancellationToken.None;
        Check.True(AppSettings.Default.AutoUpdatePricing, "automatic sync defaults on");
        Check.True((new AppSettings { SchemaVersion = 5, AutoUpdatePricing = false }).Normalize().AutoUpdatePricing,
            "old preview adopts the new default once");
        var settingsStore = new JsonSettingsStore(Path.Combine(root, "sync-settings.json"));
        await settingsStore.WriteAsync(new AppSettings { AutoUpdatePricing = false }, token);
        Check.True(!(await settingsStore.ReadAsync(token))!.AutoUpdatePricing, "explicit opt-out survives restart");

        var feedPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../pricing/catalog-v1.json"));
        if (!File.Exists(feedPath)) feedPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../../pricing/catalog-v1.json"));
        var json = await File.ReadAllTextAsync(feedPath);
        var document = JsonSerializer.Deserialize<PricingDocument>(json, PricingCatalog.JsonOptions)!;
        foreach (var rate in document.Models)
        {
            var tokens = new TokenUsageBreakdown(310000, 300000, 200000, 10000, 3000, 40000);
            var inputFactor = rate.LongContextThreshold is null ? 1 : rate.LongInputMultiplier;
            var outputFactor = rate.LongContextThreshold is null ? 1 : rate.LongOutputMultiplier;
            var expected = (60000 * rate.Input * inputFactor + 40000 * rate.Input * rate.CacheWriteMultiplier * inputFactor +
                200000 * rate.CachedInput * inputFactor + 10000 * rate.Output * outputFactor) / 1000000;
            Check.Equal(expected, ApiCostEstimator.Estimate(rate.Model, "default", tokens).Usd, "feed matches built-in " + rate.Model);
        }
        using var handler = new FeedHandler(json);
        using var client = new HttpClient(handler);
        var cache = Path.Combine(root, "prices.json");
        var updater = new PricingCatalogUpdater(cache, client);
        Check.Equal(PricingUpdateStatus.Current, await updater.UpdateAsync(token), "same catalog is not reapplied");
        handler.Body = "{bad";
        Check.Equal(PricingUpdateStatus.Unavailable, await updater.UpdateAsync(token), "bad JSON rejected");
        handler.Body = new string('x', PricingCatalogUpdater.MaximumBytes + 1);
        Check.Equal(PricingUpdateStatus.Unavailable, await updater.UpdateAsync(token), "oversize body rejected");
        handler.Status = HttpStatusCode.Found;
        Check.Equal(PricingUpdateStatus.Unavailable, await updater.UpdateAsync(token), "redirect is not a feed");
        handler.Status = HttpStatusCode.NotFound;
        Check.Equal(PricingUpdateStatus.NotPublished, await updater.UpdateAsync(token), "missing table is distinguished");
        handler.Status = HttpStatusCode.OK;
        handler.Body = JsonSerializer.Serialize(document with { Models = document.Models.Take(1).ToArray() }, PricingCatalog.JsonOptions);
        Check.Equal(PricingUpdateStatus.Unavailable, await updater.UpdateAsync(token), "incomplete table rejected");

        var edited = document with { Models = document.Models.Append(
            new ModelRate("future-model", "Future model", 3, .3m, 12, document.CheckedAt)).ToArray() };
        handler.Body = JsonSerializer.Serialize(edited, PricingCatalog.JsonOptions);
        Check.Equal(PricingUpdateStatus.Updated, await updater.UpdateAsync(token), "same revision supports a new model");
        Check.Equal("Future model", ApiCostEstimator.DisplayModel("future-model"), "new model recognized without binary update");
        Check.Equal(.003m, ApiCostEstimator.Estimate("future-model", "default", new(1000,1000,0,0,0)).Usd, "new model price applied");
        edited = edited with { Models = edited.Models.Select(r => r.Model == "future-model" ? r with { Input = 4 } : r).ToArray() };
        handler.Body = JsonSerializer.Serialize(edited, PricingCatalog.JsonOptions);
        handler.ApiStatus = HttpStatusCode.ServiceUnavailable;
        handler.RawCalls = 0;
        Check.Equal(PricingUpdateStatus.Updated, await updater.UpdateAsync(token), "API failure falls back to raw and applies edited price");
        Check.Equal(1, handler.RawCalls, "fallback actually requested");
        Check.Equal(.004m, ApiCostEstimator.Estimate("future-model", "default", new(1000,1000,0,0,0)).Usd, "same revision price edit applied");
        handler.ApiStatus = null;
        Check.Equal(PricingUpdateStatus.Current, await updater.UpdateAsync(token), "unchanged edited table is current");
        var newer = edited with { Revision = edited.Revision + 1 };
        handler.Body = JsonSerializer.Serialize(newer, PricingCatalog.JsonOptions);
        Check.Equal(PricingUpdateStatus.Updated, await updater.UpdateAsync(token), "new catalog generation applied");
        var saved = await File.ReadAllTextAsync(cache);
        handler.Body = json;
        Check.Equal(PricingUpdateStatus.Outdated, await updater.UpdateAsync(token), "downgrade rejected");
        Check.Equal(saved, await File.ReadAllTextAsync(cache), "rollback preserves last good cache");
        handler.Status = HttpStatusCode.ServiceUnavailable;
        Check.Equal(PricingUpdateStatus.Unavailable, await updater.UpdateAsync(token), "network failure is nonfatal");
        Check.Equal(saved, await File.ReadAllTextAsync(cache), "failure preserves last good cache");
        Check.True(updater.LastAttempt > DateTimeOffset.MinValue, "failed attempts participate in daily throttle");

        var cachedUpdate = newer with { Models = newer.Models.Select(r => r.Model == "future-model" ? r with { Input = 5 } : r).ToArray() };
        await File.WriteAllTextAsync(cache, JsonSerializer.Serialize(cachedUpdate, PricingCatalog.JsonOptions));
        var reloaded = new PricingCatalogUpdater(cache, client);
        await reloaded.LoadAsync(token);
        Check.Equal(.005m, ApiCostEstimator.Estimate("future-model", "default", new(1000,1000,0,0,0)).Usd, "cache applied without network");
        Check.Equal(updater.LastAttempt, reloaded.LastAttempt, "daily throttle survives restart");
        await File.WriteAllTextAsync(cache, "broken");
        await reloaded.LoadAsync(token);
        Check.Equal(.005m, ApiCostEstimator.Estimate("future-model", "default", new(1000,1000,0,0,0)).Usd, "bad cache preserves active catalog");
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try { await updater.UpdateAsync(cancelled.Token); throw new InvalidOperationException("Cancellation ignored"); }
        catch (OperationCanceledException) { }
        Console.WriteLine("Pricing checks passed (default on, opt-out, content edits, validation, fallback, persistence and cancellation).");
    }

    private sealed class FeedHandler(string body) : HttpMessageHandler
    {
        public string Body { get; set; } = body;
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public HttpStatusCode? ApiStatus { get; set; }
        public int RawCalls { get; set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
        {
            var api = request.RequestUri!.AbsoluteUri == PricingCatalogUpdater.FeedUrl;
            Check.True(api || request.RequestUri.AbsoluteUri == PricingCatalogUpdater.RawFeedUrl, "only fixed GitHub endpoints requested");
            Check.True(request.Headers.Authorization is null, "no user login required");
            Check.True(request.Headers.UserAgent.Count > 0, "GitHub user agent supplied");
            if (api) Check.True(request.Headers.Accept.Any(a => a.MediaType == "application/vnd.github.raw+json"), "API returns file bytes");
            else RawCalls++;
            return Task.FromResult(new HttpResponseMessage(api ? ApiStatus ?? Status : Status) { Content = new StringContent(Body) });
        }
    }
}
