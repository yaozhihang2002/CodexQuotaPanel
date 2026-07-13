using CodexQuotaPanel;

internal static class DetailPanelPreview
{
    internal static void Run(string outputPath)
    {
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        var now = DateTimeOffset.Now;
        var nowMinute = now.ToUniversalTime().ToUnixTimeSeconds() / 60;
        RateLimitResetCreditInfo[] resetCredits =
        [
            new("reset-2", "Full reset", null, "available", now.AddDays(14)),
            new("reset-1", "Full reset", null, "available", now.AddDays(5).AddHours(2)),
            new("reset-3", "Full reset", null, "available", now.AddDays(18)),
            new("reset-4", "Full reset", null, "available", now.AddDays(30))
        ];
        var snapshot = new QuotaSnapshot(
            "codex", null,
            new LimitBucket(11, 300, now.AddHours(4).AddMinutes(18)),
            new LimitBucket(8, 10080, now.AddDays(6).AddHours(18)),
            null, "pro", null, now, "App Server", ResetCredits: new(4, resetCredits));
        QuotaHistoryPoint[] history =
        [
            new(nowMinute - 75, 0, 300, 120),
            new(nowMinute - 60, 0, 300, 980),
            new(nowMinute - 30, 0, 300, 930),
            new(nowMinute, 0, 300, 890),
            new(nowMinute - 60, 1, 10080, 940),
            new(nowMinute, 1, 10080, 920)
        ];

        using var form = new QuotaForm();
        form.SetHistory(history);
        form.ApplySnapshot(snapshot);
        form.ShowDetails(animate: false);
        form.Show();
        Application.DoEvents();
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        form.SavePreview(fullPath);
        Console.WriteLine($"PASS enlarged detail panel with earliest reset-credit expiry | {fullPath}");
    }
}
