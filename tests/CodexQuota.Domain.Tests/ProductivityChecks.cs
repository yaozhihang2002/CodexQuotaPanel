using CodexQuota.Domain;

internal static class ProductivityChecks
{
    public static void Run()
    {
        var start = DateTimeOffset.Parse("2026-10-01T10:00:00Z");
        var old = new OfficialQuotaSnapshot(start, [new("5h", 300, 12, start.AddMinutes(1))]);
        var next = old with { ObservedAt = start.AddMinutes(2), Windows = [new("5h", 300, 99, start.AddHours(5))] };
        Check.Equal(1, QuotaResetDetector.Detect(old, next).Count, "confirmed rollover");
        Check.Equal(0, QuotaResetDetector.Detect(null, next).Count, "startup is silent");
        Check.Equal(0, QuotaResetDetector.Detect(old, next with { IsStale = true }).Count, "stale data is silent");
        Check.Equal(0, QuotaResetDetector.Detect(old, next with { ObservedAt = start }).Count, "replayed data is silent");
        Check.Equal(0, QuotaResetDetector.Detect(next, next).Count, "same cycle is silent");
        Check.Equal(0, QuotaResetDetector.Detect(old, next with { ObservedAt = start.AddSeconds(20) }).Count,
            "quota correction before boundary is not reset");
        Check.Equal(0, QuotaResetDetector.Detect(old, next with { PlanType = "other" }).Count, "plan changes are silent");
        Check.Equal(0, QuotaResetDetector.Detect(old, next with { Windows = [new("5h",300,11,start.AddHours(5))] }).Count,
            "new boundary without recovered quota is silent");
        Check.Equal(1, QuotaResetDetector.Detect(old with { ObservedAt = start.AddSeconds(90) }, next).Count,
            "late server reset is still recognized");

        var rate = new ModelRate("future-model", "Future model", 2m, .1m, 10m, "2026-09-30", 2m, 1.25m, 272000, 2, 1.5m);
        var document = new PricingDocument(1, PricingCatalog.BuiltInRevision + 1, "2026-09-30", [rate]);
        Check.Equal(1, new PricingCatalog(document).Models.Count, "valid new model catalog");
        Reject(document with { SchemaVersion = 2 });
        Reject(document with { CheckedAt = "bad date" });
        Reject(document with { Models = [rate, rate] });
        Reject(document with { Models = [rate with { Input = -1 }] });
        Reject(document with { Models = [rate with { CachedInput = 100 }] });
        Reject(document with { Models = [rate with { FastMultiplier = 0 }] });
        Reject(document with { Models = [rate with { LongContextThreshold = -1 }] });
        Reject(document with { Models = [rate with { Model = "../script" }] });
        Reject(document with { Models = [rate with { BasisDate = "2026-10-01" }] });
        Console.WriteLine("Reset and pricing validation checks passed: 19");
    }

    private static void Reject(PricingDocument document)
    {
        try { _ = new PricingCatalog(document); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException("Invalid pricing document accepted.");
    }
}
