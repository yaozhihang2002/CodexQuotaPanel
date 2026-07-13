using CodexQuotaPanel;

internal static class HoverPreviewScreenshot
{
    internal static void Run(string outputPath)
    {
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        var now = DateTimeOffset.Now;
        var snapshot = new QuotaSnapshot(
            "codex", null,
            new LimitBucket(10, 300, now.AddHours(6).AddMinutes(19)),
            new LimitBucket(100, 10080, null),
            null, "pro", null, now, "App Server");
        using var preview = new HoverPeekForm();
        preview.SetData(snapshot, RingDisplayConfiguration.FromPreferences(PanelPreferenceManager.Default));
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        preview.SavePreview(fullPath);
        Console.WriteLine($"PASS enlarged hover preview | {fullPath}");
    }
}
