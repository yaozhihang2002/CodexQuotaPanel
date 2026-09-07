using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace CodexQuota.Platform.Windows;

/// <summary>A hidden, non-message-only HWND receives display broadcasts and this session's WTS events.</summary>
[SupportedOSPlatform("windows")]
public sealed class DisplaySessionMonitor : IDisposable
{
    private readonly SubclassProc _procedure;
    private readonly Action<uint, int> _changed;
    private bool _disposed;
    public nint Handle { get; }
    public bool SessionNotificationsRegistered { get; }
    public static bool IsRemoteSession => GetSystemMetrics(0x1000) != 0;

    public DisplaySessionMonitor(Action<uint, int> changed)
    {
        _changed = changed;
        _procedure = OnMessage;
        Handle = CreateWindowExW(0, "STATIC", "CodexQuotaDisplayMonitor", 0x80000000,
            0, 0, 0, 0, 0, 0, 0, 0);
        if (Handle == 0) throw new InvalidOperationException("Display monitor window creation failed.");
        if (!SetWindowSubclass(Handle, _procedure, 1, 0))
        {
            DestroyWindow(Handle);
            throw new InvalidOperationException("Display monitor hook creation failed.");
        }
        SessionNotificationsRegistered = WTSRegisterSessionNotification(Handle, 0);
    }

    private nint OnMessage(nint window, uint message, nint wParam, nint lParam, nuint id, nuint data)
    {
        if (!_disposed && (message == 0x02B1 || message == 0x007E ||
                           message == 0x001A && wParam == 47))
        {
            // Never consume the native message or let managed code escape the WndProc.
            try { _changed(message, (int)wParam); } catch { }
        }
        return DefSubclassProc(window, message, wParam, lParam);
    }

    public static NativeWindowGeometry? ReadGeometry(nint handle)
    {
        if (handle == 0 || !GetClientRect(handle, out var client) || !GetWindowRect(handle, out var outer)) return null;
        return new(GetDpiForWindow(handle), client.Right - client.Left, client.Bottom - client.Top,
            outer.Left, outer.Top, outer.Right - outer.Left, outer.Bottom - outer.Top);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (SessionNotificationsRegistered) WTSUnRegisterSessionNotification(Handle);
        RemoveWindowSubclass(Handle, _procedure, 1);
        DestroyWindow(Handle);
    }

    private delegate nint SubclassProc(nint window, uint message, nint wParam, nint lParam, nuint id, nuint data);
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern nint CreateWindowExW(uint ex, string cls, string title, uint style, int x, int y, int width, int height, nint parent, nint menu, nint instance, nint param);
    [DllImport("user32.dll")] private static extern bool DestroyWindow(nint window);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint window);
    [DllImport("user32.dll")] private static extern bool GetClientRect(nint window, out Rect rect);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(nint window, out Rect rect);
    [DllImport("comctl32.dll")] private static extern bool SetWindowSubclass(nint window, SubclassProc callback, nuint id, nuint data);
    [DllImport("comctl32.dll")] private static extern bool RemoveWindowSubclass(nint window, SubclassProc callback, nuint id);
    [DllImport("comctl32.dll")] private static extern nint DefSubclassProc(nint window, uint message, nint wParam, nint lParam);
    [DllImport("wtsapi32.dll")] private static extern bool WTSRegisterSessionNotification(nint window, uint flags);
    [DllImport("wtsapi32.dll")] private static extern bool WTSUnRegisterSessionNotification(nint window);
}

public sealed record NativeWindowGeometry(uint Dpi, int ClientWidth, int ClientHeight,
    int X, int Y, int Width, int Height);
