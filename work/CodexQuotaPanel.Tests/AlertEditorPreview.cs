using CodexQuotaPanel;

internal static class AlertEditorPreview
{
    internal static void Run(string outputPath)
    {
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        UiPalette.SetTheme(1);
        using var editor = new AlertSettingsForm(PanelPreferenceManager.Default with
        {
            Language = 0,
            AlertsEnabled = true,
            WarningThreshold = 20,
            CriticalThreshold = 10,
            QuietHoursEnabled = true,
            QuietStartMinutes = 23 * 60,
            QuietEndMinutes = 8 * 60,
            SettingsFontScalePercent = 125
        });
        UiPalette.ApplyScaledTypography(editor, 125);
        editor.Show();
        Application.DoEvents();
        if (editor.HasVisibleScrollbars)
            throw new InvalidOperationException("Alert editor unexpectedly requires scrolling at 125% typography.");
        editor.SavePreview(outputPath);
        editor.Hide();
        Console.WriteLine($"PASS alert editor layout preview | 125% typography | {editor.LayoutMetrics} | {Path.GetFullPath(outputPath)}");
    }
}
