using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CodexQuota.UI.Avalonia;

public sealed class OrbControl : Control
{
    public static readonly StyledProperty<double> RemainingPercentProperty =
        AvaloniaProperty.Register<OrbControl, double>(nameof(RemainingPercent), 62d);

    public double RemainingPercent
    {
        get => GetValue(RemainingPercentProperty);
        set => SetValue(RemainingPercentProperty, value);
    }

    static OrbControl()
    {
        AffectsRender<OrbControl>(RemainingPercentProperty);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0d)
            return;

        var rect = new Rect(
            (Bounds.Width - size) / 2d + 1d,
            (Bounds.Height - size) / 2d + 1d,
            Math.Max(0d, size - 2d),
            Math.Max(0d, size - 2d));
        var center = rect.Center;
        var ringRect = rect.Deflate(size * 0.12d);
        var ringWidth = Math.Max(4d, size * 0.085d);

        context.DrawEllipse(
            new SolidColorBrush(Color.Parse("#161B19")),
            new Pen(new SolidColorBrush(Color.Parse("#36403B")), 1d),
            rect);
        context.DrawEllipse(
            null,
            new Pen(new SolidColorBrush(Color.Parse("#33413B")), ringWidth),
            ringRect);
        DrawArc(
            context,
            ringRect,
            Math.Clamp(RemainingPercent, 0d, 100d) / 100d,
            new Pen(new SolidColorBrush(Color.Parse("#57D9AA")), ringWidth, lineCap: PenLineCap.Round));

        var percentage = CreateText(
            $"{Math.Round(Math.Clamp(RemainingPercent, 0d, 100d)):0}%",
            Math.Max(15d, size * 0.21d),
            FontWeight.Bold,
            Brushes.White);
        context.DrawText(
            percentage,
            new Point(center.X - percentage.Width / 2d, center.Y - percentage.Height * 0.62d));

        var caption = CreateText(
            "REMAINING",
            Math.Max(7d, size * 0.055d),
            FontWeight.SemiBold,
            new SolidColorBrush(Color.Parse("#AFC0B8")));
        context.DrawText(
            caption,
            new Point(center.X - caption.Width / 2d, center.Y + percentage.Height * 0.34d));
    }

    private static FormattedText CreateText(string text, double size, FontWeight weight, IBrush brush) =>
        new(
            text,
            CultureInfo.InvariantCulture,
            FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable, Segoe UI, SF Pro Display", FontStyle.Normal, weight),
            size,
            brush);

    private static void DrawArc(DrawingContext context, Rect bounds, double progress, Pen pen)
    {
        if (progress <= 0d)
            return;

        if (progress >= 0.9999d)
        {
            context.DrawEllipse(null, pen, bounds);
            return;
        }

        const double startDegrees = 135d;
        const double sweepDegrees = 270d;
        var start = PointOnEllipse(bounds, startDegrees);
        var end = PointOnEllipse(bounds, startDegrees + sweepDegrees * progress);
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(start, false);
            stream.ArcTo(
                end,
                new Size(bounds.Width / 2d, bounds.Height / 2d),
                0d,
                progress * sweepDegrees > 180d,
                SweepDirection.Clockwise);
        }

        context.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnEllipse(Rect bounds, double degrees)
    {
        var radians = degrees * Math.PI / 180d;
        return new Point(
            bounds.Center.X + Math.Cos(radians) * bounds.Width / 2d,
            bounds.Center.Y + Math.Sin(radians) * bounds.Height / 2d);
    }
}
