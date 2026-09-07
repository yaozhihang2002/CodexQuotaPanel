using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CodexQuota.Platform.Windows;

/// <summary>Physical-pixel resize borders excluded from DWM's visible window bounds.</summary>
[SupportedOSPlatform("windows")]
public static class VisibleWindowFrame
{
    public static InvisibleFrameInsets ReadInsets(nint handle)
    {
        if (handle == 0) return default;
        // GetWindowRect is DPI-virtualized; DWM frame bounds are not. Compare
        // both in physical pixels, without changing the target window's DPI.
        var previous = SetThreadDpiAwarenessContext(-4);
        try
        {
            if (previous == 0 || !GetWindowRect(handle, out var native) ||
                DwmGetWindowAttribute(handle, 9, out var visible, Marshal.SizeOf<Rect>()) != 0)
                return default;
            var left = visible.Left - native.Left;
            var top = visible.Top - native.Top;
            var right = native.Right - visible.Right;
            var bottom = native.Bottom - visible.Bottom;
            // Bounds may briefly refer to different frames during Show/move.
            // Reject unavailable/stale measurements instead of moving by a guess.
            var limit = Math.Max(32, (int)Math.Ceiling(GetDpiForWindow(handle) / 96d * 32));
            if (visible.Right <= visible.Left || visible.Bottom <= visible.Top ||
                left < 0 || top < 0 || right < 0 || bottom < 0 ||
                Math.Max(Math.Max(left, top), Math.Max(right, bottom)) > limit)
                return default;
            return new(left, top, right, bottom);
        }
        finally { if (previous != 0) SetThreadDpiAwarenessContext(previous); }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] private static extern nint SetThreadDpiAwarenessContext(nint context);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint handle, out Rect rect);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint handle);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(nint handle, int attribute, out Rect rect, int size);
}

public readonly record struct InvisibleFrameInsets(int Left, int Top, int Right, int Bottom);
