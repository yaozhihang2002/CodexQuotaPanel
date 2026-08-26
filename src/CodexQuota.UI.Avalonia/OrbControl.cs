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
    public static readonly StyledProperty<QuotaConnectionState> ConnectionStateProperty = AvaloniaProperty.Register<OrbControl, QuotaConnectionState>(nameof(ConnectionState), QuotaConnectionState.Connecting);
    public static readonly StyledProperty<bool> MoveModeProperty = AvaloniaProperty.Register<OrbControl, bool>(nameof(MoveMode));
    public static readonly StyledProperty<bool> InteractionPausedProperty = AvaloniaProperty.Register<OrbControl, bool>(nameof(InteractionPaused));
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
    public QuotaConnectionState ConnectionState { get => GetValue(ConnectionStateProperty); set => SetValue(ConnectionStateProperty, value); }
    public bool MoveMode { get => GetValue(MoveModeProperty); set => SetValue(MoveModeProperty, value); }
    public bool InteractionPaused { get => GetValue(InteractionPausedProperty); set => SetValue(InteractionPausedProperty, value); }

    static OrbControl() => AffectsRender<OrbControl>(RemainingPercentProperty, SecondaryRemainingPercentProperty,
        CaptionProperty, PrimaryLabelProperty, SecondaryLabelProperty, OrbBackgroundProperty, OuterRingColorProperty,
        InnerRingColorProperty, FeedbackIntensityProperty, FeedbackEnabledProperty, FeedbackStyleProperty,
        AnimateFeedbackProperty, ConnectionStateProperty, MoveModeProperty, InteractionPausedProperty);

    public OrbControl()
    {
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(33) };
        _animationTimer.Tick += (_, _) =>
        {
            if (!IsEffectivelyVisible || !FeedbackEnabled || !AnimateFeedback || InteractionPaused)
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
            if (target < .08 && _displayIntensity < .08 && ConnectionState != QuotaConnectionState.Connecting)
                _animationTimer.Stop();
        };
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property != FeedbackIntensityProperty && change.Property != FeedbackEnabledProperty &&
            change.Property != AnimateFeedbackProperty && change.Property != ConnectionStateProperty &&
            change.Property != InteractionPausedProperty) return;
        var target = Math.Clamp(FeedbackIntensity, 0d, 1d);
        if (VisualRoot is null || !AnimateFeedback || InteractionPaused)
        {
            _displayIntensity = target;
            _animationTimer.Stop();
        }
        else if (FeedbackEnabled || ConnectionState == QuotaConnectionState.Connecting) _animationTimer.Start();
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _displayIntensity = Math.Clamp(FeedbackIntensity, 0d, 1d);
        if (!InteractionPaused && AnimateFeedback &&
            (FeedbackEnabled && _displayIntensity >= .08 || ConnectionState == QuotaConnectionState.Connecting))
            _animationTimer.Start();
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
        DrawConnectionEndpoint(context, outer, RemainingPercent, size);

        if (hasSecondary)
        {
            DrawCentered(context, $"{PrimaryLabel} {Math.Round(Math.Clamp(RemainingPercent, 0, 100)):0}", center.Y - size * 0.085,
                size * 0.082, new SolidColorBrush(OuterRingColor), FontWeight.SemiBold);
            DrawCentered(context, $"{SecondaryLabel} {Math.Round(Math.Clamp(SecondaryRemainingPercent, 0, 100)):0}", center.Y + size * 0.025,
                size * 0.082, new SolidColorBrush(InnerRingColor), FontWeight.SemiBold);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(PrimaryLabel))
            {
                DrawCentered(context, "—", center.Y - size * .015, size * .19, B("#AFC0B8"), FontWeight.Bold);
                DrawCentered(context, Caption, center.Y + size * .14, size * .055, B("#AFC0B8"), FontWeight.SemiBold);
            }
            else
            {
                DrawCentered(context, PrimaryLabel, center.Y - size * .145, size * .061,
                    new SolidColorBrush(OuterRingColor), FontWeight.Bold);
                DrawCentered(context, $"{Math.Round(Math.Clamp(RemainingPercent, 0, 100)):0}%", center.Y - size * .01,
                    size * .19, Brushes.White, FontWeight.Bold);
                DrawCentered(context, Caption, center.Y + size * .145, size * .052, B("#AFC0B8"), FontWeight.SemiBold);
            }
        }
        if (FeedbackEnabled) DrawFeedback(context, center, size);
        if (MoveMode) DrawMoveMode(context, rect, center, size);
    }

    private void DrawFeedback(DrawingContext context, Point center, double size)
    {
        var intensity = Math.Clamp(_displayIntensity, 0d, 1d);
        var level = intensity switch { < .08 => 0, < .28 => 1, < .55 => 2, < .78 => 3, _ => 4 };
        var y = center.Y + size * .285;
        if (level == 0) { DrawIceCrystal(context, center.X, y, size); return; }
        switch (FeedbackStyle)
        {
            case ConsumptionFeedbackStyle.Ember:
                DrawEmber(context, center.X, y, size, intensity, level);
                break;
            case ConsumptionFeedbackStyle.Pixel:
                DrawPixelFeedback(context, center.X, y, size, intensity, level);
                break;
            default:
                DrawFluidFeedback(context, center.X, y, size, intensity, level);
                break;
        }
    }

    private void DrawConnectionEndpoint(DrawingContext context, Rect outer, double remaining, double size)
    {
        var progress = Math.Clamp(remaining, 0d, 100d) / 100d;
        var point = PointOnEllipse(outer, 135 + 270 * progress);
        var pulse = ConnectionState == QuotaConnectionState.Connecting && AnimateFeedback
            ? .5 + .5 * Math.Sin(_phase * 1.35) : 0d;
        var color = ConnectionState switch
        {
            QuotaConnectionState.Live => "#57D9AA",
            QuotaConnectionState.LocalFallback => "#72BFF2",
            QuotaConnectionState.Stale => "#E9B94F",
            QuotaConnectionState.Offline => "#7E8B85",
            _ => "#E9B94F"
        };
        var radius = Math.Max(2.5, size * (.027 + pulse * .006));
        context.DrawEllipse(B("#D9000000"), null, new Rect(point.X - radius - 1.4, point.Y - radius - 1.4,
            radius * 2 + 2.8, radius * 2 + 2.8));
        context.DrawEllipse(B(color), new Pen(B("#B8000000"), Math.Max(1, size * .007)),
            new Rect(point.X - radius, point.Y - radius, radius * 2, radius * 2));
    }

    private static void DrawIceCrystal(DrawingContext context, double x, double y, double size)
    {
        var radius = Math.Max(3.2, size * .036);
        var fill = B("#B9EDFF");
        var edge = new Pen(B("#66C9F3"), Math.Max(1, size * .008), lineCap: PenLineCap.Round);
        var crystal = new StreamGeometry();
        using (var stream = crystal.Open())
        {
            stream.BeginFigure(new Point(x, y - radius), true);
            stream.LineTo(new Point(x + radius * .72, y - radius * .24));
            stream.LineTo(new Point(x + radius * .62, y + radius * .68));
            stream.LineTo(new Point(x, y + radius));
            stream.LineTo(new Point(x - radius * .62, y + radius * .68));
            stream.LineTo(new Point(x - radius * .72, y - radius * .24));
            stream.EndFigure(true);
        }
        context.DrawGeometry(fill, edge, crystal);
        context.DrawLine(edge, new Point(x, y - radius * .72), new Point(x, y + radius * .68));
        context.DrawLine(edge, new Point(x - radius * .5, y - radius * .12), new Point(x + radius * .45, y + radius * .35));
    }

    private void DrawFluidFeedback(DrawingContext context, double x, double y, double size, double intensity, int level)
    {
        var motion = AnimateFeedback ? Math.Sin(_phase) : 0d;
        var h = size * (.075 + intensity * .075) * (1 + motion * .025);
        var w = h * (.48 + level * .018);
        var palette = level switch
        {
            1 => (Outer: "#69CFFF", Inner: "#C4F2FF", Glow: "#3569CFFF"),
            2 => (Outer: "#F0B34E", Inner: "#FFE3A0", Glow: "#35F0B34E"),
            3 => (Outer: "#FF7A4D", Inner: "#FFD17A", Glow: "#45FF7A4D"),
            _ => (Outer: "#FF4E45", Inner: "#FFE071", Glow: "#58FF4E45")
        };
        context.DrawEllipse(B(palette.Glow), null, new Rect(x - w * 1.25, y - h * .42, w * 2.5, h * .85));
        var outer = FlameGeometry(x, y, w, h, motion, level >= 4);
        context.DrawGeometry(B(palette.Outer), null, outer);
        var inner = FlameGeometry(x, y + h * .06, w * .48, h * .55, -motion * .55, false);
        context.DrawGeometry(B(palette.Inner), null, inner);
        if (level >= 4)
        {
            var side = FlameGeometry(x + w * .62, y + h * .16, w * .34, h * .55, -motion, false);
            context.DrawGeometry(B("#FF8454"), null, side);
        }
    }

    private void DrawEmber(DrawingContext context, double x, double y, double size, double intensity, int level)
    {
        var motion = AnimateFeedback ? Math.Sin(_phase * .82) : 0d;
        var radius = size * (.034 + intensity * .02);
        var color = level switch { 1 => "#69CFFF", 2 => "#E9A946", 3 => "#FF754B", _ => "#FF4D43" };
        var parsed = Color.Parse(color);
        context.DrawEllipse(new SolidColorBrush(Color.FromArgb((byte)(55 + level * 16), parsed.R, parsed.G, parsed.B)), null,
            new Rect(x - radius * 2.15, y - radius * .72, radius * 4.3, radius * 1.45));
        context.DrawEllipse(B(color), new Pen(B("#8A000000"), 1),
            new Rect(x - radius, y - radius * .58, radius * 2, radius * 1.16));
        context.DrawEllipse(B(level >= 3 ? "#FFE4A0" : "#DDF6FF"), null,
            new Rect(x - radius * .34, y - radius * .3, radius * .68, radius * .52));
        var wisps = Math.Max(1, level - 1);
        for (var i = 0; i < wisps; i++)
        {
            var offset = (i - (wisps - 1) / 2d) * radius * .65;
            var pen = new Pen(new SolidColorBrush(Color.FromArgb((byte)(75 + level * 16), parsed.R, parsed.G, parsed.B)),
                Math.Max(1, size * .009), lineCap: PenLineCap.Round);
            context.DrawLine(pen, new Point(x + offset, y - radius * .72),
                new Point(x + offset + motion * radius * .35, y - radius * (1.25 + i * .28)));
        }
    }

    private void DrawPixelFeedback(DrawingContext context, double x, double y, double size, double intensity, int level)
    {
        var unit = Math.Max(2.4, Math.Round(size * .022, 1));
        var color = level switch { 1 => "#72D5FF", 2 => "#F1B64F", 3 => "#FF7B4C", _ => "#FF5047" };
        var core = level >= 2 ? "#FFE09A" : "#C8F3FF";
        var rows = level + 2;
        for (var row = 0; row < rows; row++)
        {
            var half = Math.Max(0, Math.Min(level, rows - row - 1));
            var shift = AnimateFeedback && row == rows - 1 && Math.Sin(_phase * 1.45) > .55 ? unit : 0;
            for (var column = -half; column <= half; column++)
            {
                if (row == rows - 1 && Math.Abs(column) == half && level < 4) continue;
                var brush = row < 2 && Math.Abs(column) == 0 ? B(core) : B(color);
                context.DrawRectangle(brush, null, new Rect(x + column * unit - unit / 2 + shift,
                    y - (row + .5) * unit, unit, unit));
            }
        }
        if (level >= 4)
        {
            context.DrawRectangle(B("#FF8A55"), null, new Rect(x - unit * 2.5, y - unit * 2.5, unit, unit));
            context.DrawRectangle(B("#FF8A55"), null, new Rect(x + unit * 1.5, y - unit * 3.5, unit, unit));
        }
    }

    private static StreamGeometry FlameGeometry(double x, double y, double width, double height, double motion, bool broad)
    {
        var geometry = new StreamGeometry();
        using var stream = geometry.Open();
        var tipX = x + motion * width * .22;
        stream.BeginFigure(new Point(tipX, y - height), true);
        stream.CubicBezierTo(new Point(x + width * (broad ? 1.2 : .92), y - height * .55),
            new Point(x + width, y + height * .08), new Point(x, y + height * .24));
        stream.CubicBezierTo(new Point(x - width, y + height * .08),
            new Point(x - width * (broad ? 1.02 : .78), y - height * .42), new Point(tipX, y - height));
        stream.EndFigure(true);
        return geometry;
    }

    private static void DrawMoveMode(DrawingContext context, Rect rect, Point center, double size)
    {
        context.DrawEllipse(null, new Pen(B("#E6F5C86A"), Math.Max(1.5, size * .015),
            new DashStyle([4, 3], 0)), rect.Deflate(size * .035));
        DrawCenteredStatic(context, "MOVE", center.X, center.Y - size * .31, size * .06, B("#F5C86A"), FontWeight.Bold);
    }

    private void DrawCentered(DrawingContext context, string value, double y, double size, IBrush brush, FontWeight weight)
    {
        var text = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(UiElements.AppFont, FontStyle.Normal, weight), Math.Max(7, size), brush);
        context.DrawText(text, new Point(Bounds.Center.X - text.Width / 2, y - text.Height / 2));
    }

    private static void DrawCenteredStatic(DrawingContext context, string value, double x, double y, double size, IBrush brush, FontWeight weight)
    {
        var text = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(UiElements.AppFont, FontStyle.Normal, weight), Math.Max(7, size), brush);
        context.DrawText(text, new Point(x - text.Width / 2, y - text.Height / 2));
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
