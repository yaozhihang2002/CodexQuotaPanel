using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace CodexQuota.UI.Avalonia;

public sealed class OrbControl : Control
{
    public static readonly StyledProperty<double> RemainingPercentProperty =
        AvaloniaProperty.Register<OrbControl, double>(nameof(RemainingPercent), 62d);
    public static readonly StyledProperty<double> SecondaryRemainingPercentProperty =
        AvaloniaProperty.Register<OrbControl, double>(nameof(SecondaryRemainingPercent), double.NaN);
    public static readonly StyledProperty<string> CaptionProperty =
        AvaloniaProperty.Register<OrbControl, string>(nameof(Caption), "REMAINING");

    public double RemainingPercent
    {
        get => GetValue(RemainingPercentProperty);
        set => SetValue(RemainingPercentProperty, value);
    }

    public double SecondaryRemainingPercent
    {
        get => GetValue(SecondaryRemainingPercentProperty);
        set => SetValue(SecondaryRemainingPercentProperty, value);
    }

    public string Caption
    {
        get => GetValue(CaptionProperty);
        set => SetValue(CaptionProperty, value);
    }

    static OrbControl() =>
        AffectsRender<OrbControl>(RemainingPercentProperty, SecondaryRemainingPercentProperty, CaptionProperty);

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 0d) return;

        var rect = new Rect(
            (Bounds.Width - size) / 2d + 1d,
            (Bounds.Height - size) / 2d + 1d,
            Math.Max(0d, size - 2d),
            Math.Max(0d, size - 2d));
        var center = rect.Center;
        var hasSecondary = double.IsFinite(SecondaryRemainingPercent);
        var outerWidth = Math.Max(4d, size * (hasSecondary ? 0.068d : 0.085d));
        var outerRect = rect.Deflate(size * 0.12d);

        context.DrawEllipse(Brush("#121715"), new Pen(Brush("#2D3833"), 1d), rect);
        DrawRing(context, outerRect, RemainingPercent, outerWidth, "#34413C", "#57D9AA");

        if (hasSecondary)
        {
            var innerRect = outerRect.Deflate(size * 0.13d);
            DrawRing(context, innerRect, SecondaryRemainingPercent, outerWidth, "#2C393E", "#70BDF2");
        }

        var displayed = hasSecondary
            ? Math.Min(RemainingPercent, SecondaryRemainingPercent)
            : RemainingPercent;
        var percentage = CreateText(
            $"{Math.Round(Math.Clamp(displayed, 0d, 100d)):0}%",
            Math.Max(15d, size * (hasSecondary ? 0.18d : 0.21d)),
            FontWeight.Bold,
            Brushes.White);
        context.DrawText(percentage,
            new Point(center.X - percentage.Width / 2d, center.Y - percentage.Height * 0.62d));

        var caption = CreateText(Caption, Math.Max(7d, size * 0.055d), FontWeight.SemiBold, Brush("#AFC0B8"));
        context.DrawText(caption,
            new Point(center.X - caption.Width / 2d, center.Y + percentage.Height * 0.34d));
    }

    private static void DrawRing(
        DrawingContext context,
        Rect bounds,
        double remaining,
        double width,
        string track,
        string progress)
    {
        context.DrawEllipse(null, new Pen(Brush(track), width), bounds);
        DrawArc(context, bounds, Math.Clamp(remaining, 0d, 100d) / 100d,
            new Pen(Brush(progress), width, lineCap: PenLineCap.Round));
    }

    private static FormattedText CreateText(string text, double size, FontWeight weight, IBrush brush) =>
        new(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface("Segoe UI Variable, Segoe UI, SF Pro Display", FontStyle.Normal, weight), size, brush);

    private static void DrawArc(DrawingContext context, Rect bounds, double progress, Pen pen)
    {
        if (progress <= 0d) return;
        if (progress >= 0.9999d)
        {
            context.DrawEllipse(null, pen, bounds);
            return;
        }

        const double startDegrees = 135d;
        const double sweepDegrees = 270d;
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(PointOnEllipse(bounds, startDegrees), false);
            stream.ArcTo(PointOnEllipse(bounds, startDegrees + sweepDegrees * progress),
                new Size(bounds.Width / 2d, bounds.Height / 2d), 0d,
                progress * sweepDegrees > 180d, SweepDirection.Clockwise);
        }
        context.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnEllipse(Rect bounds, double degrees)
    {
        var radians = degrees * Math.PI / 180d;
        return new Point(bounds.Center.X + Math.Cos(radians) * bounds.Width / 2d,
            bounds.Center.Y + Math.Sin(radians) * bounds.Height / 2d);
    }

    private static IBrush Brush(string value) => new SolidColorBrush(Color.Parse(value));
}
