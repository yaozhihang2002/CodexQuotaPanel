using CodexQuotaPanel;
using System.Drawing.Drawing2D;

internal static class FlameStylePreview
{
    internal static void Run(string outputPath)
    {
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        var now = DateTimeOffset.Now;
        var snapshot = new QuotaSnapshot(
            "codex", null,
            new LimitBucket(10, 300, now.AddHours(2)),
            new LimitBucket(15, 10080, now.AddDays(6)),
            null, "pro", null, now, "App Server");
        var labels = new[] { "简约余烬", "流体火焰", "像素火焰" };

        using var preview = new Bitmap(510, 190);
        using var graphics = Graphics.FromImage(preview);
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.Clear(Color.FromArgb(20, 22, 21));
        using var labelFont = UiPalette.Body(10f, FontStyle.Bold);

        for (var style = 0; style < 3; style++)
        {
            using var orb = new QuotaOrbControl
            {
                Size = new Size(128, 128),
                BackColor = UiPalette.Canvas
            };
            orb.SetSnapshot(snapshot, live: true);
            orb.SetFlameStyle(style);
            orb.SetConsumptionIntensity(0.88d);
            orb.SetFlameAnimationEnabled(true);
            orb.CreateControl();
            using var orbBitmap = new Bitmap(128, 128);
            orb.DrawToBitmap(orbBitmap, new Rectangle(0, 0, 128, 128));
            var x = 27 + style * 165;
            graphics.DrawImageUnscaled(orbBitmap, x, 12);
            TextRenderer.DrawText(graphics, labels[style], labelFont,
                new Rectangle(x - 6, 148, 140, 28), UiPalette.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        preview.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"PASS flame style preview | {fullPath}");
    }
}
