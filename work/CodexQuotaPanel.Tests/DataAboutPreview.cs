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
            SettingsFontScalePercent = 125
        };
        using var settings = new SettingsForm(preferences, startupEnabled: false, snapshot: null,
            diagnostics: "CodexQuotaPanel v0.1.0 pre-release");
        settings.Show();
        Application.DoEvents();
        settings.SelectPageForTest(4);
        Application.DoEvents();

        var links = Descendants(settings).OfType<LinkLabel>().Select(link => link.Text).ToArray();
        if (links.Length != 1 ||
            !links[0].Contains("yaozhihang2002/CodexQuotaPanel", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Data & About page must contain exactly one project link.");
        }

        settings.SavePreview(outputPath);
        settings.Hide();
        Console.WriteLine($"PASS data & about preview | v0.1.0 pre-release + GitHub project | {Path.GetFullPath(outputPath)}");
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
