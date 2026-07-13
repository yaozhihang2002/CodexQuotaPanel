using CodexQuotaPanel;

internal static class StartupOrbPreview
{
    internal static void Run(string outputPath)
    {
        UiPalette.SetTheme(1);
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        var now = DateTimeOffset.Now;
        using var form = new QuotaForm();
        form.ApplySnapshot(new QuotaSnapshot(
            "codex", null,
            new LimitBucket(72, 300, now.AddHours(3)),
            new LimitBucket(88, 10080, now.AddDays(6)),
            null, "pro", null, now, "App Server"));
        form.ShowOrb(animate: false);

        var area = Screen.PrimaryScreen!.WorkingArea;
        form.Location = new Point(
            area.Left + Math.Max(0, (area.Width - form.Width) / 2),
            area.Top + Math.Max(0, (area.Height - form.Height) / 2));
        form.Show();

        // The first event pass performs the queued expanded-preview refresh.
        // Capture the real desktop immediately after its cover-to-orb handoff.
        Application.DoEvents();
        Application.DoEvents();
        Console.WriteLine(
            $"startup-state | visible={form.Visible} | hiddenState={form.IsHidden} | collapsed={form.IsCollapsed} | " +
            $"orbVisible={form.OrbControl.Visible} | opacity={form.Opacity:0.##} | bounds={form.Bounds}");
        var bounds = form.Bounds;
        using var screenshot = new Bitmap(bounds.Width, bounds.Height);
        using (var graphics = Graphics.FromImage(screenshot))
            graphics.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size, CopyPixelOperation.SourceCopy);

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        screenshot.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
        var accentPixels = CountAccentPixels(screenshot);
        if (accentPixels < 80)
            throw new InvalidOperationException(
                $"Startup orb handoff exposed an unpainted background; accent pixels={accentPixels}; screenshot={fullPath}.");

        Console.WriteLine($"PASS startup orb cache handoff | accentPixels={accentPixels} | {fullPath}");
    }

    private static int CountAccentPixels(Bitmap bitmap)
    {
        var count = 0;
        for (var y = 0; y < bitmap.Height; y++)
        for (var x = 0; x < bitmap.Width; x++)
        {
            var color = bitmap.GetPixel(x, y);
            if (color.G >= 135 && color.G - color.R >= 22 && color.G - color.B >= -8)
                count++;
        }
        return count;
    }
}
