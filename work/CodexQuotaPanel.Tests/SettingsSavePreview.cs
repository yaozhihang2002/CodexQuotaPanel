using CodexQuotaPanel;

internal static class SettingsSavePreview
{
    internal static void Run(string outputPath)
    {
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        UiPalette.SetTheme(1);
        var baseline = PanelPreferenceManager.Default with
        {
            ThemeMode = 1,
            Language = 0,
            OrbSize = 92,
            SettingsFontScalePercent = 115
        };

        using var settings = new SettingsForm(baseline, startupEnabled: false);
        var saveCount = 0;
        PanelPreferences? restoredPreview = null;
        settings.PreviewPreferencesChanged += value => restoredPreview = value;
        settings.SaveRequested += () =>
        {
            saveCount++;
            settings.MarkSaved(settings.StartupEnabled);
        };

        settings.Show();
        Application.DoEvents();
        settings.SelectPageForTest(1);
        settings.SetOrbSizeForTest(104);
        settings.SaveForTest();
        Application.DoEvents();

        if (saveCount != 1 || !settings.Visible || settings.DialogResult != DialogResult.None ||
            settings.IsDirty || settings.SelectedPreferences.OrbSize != 104)
        {
            throw new InvalidOperationException("Saving must persist in place and keep the settings window open.");
        }

        settings.SavePreview(outputPath);
        settings.SetOrbSizeForTest(108);
        if (!settings.IsDirty)
            throw new InvalidOperationException("Settings must remain editable after saving.");

        settings.Close();
        Application.DoEvents();
        if (restoredPreview?.OrbSize != 104)
            throw new InvalidOperationException("Closing must restore the most recently saved baseline.");

        Console.WriteLine($"PASS settings save in place | visible after save + editable + saved baseline restored | {Path.GetFullPath(outputPath)}");
    }
}
