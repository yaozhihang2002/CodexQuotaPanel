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

        RenderTheme(1, darkPath);
        RenderTheme(2, lightPath);

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
        Console.WriteLine($"PASS dark/light theme preview | {fullPath}");
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
}
