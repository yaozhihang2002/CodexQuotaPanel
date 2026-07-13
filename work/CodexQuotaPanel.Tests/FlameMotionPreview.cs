using CodexQuotaPanel;

internal static class FlameMotionPreview
{
    internal static void Run(string outputPath)
    {
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        UiPalette.SetTheme(1);
        var now = DateTimeOffset.Now;
        var snapshot = new QuotaSnapshot(
            "codex", null,
            new LimitBucket(10, 300, now.AddHours(2)),
            new LimitBucket(15, 10080, now.AddDays(6)),
            null, "pro", null, now, "App Server");

        const int frameSize = 128;
        const int frameCount = 4;
        using var contact = new Bitmap(frameSize * frameCount, 292);
        using var graphics = Graphics.FromImage(contact);
        graphics.Clear(Color.FromArgb(18, 20, 19));
        using var labelFont = UiPalette.Body(9f, FontStyle.Bold);

        CaptureRow(graphics, snapshot, style: 0, y: 24, frameSize, frameCount);
        CaptureRow(graphics, snapshot, style: 2, y: 164, frameSize, frameCount);
        TextRenderer.DrawText(graphics, "Minimal ember · four consecutive frames", labelFont,
            new Rectangle(10, 2, contact.Width - 20, 22), UiPalette.Mint,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(graphics, "Pixel flame · four consecutive frames", labelFont,
            new Rectangle(10, 142, contact.Width - 20, 22), UiPalette.Mint,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        contact.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"PASS ember + pixel flame motion preview | {fullPath}");
    }

    private static void CaptureRow(
        Graphics graphics,
        QuotaSnapshot snapshot,
        int style,
        int y,
        int frameSize,
        int frameCount)
    {
        using var orb = new QuotaOrbControl
        {
            Size = new Size(frameSize, frameSize),
            BackColor = UiPalette.Canvas
        };
        orb.SetSnapshot(snapshot, live: true);
        orb.SetFlameStyle(style);
        orb.SetConsumptionIntensity(0.88d);
        orb.SetFlameAnimationEnabled(true);
        orb.CreateControl();
        for (var frame = 0; frame < frameCount; frame++)
        {
            var phase = style == 2
                ? frame / 1.65d + 0.04d
                : frame * Math.PI / (2d * 0.72d);
            orb.SetFlamePhaseForTest(phase);
            using var bitmap = new Bitmap(frameSize, frameSize);
            orb.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
            graphics.DrawImageUnscaled(bitmap, frame * frameSize, y);
        }
    }
}
