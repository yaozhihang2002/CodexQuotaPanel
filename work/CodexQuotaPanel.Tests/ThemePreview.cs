using CodexQuotaPanel;
using System.Drawing.Drawing2D;

internal static class ThemePreview
{
    internal static void Run(string outputPath)
    {
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var darkPath = Path.Combine(Path.GetDirectoryName(fullPath)!, "theme-dark.png");
        var lightPath = Path.Combine(Path.GetDirectoryName(fullPath)!, "theme-light.png");
        var liveSwitchPath = Path.Combine(Path.GetDirectoryName(fullPath)!, "theme-dark-to-light.png");

        RenderTheme(1, darkPath);
        RenderTheme(2, lightPath);
        RenderLiveThemeSwitch(liveSwitchPath);

        using var dark = new Bitmap(darkPath);
        using var light = new Bitmap(lightPath);
        using var contact = new Bitmap(dark.Width + light.Width + 18, Math.Max(dark.Height, light.Height) + 36);
        using var graphics = Graphics.FromImage(contact);
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.Clear(Color.FromArgb(46, 49, 47));
        using var font = UiPalette.Body(10f, FontStyle.Bold);
        TextRenderer.DrawText(graphics, "深色", font, new Rectangle(12, 5, dark.Width - 12, 26), Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(graphics, "浅色", font, new Rectangle(dark.Width + 30, 5, light.Width - 12, 26), Color.White,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        graphics.DrawImageUnscaled(dark, 0, 36);
        graphics.DrawImageUnscaled(light, dark.Width + 18, 36);
        contact.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"PASS dark/light + live theme switch preview | {fullPath}");
    }

    private static void RenderTheme(int themeMode, string path)
    {
        UiPalette.SetTheme(themeMode);
        using var settings = new SettingsForm(new PanelPreferences
        {
            ThemeMode = themeMode,
            Language = 0,
            SettingsFontScalePercent = 100
        }, startupEnabled: false);
        settings.SelectPageForTest(0);
        settings.Show();
        Application.DoEvents();
        settings.SavePreview(path);
    }

    private static void RenderLiveThemeSwitch(string path)
    {
        var oldCanvas = UiPalette.ResolveColors(1).Canvas;
        UiPalette.SetTheme(1);
        using var settings = new SettingsForm(new PanelPreferences
        {
            ThemeMode = 1,
            Language = 0,
            SettingsFontScalePercent = 100
        }, startupEnabled: false);
        settings.SelectPageForTest(0);
        settings.Show();
        Application.DoEvents();

        settings.SetThemeForTest(2);
        Application.DoEvents();
        settings.Activate();
        settings.BringToFront();
        Application.DoEvents();
        CaptureClient(settings, path);

        var host = Descendants(settings).OfType<BufferedSettingsHost>().Single();
        if (host.BackColor.ToArgb() != UiPalette.Canvas.ToArgb())
            throw new InvalidOperationException("The settings content host retained the dark theme background color.");

        using var bitmap = new Bitmap(path);
        var staleDarkPixels = 0;
        var unpaintedBlackPixels = 0;
        for (var y = 0; y < bitmap.Height; y += 2)
        for (var x = 0; x < bitmap.Width; x += 2)
        {
            var pixel = bitmap.GetPixel(x, y);
            if (pixel.ToArgb() == oldCanvas.ToArgb()) staleDarkPixels++;
            if (pixel.R < 8 && pixel.G < 8 && pixel.B < 8) unpaintedBlackPixels++;
        }
        if (staleDarkPixels > 250)
            throw new InvalidOperationException(
                $"Live dark-to-light switching left {staleDarkPixels} sampled dark-canvas pixels.");
        if (unpaintedBlackPixels > 1000)
            throw new InvalidOperationException(
                $"Live dark-to-light switching exposed {unpaintedBlackPixels} sampled unpainted black pixels.");
    }

    private static void CaptureClient(Form form, string path)
    {
        var clientOrigin = form.PointToScreen(Point.Empty);
        using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.CopyFromScreen(clientOrigin, Point.Empty, form.ClientSize, CopyPixelOperation.SourceCopy);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
