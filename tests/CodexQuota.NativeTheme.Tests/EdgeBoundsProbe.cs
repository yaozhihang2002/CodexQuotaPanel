using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using CodexQuota.Platform.Windows;

[SupportedOSPlatform("windows")]
internal static class EdgeBoundsProbe
{
    public static void Run()
    {
        // Exercise a real HWND outside the desktop, using a virtual adjacent
        // monitor boundary. Do not move the user's running orb or touch settings.
        var window = new Window { WindowDecorations = WindowDecorations.None,
            TransparencyLevelHint = [WindowTransparencyLevel.Transparent], Background = Brushes.Transparent,
            CanResize = false, ShowActivated = false, ShowInTaskbar = false,
            WindowStartupLocation = WindowStartupLocation.Manual, Width = 32, Height = 76,
            Position = new PixelPoint(30000, 30000), Opacity = 0 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var handle = window.TryGetPlatformHandle()!.Handle;
            foreach (var edge in new[] { "left", "right", "top", "bottom" })
            foreach (var spill in new[] { 1, 2, 8 })
            {
                window.Position = new PixelPoint(30000, 30000);
                Dispatcher.UIThread.RunJobs();
                GetWindowRect(handle, out var before);
                var left = before.Left - 100; var top = before.Top - 100;
                var right = before.Right + 100; var bottom = before.Bottom + 100;
                if (edge == "left") left = before.Left + spill;
                if (edge == "right") right = before.Right - spill;
                if (edge == "top") top = before.Top + spill;
                if (edge == "bottom") bottom = before.Bottom - spill;
                var position = ContainedWindowPosition.Read(handle, left, top, right, bottom)
                    ?? throw new InvalidOperationException("Native bounds unavailable");
                window.Position = new PixelPoint(position.X, position.Y);
                Dispatcher.UIThread.RunJobs();
                GetWindowRect(handle, out var after);
                if (after.Left < left || after.Top < top || after.Right > right || after.Bottom > bottom)
                    throw new InvalidOperationException($"{edge}: native footprint still crosses monitor boundary");
                if (after.Right - after.Left != before.Right - before.Left || after.Bottom - after.Top != before.Bottom - before.Top)
                    throw new InvalidOperationException("Containment unexpectedly resized the window");
            }
            foreach (var edge in new[] { "left", "right", "top", "bottom" })
            {
                var horizontal = edge == "left" ? -1 : edge == "right" ? 1 : 0;
                var vertical = edge == "top" ? -1 : edge == "bottom" ? 1 : 0;
                // A large window is first pushed inward; after shrinking it
                // must be pulled back out to the seam, not merely contained.
                foreach (var size in new[] { 96, 32 })
                {
                    window.Width = size; window.Height = Math.Max(38, size);
                    Dispatcher.UIThread.RunJobs();
                    var position = ContainedWindowPosition.Read(handle, 29900, 29900, 30200, 30200, horizontal, vertical)!.Value;
                    window.Position = new PixelPoint(position.X, position.Y);
                    Dispatcher.UIThread.RunJobs();
                    var clientOrigin = new Point();
                    ClientToScreen(handle, ref clientOrigin);
                    GetClientRect(handle, out var client);
                    var aligned = edge switch
                    {
                        "left" => clientOrigin.X == 29900,
                        "right" => clientOrigin.X + client.Right == 30200,
                        "top" => clientOrigin.Y == 29900,
                        _ => clientOrigin.Y + client.Bottom == 30200
                    };
                    if (!aligned) throw new InvalidOperationException($"{edge}: gap remains after native resize to {size}");
                    if (ContainedWindowPosition.Read(handle, 29900, 29900, 30200, 30200, horizontal, vertical) != position)
                        throw new InvalidOperationException("Docking correction is not stable");
                }
            }
            Console.WriteLine("PASS native edge alignment: all four client edges flush after shrinking, stable repeated measurements.");
            Console.WriteLine("PASS native edge bounds: four sides, 1/2/8 physical-pixel spill, unchanged window size.");
        }
        finally { window.Close(); }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint handle, ref Point point);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint handle, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint handle, out Rect rect);
}
