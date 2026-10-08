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
    public static void RunEdgeContainmentMotion()
    {
        var orb = new OrbWindow();
        orb.ApplySettings(new AppSettings { OrbSize = 96, EdgeAutoHide = true });
        orb.Show();
        orb.MouseMove(new Point(-100, -100));
        var screen = orb.Screens.Primary!;
        var area = screen.WorkingArea;
        var anchor = new PixelPoint(area.Right - (int)Math.Ceiling(96 * screen.Scaling), area.Y + 150);
        orb.Position = anchor;
        var target = OrbWindow.CollapsedGeometry(anchor, DockEdge.Right, area, screen.Scaling, 96).Position;
        var calls = 0;
        // Simulate a native resize that settles one frame after initial layout.
        orb.NativeEdgePosition = (_, _) => target + new PixelVector(++calls == 1 ? -64 : 0, 0);
        var visibleMoves = 0;
        orb.PositionChanged += (_, _) => { if (orb.Opacity > .001) visibleMoves++; };
        try
        {
            Check.True(orb.TryCollapseToEdge(), "animated containment starts");
            PumpUntil(() => !orb.IsEdgeAnimating, 5000);
            Check.True(calls >= 3 && orb.Position == target,
                "native corrections settle before fade-in");
            Check.True(visibleMoves == 0 && orb.Opacity == 1 && orb.IsEdgeCollapsed,
                "fade-in completion never resets visible geometry");
            var settledCalls = calls;
            orb.ExpandFromEdge(animate: true);
            PumpUntil(() => !orb.IsEdgeAnimating, 5000);
            Check.True(orb.Position == anchor && !orb.IsEdgeCollapsed && visibleMoves == 0 && calls == settledCalls,
                "expansion restores anchor only while transparent");
            Console.WriteLine("PASS edge containment motion: late native correction renders while transparent; no visible movement on collapse or expansion.");
        }
        finally { orb.Close(); }
    }

    public static void RunPlacement()
    {
        var localArea = new PixelRect(0, 0, 1920, 1040);
        var remoteArea = new PixelRect(0, 0, 1280, 720);
        var localAnchor = new OrbPlacementAnchor(new(1824, 472), localArea, 96, "local");
        Check.True(localAnchor.Project(remoteArea, 120) == new PixelPoint(1160, 300), "RDP keeps right edge and relative height at changed DPI");
        Check.True(localAnchor.Project(localArea, 96) == new PixelPoint(1824, 472), "local reconnect restores exact original anchor");
        for (var i = 0; i < 20; i++)
        {
            _ = localAnchor.Project(new(-1280, -720, 1280, 720), 144);
            Check.True(localAnchor.Project(localArea, 96) == localAnchor.Position, "repeated temporary layouts do not accumulate drift");
        }
        Check.True(localAnchor.Project(new(0, 0, 64, 64), 96) == new PixelPoint(0, 0), "tiny transient display clamps safely");
        var placementOrb = new OrbWindow();
        placementOrb.ApplySettings(new AppSettings { OrbSize = 96, ReducedMotion = true });
        placementOrb.Show();
        var placementArea = placementOrb.Screens.Primary!.WorkingArea;
        var intended = new PixelPoint(placementArea.X + 200, placementArea.Y + 180);
        placementOrb.RestorePosition(intended.X, intended.Y);
        placementOrb.Position = new PixelPoint(placementArea.X, placementArea.Y);
        placementOrb.RestoreRememberedPosition(0, 0);
        Check.True(placementOrb.Position == intended, "temporary OS position does not overwrite user anchor");
        var hides = 0;
        var reveals = 0;
        ((Control)placementOrb.Content!).PropertyChanged += (_, e) =>
        {
            if (e.Property != Visual.IsVisibleProperty) return;
            if (e.NewValue is false) hides++;
            else reveals++;
        };
        placementOrb.SetPlacementContext(true);
        Check.True(placementOrb.IsPlacementConcealed, "hide intermediate position immediately");
        placementOrb.Position = new PixelPoint(placementArea.X, placementArea.Y);
        var bursts = 0;
        var notifications = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(450) };
        notifications.Tick += (_, _) =>
        {
            placementOrb.SetPlacementContext(true, displayChanged: true);
            Check.True(placementOrb.IsPlacementConcealed, "consecutive notifications never reveal an intermediate frame");
            if (++bursts == 3) notifications.Stop();
        };
        notifications.Start();
        try { PumpUntil(() => placementOrb.Position == intended && !placementOrb.IsPlacementConcealed, 5000); }
        finally { notifications.Stop(); }
        Check.True(bursts == 3 && hides == 1 && reveals == 1, "notification burst hides once and reveals once");
        var remoteIntent = new PixelPoint(placementArea.X + 350, placementArea.Y + 250);
        placementOrb.RestorePosition(remoteIntent.X, remoteIntent.Y);
        placementOrb.SetPlacementContext(false);
        PumpUntil(() => placementOrb.Position == intended && !placementOrb.IsPlacementConcealed);
        placementOrb.SetPlacementContext(true);
        PumpUntil(() => placementOrb.Position == remoteIntent && !placementOrb.IsPlacementConcealed);
        placementOrb.SetPlacementContext(true, displayChanged: true);
        Check.True(!placementOrb.IsPlacementConcealed, "late duplicate final-layout notification does not flash again");
        placementOrb.SetPlacementContext(false);
        placementOrb.Hide();
        PumpUntil(() => !placementOrb.IsPlacementConcealed);
        Check.True(!placementOrb.IsVisible, "display recovery never reopens a user-hidden orb");
        placementOrb.Show();
        placementOrb.SetPlacementContext(true);
        Check.True(placementOrb.IsPlacementConcealed, "close test starts during pending recovery");
        placementOrb.Close();
        Check.True(!placementOrb.IsVisible, "closing cancels pending display recovery");
        foreach (var edge in new[] { DockEdge.Left, DockEdge.Right, DockEdge.Top, DockEdge.Bottom })
        {
            var compactOrb = new OrbWindow();
            compactOrb.ApplySettings(new AppSettings { OrbSize = 96, EdgeAutoHide = true, ReducedMotion = true });
            compactOrb.Show();
            var screen = compactOrb.Screens.Primary!;
            var area = screen.WorkingArea;
            var size = (int)Math.Ceiling(96 * screen.Scaling);
            var anchor = edge switch
            {
                DockEdge.Left => new PixelPoint(area.X, area.Y + 150),
                DockEdge.Right => new PixelPoint(area.Right - size, area.Y + 150),
                DockEdge.Top => new PixelPoint(area.X + 150, area.Y),
                _ => new PixelPoint(area.X + 150, area.Bottom - size)
            };
            compactOrb.MouseMove(new Point(-100, -100));
            compactOrb.RestorePosition(anchor.X, anchor.Y);
            Check.True(compactOrb.TryCollapseToEdge(), edge + " collapsed before session change");
            Check.True(((Control)compactOrb.Content!).ClipToBounds, "arc stroke is clipped at the control boundary");
            var compactPosition = compactOrb.Position;
            var expandedDuringRecovery = false;
            compactOrb.PropertyChanged += (_, e) =>
            {
                if (e.Property == ContentControl.ContentProperty && compactOrb.Content is OrbControl)
                    expandedDuringRecovery = true;
            };
            compactOrb.SetPlacementContext(true);
            compactOrb.MouseMove(new Point(10, 10));
            Dispatcher.UIThread.RunJobs();
            Check.True(compactOrb.IsEdgeCollapsed, "recovery hover cannot expand compact surface");
            compactOrb.Position = new PixelPoint(area.X + 20, area.Y + 20);
            PumpUntil(() => !compactOrb.IsPlacementPending, 5000);
            Check.True(compactOrb.CollapsedEdge == edge && compactOrb.Position == compactPosition &&
                compactOrb.ExpandedPosition == anchor && !expandedDuringRecovery,
                edge + " recovery preserves compact surface and anchor without intermediate orb");
            compactOrb.RestoreRememberedPosition(0, 0);
            Check.True(compactOrb.IsEdgeCollapsed, "coordinator restore also preserves collapsed state");
            compactOrb.SetPlacementContext(false);
            PumpUntil(() => !compactOrb.IsPlacementPending, 5000);
            Check.True(compactOrb.CollapsedEdge == edge && !expandedDuringRecovery, "local reconnect stays collapsed");
            compactOrb.MouseMove(new Point(-100, -100));
            compactOrb.MouseMove(new Point(10, 10));
            Dispatcher.UIThread.RunJobs();
            Check.True(!compactOrb.IsEdgeCollapsed, "intentional hover still expands after recovery");
            compactOrb.MouseMove(new Point(-100, -100));
            var nativeChecks = 0;
            var contained = compactPosition + new PixelVector(edge == DockEdge.Right ? -2 : 0, edge == DockEdge.Bottom ? -2 : 0);
            compactOrb.NativeEdgePosition = (_, _) => { nativeChecks++; return contained; };
            Check.True(compactOrb.TryCollapseToEdge() && compactOrb.Position == contained && nativeChecks > 0,
                "collapse uses native containment before showing compact surface");
            compactOrb.RestoreRememberedPosition(0, 0);
            Check.True(compactOrb.Position == contained && compactOrb.IsEdgeCollapsed && nativeChecks > 1,
                "display recovery reapplies containment without expanding");
            compactOrb.ExpandFromEdge();
            Check.True(compactOrb.Position == anchor && !compactOrb.IsEdgeCollapsed,
                "native edge correction never overwrites expanded anchor");
            compactOrb.Close();
            var projected = localAnchor.Project(remoteArea, 120);
            var geometry = OrbWindow.CollapsedGeometry(projected, edge, remoteArea, 1.25, 96);
            Check.True(edge switch
            {
                DockEdge.Left => geometry.Position.X == remoteArea.X,
                DockEdge.Right => geometry.Position.X + (int)Math.Ceiling(geometry.Width * 1.25) == remoteArea.Right,
                DockEdge.Top => geometry.Position.Y == remoteArea.Y,
                _ => geometry.Position.Y + (int)Math.Ceiling(geometry.Height * 1.25) == remoteArea.Bottom
            }, "compact surface stays on selected edge after DPI change");
        }
        Console.WriteLine("Placement checks passed: relative edges, DPI, negative coordinates, repeated layouts, temporary OS moves, local/remote anchors.");
    }

    public static void Run(string outputRoot)
    {
        RunPlacement();
        RunEdgeContainmentMotion();
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

    private static void PumpUntil(Func<bool> finished, int timeoutMilliseconds = 2500)
    {
        var deadline = Environment.TickCount64 + timeoutMilliseconds;
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
