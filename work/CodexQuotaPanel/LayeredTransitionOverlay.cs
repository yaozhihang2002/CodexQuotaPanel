using System.ComponentModel;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CodexQuotaPanel;

internal sealed class LayeredTransitionOverlay : Form
{
    private const int WsExLayered = 0x00080000;
    private const int WsExTransparent = 0x00000020;
    private const int WsExNoActivate = 0x08000000;
    private const int WsExToolWindow = 0x00000080;
    private const int UlwAlpha = 0x00000002;
    private const byte AcSrcOver = 0x00;
    private const byte AcSrcAlpha = 0x01;
    private const uint DibRgbColors = 0;

    private IntPtr _memoryDc;
    private IntPtr _dibSection;
    private IntPtr _previousBitmap;
    private Bitmap? _surface;

    public LayeredTransitionOverlay(Rectangle bounds, bool topMost)
    {
        AutoScaleMode = AutoScaleMode.None;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        Bounds = bounds;
        TopMost = topMost;
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var parameters = base.CreateParams;
            parameters.ExStyle |= WsExLayered | WsExTransparent | WsExNoActivate | WsExToolWindow;
            return parameters;
        }
    }

    public void Present(
        Bitmap panelPreview,
        Bitmap orbPreview,
        PointF anchor,
        double shapeProgress,
        double orbScale)
    {
        if (IsDisposed || Disposing || Width <= 0 || Height <= 0) return;
        if (!IsHandleCreated) CreateControl();
        EnsureSurface();
        using (var graphics = Graphics.FromImage(_surface!))
        {
            graphics.Clear(Color.Transparent);
            QuotaForm.DrawGeniePreview(graphics, panelPreview, anchor, shapeProgress);
            QuotaForm.DrawTransitionOrbPreview(graphics, orbPreview, anchor, orbScale);
        }
        PresentSurface();
    }

    internal (bool NativeVisible, int NonTransparentPixels, byte MaximumAlpha) InspectFrame()
    {
        if (_surface is null || !IsHandleCreated) return (false, 0, 0);
        var data = _surface.LockBits(
            new Rectangle(Point.Empty, _surface.Size),
            ImageLockMode.ReadOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var row = new byte[Math.Abs(data.Stride)];
            var nonTransparent = 0;
            byte maximumAlpha = 0;
            for (var y = 0; y < data.Height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                for (var x = 3; x < data.Width * 4; x += 4)
                {
                    var alpha = row[x];
                    if (alpha > 0) nonTransparent++;
                    if (alpha > maximumAlpha) maximumAlpha = alpha;
                }
            }
            return (IsWindowVisible(Handle), nonTransparent, maximumAlpha);
        }
        finally
        {
            _surface.UnlockBits(data);
        }
    }

    internal Bitmap? CaptureFrame() => _surface is null ? null : new Bitmap(_surface);

    private void EnsureSurface()
    {
        if (_surface is not null) return;
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) throw new Win32Exception();
        try
        {
            _memoryDc = CreateCompatibleDC(screenDc);
            if (_memoryDc == IntPtr.Zero) throw new Win32Exception();
            var info = new BitmapInfo
            {
                Header = new BitmapInfoHeader
                {
                    Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                    Width = Width,
                    Height = -Height,
                    Planes = 1,
                    BitCount = 32,
                    Compression = 0,
                    SizeImage = (uint)(Width * Height * 4)
                }
            };
            _dibSection = CreateDIBSection(screenDc, ref info, DibRgbColors, out var bits, IntPtr.Zero, 0);
            if (_dibSection == IntPtr.Zero || bits == IntPtr.Zero) throw new Win32Exception();
            _previousBitmap = SelectObject(_memoryDc, _dibSection);
            _surface = new Bitmap(Width, Height, Width * 4, PixelFormat.Format32bppPArgb, bits);
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    private void PresentSurface()
    {
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero) throw new Win32Exception();
        try
        {
            var destination = new NativePoint(Left, Top);
            var source = new NativePoint(0, 0);
            var size = new NativeSize(Width, Height);
            var blend = new BlendFunction
            {
                BlendOp = AcSrcOver,
                SourceConstantAlpha = 255,
                AlphaFormat = AcSrcAlpha
            };
            if (!UpdateLayeredWindow(Handle, screenDc, ref destination, ref size,
                    _memoryDc, ref source, 0, ref blend, UlwAlpha))
                throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        finally
        {
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _surface?.Dispose();
            _surface = null;
            if (_memoryDc != IntPtr.Zero && _previousBitmap != IntPtr.Zero)
                SelectObject(_memoryDc, _previousBitmap);
            if (_dibSection != IntPtr.Zero) DeleteObject(_dibSection);
            if (_memoryDc != IntPtr.Zero) DeleteDC(_memoryDc);
            _previousBitmap = IntPtr.Zero;
            _dibSection = IntPtr.Zero;
            _memoryDc = IntPtr.Zero;
        }
        base.Dispose(disposing);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
        public NativePoint(int x, int y) { X = x; Y = y; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSize
    {
        public int Width;
        public int Height;
        public NativeSize(int width, int height) { Width = width; Height = height; }
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public uint Size;
        public int Width;
        public int Height;
        public ushort Planes;
        public ushort BitCount;
        public uint Compression;
        public uint SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public uint ColorsUsed;
        public uint ColorsImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfo
    {
        public BitmapInfoHeader Header;
        public uint Colors;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool UpdateLayeredWindow(
        IntPtr hwnd,
        IntPtr destinationDc,
        ref NativePoint destination,
        ref NativeSize size,
        IntPtr sourceDc,
        ref NativePoint source,
        int colorKey,
        ref BlendFunction blend,
        int flags);

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hwnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr value);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern IntPtr CreateDIBSection(
        IntPtr dc,
        ref BitmapInfo bitmapInfo,
        uint usage,
        out IntPtr bits,
        IntPtr section,
        uint offset);
}
