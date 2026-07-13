using CodexQuotaPanel;
using System.Drawing.Drawing2D;
using System.Drawing.Text;

internal static class TrayIconPreview
{
    internal static void Run(string outputPath)
    {
        (double? Remaining, bool Live, string Label)[] samples =
        [
            (null, false, "Connecting"),
            (93, true, "93%"),
            (50, true, "50%"),
            (15, true, "15%"),
            (15, false, "Offline")
        ];

        using var preview = new Bitmap(640, 150);
        using var graphics = Graphics.FromImage(preview);
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
        graphics.Clear(Color.FromArgb(24, 27, 25));
        using var labelFont = UiPalette.Body(9f, FontStyle.Bold);
        using var labelBrush = new SolidBrush(UiPalette.Text);

        for (var index = 0; index < samples.Length; index++)
        {
            var sample = samples[index];
            using var icon = IconFactory.CreateTrayIcon(sample.Remaining, sample.Live);
            using var bitmap = icon.ToBitmap();
            var x = 28 + index * 124;
            graphics.DrawImage(bitmap, new Rectangle(x, 14, 88, 88), 0, 0, 32, 32, GraphicsUnit.Pixel);
            var labelBounds = new Rectangle(x - 10, 112, 108, 24);
            TextRenderer.DrawText(graphics, sample.Label, labelFont, labelBounds, UiPalette.Text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        preview.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"PASS dynamic tray icon preview | {fullPath}");
    }
}
