using CodexQuotaPanel;
using Microsoft.Win32;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;

internal static class StabilitySuite
{
    private static readonly List<string> Results = [];
    private static int _checks;

    internal static void Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        Results.Clear();
        _checks = 0;

        RunPreferenceRoundTrip();
        RunStartupIsolation();
        RunSettingsInteractions();
        RunEditorInteractions();
        RunMainWindowStress();
        RunLifecycleResourceStress();

        Results.Add($"PASS | {_checks} assertions");
        File.WriteAllLines(Path.Combine(outputDirectory, "stability-report.txt"), Results);
        Console.WriteLine($"PASS stability suite | checks={_checks} | {outputDirectory}");
    }

    private static void RunPreferenceRoundTrip()
    {
        var keyPath = $@"Software\CodexQuotaPanel.Tests\Stability\{Guid.NewGuid():N}";
        try
        {
            var expected = PanelPreferenceManager.Normalize(new PanelPreferences
            {
                OrbOpacityPercent = 67,
                OrbClickThrough = true,
                OrbX = -321,
                OrbY = 432,
                AlwaysOnTop = false,
                StartupViewMode = 3,
                LastViewMode = 2,
                OrbSize = 123,
                PositionLocked = true,
                SnapToEdge = true,
                OuterWindowMinutes = 60,
                InnerWindowMinutes = 43200,
                OuterWindowRole = 1,
                InnerWindowRole = 0,
                OuterColorArgb = Color.FromArgb(255, 12, 140, 220).ToArgb(),
                InnerColorArgb = Color.FromArgb(255, 241, 92, 130).ToArgb(),
                AlertsEnabled = false,
                WarningThreshold = 31,
                CriticalThreshold = 7,
                QuietHoursEnabled = true,
                QuietStartMinutes = 22 * 60 + 45,
                QuietEndMinutes = 7 * 60 + 15,
                AlertSoundEnabled = true,
                HoverPreviewEnabled = false,
                TrendRecordingEnabled = false,
                ConsumptionFlameEnabled = false,
                GlobalHotKeyEnabled = false,
                Language = 1
            });
            PanelPreferenceManager.Save(Registry.CurrentUser, keyPath, expected);
            var actual = PanelPreferenceManager.Load(Registry.CurrentUser, keyPath);
            Check(actual == expected, "Every preference survives an isolated registry round-trip");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
        Results.Add("PASS | complete preference round-trip");
    }

    private static void RunStartupIsolation()
    {
        var keyPath = $@"Software\CodexQuotaPanel.Tests\Stability\{Guid.NewGuid():N}\Run";
        const string valueName = "CodexQuotaPanel";
        var executable = Environment.ProcessPath ?? Application.ExecutablePath;
        try
        {
            StartupManager.SetEnabled(Registry.CurrentUser, keyPath, valueName, executable, enabled: true);
            Check(StartupManager.IsEnabled(Registry.CurrentUser, keyPath, valueName, executable),
                "Startup enable writes the exact quoted executable");
            using (var key = Registry.CurrentUser.OpenSubKey(keyPath, writable: true))
                key!.SetValue(valueName, "\"C:\\missing\\CodexQuotaPanel.exe\"", RegistryValueKind.String);
            Check(!StartupManager.IsEnabled(Registry.CurrentUser, keyPath, valueName, executable),
                "Startup validation rejects stale executable paths");
            StartupManager.SetEnabled(Registry.CurrentUser, keyPath, valueName, executable, enabled: false);
            using var verify = Registry.CurrentUser.OpenSubKey(keyPath, writable: false);
            Check(verify?.GetValue(valueName) is null, "Startup disable removes only its own value");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
        Results.Add("PASS | isolated startup enable/validate/disable");
    }

    private static void RunSettingsInteractions()
    {
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        var baseline = PanelPreferenceManager.Normalize(new PanelPreferences
        {
            OrbOpacityPercent = 73,
            AlwaysOnTop = true,
            StartupViewMode = 0,
            OrbSize = 88,
            SnapToEdge = false,
            AlertsEnabled = true,
            WarningThreshold = 23,
            CriticalThreshold = 9,
            HoverPreviewEnabled = true,
            TrendRecordingEnabled = true,
            ConsumptionFlameEnabled = true,
            GlobalHotKeyEnabled = true,
            Language = 0
        });
        var snapshot = MakeSnapshot();

        using (var settings = new SettingsForm(baseline, startupEnabled: true, snapshot, "sanitized diagnostics"))
        {
            PanelPreferences? preview = null;
            var previewCount = 0;
            settings.PreviewPreferencesChanged += value =>
            {
                preview = value;
                previewCount++;
            };
            settings.Show();
            Pump();
            Check(settings.SaveButtonVisible && !settings.IsDirty, "Settings opens clean with a visible save action");

            var startup = Field<CheckBox>(settings, "_startupToggle");
            startup.Checked = false;
            Pump();
            Check(!settings.StartupEnabled && settings.IsDirty,
                "Startup toggle marks settings dirty without changing the live desktop");

            var startupView = Field<ComboBox>(settings, "_startupViewCombo");
            startupView.SelectedIndex = (startupView.SelectedIndex + 1) % startupView.Items.Count;
            Pump();
            for (var index = 0; index < startupView.Items.Count; index++)
            {
                startupView.SelectedIndex = index;
                Pump();
                Check(preview?.StartupViewMode == index, $"Startup view {index} previews immediately");
            }

            var sizeSlider = Field<TrackBar>(settings, "_orbSizeSlider");
            var sizeInput = Field<NumericUpDown>(settings, "_orbSizeInput");
            for (var size = PanelPreferenceManager.MinimumOrbSize;
                 size <= PanelPreferenceManager.MaximumOrbSize;
                 size++)
            {
                if ((size & 1) == 0) sizeSlider.Value = size;
                else sizeInput.Value = size;
                Pump();
                Check(settings.SelectedOrbSize == size && preview?.OrbSize == size,
                    $"Exact orb size {size}px stays synchronized");
            }

            Toggle(settings, "_topMostToggle", value => value.AlwaysOnTop, ref preview);
            Check(settings.TopMost == preview!.AlwaysOnTop, "Topmost previews on the settings window itself");
            Toggle(settings, "_consumptionFlameToggle", value => value.ConsumptionFlameEnabled, ref preview);
            var embeddedOrb = Field<QuotaOrbControl>(settings, "_orbPreview");
            Check(embeddedOrb.FlameAnimationEnabled == preview!.ConsumptionFlameEnabled,
                "Embedded orb preview follows the flame toggle");
            Toggle(settings, "_positionLockedToggle", value => value.PositionLocked, ref preview);
            Toggle(settings, "_snapToEdgeToggle", value => value.SnapToEdge, ref preview);
            Toggle(settings, "_clickThroughToggle", value => value.OrbClickThrough, ref preview);
            Toggle(settings, "_hoverPreviewToggle", value => value.HoverPreviewEnabled, ref preview);
            Toggle(settings, "_globalHotKeyToggle", value => value.GlobalHotKeyEnabled, ref preview);
            Toggle(settings, "_alertSoundToggle", value => value.AlertSoundEnabled, ref preview);
            Toggle(settings, "_trendRecordingToggle", value => value.TrendRecordingEnabled, ref preview);

            settings.SetLanguageForTest(1);
            Pump();
            Check(settings.Text == "Codex Quota Panel settings" && settings.SelectedPreferences.Language == 1,
                "Open settings switches immediately to English");
            Check(settings.Font.Name.Contains("Segoe", StringComparison.OrdinalIgnoreCase) ||
                  settings.Font.Name.Equals("Tahoma", StringComparison.OrdinalIgnoreCase),
                "English settings uses a normal Windows UI font");
            settings.SetLanguageForTest(0);
            Pump();
            Check(settings.Text.Contains("额度面板设置", StringComparison.Ordinal) &&
                  settings.SelectedPreferences.Language == 0,
                "Open settings switches immediately back to Chinese");

            for (var page = 0; page < 5; page++)
            {
                settings.SelectPageForTest(page);
                Pump();
                Check(settings.SaveButtonVisible, $"Save action remains visible on settings page {page + 1}");
                CheckNoInteractiveOverlap(settings, $"settings page {page + 1}");
            }

            var selected = settings.SelectedPreferences;
            settings.SaveForTest();
            Check(settings.DialogResult == DialogResult.OK && !settings.IsDirty &&
                  settings.SelectedPreferences == selected && previewCount > 80,
                "Save & apply accepts every staged direct-control change");
        }

        using (var cancel = new SettingsForm(baseline, startupEnabled: true, snapshot))
        {
            PanelPreferences? lastPreview = null;
            cancel.PreviewPreferencesChanged += value =>
            {
                lastPreview = value;
                L10n.SetLanguage((AppLanguage)value.Language);
            };
            cancel.Show();
            Pump();
            cancel.SetOrbSizeForTest(137);
            Field<CheckBox>(cancel, "_clickThroughToggle").Checked = true;
            cancel.SetLanguageForTest(1);
            Pump();
            cancel.Close();
            Pump();
            Check(cancel.DialogResult == DialogResult.Cancel && lastPreview == baseline,
                "Cancel rolls every live preview back to the original preferences");
        }
        Results.Add("PASS | every direct settings control, save, cancel, language, and embedded preview");
    }

    private static void RunEditorInteractions()
    {
        foreach (var language in new[] { AppLanguage.SimplifiedChinese, AppLanguage.English })
        {
            L10n.SetLanguage(language);

            using (var opacity = new OpacityEditorForm(73))
            {
                var previews = new List<int>();
                opacity.PreviewChanged += previews.Add;
                opacity.Show();
                Pump();
                opacity.SetOpacityForTest(1);
                Check(opacity.SelectedOpacity == 30, "Opacity clamps to 30%");
                opacity.SetOpacityForTest(101);
                Check(opacity.SelectedOpacity == 100, "Opacity clamps to 100%");
                for (var value = 30; value <= 100; value++) opacity.SetOpacityForTest(value);
                Check(opacity.SelectedOpacity == 100 && previews.Count >= 73,
                    "Every exact opacity value previews without desynchronizing");
                PrimaryAction(opacity).PerformClick();
                Pump();
                Check(opacity.DialogResult == DialogResult.OK, "Opacity Apply closes with OK");
            }

            var snapshot = MakeSnapshot();
            var initialRings = new RingDisplayConfiguration(
                new RingWindowSelection(300, RingWindowRole.Primary),
                new RingWindowSelection(10080, RingWindowRole.Secondary),
                UiPalette.Mint,
                UiPalette.Sky);
            using (var rings = new RingSettingsForm(snapshot, initialRings))
            {
                var previews = 0;
                rings.PreviewChanged += _ => previews++;
                rings.Show();
                Pump();
                var outer = Field<ComboBox>(rings, "_outerCombo");
                var inner = Field<ComboBox>(rings, "_innerCombo");
                for (var index = 0; index < outer.Items.Count; index++) outer.SelectedIndex = index;
                for (var index = inner.Items.Count - 1; index >= 0; index--) inner.SelectedIndex = index;
                rings.SetColorsForTest(Color.FromArgb(9, 131, 219), Color.FromArgb(237, 84, 151));
                Check(rings.SelectedConfiguration.OuterColor == Color.FromArgb(255, 9, 131, 219) &&
                      rings.SelectedConfiguration.InnerColor == Color.FromArgb(255, 237, 84, 151) && previews > 0,
                    "Ring windows and arbitrary RGB colors preview immediately");
                rings.RestoreDefaultsForTest();
                Check(rings.SelectedConfiguration == initialRings,
                    "Restore defaults returns both ring windows and colors");
                PrimaryAction(rings).PerformClick();
                Pump();
                Check(rings.DialogResult == DialogResult.OK, "Ring Apply closes with OK");
            }

            using (var alerts = new AlertSettingsForm(new PanelPreferences
            {
                AlertsEnabled = true,
                WarningThreshold = 23,
                CriticalThreshold = 9,
                QuietHoursEnabled = true,
                QuietStartMinutes = 23 * 60,
                QuietEndMinutes = 8 * 60
            }))
            {
                alerts.Show();
                Pump();
                var enabled = Field<CheckBox>(alerts, "_enabled");
                var warning = Field<NumericUpDown>(alerts, "_warning");
                var critical = Field<NumericUpDown>(alerts, "_critical");
                var quiet = Field<CheckBox>(alerts, "_quietEnabled");
                var quietStart = Field<ComboBox>(alerts, "_quietStart");
                var quietEnd = Field<ComboBox>(alerts, "_quietEnd");
                enabled.Checked = false;
                Check(!warning.Enabled && !critical.Enabled && !quiet.Enabled,
                    "Disabling alerts disables dependent inputs");
                enabled.Checked = true;
                alerts.SetThresholdsForTest(10, 10);
                Check(!alerts.InputsValid, "Equal alert thresholds are rejected");
                alerts.SetThresholdsForTest(35, 8);
                quiet.Checked = true;
                quietEnd.SelectedIndex = quietStart.SelectedIndex;
                Check(!alerts.InputsValid, "Equal quiet-hours endpoints are rejected");
                quietEnd.SelectedIndex = (quietStart.SelectedIndex + 1) % quietEnd.Items.Count;
                Check(alerts.InputsValid && quietStart.Enabled && quietEnd.Enabled,
                    "Distinct thresholds and quiet hours are accepted");
                PrimaryAction(alerts).PerformClick();
                Pump();
                Check(alerts.DialogResult == DialogResult.OK && alerts.SelectedValues.WarningThreshold == 35,
                    "Alert Save accepts validated values");
            }

            using (var hover = new HoverPeekForm())
            {
                hover.SetData(snapshot, initialRings);
                hover.ApplyLanguage();
                var area = Screen.PrimaryScreen!.WorkingArea;
                hover.ShowNear(new Rectangle(area.Left + area.Width / 2, area.Top + area.Height / 2, 88, 88), topMost: true);
                Pump();
                Check(hover.Visible && hover.TopMost && hover.UsesPassiveWindowStyles,
                    "Hover preview is visible, topmost, passive, and non-interactive");
                Check(area.IntersectsWith(hover.Bounds), "Hover preview stays on a working display");
                hover.Hide();
            }
        }
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        Results.Add("PASS | opacity, rings, alerts, and hover editors in Chinese and English");
    }

    private static void RunMainWindowStress()
    {
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        using var form = new QuotaForm();
        form.ConfigureRings(new RingDisplayConfiguration(
            new RingWindowSelection(300, RingWindowRole.Primary),
            new RingWindowSelection(10080, RingWindowRole.Secondary),
            Color.FromArgb(255, 80, 230, 170),
            Color.FromArgb(255, 145, 125, 255)));
        form.ApplySnapshot(MakeSnapshot());
        form.ShowOrb(animate: false);
        Pump();

        var hotKeyCount = 0;
        form.GlobalHotKeyPressed += () => hotKeyCount++;
        Check(PostMessage(form.Handle, 0x0312, IntPtr.Zero, IntPtr.Zero), "WM_HOTKEY can be posted to the quota form");
        Pump();
        Check(hotKeyCount == 1, "Quota form raises the global-hotkey recovery event");

        var nowMinute = DateTimeOffset.UtcNow.ToUnixTimeSeconds() / 60;
        QuotaHistoryPoint[] fastHistory =
        [
            new(nowMinute - 60, 0, 300, 900),
            new(nowMinute - 30, 0, 300, 830),
            new(nowMinute, 0, 300, 760)
        ];
        form.SetHistory([]);
        form.SetConsumptionFlameEnabled(true);
        Pump();
        Check(form.ConsumptionIntensity == 0d && !form.OrbControl.FlameTimerRunning,
            "Idle blue flame remains static with no timer");
        form.SetHistory(fastHistory);
        Pump();
        Check(form.ConsumptionIntensity > 0.8d && form.OrbControl.FlameTimerRunning,
            "Fast consumption starts the energetic flame timer");
        form.HidePanel();
        Pump();
        Check(!form.OrbControl.FlameTimerRunning,
            "Tray-only parent hide stops the child flame timer");
        form.ShowOrb(animate: false);
        Pump();
        Check(form.OrbControl.FlameTimerRunning,
            "Restoring the orb resumes an enabled energetic flame");

        for (var iteration = 0; iteration < 240; iteration++)
        {
            var size = PanelPreferenceManager.MinimumOrbSize +
                       iteration % (PanelPreferenceManager.MaximumOrbSize - PanelPreferenceManager.MinimumOrbSize + 1);
            form.SetOrbSize(size);
            form.SetOrbOpacityPercent(PanelPreferenceManager.MinimumOpacity + iteration % 71);
            form.SetTopMostPreference((iteration & 1) == 0);
            form.SetPositionLocked(iteration % 3 == 0);
            form.SetSnapToEdge(iteration % 4 == 0);
            form.SetHoverPreviewEnabled(iteration % 5 != 0);
            form.SetOrbClickThroughPreference(iteration % 7 == 0);
            form.SetConsumptionFlameEnabled(iteration % 6 != 0);

            L10n.SetLanguage((iteration & 1) == 0 ? AppLanguage.SimplifiedChinese : AppLanguage.English);
            form.ApplyLanguage();
            if (iteration % 3 == 0) form.ShowDetails(animate: false);
            else form.CollapseToOrb(animate: false);
            if (iteration % 20 == 0)
            {
                form.HidePanel();
                form.ShowOrb(animate: false);
            }
            Pump();

            Check(form.OrbLogicalSize == size, $"Stress iteration {iteration} retains exact orb size");
            if (form.IsCollapsed)
                Check(form.ClientSize.Width == form.ClientSize.Height && form.OrbBounds == form.ClientRectangle,
                    $"Stress iteration {iteration} keeps collapsed geometry circular");
        }

        form.SetOrbClickThroughPreference(false);
        form.SetConsumptionFlameEnabled(false);
        form.CollapseToOrb(animate: false);
        Pump();
        Check(!form.HasClickThroughWindowStyle && !form.OrbControl.FlameTimerRunning,
            "Stress teardown removes click-through and animation state");
        Results.Add("PASS | 240-cycle main-window state, language, size, opacity, flame, and view stress");
    }

    private static void RunLifecycleResourceStress()
    {
        ForceCollection();
        var process = Process.GetCurrentProcess();
        process.Refresh();
        var beforeHandles = process.HandleCount;
        var beforeGdi = GetGuiResources(process.Handle, 0);
        var beforeUser = GetGuiResources(process.Handle, 1);
        var beforePrivate = process.PrivateMemorySize64;

        for (var iteration = 0; iteration < 40; iteration++)
        {
            L10n.SetLanguage((iteration & 1) == 0 ? AppLanguage.SimplifiedChinese : AppLanguage.English);
            using var settings = new SettingsForm(new PanelPreferences
            {
                Language = (iteration & 1),
                OrbSize = 64 + iteration % 81,
                ConsumptionFlameEnabled = iteration % 3 != 0
            }, startupEnabled: iteration % 2 == 0, MakeSnapshot());
            settings.CreateControl();
            foreach (var control in Descendants<Control>(settings)) control.CreateControl();
            settings.SelectPageForTest(iteration % 5);
            settings.SetOrbSizeForTest(64 + iteration % 81);
            settings.PerformLayout();
        }

        ForceCollection();
        process.Refresh();
        var handleDelta = process.HandleCount - beforeHandles;
        var gdiDelta = GetGuiResources(process.Handle, 0) - beforeGdi;
        var userDelta = GetGuiResources(process.Handle, 1) - beforeUser;
        var privateDelta = process.PrivateMemorySize64 - beforePrivate;
        Check(handleDelta <= 80, $"Handle growth stays bounded after lifecycle stress ({handleDelta})");
        Check(gdiDelta <= 80, $"GDI object growth stays bounded after lifecycle stress ({gdiDelta})");
        Check(userDelta <= 80, $"USER object growth stays bounded after lifecycle stress ({userDelta})");
        Check(privateDelta <= 64L * 1024 * 1024,
            $"Private memory growth stays bounded after lifecycle stress ({privateDelta / 1024d / 1024d:0.0} MiB)");
        Results.Add($"PASS | lifecycle resources Δhandles={handleDelta}, ΔGDI={gdiDelta}, ΔUSER={userDelta}, Δprivate={privateDelta / 1024d / 1024d:0.0}MiB");
    }

    private static void Toggle(
        SettingsForm settings,
        string fieldName,
        Func<PanelPreferences, bool> selector,
        ref PanelPreferences? preview)
    {
        var toggle = Field<CheckBox>(settings, fieldName);
        var expected = !toggle.Checked;
        toggle.Checked = expected;
        Pump();
        Check(preview is not null && selector(preview) == expected,
            $"{fieldName} previews immediately");
    }

    private static void CheckNoInteractiveOverlap(Control root, string context)
    {
        foreach (var parent in DescendantsAndSelf(root))
        {
            var interactive = parent.Controls.Cast<Control>()
                .Where(control => control.Visible && control is ButtonBase or ComboBox or NumericUpDown or TrackBar or TextBox)
                .ToArray();
            for (var first = 0; first < interactive.Length; first++)
            for (var second = first + 1; second < interactive.Length; second++)
                Check(!interactive[first].Bounds.IntersectsWith(interactive[second].Bounds),
                    $"No interactive controls overlap in {context}: {interactive[first].Name}/{interactive[second].Name}");
        }
    }

    private static T Field<T>(object instance, string name) where T : class =>
        instance.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(instance) as T
        ?? throw new InvalidOperationException($"Missing field {instance.GetType().Name}.{name}");

    private static ActionButton PrimaryAction(Control root) =>
        Descendants<ActionButton>(root).First(button => button.Primary);

    private static IEnumerable<T> Descendants<T>(Control root) where T : Control =>
        DescendantsAndSelf(root).OfType<T>().Skip(root is T ? 1 : 0);

    private static IEnumerable<Control> DescendantsAndSelf(Control root)
    {
        yield return root;
        foreach (Control child in root.Controls)
        foreach (var descendant in DescendantsAndSelf(child))
            yield return descendant;
    }

    private static QuotaSnapshot MakeSnapshot()
    {
        var now = DateTimeOffset.Now;
        return new QuotaSnapshot(
            "codex",
            null,
            new LimitBucket(48, 300, now.AddHours(2)),
            new LimitBucket(8, 10080, now.AddDays(6)),
            null,
            "pro",
            null,
            now,
            "App Server");
    }

    private static void Pump()
    {
        Application.DoEvents();
        Thread.Yield();
    }

    private static void ForceCollection()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
    }

    private static void Check(bool condition, string message)
    {
        _checks++;
        if (!condition) throw new InvalidOperationException($"STABILITY FAIL: {message}");
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int GetGuiResources(IntPtr process, int flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostMessage(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);
}
