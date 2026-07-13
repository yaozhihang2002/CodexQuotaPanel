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

        var preferences = PanelPreferenceManager.Default with { Language = 0 };
        using var settings = new SettingsForm(preferences, startupEnabled: false, snapshot);
        settings.Show();
        Application.DoEvents();
        var tabWatch = Stopwatch.StartNew();
        var maximumTabMs = 0L;
        for (var pass = 0; pass < 2; pass++)
        {
            for (var page = 0; page < 5; page++)
            {
                var started = tabWatch.ElapsedMilliseconds;
                settings.SelectPageForTest(page);
                maximumTabMs = Math.Max(maximumTabMs, tabWatch.ElapsedMilliseconds - started);
            }
        }
        settings.SelectPageForTest(2);
        Application.DoEvents();
        settings.SavePreview(outputPath);

        Console.WriteLine(
            $"MOTION expand prep={expand.Preparation}ms call={expand.Call}ms total={expand.Duration}ms " +
            $"frames={expand.Frames} max-gap={expand.MaxGap}ms");
        Console.WriteLine(
            $"MOTION collapse prep={collapse.Preparation}ms call={collapse.Call}ms total={collapse.Duration}ms " +
            $"frames={collapse.Frames} max-gap={collapse.MaxGap}ms");
        Console.WriteLine($"MOTION settings max synchronous tab switch={maximumTabMs}ms");
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
