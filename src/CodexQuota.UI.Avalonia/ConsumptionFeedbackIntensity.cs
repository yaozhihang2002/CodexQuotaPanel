using CodexQuota.Domain;

namespace CodexQuota.UI.Avalonia;

/// <summary>
/// Maps quota pace to five visual stages. With reset time available, the
/// mapping is relative to sustainable pace so 5-hour and 7-day windows behave
/// consistently at the same quota pressure.
/// </summary>
public static class ConsumptionFeedbackIntensity
{
    private const double FrozenMaximum = .03;
    private const double CoolMaximum = .25;
    private const double WarmMaximum = .52;
    private const double HotMaximum = .78;

    public static double From(UsageForecast? forecast)
    {
        if (forecast is null || !double.IsFinite(forecast.PercentPerHour) || forecast.PercentPerHour < .05)
            return 0d;
        if (forecast.SustainablePercentPerHour is { } sustainable &&
            double.IsFinite(sustainable) && sustainable > .001)
            return FromPressure(forecast.PercentPerHour / sustainable);
        return Math.Clamp(forecast.PercentPerHour / 8d, 0d, 1d);
    }

    public static double FromPressure(double pressure)
    {
        if (!double.IsFinite(pressure) || pressure <= .10) return 0d;
        if (pressure <= .70) return Scale(pressure, .10, .70, FrozenMaximum + .001, CoolMaximum);
        if (pressure <= 1.30) return Scale(pressure, .70, 1.30, CoolMaximum + .001, WarmMaximum);
        if (pressure <= 2.00) return Scale(pressure, 1.30, 2.00, WarmMaximum + .001, HotMaximum);
        return Scale(Math.Min(pressure, 4d), 2.00, 4.00, HotMaximum + .001, 1d);
    }

    public static double MotionStep(double intensity)
    {
        var value = Math.Clamp(intensity, 0d, 1d);
        var eased = value * value * (3d - 2d * value);
        return .045d + eased * .30d;
    }

    private static double Scale(double value, double from, double to, double outputFrom, double outputTo)
    {
        var progress = Math.Clamp((value - from) / (to - from), 0d, 1d);
        return outputFrom + (outputTo - outputFrom) * progress;
    }
}
