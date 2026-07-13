using CodexQuotaPanel;
using System.Diagnostics;
using System.ComponentModel;
using System.Runtime.InteropServices;

internal static class LayeredRuntimePreview
{
    internal static void Run(string outputPath)
    {
        UiPalette.SetTheme(1);
        L10n.SetLanguage(AppLanguage.SimplifiedChinese);
        var now = DateTimeOffset.Now;
        using var form = new QuotaForm();
        form.ApplySnapshot(new QuotaSnapshot(
            "codex", null,
            new LimitBucket(18, 300, now.AddHours(3)),
            new LimitBucket(9, 10080, now.AddDays(6)),
            null, "pro", null, now, "App Server"));
        form.ShowOrb(animate: false);
        var area = Screen.PrimaryScreen!.WorkingArea;
        form.Location = new Point(
            area.Left + Math.Max(0, (area.Width - form.Width) / 2),
            area.Top + Math.Max(0, (area.Height - form.Height) / 2));
        form.Show();
        Application.DoEvents();

        var sampleTimes = new[] { 45, 135, 225, 295, 325 };
        var frames = new List<Bitmap>();
        form.ShowDetails(animate: true);
        var watch = Stopwatch.StartNew();
        foreach (var sampleTime in sampleTimes)
        {
            while (watch.ElapsedMilliseconds < sampleTime)
            {
                Application.DoEvents();
                Thread.Sleep(1);
            }
            Application.DoEvents();
            var overlay = form.InspectTransitionOverlay();
            Console.WriteLine(overlay is null
                ? $"runtime {sampleTime}ms | overlay missing"
                : $"runtime {sampleTime}ms | visible={overlay.Value.NativeVisible} | alphaPixels={overlay.Value.NonTransparentPixels} | maxAlpha={overlay.Value.MaximumAlpha}");
            var bounds = form.Bounds;
            var frame = new Bitmap(bounds.Width, bounds.Height);
            using (var graphics = Graphics.FromImage(frame))
            {
                CaptureLayeredScreen(graphics, bounds);
                using var overlayFrame = form.CaptureTransitionOverlayFrame();
                if (overlayFrame is not null)
                    graphics.DrawImageUnscaled(overlayFrame, Point.Empty);
            }
            frames.Add(frame);
        }

        const int labelHeight = 28;
        var cellWidth = frames.Max(frame => frame.Width);
        var cellHeight = frames.Max(frame => frame.Height) + labelHeight;
        using var contact = new Bitmap(cellWidth * frames.Count, cellHeight);
        using var contactGraphics = Graphics.FromImage(contact);
        contactGraphics.Clear(Color.FromArgb(12, 14, 13));
        using var labelFont = UiPalette.Mono(8f, FontStyle.Bold);
        for (var index = 0; index < frames.Count; index++)
        {
            contactGraphics.DrawImageUnscaled(frames[index], index * cellWidth, labelHeight);
            TextRenderer.DrawText(contactGraphics, $"runtime · {sampleTimes[index]} ms", labelFont,
                new Rectangle(index * cellWidth + 8, 2, cellWidth - 16, 24), UiPalette.Mint,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            frames[index].Dispose();
        }

        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        contact.Save(fullPath, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"PASS live layered-window screen capture | {fullPath}");
    }

    private static void CaptureLayeredScreen(Graphics destination, Rectangle bounds)
    {
        const int sourceCopyWithLayeredWindows = 0x40CC0020;
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) throw new Win32Exception();
        var destinationDc = destination.GetHdc();
        try
        {
            if (!BitBlt(destinationDc, 0, 0, bounds.Width, bounds.Height,
                    screenDc, bounds.Left, bounds.Top, sourceCopyWithLayeredWindows))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            destination.ReleaseHdc(destinationDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern bool BitBlt(
        IntPtr destinationDc, int x, int y, int width, int height,
        IntPtr sourceDc, int sourceX, int sourceY, int rasterOperation);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);
}
