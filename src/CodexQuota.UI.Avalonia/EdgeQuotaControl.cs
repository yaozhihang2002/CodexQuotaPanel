using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CodexQuota.UI.Avalonia;

/// <summary>Continuous, rounded Bezier arcs inside the compact native footprint.</summary>
internal sealed class EdgeQuotaControl : Control
{
    public EdgeQuotaControl() => ClipToBounds = true;

    public DockEdge Edge { get; set; }
    public double Primary { get; set; }
    public double Secondary { get; set; } = double.NaN;
    public string PrimaryLabel { get; set; } = "";
    public string SecondaryLabel { get; set; } = "";
    public Color PrimaryColor { get; set; }
    public Color SecondaryColor { get; set; }
    public double RecoveryHighlight { get; set; }
    internal int ArcCount => string.IsNullOrEmpty(PrimaryLabel) ? 0 : double.IsFinite(Secondary) ? 2 : 1;

    private readonly record struct Curve(Point A, Point B, Point C, Point D)
    {
        public Curve Prefix(double t)
        {
            var ab = Mix(A, B, t); var bc = Mix(B, C, t); var cd = Mix(C, D, t);
            var abc = Mix(ab, bc, t); var bcd = Mix(bc, cd, t);
            return new(A, ab, abc, Mix(abc, bcd, t));
        }

        public double ParameterAtLength(double fraction)
        {
            Span<double> lengths = stackalloc double[65];
            lengths[0] = 0;
            var last = A;
            for (var i = 1; i <= 64; i++)
            {
                var point = Prefix(i / 64d).D;
                lengths[i] = lengths[i - 1] + Math.Sqrt(Math.Pow(point.X - last.X, 2) + Math.Pow(point.Y - last.Y, 2));
                last = point;
            }
            var target = fraction * lengths[64];
            for (var i = 1; i <= 64; i++)
                if (lengths[i] >= target)
                    return (i - 1 + (target - lengths[i - 1]) / (lengths[i] - lengths[i - 1])) / 64;
            return 1;
        }
        private static Point Mix(Point a, Point b, double t) => a + (b - a) * t;
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var dual = double.IsFinite(Secondary);
        var outer = dual ? new Curve(new(3, 6), new(31, 17), new(31, 59), new(3, 70))
            : new Curve(new(3, 9), new(27, 20), new(27, 56), new(3, 67));
        var inner = new Curve(new(3, 16), new(21, 25), new(21, 51), new(3, 60));
        // Subtle dark backing keeps the thin arcs readable on both light and dark desktops.
        Draw(context, outer, new SolidColorBrush(Color.Parse("#111D18"), .94), 10);
        if (dual) Draw(context, inner, new SolidColorBrush(Color.Parse("#111D18"), .94), 10);
        DrawQuota(context, outer, Primary, PrimaryColor, !string.IsNullOrEmpty(PrimaryLabel));
        if (dual) DrawQuota(context, inner, Secondary, SecondaryColor, true);
    }

    private void DrawQuota(DrawingContext context, Curve curve, double value, Color color, bool hasData)
    {
        Draw(context, curve, UiPalette.B("#3A5045"), 3);
        if (!hasData || !double.IsFinite(value) || value <= 0) return;
        var progress = curve.Prefix(curve.ParameterAtLength(Math.Clamp(value, 0, 100) / 100));
        if (RecoveryHighlight > 0) Draw(context, progress, new SolidColorBrush(color, RecoveryHighlight * .2), 8);
        Draw(context, progress, new SolidColorBrush(color), 3);
    }

    private void Draw(DrawingContext context, Curve curve, IBrush brush, double thickness)
    {
        var geometry = new StreamGeometry();
        using (var path = geometry.Open())
        {
            path.BeginFigure(Orient(curve.A), false);
            path.CubicBezierTo(Orient(curve.B), Orient(curve.C), Orient(curve.D));
            path.EndFigure(false);
        }
        context.DrawGeometry(null, new Pen(brush, thickness, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round), geometry);
    }

    private Point Orient(Point p) => Edge switch
    {
        DockEdge.Right => new(Bounds.Width * (1 - p.X / 32), Bounds.Height * p.Y / 76),
        DockEdge.Top => new(Bounds.Width * p.Y / 76, Bounds.Height * p.X / 32),
        DockEdge.Bottom => new(Bounds.Width * p.Y / 76, Bounds.Height * (1 - p.X / 32)),
        _ => new(Bounds.Width * p.X / 32, Bounds.Height * p.Y / 76)
    };
}
