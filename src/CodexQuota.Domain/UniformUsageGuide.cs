namespace CodexQuota.Domain;

public static class UniformUsageGuide
{
    public static double? RemainingPercentAt(
        DateTimeOffset cycleStart,
        DateTimeOffset cycleEnd,
        DateTimeOffset sampleTime)
    {
        if (cycleEnd <= cycleStart)
            return null;

        if (sampleTime <= cycleStart)
            return 100d;

        if (sampleTime >= cycleEnd)
            return 0d;

        var elapsed = (sampleTime - cycleStart).TotalSeconds;
        var duration = (cycleEnd - cycleStart).TotalSeconds;
        return Math.Clamp(100d * (1d - elapsed / duration), 0d, 100d);
    }

    public static double? DeltaFromPlan(
        double actualRemainingPercent,
        DateTimeOffset cycleStart,
        DateTimeOffset cycleEnd,
        DateTimeOffset sampleTime)
    {
        var planned = RemainingPercentAt(cycleStart, cycleEnd, sampleTime);
        return planned is null ? null : actualRemainingPercent - planned.Value;
    }
}
