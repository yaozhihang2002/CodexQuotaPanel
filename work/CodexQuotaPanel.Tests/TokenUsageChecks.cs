using CodexQuotaPanel;

internal static class TokenUsageChecks
{
    internal static void Run()
    {
        var root = Path.Combine(Path.GetTempPath(), "CodexQuotaPanel.TokenUsage", Guid.NewGuid().ToString("N"));
        try
        {
            var sessions = Path.Combine(root, "sessions", "2026", "08", "01");
            Directory.CreateDirectory(sessions);
            var startsAt = new DateTimeOffset(2026, 8, 1, 12, 0, 0, TimeSpan.FromHours(8));
            var resetsAt = startsAt.AddDays(7);
            File.WriteAllLines(Path.Combine(sessions, "rollout-a.jsonl"),
            [
                Event(startsAt.AddHours(-1), 30, 30, 24, 8, 6, 2),
                Event(startsAt.AddHours(1), 100, 70, 60, 20, 10, 5),
                Event(startsAt.AddHours(1).AddMinutes(1), 100, 70, 60, 20, 10, 5),
                Event(startsAt.AddHours(11).AddMinutes(50), 150, 50, 40, 10, 10, 2),
                Event(startsAt.AddHours(12).AddMinutes(10), 300, 150, 130, 50, 20, 8),
                Event(resetsAt.AddMinutes(1), 400, 100, 80, 20, 20, 5)
            ]);
            File.WriteAllLines(Path.Combine(sessions, "rollout-b.jsonl"),
            [
                Event(startsAt.AddHours(20), 40, 40, 32, 12, 8, 3)
            ]);

            var store = new TokenUsageHistory(root);
            var usage = store.ReadCycle(startsAt, resetsAt, 10080, startsAt.AddDays(2));
            Assert(usage.Days.Count == 3, "The reset-cycle day range was not preserved.");
            Assert(usage.Days[0].Usage == new TokenUsageBreakdown(120, 100, 30, 20, 7),
                "The first local day token breakdown is inaccurate.");
            Assert(usage.Days[1].Usage == new TokenUsageBreakdown(190, 162, 62, 28, 11),
                "Cross-midnight token attribution is inaccurate.");
            Assert(usage.Days[2].Usage == TokenUsageBreakdown.Empty,
                "A zero-usage day was omitted or filled with invented data.");
            Assert(usage.Total.TotalTokens == 310,
                "Duplicate cumulative token events were counted more than once.");

            var snapshot = new QuotaSnapshot("codex", null, null,
                new LimitBucket(38, 10080, resetsAt), null, "pro", null,
                startsAt.AddDays(2), "App Server");
            var selected = TokenCycleSelector.Select(snapshot);
            Assert(selected is { WindowMinutes: 10080 } && selected.Value.StartsAt == startsAt,
                "The single available quota window was not selected as the reset cycle.");

            using var form = new QuotaForm();
            form.SetTokenCycleUsage(usage);
            form.ApplySnapshot(snapshot);
            Assert(form.VisibleQuotaRowCount == 1 && form.ExpandedLogicalSize == new Size(368, 518),
                "The detail panel did not compact to one quota window.");
            Assert(form.OrbControl.AvailableRingCount == 1 && form.OrbControl.InnerLabel == "7D",
                "The orb did not switch to a single actual-duration ring.");
            var hover = form.DailyTokenUsage.ShowDayForTest(1);
            Assert(hover is not null && hover.Contains("190", StringComparison.Ordinal) &&
                   hover.Contains("162", StringComparison.Ordinal),
                "The daily token hover does not expose exact totals and breakdowns.");

            form.ConfigureRings(new RingDisplayConfiguration(
                new RingWindowSelection(10080, RingWindowRole.Primary),
                new RingWindowSelection(10080, RingWindowRole.Primary),
                Color.MediumAquamarine,
                Color.CornflowerBlue));
            Assert(form.OrbControl.AvailableRingCount == 1,
                "A duplicated saved ring role turned one server window into two rings.");

            form.ApplySnapshot(snapshot with
            {
                Primary = new LimitBucket(20, 300, startsAt.AddHours(5))
            });
            Assert(form.VisibleQuotaRowCount == 2 && form.ExpandedLogicalSize == new Size(368, 596) &&
                   form.OrbControl.AvailableRingCount == 2,
                "The UI did not automatically restore the second window and ring.");

            form.ApplySnapshot(snapshot with
            {
                Primary = new LimitBucket(25, 43200, resetsAt.AddDays(23)),
                Secondary = null
            });
            Assert(form.OrbControl.AvailableRingCount == 1 && form.OrbControl.OuterLabel == "30D",
                "A changed server window duration left a stale configured ring label.");

            form.SetTopMostPreference(true);
            form.CreateControl();
            form.TopMost = false;
            form.ReassertTopMostPreference();
            Assert(form.AlwaysOnTopPreference && form.TopMost,
                "The saved top-most preference was not reasserted after native state loss.");
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    private static string Event(
        DateTimeOffset timestamp,
        long cumulative,
        long total,
        long input,
        long cached,
        long output,
        long reasoning) => System.Text.Json.JsonSerializer.Serialize(new
        {
            timestamp = timestamp.UtcDateTime.ToString("O"),
            type = "event_msg",
            payload = new
            {
                type = "token_count",
                info = new
                {
                    total_token_usage = new
                    {
                        input_tokens = cumulative - output,
                        cached_input_tokens = 0,
                        output_tokens = output,
                        reasoning_output_tokens = 0,
                        total_tokens = cumulative
                    },
                    last_token_usage = new
                    {
                        input_tokens = input,
                        cached_input_tokens = cached,
                        output_tokens = output,
                        reasoning_output_tokens = reasoning,
                        total_tokens = total
                    },
                    model_context_window = 200000
                },
                rate_limits = new { limit_id = "codex" }
            }
        });

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
