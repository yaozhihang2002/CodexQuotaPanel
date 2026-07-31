using System.Runtime.InteropServices;

namespace CodexQuotaPanel;

internal sealed class NativeRedrawScope : IDisposable
{
    private const int WmSetRedraw = 0x000B;
    private const uint RdwInvalidate = 0x0001;
    private const uint RdwErase = 0x0004;
    private const uint RdwAllChildren = 0x0080;
    private const uint RdwUpdateNow = 0x0100;

    private readonly Control? _control;
    private readonly IntPtr _handle;
    private bool _disposed;

    private NativeRedrawScope(Control control)
    {
        if (!control.IsHandleCreated || control.IsDisposed) return;
        _control = control;
        _handle = control.Handle;
        SendMessage(_handle, WmSetRedraw, IntPtr.Zero, IntPtr.Zero);
    }

    public static NativeRedrawScope Suspend(Control control) => new(control);

    public static void RedrawNow(Control control)
    {
        if (control.IsDisposed || !control.IsHandleCreated) return;
        RedrawWindow(control.Handle, IntPtr.Zero, IntPtr.Zero,
            RdwInvalidate | RdwErase | RdwAllChildren | RdwUpdateNow);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_control is null || _control.IsDisposed || !_control.IsHandleCreated) return;
        SendMessage(_handle, WmSetRedraw, new IntPtr(1), IntPtr.Zero);
        RedrawWindow(_handle, IntPtr.Zero, IntPtr.Zero,
            RdwInvalidate | RdwAllChildren);
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool RedrawWindow(IntPtr hwnd, IntPtr updateRect, IntPtr updateRegion, uint flags);
}

internal static class NativeZOrder
{
    private static readonly IntPtr HwndTop = IntPtr.Zero;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoRedraw = 0x0008;
    private const uint SwpNoOwnerZOrder = 0x0200;

    internal static void BringToFront(Control control)
    {
        if (control.IsDisposed || !control.IsHandleCreated) return;
        _ = SetWindowPos(
            control.Handle,
            HwndTop,
            0,
            0,
            0,
            0,
            SwpNoSize | SwpNoMove | SwpNoActivate | SwpNoOwnerZOrder | SwpNoRedraw);
        // NOREDRAW keeps the click handler non-blocking. Queue repainting only
        // for the selected page; invalidating the host would repaint every
        // cached page stacked behind it as well.
        control.Invalidate(invalidateChildren: true);
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int cx,
        int cy,
        uint flags);
}
