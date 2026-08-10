using CodexQuotaPanel;
using System.Diagnostics;

internal static class MotionPerformancePreview
{
    internal static void Run(string outputPath)
    {
        UiPalette.SetTheme(1);
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        var now = DateTimeOffset.Now;
        var snapshot = new QuotaSnapshot(
            "codex", null,
            new LimitBucket(18, 300, now.AddHours(3)),
            new LimitBucket(9, 10080, now.AddDays(6)),
            null, "pro", null, now, "App Server");

        using var form = new QuotaForm();
        form.ApplySnapshot(snapshot);
        form.ShowOrb(animate: false);
        form.Show();
        Application.DoEvents();

        var expandCall = Stopwatch.StartNew();
        form.ShowDetails(animate: true);
        expandCall.Stop();
        WaitForTransition(form);
        var expand = Snapshot(form, expandCall.ElapsedMilliseconds);

        var collapseCall = Stopwatch.StartNew();
        form.CollapseToOrb(animate: true);
        collapseCall.Stop();
        WaitForTransition(form);
        var collapse = Snapshot(form, collapseCall.ElapsedMilliseconds);

        var preferences = PanelPreferenceManager.Default with
        {
            Language = 0,
            SettingsFontScalePercent = PanelPreferenceManager.MaximumSettingsFontScale
        };
        var settingsConstructor = Stopwatch.StartNew();
        using var settings = new SettingsForm(preferences, startupEnabled: false, snapshot);
        settingsConstructor.Stop();
        var settingsShow = Stopwatch.StartNew();
        settings.Show();
        Application.DoEvents();
        settingsShow.Stop();
        var prewarm = Stopwatch.StartNew();
        settings.PrewarmAllPagesForTest();
        Application.DoEvents();
        prewarm.Stop();
        if (settings.BuiltPageCountForTest != 5)
            throw new InvalidOperationException("Settings prewarming did not prepare every page.");
        var firstMaximumTabMs = 0L;
        var firstMaximumCallMs = 0L;
        var firstTabTimes = new List<long>();
        var firstTabCallTimes = new List<long>();
        for (var page = 1; page < 5; page++)
        {
            var firstSwitch = Stopwatch.StartNew();
            settings.SelectPageForTest(page);
            firstTabCallTimes.Add(firstSwitch.ElapsedMilliseconds);
            firstMaximumCallMs = Math.Max(firstMaximumCallMs, firstSwitch.ElapsedMilliseconds);
            Application.DoEvents();
            firstSwitch.Stop();
            firstTabTimes.Add(firstSwitch.ElapsedMilliseconds);
            firstMaximumTabMs = Math.Max(firstMaximumTabMs, firstSwitch.ElapsedMilliseconds);
        }
        settings.SelectPageForTest(0);
        Application.DoEvents();

        var warmedMaximumTabMs = 0L;
        var warmedMaximumCallMs = 0L;
        for (var pass = 0; pass < 2; pass++)
        {
            for (var page = 0; page < 5; page++)
            {
                var warmedSwitch = Stopwatch.StartNew();
                settings.SelectPageForTest(page);
                warmedMaximumCallMs = Math.Max(warmedMaximumCallMs, warmedSwitch.ElapsedMilliseconds);
                Application.DoEvents();
                warmedSwitch.Stop();
                warmedMaximumTabMs = Math.Max(warmedMaximumTabMs, warmedSwitch.ElapsedMilliseconds);
            }
        }

        var languageSwitch = Stopwatch.StartNew();
        settings.SetLanguageForTest(1);
        Application.DoEvents();
        languageSwitch.Stop();
        if (!string.Equals(settings.Text, "Codex Quota Panel settings", StringComparison.Ordinal))
            throw new InvalidOperationException("The in-place English settings localization did not complete.");
        settings.SetLanguageForTest(0);
        Application.DoEvents();
        settings.SelectPageForTest(2);
        Application.DoEvents();
        settings.SavePreview(outputPath);

        Console.WriteLine(
            $"MOTION expand prep={expand.Preparation}ms call={expand.Call}ms total={expand.Duration}ms " +
            $"frames={expand.Frames} max-gap={expand.MaxGap}ms");
        Console.WriteLine(
            $"MOTION collapse prep={collapse.Preparation}ms call={collapse.Call}ms total={collapse.Duration}ms " +
            $"frames={collapse.Frames} max-gap={collapse.MaxGap}ms");
        Console.WriteLine(
            $"MOTION settings constructor={settingsConstructor.ElapsedMilliseconds}ms " +
            $"first-show={settingsShow.ElapsedMilliseconds}ms prewarm={prewarm.ElapsedMilliseconds}ms " +
            $"at maximum typography");
        Console.WriteLine(
            $"MOTION settings first tab after prewarm call=[{string.Join(",", firstTabCallTimes)}]ms " +
            $"settled=[{string.Join(",", firstTabTimes)}]ms max={firstMaximumTabMs}ms " +
            $"warmed-call={warmedMaximumCallMs}ms warmed-settled={warmedMaximumTabMs}ms");
        Console.WriteLine($"MOTION settings in-place language switch={languageSwitch.ElapsedMilliseconds}ms");
        if (settingsConstructor.ElapsedMilliseconds > 300 ||
            settingsShow.ElapsedMilliseconds > 900 ||
            firstMaximumCallMs > 24 || firstMaximumTabMs > 180 ||
            warmedMaximumCallMs > 16 || warmedMaximumTabMs > 180 ||
            languageSwitch.ElapsedMilliseconds > 650)
        {
            throw new InvalidOperationException(
                "Settings opening or tab switching exceeded the maximum-typography responsiveness budget.");
        }
    }

    private static void WaitForTransition(QuotaForm form)
    {
        var timeout = Stopwatch.StartNew();
        while (form.IsAnimating && timeout.ElapsedMilliseconds < 1500)
        {
            Application.DoEvents();
            Thread.Sleep(1);
        }
        if (form.IsAnimating)
            throw new InvalidOperationException("Transition did not finish within 1.5 seconds.");
    }

    private static MotionSnapshot Snapshot(QuotaForm form, long call) => new(
        form.TransitionPreparationMs,
        call,
        form.LastTransitionDurationMs,
        form.TransitionPaintFrames,
        form.TransitionMaxPaintGapMs);

    private sealed record MotionSnapshot(long Preparation, long Call, long Duration, int Frames, long MaxGap);
}
