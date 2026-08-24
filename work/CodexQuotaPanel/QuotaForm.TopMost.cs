using System.Runtime.InteropServices;

namespace CodexQuotaPanel;

internal sealed partial class QuotaForm
{
    private static readonly IntPtr HwndTopMost = new(-1);
    private static readonly IntPtr HwndNotTopMost = new(-2);
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpNoOwnerZOrder = 0x0200;
    private const uint SwpNoSendChanging = 0x0400;

    internal bool AlwaysOnTopPreference => _alwaysOnTopPreference;

    public void ReassertTopMostPreference()
    {
        if (InvokeRequired)
        {
            BeginInvoke(ReassertTopMostPreference);
            return;
        }
        if (IsDisposed || Disposing) return;

        if (TopMost != _alwaysOnTopPreference)
            TopMost = _alwaysOnTopPreference;
        _hoverPeek.TopMost = _alwaysOnTopPreference;
        _pinButton.ForeColor = _alwaysOnTopPreference ? UiPalette.Mint : UiPalette.Muted;
        _pinButton.Text = _alwaysOnTopPreference ? PinGlyph : UnpinGlyph;
        if (!IsHandleCreated) return;

        _ = SetWindowPos(
            Handle,
            _alwaysOnTopPreference ? HwndTopMost : HwndNotTopMost,
            0, 0, 0, 0,
            SwpNoMove | SwpNoSize | SwpNoActivate | SwpNoOwnerZOrder | SwpNoSendChanging);
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);
}
