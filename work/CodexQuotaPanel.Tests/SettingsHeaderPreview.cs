using CodexQuotaPanel;

internal static class SettingsHeaderPreview
{
    internal static void Run(string outputPath)
    {
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        using var settings = new SettingsForm(
            PanelPreferenceManager.Default with { Language = 0 },
            startupEnabled: true)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(32, 32)
        };
        settings.Show();
        Application.DoEvents();
        settings.PerformLayout();
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        settings.SavePreview(fullPath);
        Console.WriteLine($"PASS settings brand header preview | {fullPath}");
    }
}
