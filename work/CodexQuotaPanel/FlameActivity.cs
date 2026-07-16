namespace CodexQuotaPanel;

/// <summary>
/// A presentation-only view of recent quota consumption.  The thresholds are
/// intentionally expressed in the existing normalized intensity so they do not
/// change the history format or persisted preferences.
/// </summary>
internal enum FlameActivityLevel
{
    Frozen,
    Cool,
    Warm,
    Hot,
    Inferno
}

internal static class FlameActivity
{
    internal const double FrozenMaximum = 0.03d;
    internal const double CoolMaximum = 0.25d;
    internal const double WarmMaximum = 0.52d;
    internal const double HotMaximum = 0.78d;

    private const double Hysteresis = 0.025d;

    internal static FlameActivityLevel Classify(double intensity)
    {
        intensity = Math.Clamp(intensity, 0d, 1d);
        if (intensity <= FrozenMaximum) return FlameActivityLevel.Frozen;
        if (intensity <= CoolMaximum) return FlameActivityLevel.Cool;
        if (intensity <= WarmMaximum) return FlameActivityLevel.Warm;
        if (intensity <= HotMaximum) return FlameActivityLevel.Hot;
        return FlameActivityLevel.Inferno;
    }

    /// <summary>
    /// Keeps the visual state stable while a smoothed value sits directly on a
    /// boundary.  Geometry and colour still interpolate continuously inside the
    /// selected state.
    /// </summary>
    internal static FlameActivityLevel Classify(double intensity, FlameActivityLevel previous)
    {
        intensity = Math.Clamp(intensity, 0d, 1d);
        var candidate = Classify(intensity);
        if (candidate == previous) return previous;
        // Frozen also controls whether the animation timer can stop.  Do not
        // create a hysteresis dead-band that would leave a static ice icon with
        // a timer running for a small but sustained non-zero value.
        if (candidate == FlameActivityLevel.Frozen || previous == FlameActivityLevel.Frozen)
            return candidate;

        if ((int)candidate > (int)previous)
        {
            var exitBoundary = UpperBoundary(previous);
            return intensity < exitBoundary + Hysteresis ? previous : candidate;
        }

        var enterBoundary = UpperBoundary(candidate);
        return intensity > enterBoundary - Hysteresis ? previous : candidate;
    }

    internal static float Progress(double intensity, FlameActivityLevel level)
    {
        var value = Math.Clamp(intensity, 0d, 1d);
        var (minimum, maximum) = level switch
        {
            FlameActivityLevel.Cool => (FrozenMaximum, CoolMaximum),
            FlameActivityLevel.Warm => (CoolMaximum, WarmMaximum),
            FlameActivityLevel.Hot => (WarmMaximum, HotMaximum),
            FlameActivityLevel.Inferno => (HotMaximum, 1d),
            _ => (0d, FrozenMaximum)
        };
        if (maximum <= minimum) return 0f;
        return (float)Math.Clamp((value - minimum) / (maximum - minimum), 0d, 1d);
    }

    private static double UpperBoundary(FlameActivityLevel level) => level switch
    {
        FlameActivityLevel.Frozen => FrozenMaximum,
        FlameActivityLevel.Cool => CoolMaximum,
        FlameActivityLevel.Warm => WarmMaximum,
        FlameActivityLevel.Hot => HotMaximum,
        _ => 1d
    };
}
