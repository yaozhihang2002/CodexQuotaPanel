using CodexQuotaPanel;

internal static class InnovationPreview
{
    internal static void Run(string outputPath)
    {
        var fullPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullPath)!;
        Directory.CreateDirectory(directory);
        var cells = new List<(string Label, Bitmap Image)>();

        try
        {
            foreach (var theme in new[] { 1, 2 })
            foreach (var language in new[] { AppLanguage.SimplifiedChinese, AppLanguage.English })
            {
                UiPalette.SetTheme(theme);
                L10n.SetLanguage(language);
                var now = DateTimeOffset.Now;
                var minute = now.ToUniversalTime().ToUnixTimeSeconds() / 60;
                var resetCredits = new RateLimitResetCreditInfo[]
                {
                    new("reset-1", "Full reset", null, "available", now.AddDays(2)),
                    new("reset-2", "Full reset", null, "available", now.AddDays(7)),
                    new("reset-3", "Full reset", null, "available", now.AddDays(15)),
                    new("reset-4", "Full reset", null, "available", now.AddDays(28))
                };
                var snapshot = new QuotaSnapshot(
                    "codex", null,
                    new LimitBucket(76, 300, now.AddHours(4)),
                    new LimitBucket(30, 10080, now.AddDays(6)),
                    null, "pro", null, now, "App Server",
                    ResetCredits: new RateLimitResetCreditsInfo(4, resetCredits));
                QuotaHistoryPoint[] history =
                {
                    new(minute - 60, 0, 300, 360),
                    new(minute - 30, 0, 300, 300),
                    new(minute, 0, 300, 240),
                    new(minute - 60, 1, 10080, 720),
                    new(minute - 30, 1, 10080, 710),
                    new(minute, 1, 10080, 700)
                };

                using var form = new QuotaForm();
                form.SetHistory(history);
                form.ApplySnapshot(snapshot);
                form.ShowDetails(animate: false);
                form.Show();
                Application.DoEvents();
                if (form.CurrentRunwayForecast is not { State: QuotaRunwayState.AtRisk })
                    throw new InvalidOperationException("At-risk runway was not rendered in the visual matrix.");
                var cellPath = Path.Combine(directory,
                    $"innovation-{(theme == 1 ? "dark" : "light")}-{(language == AppLanguage.SimplifiedChinese ? "zh" : "en")}.png");
                form.SavePreview(cellPath);
                var image = new Bitmap(cellPath);
                EnsureImageHasContent(image);
                cells.Add(($"{(theme == 1 ? "Dark" : "Light")} · {(language == AppLanguage.SimplifiedChinese ? "中文" : "English")}", image));
            }

            const int gap = 14;
            const int heading = 30;
            var cellWidth = cells.Max(cell => cell.Image.Width);
            var cellHeight = cells.Max(cell => cell.Image.Height);
            using var sheet = new Bitmap(cellWidth * 2 + gap, (cellHeight + heading) * 2 + gap);
            using var graphics = Graphics.FromImage(sheet);
            graphics.Clear(Color.FromArgb(37, 40, 39));
            using var font = UiPalette.Body(9f, FontStyle.Bold);
            for (var index = 0; index < cells.Count; index++)
            {
                var column = index % 2;
                var row = index / 2;
                var x = column * (cellWidth + gap);
                var y = row * (cellHeight + heading + gap);
                TextRenderer.DrawText(graphics, cells[index].Label, font,
                    new Rectangle(x + 8, y, cellWidth - 8, heading), Color.White,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                graphics.DrawImageUnscaled(cells[index].Image, x, y + heading);
            }
            sheet.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine($"PASS runway visual matrix dark/light zh/en | {fullPath}");
        }
        finally
        {
            foreach (var cell in cells) cell.Image.Dispose();
        }
    }

    private static void EnsureImageHasContent(Bitmap image)
    {
        var distinct = new HashSet<int>();
        for (var y = 0; y < image.Height; y += 16)
        for (var x = 0; x < image.Width; x += 16)
            distinct.Add(image.GetPixel(x, y).ToArgb());
        if (distinct.Count < 8)
            throw new InvalidOperationException("Visual matrix produced an incomplete or blank panel image.");
    }
}
