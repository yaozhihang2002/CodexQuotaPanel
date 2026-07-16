using System.Runtime.InteropServices;

namespace CodexQuotaPanel;

/// <summary>
/// Monitor selection and DPI-aware geometry helpers kept outside the forms so
/// placement rules can be tested without exercising a live top-level window.
/// </summary>
internal static class DisplayPlacement
{
    private const int MonitorDefaultToNearest = 2;
    private const int EffectiveDpi = 0;

    internal static Screen SelectScreen(Rectangle bounds)
    {
        var screens = Screen.AllScreens;
        if (screens.Length == 0)
            return Screen.PrimaryScreen ?? Screen.FromPoint(bounds.Location);

        Screen? best = null;
        long bestIntersection = 0;
        foreach (var screen in screens)
        {
            var intersection = Rectangle.Intersect(bounds, screen.WorkingArea);
            var area = (long)Math.Max(0, intersection.Width) * Math.Max(0, intersection.Height);
            if (area <= bestIntersection) continue;
            best = screen;
            bestIntersection = area;
        }

        if (best is not null) return best;
        var center = new Point(
            bounds.Left + Math.Max(0, bounds.Width) / 2,
            bounds.Top + Math.Max(0, bounds.Height) / 2);
        return Screen.FromPoint(center);
    }

    internal static Rectangle ClampToArea(Rectangle bounds, Rectangle area)
    {
        var maxX = Math.Max(area.Left, area.Right - bounds.Width);
        var maxY = Math.Max(area.Top, area.Bottom - bounds.Height);
        bounds.X = Math.Clamp(bounds.X, area.Left, maxX);
        bounds.Y = Math.Clamp(bounds.Y, area.Top, maxY);
        return bounds;
    }

    internal static int ScaleLogicalPixels(int logicalPixels, int dpi) =>
        Math.Max(1, (int)Math.Round(logicalPixels * Math.Max(96, dpi) / 96d));

    internal static int GetEffectiveDpi(Screen screen, int fallbackDpi)
    {
        try
        {
            var center = new NativePoint(
                screen.Bounds.Left + screen.Bounds.Width / 2,
                screen.Bounds.Top + screen.Bounds.Height / 2);
            var monitor = MonitorFromPoint(center, MonitorDefaultToNearest);
            if (monitor != IntPtr.Zero &&
                GetDpiForMonitor(monitor, EffectiveDpi, out var dpiX, out _) == 0 &&
                dpiX is >= 96 and <= 960)
                return (int)dpiX;
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }

        return Math.Max(96, fallbackDpi);
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativePoint
    {
        public readonly int X;
        public readonly int Y;

        public NativePoint(int x, int y)
        {
            X = x;
            Y = y;
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(NativePoint point, int flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(
        IntPtr monitor,
        int dpiType,
        out uint dpiX,
        out uint dpiY);
}
