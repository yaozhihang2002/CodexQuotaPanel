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
                TurnContext(startsAt.AddHours(-2), "gpt-5.6-luna"),
                ThreadSettings(startsAt.AddHours(-2), "gpt-5.6-luna", "default"),
                Event(startsAt.AddHours(-1), 30, 30, 24, 8, 6, 2),
                Event(startsAt.AddHours(1), 100, 70, 60, 20, 10, 5),
                Event(startsAt.AddHours(1).AddMinutes(1), 100, 70, 60, 20, 10, 5),
                Event(startsAt.AddHours(11).AddMinutes(50), 150, 50, 40, 10, 10, 2),
                ThreadSettings(startsAt.AddHours(12), "gpt-5.6-sol", "priority"),
                Event(startsAt.AddHours(12).AddMinutes(10), 300, 150, 130, 50, 20, 8),
                Event(resetsAt.AddMinutes(1), 400, 100, 80, 20, 20, 5)
            ]);
            File.WriteAllLines(Path.Combine(sessions, "rollout-b.jsonl"),
            [
                TurnContext(startsAt.AddHours(19), "gpt-5.6-terra"),
                ThreadSettings(startsAt.AddHours(19), "gpt-5.6-terra", "default"),
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
            Assert(usage.Days[0].Slices.Count == 1 &&
                   usage.Days[0].Slices[0].ModelDisplay == "GPT-5.6 Luna" &&
                   usage.Days[0].Slices[0].SpeedDisplay == "Default" &&
                   usage.Days[0].EstimatedUsd == 0.0000386m,
                "Default Luna token attribution or API cost estimate is inaccurate.");
            Assert(usage.Days[1].Slices.Count == 2 && usage.Days[1].EstimatedUsd == 0.0016184m &&
                   usage.Days[1].Slices.Any(slice => slice.ModelDisplay == "GPT-5.6 Sol" &&
                       slice.SpeedDisplay == "Fast" && slice.EstimatedUsd == 0.00148m),
                "Per-model Fast/Default attribution is inaccurate.");
            Assert(usage.EstimatedUsd == 0.001657m && usage.UnpricedTokens == 0,
                "Cycle API cost aggregation is inaccurate.");

            var baseline = ApiCostEstimator.Estimate(
                "gpt-5.6-luna", "default", new TokenUsageBreakdown(130, 100, 20, 30, 12));
            var fast = ApiCostEstimator.Estimate(
                "gpt-5.6-luna", "priority", new TokenUsageBreakdown(130, 100, 20, 30, 12));
            var unknown = ApiCostEstimator.Estimate(
                "gpt-5.5", "default", new TokenUsageBreakdown(130, 100, 20, 30, 12));
            var withoutReasoningDetail = ApiCostEstimator.Estimate(
                "gpt-5.6-luna", "default", new TokenUsageBreakdown(130, 100, 20, 30, 0));
            var longContext = ApiCostEstimator.Estimate(
                "gpt-5.6-luna", "default", new TokenUsageBreakdown(272_101, 272_001, 0, 100, 40));
            Assert(baseline == new ApiCostEstimate(0.0000524m, true) &&
                   fast == new ApiCostEstimate(0.0001048m, true) &&
                   withoutReasoningDetail == baseline &&
                   longContext == new ApiCostEstimate(0.1089804m, true) &&
                   !unknown.IsPriced,
                "The API estimate, Fast/long-context multiplier, reasoning subset, or unknown-model boundary is wrong.");

            VerifyMissingTierFallback(root, startsAt, resetsAt);

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
            Assert(hover is not null && hover.Contains("$0.0016", StringComparison.Ordinal) &&
                   hover.Contains("190", StringComparison.Ordinal) &&
                   hover.Contains("162", StringComparison.Ordinal),
                "The daily token hover does not expose exact totals and breakdowns.");

            using (var details = new TokenUsageDetailsForm(usage))
            {
                Assert(details.Usage.Slices.Count == 3 &&
                       details.Usage.Slices.All(slice => slice.SpeedDisplay is "Default" or "Fast"),
                    "The details view exposed an unsupported speed label or lost an aggregate row.");
            }

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

    private static string TurnContext(DateTimeOffset timestamp, string model) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            timestamp = timestamp.UtcDateTime.ToString("O"),
            type = "turn_context",
            payload = new { model }
        });

    private static string ThreadSettings(
        DateTimeOffset timestamp,
        string model,
        string serviceTier) => System.Text.Json.JsonSerializer.Serialize(new
        {
            timestamp = timestamp.UtcDateTime.ToString("O"),
            type = "event_msg",
            payload = new
            {
                type = "thread_settings_applied",
                thread_settings = new { model, service_tier = serviceTier }
            }
        });

    private static void VerifyMissingTierFallback(
        string root,
        DateTimeOffset startsAt,
        DateTimeOffset resetsAt)
    {
        var fallbackRoot = Path.Combine(root, "tier-fallback");
        var sessions = Path.Combine(fallbackRoot, "sessions", "2026", "08", "01");
        Directory.CreateDirectory(sessions);
        File.WriteAllLines(Path.Combine(sessions, "rollout-leading-tier.jsonl"),
        [
            TurnContext(startsAt, "gpt-5.6-terra"),
            Event(startsAt.AddMinutes(1), 40, 40, 32, 12, 8, 3),
            ThreadSettings(startsAt.AddMinutes(2), "gpt-5.6-terra", "priority"),
            Event(startsAt.AddMinutes(3), 100, 60, 48, 18, 12, 4)
        ]);
        File.WriteAllLines(Path.Combine(sessions, "rollout-no-tier.jsonl"),
        [
            TurnContext(startsAt, "codex-auto-review"),
            Event(startsAt.AddMinutes(4), 25, 25, 20, 10, 5, 2)
        ]);

        var usage = new TokenUsageHistory(fallbackRoot)
            .ReadCycle(startsAt, resetsAt, 10080, startsAt.AddHours(1));
        var terra = usage.Slices.Single(slice => slice.Model == "gpt-5.6-terra");
        var reviewer = usage.Slices.Single(slice => slice.Model == "codex-auto-review");
        Assert(terra.SpeedDisplay == "Fast" && terra.Usage.TotalTokens == 100 && terra.IsPriced,
            "Token events before the first explicit priority tier were not backfilled as Fast.");
        Assert(reviewer.SpeedDisplay == "Default" && reviewer.Usage.TotalTokens == 25 && !reviewer.IsPriced,
            "A session with no service tier was not treated as Default or its private price boundary was lost.");
        Assert(usage.Slices.All(slice => slice.SpeedDisplay is "Default" or "Fast"),
            "A missing service tier leaked into the details view as Unknown.");

        var originalLanguage = L10n.Current;
        try
        {
            L10n.SetLanguage(AppLanguage.SimplifiedChinese);
            Assert(DailyTokenUsageControl.NoPublicRateLabel == "未公开计价",
                "The Chinese no-public-rate label is unclear.");
            L10n.SetLanguage(AppLanguage.English);
            Assert(DailyTokenUsageControl.NoPublicRateLabel == "No public rate",
                "The English no-public-rate label is unclear.");
        }
        finally
        {
            L10n.SetLanguage(originalLanguage);
        }
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
