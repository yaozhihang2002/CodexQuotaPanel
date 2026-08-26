using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CodexQuota.Application;

namespace CodexQuota.UI.Avalonia;

public sealed partial class OrbControl : Control
{
    private const double RingStartDegrees = 145d;
    private const double RingSweepDegrees = 250d;
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
            DrawCentered(context, $"{PrimaryLabel} {Math.Round(Math.Clamp(RemainingPercent, 0, 100)):0}",
                center.Y - size * .095, size * .074, new SolidColorBrush(OuterRingColor), FontWeight.SemiBold, 6.2);
            DrawCentered(context, $"{SecondaryLabel} {Math.Round(Math.Clamp(SecondaryRemainingPercent, 0, 100)):0}",
                center.Y + size * .055, size * .074, new SolidColorBrush(InnerRingColor), FontWeight.SemiBold, 6.2);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(PrimaryLabel))
            {
                DrawCentered(context, "—", center.Y - size * .015, size * .19, B("#AFC0B8"), FontWeight.Bold);
            }
            else
            {
                DrawCentered(context, PrimaryLabel, center.Y - size * .145, size * .071,
                    new SolidColorBrush(OuterRingColor), FontWeight.Bold, 8.2);
                DrawCentered(context, $"{Math.Round(Math.Clamp(RemainingPercent, 0, 100)):0}%", center.Y - size * .01,
                    size * .19, Brushes.White, FontWeight.Bold);
            }
        }
        if (FeedbackEnabled) DrawFeedback(context, center, size);
        if (MoveMode) DrawMoveMode(context, rect, center, size);
    }

    private void DrawFeedback(DrawingContext context, Point center, double size)
    {
        var intensity = Math.Clamp(_displayIntensity, 0d, 1d);
        switch (FeedbackStyle)
        {
            case ConsumptionFeedbackStyle.Ember:
                DrawWinFormsEmber(context, center, size, intensity);
                break;
            case ConsumptionFeedbackStyle.Pixel:
                DrawWinFormsPixelFlame(context, center, size, intensity);
                break;
            default:
                DrawWinFormsFluidFlame(context, center, size, intensity);
                break;
        }
    }

    private void DrawConnectionEndpoint(DrawingContext context, Rect outer, double remaining, double size)
    {
        var progress = Math.Clamp(remaining, 0d, 100d) / 100d;
        var point = PointOnEllipse(outer, RingStartDegrees + RingSweepDegrees * progress);
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

    private static void DrawMoveMode(DrawingContext context, Rect rect, Point center, double size)
    {
        context.DrawEllipse(null, new Pen(B("#E6F5C86A"), Math.Max(1.5, size * .015),
            new DashStyle([4, 3], 0)), rect.Deflate(size * .035));
        DrawCenteredStatic(context, "MOVE", center.X, center.Y - size * .31, size * .06, B("#F5C86A"), FontWeight.Bold);
    }

    private void DrawCentered(DrawingContext context, string value, double y, double size, IBrush brush,
        FontWeight weight, double minimumSize = 7)
    {
        var text = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(UiElements.AppFont, FontStyle.Normal, weight), Math.Max(minimumSize, size), brush);
        context.DrawText(text, new Point(Bounds.Width / 2d - text.Width / 2d, y - text.Height / 2d));
    }

    private static void DrawCenteredStatic(DrawingContext context, string value, double x, double y, double size, IBrush brush, FontWeight weight)
    {
        var text = new FormattedText(value, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            new Typeface(UiElements.AppFont, FontStyle.Normal, weight), Math.Max(7, size), brush);
        context.DrawText(text, new Point(x - text.Width / 2, y - text.Height / 2));
    }

    private static void DrawRing(DrawingContext context, Rect bounds, double remaining, double width, IBrush track, IBrush progress)
    {
        DrawArc(context, bounds, 1d, new Pen(track, width, lineCap: PenLineCap.Round));
        DrawArc(context, bounds, Math.Clamp(remaining, 0d, 100d) / 100d,
            new Pen(progress, width, lineCap: PenLineCap.Round));
    }

    private static void DrawArc(DrawingContext context, Rect bounds, double progress, Pen pen)
    {
        if (progress <= 0) return;
        var geometry = new StreamGeometry();
        using (var stream = geometry.Open())
        {
            stream.BeginFigure(PointOnEllipse(bounds, RingStartDegrees), false);
            stream.ArcTo(PointOnEllipse(bounds, RingStartDegrees + RingSweepDegrees * progress),
                new Size(bounds.Width / 2, bounds.Height / 2), 0,
                progress * RingSweepDegrees > 180, SweepDirection.Clockwise);
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
