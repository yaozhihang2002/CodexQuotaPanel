using System.Drawing.Drawing2D;

namespace CodexQuotaPanel;

internal sealed partial class QuotaOrbControl
{
    private const int MaximumOrbitNodes = 6;

    internal int ResetOrbitNodeCount => GetOrbitCredits(DateTimeOffset.Now).Count;

    private void DrawResetCreditOrbit(Graphics graphics, float scale)
    {
        var credits = GetOrbitCredits(DateTimeOffset.Now);
        if (credits.Count == 0) return;

        // A quiet, non-animated orbit keeps reset cards discoverable without
        // competing with either quota ring or the activity flame. Time flows
        // from left to right; the earliest expiry receives the only halo.
        var bounds = new RectangleF(
            4.25f * scale,
            4.25f * scale,
            Width - 8.5f * scale,
            Height - 8.5f * scale);
        const float startAngle = 202f;
        const float sweepAngle = 136f;
        using (var orbit = new Pen(Color.FromArgb(42, UiPalette.Mint), Math.Max(0.65f, 0.72f * scale))
               {
                   DashStyle = DashStyle.Dot,
                   DashCap = DashCap.Round
               })
            graphics.DrawArc(orbit, bounds, startAngle, sweepAngle);

        var now = DateTimeOffset.Now;
        var maximumDays = Math.Max(1d, credits.Max(credit =>
            Math.Max(0d, (credit.ExpiresAt!.Value - now).TotalDays)));
        var previousAngle = startAngle - 9f;
        for (var index = 0; index < credits.Count; index++)
        {
            var remainingDays = Math.Max(0d, (credits[index].ExpiresAt!.Value - now).TotalDays);
            var timeProgress = (float)Math.Sqrt(Math.Clamp(remainingDays / maximumDays, 0d, 1d));
            var angle = startAngle + 5f + timeProgress * (sweepAngle - 10f);
            angle = Math.Max(angle, previousAngle + 8f);
            angle = Math.Min(angle, startAngle + sweepAngle - 4f);
            previousAngle = angle;

            var radians = angle * MathF.PI / 180f;
            var center = new PointF(
                bounds.Left + bounds.Width / 2f + bounds.Width / 2f * MathF.Cos(radians),
                bounds.Top + bounds.Height / 2f + bounds.Height / 2f * MathF.Sin(radians));
            var earliest = index == 0;
            var radius = (earliest ? 2.15f : 1.4f) * scale;
            if (earliest)
            {
                using var halo = new SolidBrush(Color.FromArgb(48, UiPalette.Mint));
                graphics.FillEllipse(halo,
                    center.X - radius * 2.1f,
                    center.Y - radius * 2.1f,
                    radius * 4.2f,
                    radius * 4.2f);
            }

            var points = new[]
            {
                new PointF(center.X, center.Y - radius),
                new PointF(center.X + radius, center.Y),
                new PointF(center.X, center.Y + radius),
                new PointF(center.X - radius, center.Y)
            };
            using var fill = new SolidBrush(earliest
                ? Color.FromArgb(238, UiPalette.Mint)
                : Color.FromArgb(150, Blend(UiPalette.Mint, Color.White, 0.18f)));
            graphics.FillPolygon(fill, points);
            using var rim = new Pen(Color.FromArgb(earliest ? 220 : 115, ResolveOrbSurface().End),
                Math.Max(0.55f, 0.62f * scale));
            graphics.DrawPolygon(rim, points);
        }
    }

    private List<RateLimitResetCreditInfo> GetOrbitCredits(DateTimeOffset now) =>
        _snapshot?.ResetCredits?.Credits?
            .Where(credit =>
                string.Equals(credit.Status, "available", StringComparison.OrdinalIgnoreCase) &&
                credit.ExpiresAt is { } expiry && expiry > now)
            .OrderBy(credit => credit.ExpiresAt)
            .Take(MaximumOrbitNodes)
            .ToList() ?? [];

    private static string FormatResetCreditAccessibility(RateLimitResetCreditsInfo? resetCredits)
    {
        var count = Math.Max(0, resetCredits?.AvailableCount ?? 0);
        return L10n.Pick($"可用重置卡 {count} 张", $"{count} reset cards available");
    }
}
