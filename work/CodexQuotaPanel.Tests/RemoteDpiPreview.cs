using CodexQuotaPanel;
using System.Drawing.Drawing2D;

internal static class RemoteDpiPreview
{
    internal static void Run(string outputPath)
    {
        UiPalette.SetTheme(1);
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        using var standardOrb = CreateOrb(141);
        using var remoteOrb = CreateOrb(282);
        using var standard = standardOrb.RenderTransparentPreview(96f);
        using var remotePhysical = remoteOrb.RenderTransparentPreview(192f);
        using var remoteNormalized = new Bitmap(141, 141);
        using (var graphics = Graphics.FromImage(remoteNormalized))
        {
            graphics.Clear(Color.Transparent);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(remotePhysical, new Rectangle(0, 0, 141, 141));
        }

        var difference = MeanRgbDifference(standard, remoteNormalized);
        if (difference > 18d)
            throw new InvalidOperationException(
                $"The normalized 200% DPI orb differs too much from 100%: {difference:0.00}");

        const int margin = 18;
        const int cellWidth = 172;
        const int labelHeight = 36;
        using var contact = new Bitmap(margin * 3 + cellWidth * 2, margin * 2 + labelHeight + 141);
        contact.SetResolution(96f, 96f);
        using var canvas = Graphics.FromImage(contact);
        canvas.Clear(Color.FromArgb(16, 19, 18));
        canvas.InterpolationMode = InterpolationMode.HighQualityBicubic;
        var leftCell = new Rectangle(margin, margin + labelHeight, cellWidth, 141);
        var rightCell = new Rectangle(margin * 2 + cellWidth, margin + labelHeight, cellWidth, 141);
        canvas.DrawImage(
            standard,
            new Rectangle(leftCell.X + (cellWidth - 141) / 2, leftCell.Y, 141, 141),
            new Rectangle(Point.Empty, standard.Size),
            GraphicsUnit.Pixel);
        canvas.DrawImage(
            remoteNormalized,
            new Rectangle(rightCell.X + (cellWidth - 141) / 2, rightCell.Y, 141, 141),
            new Rectangle(Point.Empty, remoteNormalized.Size),
            GraphicsUnit.Pixel);
        using var labelFont = UiPalette.MonoPixels(10f, FontStyle.Bold);
        TextRenderer.DrawText(canvas, "100% · 141 px", labelFont,
            new Rectangle(margin, margin, cellWidth, labelHeight), UiPalette.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        TextRenderer.DrawText(canvas, "RDP 200% · 282→141", labelFont,
            new Rectangle(margin * 2 + cellWidth, margin, cellWidth, labelHeight), UiPalette.Text,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        contact.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"PASS remote DPI orb normalization | mean RGB difference={difference:0.00} | {fullPath}");
    }

    private static QuotaOrbControl CreateOrb(int pixelSize)
    {
        var now = DateTimeOffset.Now;
        var orb = new QuotaOrbControl { Size = new Size(pixelSize, pixelSize) };
        orb.ConfigureRings(new RingDisplayConfiguration(
            new RingWindowSelection(300, RingWindowRole.Primary),
            new RingWindowSelection(10080, RingWindowRole.Secondary),
            UiPalette.Mint,
            UiPalette.Sky));
        orb.SetSnapshot(new QuotaSnapshot(
            "codex",
            null,
            new LimitBucket(24, 300, now.AddHours(2)),
            new LimitBucket(69, 10080, now.AddDays(5)),
            null,
            "pro",
            null,
            now,
            "Preview"), live: true);
        orb.SetFlameStyle(2);
        orb.SetConsumptionIntensity(0d);
        return orb;
    }

    private static double MeanRgbDifference(Bitmap left, Bitmap right)
    {
        long total = 0;
        for (var y = 0; y < left.Height; y++)
        for (var x = 0; x < left.Width; x++)
        {
            var a = left.GetPixel(x, y);
            var b = right.GetPixel(x, y);
            total += Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B);
        }

        return total / (left.Width * left.Height * 3d);
    }
}
