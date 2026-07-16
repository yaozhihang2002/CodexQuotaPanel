namespace CodexQuotaPanel;

internal enum RingWindowRole
{
    Primary = 0,
    Secondary = 1
}

internal sealed record RingWindowSelection(int WindowMinutes, RingWindowRole Role);

internal sealed record RingDisplayConfiguration(
    RingWindowSelection Outer,
    RingWindowSelection Inner,
    Color OuterColor,
    Color InnerColor)
{
    public static RingDisplayConfiguration FromPreferences(PanelPreferences preferences)
    {
        var colors = UiPalette.ResolveColors(preferences.ThemeMode);
        var outer = Color.FromArgb(preferences.OuterColorArgb);
        var inner = Color.FromArgb(preferences.InnerColorArgb);

        // Legacy defaults were tuned only for the old dark canvas and become
        // nearly invisible on a light orb.  Treat those two exact values as
        // semantic defaults while preserving every genuinely custom colour.
        if (preferences.OuterColorArgb == PanelPreferenceManager.DefaultOuterColorArgb)
            outer = colors.Mint;
        if (preferences.InnerColorArgb == PanelPreferenceManager.DefaultInnerColorArgb)
            inner = colors.Sky;

        return new RingDisplayConfiguration(
            new RingWindowSelection(preferences.OuterWindowMinutes, (RingWindowRole)preferences.OuterWindowRole),
            new RingWindowSelection(preferences.InnerWindowMinutes, (RingWindowRole)preferences.InnerWindowRole),
            outer,
            inner);
    }
}

internal sealed record RingWindowOption(RingWindowSelection Selection, string Label, bool Available)
{
    public override string ToString() => Label;
}

internal static class RingWindowCatalog
{
    public static IReadOnlyList<RingWindowOption> GetOptions(
        QuotaSnapshot? snapshot,
        params RingWindowSelection[] retained)
    {
        var options = new List<RingWindowOption>();
        if (snapshot?.Primary is { WindowMinutes: > 0 } primary)
            options.Add(Create(primary, RingWindowRole.Primary, available: true));
        if (snapshot?.Secondary is { WindowMinutes: > 0 } secondary)
            options.Add(Create(secondary, RingWindowRole.Secondary, available: true));

        foreach (var selection in retained)
        {
            if (options.Any(option => option.Selection == selection)) continue;
            options.Add(new RingWindowOption(selection,
                $"{FormatLong(selection.WindowMinutes)} · {L10n.TemporarilyUnavailable}", Available: false));
        }

        if (options.Count == 0)
        {
            options.Add(new RingWindowOption(new RingWindowSelection(300, RingWindowRole.Primary), L10n.FormatWindow(300), false));
            options.Add(new RingWindowOption(new RingWindowSelection(10080, RingWindowRole.Secondary), L10n.FormatWindow(10080), false));
        }
        return options;
    }

    public static LimitBucket? FindBucket(QuotaSnapshot? snapshot, RingWindowSelection selection)
    {
        if (snapshot is null) return null;
        var preferred = selection.Role == RingWindowRole.Primary ? snapshot.Primary : snapshot.Secondary;
        if (preferred?.WindowMinutes == selection.WindowMinutes) return preferred;

        var matches = snapshot.Buckets
            .Where(bucket => bucket.WindowMinutes == selection.WindowMinutes)
            .ToArray();
        return matches.Length == 1 ? matches[0] : null;
    }

    public static string FormatShort(int minutes)
    {
        if (minutes <= 0) return "—";
        if (minutes % 10080 == 0) return $"{minutes / 10080 * 7}D";
        if (minutes % 1440 == 0) return $"{minutes / 1440}D";
        if (minutes % 60 == 0) return $"{minutes / 60}H";
        return $"{minutes}M";
    }

    public static string FormatLong(int minutes) => L10n.FormatWindow(minutes);

    private static RingWindowOption Create(LimitBucket bucket, RingWindowRole role, bool available)
    {
        var minutes = bucket.WindowMinutes!.Value;
        var remaining = Math.Round(bucket.RemainingPercent);
        var label = L10n.Pick(
            $"{FormatLong(minutes)} · {remaining:0}% 剩余",
            $"{FormatLong(minutes)} · {remaining:0}% left");
        return new RingWindowOption(
            new RingWindowSelection(minutes, role),
            label,
            available);
    }
}
