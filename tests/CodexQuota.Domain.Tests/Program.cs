using CodexQuota.Domain;

var start = new DateTimeOffset(2026, 8, 24, 8, 47, 0, TimeSpan.Zero);
var end = start.AddDays(7);

Check.Equal(100d, UniformUsageGuide.RemainingPercentAt(start, end, start), "guide start");
Check.Equal(50d, UniformUsageGuide.RemainingPercentAt(start, end, start.AddDays(3.5)), "guide midpoint");
Check.Equal(0d, UniformUsageGuide.RemainingPercentAt(start, end, end), "guide end");
Check.Equal(12d, UniformUsageGuide.DeltaFromPlan(62d, start, end, start.AddDays(3.5)), "guide delta");
Check.True(UniformUsageGuide.RemainingPercentAt(end, start, start) is null, "invalid cycle");

var snapshot = new OfficialQuotaSnapshot(start,
[
    new QuotaWindow("weekly", 10_080, 62d, end),
    new QuotaWindow("invalid", 0, 10d, end)
]);
Check.Equal(1, snapshot.VisibleWindows.Count, "adaptive window filtering");

Console.WriteLine("Domain checks passed: 6");

static class Check
{
    public static void Equal(double expected, double? actual, string name)
    {
        if (actual is null || Math.Abs(expected - actual.Value) > 0.000_001d)
            throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
    }

    public static void Equal(int expected, int actual, string name)
    {
        if (expected != actual)
            throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
    }

    public static void True(bool value, string name)
    {
        if (!value)
            throw new InvalidOperationException($"{name}: expected true");
    }
}
