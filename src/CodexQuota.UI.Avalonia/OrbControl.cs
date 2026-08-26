using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CodexQuota.Application;

namespace CodexQuota.UI.Avalonia;

public sealed class OrbControl : Control
{
    public static readonly StyledProperty<double> RemainingPercentProperty = AvaloniaProperty.Register<OrbControl, double>(nameof(RemainingPercent), 62d);
    public static readonly StyledProperty<double> SecondaryRemainingPercentProperty = AvaloniaProperty.Register<OrbControl, double>(nameof(SecondaryRemainingPercent), double.NaN);
    public static readonly StyledProperty<string> CaptionProperty = AvaloniaProperty.Register<OrbControl, string>(nameof(Caption), "REMAINING");
    public static readonly StyledProperty<string> PrimaryLabelProperty = AvaloniaProperty.Register<OrbControl, string>(nameof(PrimaryLabel), "7D");
    public static readonly StyledProperty<string> SecondaryLabelProperty = AvaloniaProperty.Register<OrbControl, string>(nameof(SecondaryLabel), "5H");
    public static readonly StyledProperty<Color> OrbBackgroundProperty = AvaloniaProperty.Register<OrbControl, Color>(nameof(OrbBackground), Colors.Black);
    public static readonly StyledProperty<Color> OuterRingColorProperty = AvaloniaProperty.Register<OrbControl, Color>(nameof(OuterRingColor), Color.Parse("#6AE4B0"));
    public static readonly StyledProperty<Color> InnerRingColorProperty = AvaloniaProperty.Register<OrbControl, Color>(nameof(InnerRingColor), Color.Parse("#7EC4FF"));
    public static readonly StyledProperty<double> FeedbackIntensityProperty = AvaloniaProperty.Register<OrbControl, double>(nameof(FeedbackIntensity));
    public static readonly StyledProperty<bool> FeedbackEnabledProperty = AvaloniaProperty.Register<OrbControl, bool>(nameof(FeedbackEnabled), true);
    public static readonly StyledProperty<ConsumptionFeedbackStyle> FeedbackStyleProperty = AvaloniaProperty.Register<OrbControl, ConsumptionFeedbackStyle>(nameof(FeedbackStyle), ConsumptionFeedbackStyle.Fluid);
    public static readonly StyledProperty<bool> AnimateFeedbackProperty = AvaloniaProperty.Register<OrbControl, bool>(nameof(AnimateFeedback), true);
    private readonly DispatcherTimer _animationTimer;
    private double _displayIntensity;
    private double _phase;

    public double RemainingPercent { get => GetValue(RemainingPercentProperty); set => SetValue(RemainingPercentProperty, value); }
    public double SecondaryRemainingPercent { get => GetValue(SecondaryRemainingPercentProperty); set => SetValue(SecondaryRemainingPercentProperty, value); }
    public string Caption { get => GetValue(CaptionProperty); set => SetValue(CaptionProperty, value); }
    public string PrimaryLabel { get => GetValue(PrimaryLabelProperty); set => SetValue(PrimaryLabelProperty, value); }
    public string SecondaryLabel { get => GetValue(SecondaryLabelProperty); set => SetValue(SecondaryLabelProperty, value); }
    public Color OrbBackground { get => GetValue(OrbBackgroundProperty); set => SetValue(OrbBackgroundProperty, value); }
    public Color OuterRingColor { get => GetValue(OuterRingColorProperty); set => SetValue(OuterRingColorProperty, value); }
    public Color InnerRingColor { get => GetValue(InnerRingColorProperty); set => SetValue(InnerRingColorProperty, value); }
    public double FeedbackIntensity { get => GetValue(FeedbackIntensityProperty); set => SetValue(FeedbackIntensityProperty, value); }
    public bool FeedbackEnabled { get => GetValue(FeedbackEnabledProperty); set => SetValue(FeedbackEnabledProperty, value); }
    public ConsumptionFeedbackStyle FeedbackStyle { get => GetValue(FeedbackStyleProperty); set => SetValue(FeedbackStyleProperty, value); }
    public bool AnimateFeedback { get => GetValue(AnimateFeedbackProperty); set => SetValue(AnimateFeedbackProperty, value); }

    static OrbControl() => AffectsRender<OrbControl>(RemainingPercentProperty, SecondaryRemainingPercentProperty,
        CaptionProperty, PrimaryLabelProperty, SecondaryLabelProperty, OrbBackgroundProperty, OuterRingColorProperty,
        InnerRingColorProperty, FeedbackIntensityProperty, FeedbackEnabledProperty, FeedbackStyleProperty,
        AnimateFeedbackProperty);

    public OrbControl()
    {
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _animationTimer.Tick += (_, _) =>
        {
            if (!IsEffectivelyVisible || !FeedbackEnabled || !AnimateFeedback)
            {
                _animationTimer.Stop();
                _displayIntensity = Math.Clamp(FeedbackIntensity, 0d, 1d);
                return;
            }
            var target = Math.Clamp(FeedbackIntensity, 0d, 1d);
            _displayIntensity += (target - _displayIntensity) * .18;
            _phase = (_phase + .16 + _displayIntensity * .12) % (Math.PI * 2);
            if (Math.Abs(target - _displayIntensity) < .002) _displayIntensity = target;
            InvalidateVisual();
            if (target < .08 && _displayIntensity < .08) _animationTimer.Stop();
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != FeedbackIntensityProperty && change.Property != FeedbackEnabledProperty &&
            change.Property != AnimateFeedbackProperty) return;
        var target = Math.Clamp(FeedbackIntensity, 0d, 1d);
        if (VisualRoot is null || !AnimateFeedback)
        {
            _displayIntensity = target;
            _animationTimer.Stop();
        }
        else if (FeedbackEnabled) _animationTimer.Start();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _displayIntensity = Math.Clamp(FeedbackIntensity, 0d, 1d);
        if (FeedbackEnabled && AnimateFeedback && _displayIntensity >= .08) _animationTimer.Start();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        _animationTimer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var size = Math.Min(Bounds.Width, Bounds.Height);
        if (size <= 1d) return;
        var rect = new Rect((Bounds.Width - size) / 2d + 1d, (Bounds.Height - size) / 2d + 1d,
            Math.Max(0d, size - 2d), Math.Max(0d, size - 2d));
        var center = rect.Center;
        var hasSecondary = double.IsFinite(SecondaryRemainingPercent);
        var width = Math.Max(4d, size * (hasSecondary ? 0.065d : 0.082d));
        var outer = rect.Deflate(size * 0.13d);

        context.DrawEllipse(new SolidColorBrush(OrbBackground), new Pen(B("#39443F"), 1d), rect);
        DrawRing(context, outer, RemainingPercent, width, B("#34413C"), new SolidColorBrush(OuterRingColor));
        if (hasSecondary)
            DrawRing(context, outer.Deflate(size * 0.13d), SecondaryRemainingPercent, width,
                B("#2C393E"), new SolidColorBrush(InnerRingColor));

        if (hasSecondary)
        {
            DrawCentered(context, $"{PrimaryLabel} {Math.Round(Math.Clamp(RemainingPercent, 0, 100)):0}", center.Y - size * 0.085,
                size * 0.082, new SolidColorBrush(OuterRingColor), FontWeight.SemiBold);
            DrawCentered(context, $"{SecondaryLabel} {Math.Round(Math.Clamp(SecondaryRemainingPercent, 0, 100)):0}", center.Y + size * 0.025,
                size * 0.082, new SolidColorBrush(InnerRingColor), FontWeight.SemiBold);
        }
        else
        {
            DrawCentered(context, $"{Math.Round(Math.Clamp(RemainingPercent, 0, 100)):0}%", center.Y - size * 0.04,
                size * 0.205, Brushes.White, FontWeight.Bold);
            DrawCentered(context, Caption, center.Y + size * 0.135, size * 0.055, B("#AFC0B8"), FontWeight.SemiBold);
        }
        if (FeedbackEnabled) DrawFeedback(context, center, size);
    }

    private void DrawFeedback(DrawingContext context, Point center, double size)
    {
        var intensity = Math.Clamp(_displayIntensity, 0d, 1d);
        var y = center.Y + size * 0.255;
        if (intensity < 0.08)
        {
            var ice = B("#8DDCFF");
            var r = Math.Max(2, size * 0.027);
            var pen = new Pen(ice, Math.Max(1, size * 0.01));
            context.DrawLine(pen, new Point(center.X - r, y), new Point(center.X + r, y));
            context.DrawLine(pen, new Point(center.X, y - r), new Point(center.X, y + r));
            context.DrawLine(pen, new Point(center.X - r * .7, y - r * .7), new Point(center.X + r * .7, y + r * .7));
            context.DrawLine(pen, new Point(center.X + r * .7, y - r * .7), new Point(center.X - r * .7, y + r * .7));
            return;
        }
        var motion = AnimateFeedback ? Math.Sin(_phase) : 0d;
        var h = size * (0.035 + intensity * 0.09) * (1d + motion * .035);
        var w = h * (FeedbackStyle == ConsumptionFeedbackStyle.Pixel ? .82 : .62);
        var color = intensity < .35 ? "#79CBFF" : intensity < .65 ? "#F2BE5C" : intensity < .85 ? "#FF895C" : "#FF5E55";
        if (FeedbackStyle == ConsumptionFeedbackStyle.Pixel)
        {
            var unit = Math.Max(2d, size * .018);
            var rows = 2 + (int)Math.Round(intensity * 3);
            var flicker = AnimateFeedback && Math.Sin(_phase * 1.7) > .35 ? 1 : 0;
            for (var row = 0; row < rows; row++)
            {
                var cells = Math.Max(1, rows - row - (row == rows - 1 ? flicker : 0));
                for (var cell = 0; cell < cells; cell++)
                    context.DrawRectangle(B(color), null, new Rect(center.X + (cell - (cells - 1) / 2d) * unit,
                        y - row * unit, unit, unit));
            }
            return;
        }
        if (FeedbackStyle == ConsumptionFeedbackStyle.Ember)
        {
            var glow = Color.Parse(color);
            var alpha = (byte)Math.Clamp(35 + intensity * 65, 0, 120);
            context.DrawEllipse(new SolidColorBrush(Color.FromArgb(alpha, glow.R, glow.G, glow.B)), null,
                new Rect(center.X - w * 1.1, y - h * .35, w * 2.2, h * .7));
            context.DrawEllipse(new SolidColorBrush(glow), null,
                new Rect(center.X - w * .42, y - h * .28, w * .84, h * .56));
            if (intensity > .72)
                context.DrawEllipse(B("#FFE6A0"), null,
                    new Rect(center.X - w * .14, y - h * .16, w * .28, h * .28));
            return;
        }
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            var tipX = center.X + motion * w * .22;
            stream.BeginFigure(new Point(tipX, y - h), true);
            stream.CubicBezierTo(new Point(center.X + w * .95, y - h * .45), new Point(center.X + w, y + h * .2), new Point(center.X, y + h * .35));
            stream.CubicBezierTo(new Point(center.X - w, y + h * .2), new Point(center.X - w * .75, y - h * .35), new Point(tipX, y - h));
        }
        context.DrawGeometry(B(color), null, geometry);
        if (FeedbackStyle == ConsumptionFeedbackStyle.Fluid && intensity > .45)
            context.DrawEllipse(B("#FFE89A"), null, new Rect(center.X - w * .23, y - h * .16, w * .46, h * .38));
    }

    private void DrawCentered(DrawingContext context, string value, double y, double size, IBrush brush, FontWeight weight)
    {
        var text = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(UiElements.AppFont, FontStyle.Normal, weight), Math.Max(7, size), brush);
        context.DrawText(text, new Point(Bounds.Center.X - text.Width / 2, y - text.Height / 2));
    }

    private static void DrawRing(DrawingContext context, Rect bounds, double remaining, double width, IBrush track, IBrush progress)
    {
        context.DrawEllipse(null, new Pen(track, width), bounds);
        DrawArc(context, bounds, Math.Clamp(remaining, 0d, 100d) / 100d, new Pen(progress, width, lineCap: PenLineCap.Round));
    }

    private static void DrawArc(DrawingContext context, Rect bounds, double progress, Pen pen)
    {
        if (progress <= 0) return;
        if (progress >= .9999) { context.DrawEllipse(null, pen, bounds); return; }
        const double start = 135, sweep = 270;
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(PointOnEllipse(bounds, start), false);
            stream.ArcTo(PointOnEllipse(bounds, start + sweep * progress), new Size(bounds.Width / 2, bounds.Height / 2),
                0, progress * sweep > 180, SweepDirection.Clockwise);
        }
        context.DrawGeometry(null, pen, geometry);
    }

    private static Point PointOnEllipse(Rect bounds, double degrees)
    {
        var radians = degrees * Math.PI / 180d;
        return new Point(bounds.Center.X + Math.Cos(radians) * bounds.Width / 2d,
            bounds.Center.Y + Math.Sin(radians) * bounds.Height / 2d);
    }

    private static IBrush B(string value) => UiPalette.B(value);
}
