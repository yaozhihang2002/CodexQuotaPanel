using CodexQuotaPanel;

internal static class AnimationPreview
{
    internal static void Run(string outputPath, bool collapsing = false)
    {
        UiPalette.SetTheme(1);
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        var now = DateTimeOffset.Now;
        using var form = new QuotaForm();
        form.ApplySnapshot(new QuotaSnapshot(
            "codex", null,
            new LimitBucket(18, 300, now.AddHours(3)),
            new LimitBucket(9, 10080, now.AddDays(6)),
            null, "pro", null, now, "App Server",
            ResetCredits: new RateLimitResetCreditsInfo(1,
                [new RateLimitResetCreditInfo("reset-1", "Full reset", null, "available", now.AddDays(5))])));
        if (collapsing)
            form.ShowDetails(animate: false);
        else
            form.ShowOrb(animate: false);
        form.Show();
        Application.DoEvents();

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputPath))!;
        Directory.CreateDirectory(directory);
        var framePaths = new List<string>();
        var sampleTimes = new[] { 0, 60, 120, 195, 300 };
        if (collapsing)
            form.CollapseToOrb(animate: true);
        else
            form.ShowDetails(animate: true);
        for (var index = 0; index < sampleTimes.Length; index++)
        {
            form.SetTransitionPreviewProgress(Math.Clamp(sampleTimes[index] / 300d, 0d, 1d));
            Application.DoEvents();
            var framePath = Path.Combine(directory, $"lamp-frame-{index}.png");
            form.SavePreview(framePath);
            framePaths.Add(framePath);
        }

        const int cellWidth = 382;
        const int cellHeight = 536;
        using var contact = new Bitmap(cellWidth * framePaths.Count, cellHeight);
        using var graphics = Graphics.FromImage(contact);
        graphics.Clear(Color.FromArgb(12, 14, 13));
        using var labelFont = UiPalette.Mono(8f, FontStyle.Bold);
        for (var index = 0; index < framePaths.Count; index++)
        {
            using var frame = new Bitmap(framePaths[index]);
            var x = index * cellWidth + cellWidth - frame.Width - 8;
            var y = cellHeight - frame.Height - 8;
            graphics.DrawImageUnscaled(frame, x, y);
            TextRenderer.DrawText(graphics, $"{sampleTimes[index]} ms", labelFont,
                new Rectangle(index * cellWidth + 10, 8, cellWidth - 20, 24), UiPalette.Mint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        var fullPath = Path.GetFullPath(outputPath);
        contact.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"PASS coordinated {(collapsing ? "collapse" : "expand")} transition frame preview | {fullPath}");
    }
}
