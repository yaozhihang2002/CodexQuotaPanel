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

var usage = new TokenUsageBreakdown(110_000, 100_000, 40_000, 10_000, 3_000);
var standardCost = ApiCostEstimator.Estimate("gpt-5.6-sol", "default", usage);
Check.True(standardCost.IsPriced, "standard priced");
Check.Equal(0.456m, standardCost.Usd, "standard cost");
Check.Equal(0.912m, ApiCostEstimator.Estimate("gpt-5.6-sol", "fast", usage).Usd, "fast cost");
Check.Equal(0.31m, ApiCostEstimator.Estimate("codex-auto-review", "default", usage).Usd,
    "auto review uses the official GPT-5.4 API-equivalent rate");
Check.Equal(0.62m, ApiCostEstimator.Estimate("gpt-5.5", "default", usage).Usd,
    "gpt-5.5 official API rate");
Check.Equal(0.093m, ApiCostEstimator.Estimate("gpt-5.4-mini", "default", usage).Usd,
    "gpt-5.4 mini official API rate");
Check.Equal(0.252m, ApiCostEstimator.Estimate("gpt-5.3-codex", "default", usage).Usd,
    "gpt-5.3-codex official API rate");
Check.True(!ApiCostEstimator.Estimate("gpt-5.4-mini", "fast", usage).IsPriced,
    "unsupported fast tier remains unpriced");
Check.Equal("Auto-review", ApiCostEstimator.DisplayModel("codex-auto-review"), "auto review display");
Check.Equal("Fast", ApiCostEstimator.DisplayTier("priority"), "priority display");
Check.Equal(ServiceTier.Unknown, ApiCostEstimator.NormalizeTier(null), "missing tier remains unknown");

var daily = UsageSummaryCalculator.SummarizeByDay(
[
    new ObservedUsage(start, "gpt-5.6-sol", "default", usage, "a", true),
    new ObservedUsage(start.AddMinutes(1), "codex-auto-review", "default", usage, "b", true)
], TimeZoneInfo.Utc);
Check.Equal(1, daily.Count, "daily grouping");
Check.Equal(220_000L, daily[0].Usage.TotalTokens, "daily token total");
Check.Equal(0, daily[0].UnpricedEventCount, "daily auto review priced count");
Check.Equal(0.766m, daily[0].EstimatedApiUsd, "daily API-equivalent total includes auto review");
Check.Equal(2, daily[0].Slices.Count, "model tier slices");

var history = Enumerable.Range(0, 13)
    .Select(index => new QuotaHistoryPoint(
        start.AddHours(-6).AddMinutes(index * 30), "weekly", 10_080, 64.4d - index * 0.2d))
    .ToArray();
var forecast = QuotaRunwayForecaster.Evaluate(snapshot, history, start);
Check.True(forecast is not null, "forecast available");
Check.Equal(0.4d, forecast!.PercentPerHour, "idle inclusive steady rate");
Check.Equal(ForecastState.Sustainable, forecast.State, "steady forecast state");

var burst = Enumerable.Range(0, 13)
    .Select(index => new QuotaHistoryPoint(
        start.AddHours(-6).AddMinutes(index * 30), "weekly", 10_080, 66d - (index == 12 ? 4d : 0d)))
    .ToArray();
var burstForecast = QuotaRunwayForecaster.Evaluate(snapshot, burst, start);
Check.True(burstForecast is not null && burstForecast.PercentPerHour < 2d,
    "long idle view tempers one burst");

var dualSnapshot = new OfficialQuotaSnapshot(start,
[
    new QuotaWindow("weekly-pressure", 10_080, 50d, start.AddHours(84)),
    new QuotaWindow("five-hour-pressure", 300, 50d, start.AddHours(2.5))
]);
var dualHistory = Enumerable.Range(0, 7).SelectMany(index => new[]
{
    new QuotaHistoryPoint(start.AddHours(-3).AddMinutes(index * 30),
        "weekly-pressure", 10_080, 51.8d - index * .3d),
    new QuotaHistoryPoint(start.AddHours(-3).AddMinutes(index * 30),
        "five-hour-pressure", 300, 56d - index)
}).ToArray();
var pressureForecast = QuotaRunwayForecaster.Evaluate(dualSnapshot, dualHistory, start);
Check.Equal("weekly-pressure", pressureForecast?.WindowId,
    "dual-window forecast selects the higher relative quota pressure");

Console.WriteLine("Domain checks passed: 22");

static class Check
{
    public static void Equal(double expected, double? actual, string name)
    {
        if (actual is null || Math.Abs(expected - actual.Value) > 0.000_001d)
            throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
    }

    public static void Equal(decimal expected, decimal actual, string name)
    {
        if (expected != actual)
            throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
    }

    public static void Equal<T>(T expected, T actual, string name)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
            throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
    }

    public static void Equal(int expected, int actual, string name)
    {
        if (expected != actual)
            throw new InvalidOperationException($"{name}: expected {expected}, actual {actual}");
    }

    public static void True(bool value, string name)
    {
        if (!value) throw new InvalidOperationException($"{name}: expected true");
    }
}
