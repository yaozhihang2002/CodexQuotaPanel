using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CodexQuota.Application;
using CodexQuota.Domain;
using CodexQuota.UI.Avalonia;

internal static class ProductivityChecks
{
    public static void Run(string outputRoot)
    {
        var now = DateTimeOffset.Parse("2026-10-02T07:00:00Z");
        var usage = new[] { new ObservedUsage(now.AddMinutes(-5), "gpt-6.1-sol", "default",
            new(51000,50000,30000,1000,200), "fixture", true),
            new ObservedUsage(now.AddMinutes(-3), "future-model", "unknown", new(9000,8000,0,1000,0), "unknown", false) };
        foreach (var (language, theme) in new[] { (AppLanguage.SimplifiedChinese, AppTheme.Dark), (AppLanguage.English, AppTheme.Light) })
        {
            var window = new UsageDetailsWindow(new AppSettings { Language = language, Theme = theme });
            window.Show();
            window.ApplyUsage(usage, now.AddDays(-1), now.AddDays(6));
            Save(window, Path.Combine(outputRoot, $"usage-simplified-{language}-{theme}.png"));
            Check.True(!window.GetVisualDescendants().OfType<Button>().Any(b => b.Content?.ToString() is "Start work" or "开始一段工作" or "结束计数"),
                "manual work controls removed");
            Check.True(window.GetVisualDescendants().OfType<TextBlock>().Any(t => (t.Text ?? "").Contains("Unpriced")), "unknown rates remain visible");
            window.Close();
        }

        var area = new PixelRect(-1920, -1080, 1920, 1080);
        Check.True(OrbWindow.SelectEdge(new(-1920, -1080), 96, area, 18) == DockEdge.Left, "top-left prefers left");
        Check.True(OrbWindow.SelectEdge(new(-96, -96), 96, area, 18) == DockEdge.Right, "bottom-right prefers right");
        Check.True(OrbWindow.SelectEdge(new(-1000, -1080), 96, area, 18) == DockEdge.Top, "negative display top edge");
        Check.True(OrbWindow.SelectEdge(new(-1000, -96), 96, area, 18) == DockEdge.Bottom, "negative display bottom edge");
        Check.True(OrbWindow.SelectEdge(new(-1000, -600), 96, area, 18) == DockEdge.None, "center never collapses");

        var orb = new OrbWindow();
        orb.ApplySettings(new AppSettings { OrbSize = 96, EdgeAutoHide = true, ReducedMotion = true });
        orb.ApplyPresentation(QuotaPresentation.Empty with { Snapshot = new OfficialQuotaSnapshot(now,
            [new("5h", 300, 71, now.AddHours(2)), new("7d", 10080, 42, now.AddDays(3))]) });
        orb.Show();
        if (orb.Screens.Primary is not { } screen) throw new InvalidOperationException("No test screen");
        area = screen.WorkingArea;
        var size = (int)Math.Ceiling(96 * screen.Scaling);
        foreach (var edge in new[] { DockEdge.Left, DockEdge.Right, DockEdge.Top, DockEdge.Bottom })
        {
            var anchor = edge switch
            {
                DockEdge.Left => new PixelPoint(area.X, area.Y + 150),
                DockEdge.Right => new PixelPoint(area.Right - size, area.Y + 150),
                DockEdge.Top => new PixelPoint(area.X + 150, area.Y),
                _ => new PixelPoint(area.X + 150, area.Bottom - size)
            };
            orb.MouseMove(new Point(-100, -100));
            orb.Position = anchor;
            Check.True(orb.TryCollapseToEdge() && orb.CollapsedEdge == edge, edge + " collapses");
            var horizontal = edge is DockEdge.Top or DockEdge.Bottom;
            Check.True(orb.Width == (horizontal ? 76 : OrbWindow.EdgeWidth) && orb.Height == (horizontal ? OrbWindow.EdgeHorizontalHeight : 76),
                edge + " native window uses the compact input footprint");
            Check.True(orb.ExpandedPosition == anchor, "docking retains original anchor");
            Check.True(((EdgeQuotaControl)orb.Content!).ArcCount == 2, "5-hour and weekly quotas use two arcs");
            Save(orb, Path.Combine(outputRoot, $"edge-handle-{edge}.png"));
            orb.MouseMove(new Point(10, 10));
            Dispatcher.UIThread.RunJobs();
            Check.True(orb.Width == 96 && orb.Position == anchor && !orb.IsEdgeCollapsed, edge + " hover restores size and anchor");
        }
        orb.MouseMove(new Point(-100, -100));
        orb.ApplySettings(new AppSettings { ClickThrough = true, EdgeAutoHide = true });
        Check.True(!orb.TryCollapseToEdge(), "click-through orb never hides recovery target");
        orb.ApplySettings(new AppSettings { OrbSize = 96, EdgeAutoHide = true, ReducedMotion = true });
        orb.SetMoveMode(true);
        Check.True(!orb.TryCollapseToEdge(), "move mode never docks");
        orb.SetMoveMode(false);
        orb.Position = new PixelPoint(area.Right - size, area.Y + 150);
        Check.True(orb.TryCollapseToEdge(), "right edge collapses");
        orb.ApplySettings(new AppSettings { OrbSize = 120, EdgeAutoHide = false });
        Check.True(!orb.IsEdgeCollapsed && orb.Width == 120, "disabling docking expands before resize");
        orb.Close();
        Check.True(!orb.TryCollapseToEdge(), "closed orb cannot redock");

        var animated = new OrbWindow();
        animated.ApplySettings(new AppSettings { OrbSize = 96, EdgeAutoHide = true });
        animated.ApplyPresentation(QuotaPresentation.Empty with { Snapshot = new OfficialQuotaSnapshot(now,
            [new("7d", 10080, 73, now.AddDays(3))]) });
        animated.Position = new PixelPoint(area.X, area.Y + 150);
        animated.Show();
        var fadeAnchor = animated.Position;
        var visibleGeometryChanges = 0;
        animated.PropertyChanged += (_, e) =>
        {
            if (animated.IsEdgeAnimating && animated.Opacity > .001 &&
                (e.Property == Window.WidthProperty || e.Property == Window.HeightProperty))
                visibleGeometryChanges++;
        };
        animated.PositionChanged += (_, _) =>
        {
            if (animated.IsEdgeAnimating && animated.Opacity > .001) visibleGeometryChanges++;
        };
        Check.True(animated.TryCollapseToEdge() && animated.IsEdgeAnimating, "default motion animates");
        PumpUntil(() => animated.EdgeMotionProgress >= .15);
        Check.True(animated.Width == 96 && animated.Position == fadeAnchor && animated.Opacity is > 0 and < 1,
            "orb fades out in place without resizing");
        Save(animated, Path.Combine(outputRoot, "edge-motion-mid-collapse.png"));
        PumpUntil(() => animated.IsEdgeClearing);
        Check.True(animated.Width == 96 && animated.Position == fadeAnchor && animated.Content is null && animated.Opacity == 0,
            "clear the orb before changing its native footprint");
        Save(animated, Path.Combine(outputRoot, "edge-motion-cleared-orb.png"), requireTransparent: true);
        PumpUntil(() => animated.EdgeMotionProgress >= .65);
        Check.True(animated.Width == 32 && animated.Content is EdgeQuotaControl && animated.Opacity is > 0 and < 1,
            "arc fades in at its final size");
        Save(animated, Path.Combine(outputRoot, "edge-motion-arc-fade-in.png"));
        PumpUntil(() => !animated.IsEdgeAnimating);
        Check.True(animated.Width == 32 && ((EdgeQuotaControl)animated.Content!).ArcCount == 1, "single quota renders one arc");
        Save(animated, Path.Combine(outputRoot, "edge-single.png"));
        animated.ExpandFromEdge(animate: true);
        PumpUntil(() => animated.EdgeMotionProgress >= .15);
        Check.True(animated.Width == 32 && animated.Opacity is > 0 and < 1, "arc fades out without resizing");
        Save(animated, Path.Combine(outputRoot, "edge-motion-mid-expand.png"));
        PumpUntil(() => animated.IsEdgeClearing);
        Check.True(animated.Width == 32 && animated.Content is null && animated.Opacity == 0,
            "clear the arc before restoring the native orb footprint");
        Save(animated, Path.Combine(outputRoot, "edge-motion-cleared-arc.png"), requireTransparent: true);
        PumpUntil(() => animated.EdgeMotionProgress >= .65);
        Check.True(animated.Width == 96 && animated.Position == fadeAnchor && animated.Opacity is > 0 and < 1,
            "orb fades in at its original size and position");
        Save(animated, Path.Combine(outputRoot, "edge-motion-orb-fade-in.png"));
        PumpUntil(() => !animated.IsEdgeAnimating);
        Check.True(animated.Width == 96 && !animated.IsEdgeCollapsed && animated.Opacity == 1, "animated expansion completes");
        Check.True(visibleGeometryChanges == 0, "all geometry switches happen while transparent");
        animated.MouseMove(new Point(-100, -100));
        Check.True(animated.TryCollapseToEdge(), "can collapse again");
        PumpUntil(() => animated.EdgeMotionProgress >= .15);
        animated.ExpandFromEdge(animate: true);
        PumpUntil(() => !animated.IsEdgeAnimating);
        Check.True(animated.Width == 96 && !animated.IsEdgeCollapsed, "hover interrupts collapse safely");
        foreach (var interruptProgress in new[] { .5, .7 })
        {
            animated.MouseMove(new Point(-100, -100));
            Check.True(animated.TryCollapseToEdge(), "collapse for interruption check");
            PumpUntil(() => animated.EdgeMotionProgress >= interruptProgress);
            animated.ExpandFromEdge(animate: true);
            PumpUntil(() => !animated.IsEdgeAnimating);
            Check.True(animated.Content is OrbControl && animated.Opacity == 1 && animated.Position == fadeAnchor && !animated.IsEdgeCollapsed,
                "hover during clearing or arc fade-in safely restores the orb");
        }
        animated.MouseMove(new Point(-100, -100));
        Check.True(animated.TryCollapseToEdge(), "shutdown starts during motion");
        animated.Close();
        Check.True(!animated.IsEdgeAnimating && !animated.TryCollapseToEdge(), "shutdown cancels animation and prevents reshow");
        Console.WriteLine("UI refinement checks passed (simplified usage, four edges, side priority, hover recovery and opt-outs).");
    }

    private static void PumpUntil(Func<bool> finished)
    {
        var deadline = Environment.TickCount64 + 2500;
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(5) };
        timer.Tick += (_, _) =>
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            if (finished() || Environment.TickCount64 >= deadline) frame.Continue = false;
        };
        timer.Start();
        try { Dispatcher.UIThread.PushFrame(frame); }
        finally { timer.Stop(); }
        Check.True(finished(), "edge animation finishes within deadline");
    }

    private static void Save(Window window, string path, bool requireTransparent = false)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("Missing UI frame");
        if (requireTransparent)
        {
            Check.True(frame.Format?.BitsPerPixel == 32, "transparent-frame test uses a 32-bit bitmap");
            var stride = frame.PixelSize.Width * 4;
            var bytes = new byte[stride * frame.PixelSize.Height];
            var pinned = GCHandle.Alloc(bytes, GCHandleType.Pinned);
            try { frame.CopyPixels(new PixelRect(frame.PixelSize), pinned.AddrOfPinnedObject(), bytes.Length, stride); }
            finally { pinned.Free(); }
            Check.True(Enumerable.Range(0, bytes.Length / 4).All(p => bytes[p * 4 + 3] == 0),
                "actual rendered frame is fully transparent, not only the opacity property");
        }
        using var output = File.Create(path);
        frame.Save(output, PngBitmapEncoderOptions.Default);
    }
}
