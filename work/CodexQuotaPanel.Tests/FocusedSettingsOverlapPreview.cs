using CodexQuotaPanel;
using System.Reflection;

internal static class FocusedSettingsOverlapPreview
{
    internal static void Run(string outputPath)
    {
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        var now = DateTimeOffset.Now;
        var snapshot = new QuotaSnapshot(
            "codex",
            null,
            new LimitBucket(48, 300, now.AddHours(2)),
            new LimitBucket(3, 10080, now.AddDays(6)),
            null,
            "pro",
            null,
            now,
            "App Server");
        var preferences = PanelPreferenceManager.Default with
        {
            AlwaysOnTop = true,
            Language = 0,
            OrbSize = 88
        };

        var area = Screen.PrimaryScreen!.WorkingArea;
        using var settings = new SettingsForm(preferences, startupEnabled: true, snapshot)
        {
            StartPosition = FormStartPosition.Manual,
            Location = new Point(area.Left + 24, area.Top + 24),
            TopMost = true
        };
        settings.Show();
        Application.DoEvents();

        // Reproduce the sequence most likely to expose the reported artifact:
        // show the appearance preview, switch language, then return to General.
        settings.SelectPageForTest(1);
        settings.SetOrbSizeForTest(PanelPreferenceManager.MaximumOrbSize);
        settings.SetFontScaleForTest(PanelPreferenceManager.MaximumSettingsFontScale);
        Field<ComboBox>(settings, "_flameStyleCombo").SelectedIndex = 2;
        settings.SetLanguageForTest(1);
        settings.SelectPageForTest(0);
        settings.Refresh();
        Application.DoEvents();

        var navigation = Field<List<SettingsNavButton>>(settings, "_navigation");
        for (var index = 1; index < navigation.Count; index++)
        {
            if (navigation[index - 1].Bounds.IntersectsWith(navigation[index].Bounds))
                throw new InvalidOperationException("Navigation buttons overlap after page/language switching.");
        }
        var preview = Field<QuotaOrbControl>(settings, "_orbPreview");
        if (preview.Visible)
            throw new InvalidOperationException("Appearance orb preview remained visible on the General page.");
        if (settings.SelectedFontScalePercent != PanelPreferenceManager.MaximumSettingsFontScale ||
            settings.SelectedPreferences.SettingsFontScalePercent != PanelPreferenceManager.MaximumSettingsFontScale)
            throw new InvalidOperationException("Font scale preview was not retained in the staged preferences.");
        if (settings.SelectedPreferences.OrbSize != PanelPreferenceManager.MaximumOrbSize ||
            settings.SelectedPreferences.ConsumptionFlameStyle != 2)
            throw new InvalidOperationException("Expanded orb range or flame style was not retained in staged preferences.");

        // Finish on Interaction and scroll the persisted reminder switch into
        // view. The screenshot verifies it at the maximum typography scale.
        settings.SelectPageForTest(2);
        var reminderToggle = Field<SettingsToggle>(settings, "_clickThroughReminderToggle");
        for (Control? parent = reminderToggle.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is ScrollableControl { AutoScroll: true } scrollHost)
            {
                scrollHost.ScrollControlIntoView(reminderToggle);
                break;
            }
        }
        settings.Refresh();
        Application.DoEvents();
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        settings.SavePreview(fullPath);
        Console.WriteLine($"PASS focused settings overlap screenshot | {fullPath}");
    }

    private static T Field<T>(object instance, string name) where T : class =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance) as T
        ?? throw new InvalidOperationException($"Missing field {name}.");
}
