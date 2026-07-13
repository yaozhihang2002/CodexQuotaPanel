using System.Runtime.InteropServices;

namespace CodexQuotaPanel;

internal sealed class NativeRedrawScope : IDisposable
{
    private const int WmSetRedraw = 0x000B;
    private const uint RdwInvalidate = 0x0001;
    private const uint RdwAllChildren = 0x0080;

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
