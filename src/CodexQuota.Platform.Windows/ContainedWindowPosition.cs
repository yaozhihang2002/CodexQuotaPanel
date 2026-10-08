using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CodexQuota.Platform.Windows;

/// <summary>Align the actual client surface, excluding invisible native borders.</summary>
[SupportedOSPlatform("windows")]
public static class ContainedWindowPosition
{
    public static (int X, int Y)? Read(nint handle, int left, int top, int right, int bottom,
        int horizontalDock = 0, int verticalDock = 0)
    {
        if (handle == 0 || right <= left || bottom <= top) return null;
        var previous = SetThreadDpiAwarenessContext(-4);
        try
        {
            if (previous == 0 || !GetWindowRect(handle, out var rect) ||
                rect.Right <= rect.Left || rect.Bottom <= rect.Top) return null;
            var origin = new Point();
            if (!GetClientRect(handle, out var client) || !ClientToScreen(handle, ref origin) ||
                client.Right <= 0 || client.Bottom <= 0) return null;
            var maxX = Math.Max(left, right - client.Right);
            var maxY = Math.Max(top, bottom - client.Bottom);
            var x = horizontalDock < 0 ? left : horizontalDock > 0 ? maxX : Math.Clamp(origin.X, left, maxX);
            var y = verticalDock < 0 ? top : verticalDock > 0 ? maxY : Math.Clamp(origin.Y, top, maxY);
            // Return the outer-window origin expected by Avalonia.Position.
            return (x - (origin.X - rect.Left), y - (origin.Y - rect.Top));
        }
        finally { if (previous != 0) SetThreadDpiAwarenessContext(previous); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct Point { public int X, Y; }
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint handle, out Rect rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(nint handle, ref Point point);
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint handle, out Rect rect);
}
