using CodexQuotaPanel;

internal static class FlameStatePreview
{
    internal static void Run(string outputPath)
    {
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        UiPalette.SetTheme(1);
        var now = DateTimeOffset.Now;
        var snapshot = new QuotaSnapshot(
            "codex", null,
            new LimitBucket(32, 300, now.AddHours(2)),
            new LimitBucket(71, 10080, now.AddDays(5)),
            null, "pro", null, now, "Preview");
        var states = new (string Label, double Intensity)[]
        {
            ("霜晶", 0d),
            ("冷焰", 0.12d),
            ("温焰", 0.4d),
            ("热焰", 0.68d),
            ("烈焰", 0.92d)
        };
        var styles = new[] { "简约余烬", "流体火焰", "像素火焰" };

        const int cellWidth = 116;
        const int rowHeight = 132;
        const int left = 94;
        const int top = 34;
        using var sheet = new Bitmap(left + states.Length * cellWidth + 12, top + styles.Length * rowHeight + 8);
        using var graphics = Graphics.FromImage(sheet);
        graphics.Clear(UiPalette.Canvas);
        using var heading = UiPalette.Body(8.5f, FontStyle.Bold);
        using var label = UiPalette.Body(8f, FontStyle.Bold);

        for (var column = 0; column < states.Length; column++)
            TextRenderer.DrawText(graphics, states[column].Label, heading,
                new Rectangle(left + column * cellWidth, 7, cellWidth, 24), UiPalette.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        for (var style = 0; style < styles.Length; style++)
        {
            TextRenderer.DrawText(graphics, styles[style], label,
                new Rectangle(8, top + style * rowHeight, left - 12, 96), UiPalette.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            for (var column = 0; column < states.Length; column++)
            {
                using var orb = new QuotaOrbControl { Size = new Size(96, 96) };
                orb.SetSnapshot(snapshot, live: true);
                orb.SetFlameStyle(style);
                orb.SetConsumptionIntensity(states[column].Intensity);
                orb.SetFlamePhaseForTest(1.15d + column * 0.37d);
                orb.CreateControl();
                using var bitmap = orb.RenderTransparentPreview();
                graphics.DrawImageUnscaled(bitmap,
                    left + column * cellWidth + (cellWidth - bitmap.Width) / 2,
                    top + style * rowHeight);
            }
        }

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        sheet.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
        var boundaryPath = Path.Combine(Path.GetDirectoryName(fullPath)!, "flame-transition-boundaries.png");
        RenderBoundarySheet(boundaryPath, snapshot, styles);
        Console.WriteLine($"PASS five-state flame + boundary transitions | {fullPath} | {boundaryPath}");
    }

    private static void RenderBoundarySheet(
        string path,
        QuotaSnapshot snapshot,
        IReadOnlyList<string> styles)
    {
        var samples = new (string Label, double Intensity)[]
        {
            ("静息", 0d),
            ("冷前", 0.029d), ("冷后", 0.031d),
            ("温前", 0.249d), ("温后", 0.251d),
            ("热前", 0.519d), ("热后", 0.521d),
            ("烈前", 0.779d), ("烈后", 0.781d),
            ("烈焰", 0.92d)
        };
        const int cellWidth = 92;
        const int rowHeight = 112;
        const int left = 92;
        const int top = 32;
        using var sheet = new Bitmap(left + samples.Length * cellWidth + 8,
            top + styles.Count * rowHeight + 8);
        using var graphics = Graphics.FromImage(sheet);
        graphics.Clear(UiPalette.Canvas);
        using var heading = UiPalette.Body(7.3f, FontStyle.Bold);
        using var label = UiPalette.Body(7.6f, FontStyle.Bold);

        for (var column = 0; column < samples.Length; column++)
            TextRenderer.DrawText(graphics, samples[column].Label, heading,
                new Rectangle(left + column * cellWidth, 5, cellWidth, 22), UiPalette.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        for (var style = 0; style < styles.Count; style++)
        {
            TextRenderer.DrawText(graphics, styles[style], label,
                new Rectangle(6, top + style * rowHeight, left - 10, 88), UiPalette.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
            for (var column = 0; column < samples.Length; column++)
            {
                using var orb = new QuotaOrbControl { Size = new Size(88, 88) };
                orb.SetSnapshot(snapshot, live: true);
                orb.SetFlameStyle(style);
                orb.SetConsumptionIntensity(samples[column].Intensity);
                orb.SetFlamePhaseForTest(1.35d);
                orb.CreateControl();
                using var bitmap = orb.RenderTransparentPreview();
                graphics.DrawImageUnscaled(bitmap,
                    left + column * cellWidth + (cellWidth - bitmap.Width) / 2,
                    top + style * rowHeight);
            }
        }
        sheet.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }
}
