using CodexQuotaPanel;

internal static class AlertLayoutPreview
{
    internal static void Run(string outputPath)
    {
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        UiPalette.SetTheme(1);
        var preferences = PanelPreferenceManager.Default with
        {
            Language = 0,
            AlertsEnabled = true,
            WarningThreshold = 20,
            CriticalThreshold = 10,
            SettingsFontScalePercent = PanelPreferenceManager.MaximumSettingsFontScale
        };
        using var settings = new SettingsForm(preferences, startupEnabled: false);
        settings.Show();
        Application.DoEvents();
        settings.SelectPageForTest(3);
        Application.DoEvents();
        settings.SavePreview(outputPath);
        Console.WriteLine($"PASS alert summary layout preview | {Path.GetFullPath(outputPath)}");
    }
}
