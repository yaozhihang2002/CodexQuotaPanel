using CodexQuotaPanel;

internal static class DataAboutPreview
{
    internal static void Run(string outputPath)
    {
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        UiPalette.SetTheme(1);
        var preferences = PanelPreferenceManager.Default with
        {
            Language = 0,
            ThemeMode = 1,
            SettingsFontScalePercent = PanelPreferenceManager.MaximumSettingsFontScale
        };
        using var settings = new SettingsForm(preferences, startupEnabled: false, snapshot: null,
            diagnostics: "CodexQuotaPanel v0.4.1 Pre-release");
        settings.Show();
        Application.DoEvents();
        settings.SelectPageForTest(4);
        Application.DoEvents();

        var page = (ResponsiveSettingsPage)settings.SelectedPageForTest;
        var cards = Descendants(page).OfType<SettingsCard>()
            .Where(card => card.Parent is TableLayoutPanel)
            .OrderBy(card => card.Top)
            .ToArray();
        for (var index = 1; index < cards.Length; index++)
        {
            if (cards[index - 1].Bounds.IntersectsWith(cards[index].Bounds))
                throw new InvalidOperationException("Data & About cards overlap at maximum typography.");
        }

        // Exercise several wheel-like positions before capturing the settled
        // viewport. This catches stale copied pixels without running the full UI suite.
        foreach (var offset in new[] { 96, 208, 344, 236 })
        {
            page.AutoScrollPosition = new Point(0, offset);
            Application.DoEvents();
        }

        // Keep a top-of-page proof for the project link/baseline fix as well
        // as the bottom proof for the About card's CJK line box.
        page.AutoScrollPosition = Point.Empty;
        NativeRedrawScope.RedrawNow(page);
        Application.DoEvents();
        settings.Activate();
        settings.BringToFront();
        Application.DoEvents();
        CaptureClient(settings, AddSuffix(outputPath, "-top"));

        page.AutoScrollPosition = new Point(0, page.VerticalScroll.Maximum);
        Application.DoEvents();
        Console.WriteLine(
            $"DATA PAGE client={page.ClientSize.Width}x{page.ClientSize.Height} " +
            $"display={page.DisplayRectangle.Width}x{page.DisplayRectangle.Height} " +
            $"scroll={page.AutoScrollPosition.X},{page.AutoScrollPosition.Y} " +
            $"vertical={page.VerticalScroll.Visible}");
        NativeRedrawScope.RedrawNow(page);
        Application.DoEvents();

        var links = Descendants(settings).OfType<LinkLabel>().Select(link => link.Text).ToArray();
        if (links.Length != 1 ||
            !links[0].Contains("yaozhihang2002/CodexQuotaPanel", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Data & About page must contain exactly one project link.");
        }

        settings.Activate();
        settings.BringToFront();
        Application.DoEvents();
        CaptureClient(settings, outputPath);
        settings.Hide();
        Console.WriteLine($"PASS data & about preview | maximum typography + v0.4.1 Pre-release | {Path.GetFullPath(outputPath)}");
    }

    private static string AddSuffix(string path, string suffix)
    {
        var directory = Path.GetDirectoryName(path);
        var fileName = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        return Path.Combine(directory ?? string.Empty, fileName + suffix + extension);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void CaptureClient(Form form, string path)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        try
        {
            var clientOrigin = form.PointToScreen(Point.Empty);
            using var bitmap = new Bitmap(form.ClientSize.Width, form.ClientSize.Height);
            using var graphics = Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(clientOrigin, Point.Empty, form.ClientSize, CopyPixelOperation.SourceCopy);
            bitmap.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Headless CI cannot read the desktop; retain a deterministic fallback.
            if (form is SettingsForm settings) settings.SavePreview(fullPath);
            else throw;
        }
    }
}
