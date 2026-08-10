using System.Globalization;

namespace CodexQuotaPanel;

internal sealed partial class QuotaForm
{
    private long _runwayInsightMinute = long.MinValue;
    internal QuotaRunwayForecast? CurrentRunwayForecast { get; private set; }

    private void UpdateRunwayInsight(DateTimeOffset? now = null, bool force = false)
    {
        if (_snapshot is null)
        {
            CurrentRunwayForecast = null;
            return;
        }

        var current = now ?? DateTimeOffset.Now;
        var currentMinute = current.ToUniversalTime().ToUnixTimeSeconds() / 60;
        if (!force && currentMinute == _runwayInsightMinute) return;
        _runwayInsightMinute = currentMinute;
        CurrentRunwayForecast = QuotaRunwayForecaster.Evaluate(_snapshot, _history, current);
        var resetCredit = FindSoonestResetCredit(_snapshot.ResetCredits, current);

        if (CurrentRunwayForecast is not { } forecast)
        {
            _nextResetLabel.Text = resetCredit?.ExpiresAt is { } expiry
                ? L10n.Pick(
                    $"最早到期重置卡\n{L10n.FormatLocalDate(expiry)}",
                    $"Earliest reset-credit expiry\n{L10n.FormatLocalDate(expiry)}")
                : L10n.ResetCreditExpiryUnavailable;
            _nextResetLabel.ForeColor = UiPalette.Muted;
            _toolTip.SetToolTip(_nextResetLabel, _nextResetLabel.Text.Replace('\n', ' '));
            return;
        }

        var runway = forecast.ExhaustsAt is { } exhausts
            ? exhausts - current
            : TimeSpan.Zero;
        var window = LimitRowControl.FormatWindow(forecast.WindowMinutes);
        var headline = forecast.State switch
        {
            QuotaRunwayState.Exhausted => L10n.Pick("续航预测 · 额度已用尽", "Runway · quota exhausted"),
            QuotaRunwayState.AtRisk => L10n.Pick(
                $"预计可用 {FormatRunway(runway, forecast.Confidence)} · {window}",
                $"About {FormatRunway(runway, forecast.Confidence)} left · {window}"),
            _ when forecast.ResetsAt is not null => L10n.Pick(
                $"续航预测 · 可维持到重置", "Runway · lasts until reset"),
            _ => L10n.Pick(
                $"预计可用 {FormatRunway(runway, forecast.Confidence)} · {window}",
                $"About {FormatRunway(runway, forecast.Confidence)} left · {window}")
        };

        var detail = resetCredit?.ExpiresAt is { } resetExpiry
            ? L10n.Pick(
                $"重置卡 · {resetExpiry.ToLocalTime():M月d日 HH:mm} 到期",
                $"Reset card · expires {resetExpiry.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.InvariantCulture)}")
            : forecast.SustainablePercentPerHour is { } sustainable
                ? L10n.Pick(
                    $"当前 {forecast.PercentPerHour:0.#}%/小时 · 安全速度 {sustainable:0.#}%/小时",
                    $"Now {forecast.PercentPerHour:0.#}%/h · safe {sustainable:0.#}%/h")
                : L10n.Pick(
                    $"当前消耗 {forecast.PercentPerHour:0.#}%/小时",
                    $"Current burn {forecast.PercentPerHour:0.#}%/h");

        var text = $"{headline}\n{detail}";
        if (!string.Equals(_nextResetLabel.Text, text, StringComparison.Ordinal))
            _nextResetLabel.Text = text;
        _nextResetLabel.ForeColor = forecast.State is QuotaRunwayState.AtRisk or QuotaRunwayState.Exhausted
            ? UiPalette.Amber
            : UiPalette.Muted;
        var confidence = forecast.Confidence >= 0.78d
            ? L10n.Pick("高", "high")
            : forecast.Confidence >= 0.58d
                ? L10n.Pick("中", "medium")
                : L10n.Pick("较低", "low");
        _toolTip.SetToolTip(_nextResetLabel, L10n.Pick(
            $"空闲时间已计入；短期 {forecast.ShortPercentPerHour:0.#}%/小时，长期 {forecast.LongPercentPerHour:0.#}%/小时，置信度{confidence}。不读取对话内容。",
            $"Idle time included; short {forecast.ShortPercentPerHour:0.#}%/h, long {forecast.LongPercentPerHour:0.#}%/h, {confidence} confidence. Conversation content is not read."));
    }

    private static RateLimitResetCreditInfo? FindSoonestResetCredit(
        RateLimitResetCreditsInfo? resetCredits,
        DateTimeOffset now) => resetCredits?.Credits?
        .Where(credit =>
            string.Equals(credit.Status, "available", StringComparison.OrdinalIgnoreCase) &&
            credit.ExpiresAt is { } expiry && expiry > now)
        .OrderBy(credit => credit.ExpiresAt)
        .FirstOrDefault();

    internal static string FormatRunway(TimeSpan runway, double confidence = 1d)
    {
        if (runway <= TimeSpan.Zero) return L10n.Pick("不足 1 分钟", "less than 1 min");
        var stepMinutes = runway.TotalHours >= 24d
            ? 6 * 60
            : confidence >= 0.78d ? 15 : confidence >= 0.58d ? 30 : 60;
        var roundedMinutes = Math.Max(stepMinutes,
            (int)Math.Ceiling(runway.TotalMinutes / stepMinutes) * stepMinutes);
        var rounded = TimeSpan.FromMinutes(roundedMinutes);
        if (rounded.TotalMinutes < 60)
            return L10n.Pick($"{roundedMinutes} 分钟", $"{roundedMinutes} min");
        if (rounded.TotalHours < 24)
            return rounded.Minutes == 0
                ? L10n.Pick($"{(int)rounded.TotalHours}小时", $"{(int)rounded.TotalHours}h")
                : L10n.Pick($"{(int)rounded.TotalHours}小时 {rounded.Minutes}分",
                    $"{(int)rounded.TotalHours}h {rounded.Minutes}m");
        return L10n.Pick($"{(int)rounded.TotalDays}天 {rounded.Hours}小时",
            $"{(int)rounded.TotalDays}d {rounded.Hours}h");
    }
}
