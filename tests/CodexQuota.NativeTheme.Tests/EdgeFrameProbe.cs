using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.Json;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using CodexQuota.Application;
using CodexQuota.Domain;
using CodexQuota.Platform.Windows;
using CodexQuota.UI.Avalonia;

// Capture only this test's own solid-color backdrop and orb, not the full desktop.
[SupportedOSPlatform("windows")]
internal static class EdgeFrameProbe
{
    public static void Run(string outputDirectory, bool dual = false)
    {
        Directory.CreateDirectory(outputDirectory);
        var orb = new OrbWindow();
        var screen = orb.Screens.Primary!;
        var area = screen.WorkingArea;
        var scale = screen.Scaling;
        var origin = new PixelPoint(area.Right - (int)(240 * scale), area.Y + (int)(240 * scale));
        var backdrop = new Window { WindowDecorations = WindowDecorations.None, ShowActivated = false,
            ShowInTaskbar = false, Topmost = true, Width = 240, Height = 180, Position = origin,
            Background = new SolidColorBrush(Color.Parse("#506070")) };
        backdrop.Show();
        Pump(250);
        var size = new PixelSize((int)(240 * scale), (int)(180 * scale));
        var baseline = Capture(origin, size);
        orb.ApplySettings(new AppSettings { OrbSize = 96, EdgeAutoHide = true, ConsumptionFeedbackEnabled = false, HoverPreviewEnabled = false });
        orb.ApplyPresentation(QuotaPresentation.Empty with { Snapshot = new OfficialQuotaSnapshot(DateTimeOffset.Now,
            dual ? [new("5h", 300, 56, DateTimeOffset.Now.AddHours(2)), new("7d", 10080, 73, DateTimeOffset.Now.AddDays(3))]
                : [new("7d", 10080, 73, DateTimeOffset.Now.AddDays(3))]) });
        orb.Position = new PixelPoint(area.Right - (int)(96 * scale), origin.Y + (int)(40 * scale));
        orb.Show();
        new WindowsPlatformShell().SetWindowTopMost(orb.TryGetPlatformHandle()!.Handle, true);
        Pump(200);
        var initial = Capture(origin, size);
        Save(initial, size, Path.Combine(outputDirectory, "initial.png"));
        if (CountChanged(initial, baseline) < 100)
        {
            orb.Close(); backdrop.Close();
            throw new InvalidOperationException("Frame capture cannot see the test orb; no pixel result is valid.");
        }
        var frames = new List<object>();
        var samples = new List<(string Mode, bool Arc, byte[] Pixels, int Changed)>();
        try
        {
            Record("collapse", () => { if (!orb.TryCollapseToEdge()) throw new InvalidOperationException("Capture orb did not collapse."); });
            Record("expand", () => orb.ExpandFromEdge(animate: true));
            var outsideArcResiduals = ResidualPixelsAfterBlank("collapse");
            var outsideOrbResiduals = ResidualPixelsAfterBlank("expand");
            var result = new { samples = frames.Count, blankCollapseFrames = samples.Count(s => s.Mode == "collapse" && s.Changed <= 4),
                blankExpandFrames = samples.Count(s => s.Mode == "expand" && s.Changed <= 4), outsideArcResidualPixels = outsideArcResiduals,
                outsideOrbResidualPixels = outsideOrbResiduals, dual, scale, frames };
            for (var n = 0; n < samples.Count; n++)
                Save(samples[n].Pixels, size, Path.Combine(outputDirectory, $"{n:D3}-{samples[n].Mode}.png"));
            File.WriteAllText(Path.Combine(outputDirectory, "frames.json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
            Console.WriteLine(JsonSerializer.Serialize(new { result.samples, result.blankCollapseFrames, result.blankExpandFrames,
                result.outsideArcResidualPixels, result.outsideOrbResidualPixels, dual, outputDirectory }));
            if (result.blankCollapseFrames == 0 || result.blankExpandFrames == 0 || outsideArcResiduals > 4 || outsideOrbResiduals > 4)
                throw new InvalidOperationException("Native frames must show a fully clear gap and only the destination shape after it.");
        }
        finally { orb.Close(); backdrop.Close(); }

        int ResidualPixelsAfterBlank(string mode)
        {
            var sequence = samples.Where(s => s.Mode == mode).ToArray();
            var firstBlank = Array.FindIndex(sequence, s => s.Changed <= 4);
            if (firstBlank < 0) return int.MaxValue;
            var destination = sequence[^1].Pixels;
            if (sequence[^1].Changed < 100) throw new InvalidOperationException("Destination shape was not captured.");
            return sequence.Skip(firstBlank).Max(s => CountChanged(s.Pixels, baseline, destination));
        }

        void Record(string mode, Action begin)
        {
            var watch = Stopwatch.StartNew();
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            begin();
            timer.Tick += (_, _) =>
            {
                var pixels = Capture(origin, size);
                var n = frames.Count;
                var changed = CountChanged(pixels, baseline);
                var arc = orb.Content is EdgeQuotaControl;
                samples.Add((mode, arc, pixels, changed));
                frames.Add(new { n, mode, ms = watch.ElapsedMilliseconds, opacity = orb.Opacity, progress = orb.EdgeMotionProgress,
                    arc, clearing = orb.IsEdgeClearing, width = orb.Width, changedPixels = changed });
                if (watch.ElapsedMilliseconds >= 900 && !orb.IsEdgeAnimating) frame.Continue = false;
                if (watch.ElapsedMilliseconds >= 3000) throw new TimeoutException("Frame capture timed out.");
            };
            timer.Start();
            try { Dispatcher.UIThread.PushFrame(frame); }
            finally { timer.Stop(); }
        }
    }

    private static int CountChanged(byte[] pixels, byte[] baseline, byte[]? exclude = null)
    {
        var changed = 0;
        for (var p = 0; p < pixels.Length; p += 4)
        {
            int Delta(byte[] a) => Math.Max(Math.Abs(a[p] - baseline[p]), Math.Max(Math.Abs(a[p + 1] - baseline[p + 1]), Math.Abs(a[p + 2] - baseline[p + 2])));
            if (Delta(pixels) > 12 && (exclude is null || Delta(exclude) <= 12)) changed++;
        }
        return changed;
    }

    private static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => frame.Continue = false;
        timer.Start();
        try { Dispatcher.UIThread.PushFrame(frame); } finally { timer.Stop(); }
    }

    private static byte[] Capture(PixelPoint origin, PixelSize size)
    {
        var screen = GetDC(0);
        var dc = CreateCompatibleDC(screen);
        var bitmap = CreateCompatibleBitmap(screen, size.Width, size.Height);
        var previous = SelectObject(dc, bitmap);
        try
        {
            if (!BitBlt(dc, 0, 0, size.Width, size.Height, screen, origin.X, origin.Y, 0x40CC0020))
                throw new InvalidOperationException("Test-region capture failed.");
            SelectObject(dc, previous);
            var info = new BitmapInfo { Size = 40, Width = size.Width, Height = -size.Height, Planes = 1, BitCount = 32 };
            var pixels = new byte[size.Width * size.Height * 4];
            if (GetDIBits(dc, bitmap, 0, (uint)size.Height, pixels, ref info, 0) == 0)
                throw new InvalidOperationException("Test-region pixel read failed.");
            for (var p = 3; p < pixels.Length; p += 4) pixels[p] = 255;
            return pixels;
        }
        finally { SelectObject(dc, previous); DeleteObject(bitmap); DeleteDC(dc); ReleaseDC(0, screen); }
    }

    private static void Save(byte[] pixels, PixelSize size, string path)
    {
        using var bitmap = new WriteableBitmap(size, new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
        using (var buffer = bitmap.Lock())
            for (var row = 0; row < size.Height; row++)
                Marshal.Copy(pixels, row * size.Width * 4, buffer.Address + row * buffer.RowBytes, size.Width * 4);
        using var output = File.Create(path);
        bitmap.Save(output, PngBitmapEncoderOptions.Default);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public uint Size; public int Width, Height; public ushort Planes, BitCount;
        public uint Compression, ImageSize; public int XPels, YPels; public uint ColorsUsed, ColorsImportant;
    }
    [DllImport("user32.dll")] private static extern nint GetDC(nint window);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint window, nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleBitmap(nint dc, int width, int height);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc, nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool BitBlt(nint target, int x, int y, int width, int height, nint source, int sourceX, int sourceY, uint rop);
    [DllImport("gdi32.dll")] private static extern int GetDIBits(nint dc, nint bitmap, uint start, uint lines, byte[] pixels, ref BitmapInfo info, uint usage);
}
