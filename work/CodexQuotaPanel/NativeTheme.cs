using System.Runtime.InteropServices;

namespace CodexQuotaPanel;

/// <summary>
/// Keeps native captions, drop-downs, steppers and scroll bars aligned with the
/// app palette. All calls are best-effort so older Windows builds keep working.
/// </summary>
internal static class NativeTheme
{
    private const int DwmUseImmersiveDarkMode = 20;
    private const int WmThemeChanged = 0x031A;

    internal static void Apply(Form form)
    {
        if (form.IsDisposed || !form.IsHandleCreated) return;
        var dark = UiPalette.Canvas.GetBrightness() < 0.45f;
        ApplyCaption(form, dark);
        ApplyToTree(form, dark);
    }

    internal static void ApplyCaption(Form form)
    {
        if (form.IsDisposed || !form.IsHandleCreated) return;
        ApplyCaption(form, UiPalette.Canvas.GetBrightness() < 0.45f);
    }

    private static void ApplyCaption(Form form, bool dark)
    {
        try
        {
            var enabled = dark ? 1 : 0;
            _ = DwmSetWindowAttribute(form.Handle, DwmUseImmersiveDarkMode,
                ref enabled, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    internal static void Apply(Control root)
    {
        if (root.IsDisposed || !root.IsHandleCreated) return;
        var dark = UiPalette.Canvas.GetBrightness() < 0.45f;
        ApplyControl(root, dark);
        ApplyToTree(root, dark);
    }

    private static void ApplyToTree(Control root, bool dark)
    {
        foreach (Control child in root.Controls)
        {
            ApplyControl(child, dark);
            ApplyToTree(child, dark);
        }
    }

    private static void ApplyControl(Control control, bool dark)
    {
        if (!control.IsHandleCreated ||
            (control is not ComboBox and not NumericUpDown and not TrackBar and not ScrollBar &&
             control is not ScrollableControl { AutoScroll: true })) return;
        try
        {
            // Set explicit application colours before asking Windows to refresh
            // the native parts. "Explorer" can otherwise inherit the OS dark
            // theme even when this window is intentionally using light mode.
            switch (control)
            {
                case ComboBox:
                    control.BackColor = UiPalette.SurfaceRaised;
                    control.ForeColor = UiPalette.Text;
                    break;
                case NumericUpDown:
                    control.BackColor = UiPalette.SurfaceRaised;
                    control.ForeColor = UiPalette.Text;
                    break;
                case TrackBar:
                    control.BackColor = UiPalette.Surface;
                    control.ForeColor = UiPalette.Text;
                    break;
            }

            var theme = dark
                ? control is ComboBox or NumericUpDown ? "DarkMode_CFD" : "DarkMode_Explorer"
                : "Explorer";
            _ = SetWindowTheme(control.Handle, theme, null);
            _ = SendMessage(control.Handle, WmThemeChanged, IntPtr.Zero, IntPtr.Zero);
            control.Invalidate();
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(
        IntPtr hwnd,
        int attribute,
        ref int value,
        int valueSize);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode)]
    private static extern int SetWindowTheme(IntPtr hwnd, string? subAppName, string? subIdList);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam);
}

internal sealed class ThemedComboBox : ComboBox
{
    public ThemedComboBox()
    {
        DrawMode = DrawMode.OwnerDrawFixed;
        DropDownStyle = ComboBoxStyle.DropDownList;
        FlatStyle = FlatStyle.Flat;
        IntegralHeight = false;
        ItemHeight = 25;
        DropDownHeight = 200;
        BackColor = UiPalette.SurfaceRaised;
        ForeColor = UiPalette.Text;
    }

    protected override void OnDrawItem(DrawItemEventArgs e)
    {
        if (e.Index < 0)
        {
            base.OnDrawItem(e);
            return;
        }

        var selected = (e.State & DrawItemState.Selected) != 0;
        using var background = new SolidBrush(selected
            ? UiPalette.Mix(UiPalette.SurfaceRaised, UiPalette.Mint, 0.16f)
            : UiPalette.SurfaceRaised);
        e.Graphics.FillRectangle(background, e.Bounds);
        var text = GetItemText(Items[e.Index]);
        TextRenderer.DrawText(e.Graphics, text, Font,
            Rectangle.Inflate(e.Bounds, -8, 0),
            selected ? UiPalette.Text : ForeColor,
            TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        if ((e.State & DrawItemState.Focus) != 0) e.DrawFocusRectangle();
    }
}

internal sealed class BufferedTableLayoutPanel : TableLayoutPanel
{
    public BufferedTableLayoutPanel()
    {
        SetStyle(ControlStyles.UserPaint |
                 ControlStyles.AllPaintingInWmPaint |
                 ControlStyles.OptimizedDoubleBuffer, true);
        UpdateStyles();
    }
}
