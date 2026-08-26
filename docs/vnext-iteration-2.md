# vNext iteration 2: live data, history, forecasts, and render matrix

This iteration moves the first production data path into the cross-platform branch without replacing the stable WinForms release.

## Delivered boundaries

- `CodexAppServerQuotaSource` reads an official rate-limit snapshot through the local Codex app-server adapter.
- `JsonlQuotaSource` is the offline/fallback quota adapter and automatically returns one or two valid windows.
- `JsonlUsageEventSource` parses local `token_count` records, normalizes cumulative counters, detects model and service tier, and backfills early missing service tiers when a later explicit tier appears.
- Missing service tier is treated as `default` by the JSONL source. Explicit `priority` is normalized as `Fast`; invented speed labels are not used.
- `SqliteUsageHistoryStore` persists quota points and token statistics with versioned schema metadata and fingerprint-based deduplication.
- `QuotaRunwayForecaster` blends an idle-inclusive 90-minute view with a six-hour view and exposes confidence instead of presenting a burst as certainty.
- `UsageSummaryCalculator` produces per-day and per-model/service-tier totals.
- `ApiCostEstimator` reports only a dated API-equivalent USD estimate. It is not an invoice and is not a conversion of Codex subscription quota.
- The preview consumes real one-window/two-window snapshots and adapts between a single ring and dual rings.

## Privacy contract

The SQLite schema stores timestamps, window percentages, model/tier labels, token counts, and a deduplication fingerprint. It does not store prompts, responses, transcript text, account identifiers, or source file paths.

## Visual regression matrix

The headless render check produces eight screenshots:

- Simplified Chinese and English
- dark and light themes
- single and dual rings
- 100%, 150%, and 200% render scaling

The orb remains dark in both themes. The light theme uses an off-white canvas and theme-matched rounded borders rather than pure white/black corner artifacts.

## Validation gates

- one restore and one build per platform
- domain, application, infrastructure, and UI checks run with `--no-build`
- Windows and macOS GitHub runners upload the full screenshot matrix
- native click-through, menu-bar/tray, and macOS signing remain later platform acceptance gates

## Upstream stability note

The Codex app-server and local JSONL shapes are local integration surfaces rather than permanent public API contracts. Both are isolated behind ports and fixture-tested so a future upstream change can be repaired without rewriting the UI or history model.
