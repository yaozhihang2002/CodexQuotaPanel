using CodexQuotaPanel;

internal static class TokenUsagePreview
{
    internal static void Run(string outputPath)
    {
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        var now = DateTimeOffset.Now;
        var reset = now.AddDays(4).AddHours(8);
        var start = reset.AddDays(-7);
        var usage = new TokenCycleUsage(
            start,
            reset,
            10080,
            [
                Day(start, 46_820, 41_200, 18_200, 5_620, 2_140, includeReviewer: true),
                Day(start.AddDays(1), 128_430, 116_000, 62_400, 12_430, 5_280),
                Day(start.AddDays(2), 0, 0, 0, 0, 0),
                Day(start.AddDays(3), 79_610, 72_100, 31_600, 7_510, 3_020)
            ],
            now,
            12,
            new TokenUsageDiagnostics(12, 9, 2, 184, 0, 6, 11, 272_860, 272_860));
        var snapshot = new QuotaSnapshot(
            "codex", null, null,
            new LimitBucket(37, 10080, reset),
            null, "pro", null, now, "App Server");

        using var form = new QuotaForm();
        form.SetTokenCycleUsage(usage);
        form.ApplySnapshot(snapshot);
        form.ShowDetails(animate: false);
        form.Show();
        Application.DoEvents();
        var hover = form.DailyTokenUsage.ShowDayForTest(1);
        if (hover is null || !hover.Contains("128,430", StringComparison.Ordinal) ||
            form.OrbControl.AvailableRingCount != 1 || form.VisibleQuotaRowCount != 1)
            throw new InvalidOperationException("The single-window token preview is incomplete.");
        Application.DoEvents();
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        form.SavePreview(fullPath);
        using (var details = new TokenUsageDetailsForm(usage))
        {
            details.Show();
            Application.DoEvents();
            using var detailsBitmap = details.CaptureContentForTest();
            var detailsPath = Path.Combine(
                Path.GetDirectoryName(fullPath)!,
                Path.GetFileNameWithoutExtension(fullPath) + "-details" + Path.GetExtension(fullPath));
            detailsBitmap.Save(detailsPath);
        }
        form.ShowOrb(animate: false);
        Application.DoEvents();
        var orbPath = Path.Combine(
            Path.GetDirectoryName(fullPath)!,
            Path.GetFileNameWithoutExtension(fullPath) + "-orb" + Path.GetExtension(fullPath));
        form.SavePreview(orbPath);
        Console.WriteLine($"PASS single-window daily token preview | {fullPath} | details | {orbPath}");
    }

    private static DailyTokenUsage Day(
        DateTimeOffset timestamp,
        long total,
        long input,
        long cached,
        long output,
        long reasoning,
        bool includeReviewer = false)
    {
        var usage = new TokenUsageBreakdown(total, input, cached, output, reasoning);
        if (total == 0)
            return new DailyTokenUsage(
                DateOnly.FromDateTime(timestamp.ToLocalTime().DateTime),
                usage,
                []);
        var model = timestamp.Day % 2 == 0 ? "gpt-5.6-sol" : "gpt-5.6-terra";
        var speed = timestamp.Day % 3 == 0 ? "Fast" : "Default";
        var estimate = ApiCostEstimator.Estimate(model, speed, usage);
        if (includeReviewer)
        {
            var reviewerUsage = new TokenUsageBreakdown(18_000, 16_000, 12_000, 2_000, 600);
            return new DailyTokenUsage(
                DateOnly.FromDateTime(timestamp.ToLocalTime().DateTime),
                usage.Add(reviewerUsage),
                [
                    new TokenUsageSlice(model, speed, usage, estimate.Usd, estimate.IsPriced),
                    new TokenUsageSlice("codex-auto-review", "Default", reviewerUsage, 0m, false)
                ]);
        }
        return new DailyTokenUsage(
            DateOnly.FromDateTime(timestamp.ToLocalTime().DateTime),
            usage,
            [new TokenUsageSlice(model, speed, usage, estimate.Usd, estimate.IsPriced)]);
    }
}
