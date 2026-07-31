using CodexQuotaPanel;
using System.Drawing.Drawing2D;

internal static class OrbBackgroundPreview
{
    internal static void Run(string outputPath)
    {
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var now = DateTimeOffset.Now;
        var snapshot = new QuotaSnapshot(
            "codex", null,
            new LimitBucket(68, 300, now.AddHours(3)),
            new LimitBucket(29, 10080, now.AddDays(5)),
            null, "pro", null, now, "Preview");

        using var contact = new Bitmap(570, 252);
        using var canvas = Graphics.FromImage(contact);
        canvas.SmoothingMode = SmoothingMode.AntiAlias;
        using var wallpaper = new LinearGradientBrush(
            new Rectangle(Point.Empty, contact.Size),
            Color.FromArgb(207, 221, 232),
            Color.FromArgb(181, 194, 180),
            LinearGradientMode.ForwardDiagonal);
        canvas.FillRectangle(wallpaper, new Rectangle(Point.Empty, contact.Size));

        DrawState(canvas, snapshot, 1, null, 20, "深色自动 / DARK AUTO");
        DrawState(canvas, snapshot, 2, null, 210, "浅色自动 / LIGHT AUTO");
        DrawState(canvas, snapshot, 2, Color.FromArgb(255, 105, 126, 116).ToArgb(), 400,
            "自定义 / CUSTOM");
        contact.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);

        UiPalette.SetTheme(2);
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        using (var lightOrbWindow = new QuotaForm())
        {
            lightOrbWindow.SetOrbBackgroundColor(null);
            lightOrbWindow.ShowOrb(animate: false);
            if (lightOrbWindow.BackColor.GetBrightness() > 0.15f)
                throw new InvalidOperationException(
                    "The collapsed light-theme orb window retained a bright backdrop halo.");
        }

        var settingsPath = Path.Combine(Path.GetDirectoryName(fullPath)!, "orb-background-settings.png");
        var preferences = PanelPreferenceManager.Default with
        {
            ThemeMode = 2,
            Language = 0,
            OrbBackgroundColorArgb = Color.FromArgb(255, 105, 126, 116).ToArgb()
        };
        var portable = SettingsTransferService.MakePortable(preferences);
        if (portable.OrbBackgroundColorArgb != preferences.OrbBackgroundColorArgb)
            throw new InvalidOperationException("Orb background color was not retained by portable settings.");

        using var settings = new SettingsForm(preferences, startupEnabled: false, snapshot);
        settings.SelectPageForTest(1);
        settings.Show();
        Application.DoEvents();
        settings.SavePreview(settingsPath);

        var scaledSettingsPath = Path.Combine(
            Path.GetDirectoryName(fullPath)!, "orb-background-light-scaled.png");
        using var scaledSettings = new SettingsForm(PanelPreferenceManager.Default with
        {
            ThemeMode = 2,
            Language = 0,
            SettingsFontScalePercent = 132,
            OrbSize = 141,
            OrbOpacityPercent = 62,
            OrbBackgroundColorArgb = null
        }, startupEnabled: false, snapshot);
        scaledSettings.SelectPageForTest(1);
        scaledSettings.Show();
        Application.DoEvents();
        scaledSettings.SavePreview(scaledSettingsPath);

        Console.WriteLine($"PASS fixed-black/custom orb + scaled light settings preview | " +
                          $"{fullPath} | {settingsPath} | {scaledSettingsPath}");
    }

    private static void DrawState(
        Graphics canvas,
        QuotaSnapshot snapshot,
        int theme,
        int? customColor,
        int x,
        string label)
    {
        UiPalette.SetTheme(theme);
        using var orb = new QuotaOrbControl { Size = new Size(150, 150) };
        orb.ConfigureRings(new RingDisplayConfiguration(
            new RingWindowSelection(300, RingWindowRole.Primary),
            new RingWindowSelection(10080, RingWindowRole.Secondary),
            UiPalette.Mint,
            UiPalette.Sky));
        orb.SetBackgroundColor(customColor);
        orb.SetSnapshot(snapshot, live: true);
        orb.SetFlameStyle(1);
        orb.SetConsumptionIntensity(0.58d);
        using var image = orb.RenderTransparentPreview();
        using var shadow = new SolidBrush(Color.FromArgb(42, 18, 25, 22));
        canvas.FillEllipse(shadow, x + 9, 48, 150, 150);
        canvas.DrawImage(image, new Rectangle(x, 40, 150, 150));
        using var font = UiPalette.MonoPixels(8.5f, FontStyle.Bold);
        TextRenderer.DrawText(canvas, label, font, new Rectangle(x - 8, 216, 174, 28),
            Color.FromArgb(28, 39, 34),
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix);
    }
}
