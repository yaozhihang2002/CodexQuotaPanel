# CodexQuotaPanel vNext architecture

Status: approved direction for the `codex/vnext-windows-macos` branch. The released WinForms application remains the Windows compatibility baseline until vNext passes the gates below.

The companion [vNext experience specification](vnext-experience-spec.md) defines feature parity, visual direction, motion behavior, and measurable responsiveness gates.

## Goal

Deliver one lightweight desktop application that behaves consistently on Windows 10/11 and supported macOS versions while sharing quota, usage, pricing, forecasting, settings, and history logic.

The vNext application is a modular monolith: one local process, no local web service and no microservices.

## Technology baseline

- .NET 10 LTS for the release line.
- Avalonia 12 for shared Windows/macOS UI.
- Pure C# domain and application projects with no UI or operating-system dependencies.
- JSON for user preferences and device-local window state.
- SQLite for versioned quota snapshots, normalized usage events, daily aggregates, and pricing-rule metadata.
- Self-contained packages first; Native AOT is evaluated only after functional parity and trimming tests.

The current development PC has only the .NET 9 SDK installed. The first scaffold may be inspected without replacing the Windows release toolchain, but vNext builds must move to .NET 10 before release validation.

## Project boundaries

```text
src/
  CodexQuota.Domain/
  CodexQuota.Application/
  CodexQuota.Infrastructure/
  CodexQuota.UI.Avalonia/
  CodexQuota.Platform.Windows/
  CodexQuota.Platform.macOS/
  CodexQuota.App/
tests/
  CodexQuota.Domain.Tests/
  CodexQuota.Application.Tests/
  CodexQuota.Infrastructure.Tests/
  CodexQuota.UI.Tests/
```

### Domain

Owns immutable concepts such as `OfficialQuotaSnapshot`, `QuotaWindow`, `ObservedUsage`, `EstimatedApiCost`, `UsageForecast`, `ForecastConfidence`, `ResetCredit`, and `PricingRule`.

It must not reference Avalonia, WinForms, Win32, AppKit, the Windows registry, file pickers, tray menus, or installation code.

### Application

Owns use cases and the single immutable application state tree:

```text
AppState
  LiveQuotaState
  UsageHistoryState
  OrbState
  SettingsState
  ThemeState
  WindowState
```

Settings editing uses a `SettingsDraft`. Preview changes affect the draft and preview state; Save commits atomically; Cancel restores the state captured when the editor opened.

### Infrastructure

Implements replaceable ports:

```csharp
public interface IQuotaSource
{
    Task<OfficialQuotaSnapshot?> ReadAsync(CancellationToken cancellationToken);
}

public interface IUsageEventSource
{
    IAsyncEnumerable<ObservedUsage> WatchAsync(CancellationToken cancellationToken);
}
```

Initial adapters:

- Codex App Server quota source.
- Codex JSONL usage source under the resolved Codex home.
- SQLite history and aggregate store.
- Versioned JSON settings store.
- GitHub release update source.
- Deterministic sample sources for tests and previews.

The application must never present locally observed Token counts, API-equivalent cost, or forecasts as an official subscription bill or official quota percentage.

### Shared Avalonia UI

- Settings, dashboard, usage details, alerts, and themes use normal Avalonia controls with compiled bindings.
- Orb rendering uses one custom-drawn control for rings, text, activity effect, status marker, and hit testing.
- Tabs are retained after first creation; switching does not reconstruct the page.
- Background calculations publish coalesced immutable snapshots to the UI thread.
- Window resize and orb drag suspend nonessential animation and data redraw.

### Platform adapters

Shared interfaces cover click-through, always-on-top repair, launch at login, monitor/work-area placement, tray/menu-bar integration, notifications, and installer/update handoff.

Windows implementations may use Win32 and the registry only inside `CodexQuota.Platform.Windows`. macOS implementations may use AppKit/CoreGraphics bindings only inside `CodexQuota.Platform.macOS`.

## Data locations

- Codex input root: resolve `CODEX_HOME` first, then fall back to `~/.codex`.
- Windows app data: `%LocalAppData%\CodexQuotaPanel`.
- macOS app data: `~/Library/Application Support/CodexQuotaPanel`.
- User preferences are portable; monitor coordinates, launch-at-login state, caches, and history remain device-local.

OpenAI documents the user-level Codex configuration under `~/.codex/config.toml`. Internal quota and App Server behavior is treated as an adapter contract that must be revalidated, not as a public API guarantee.

## Migration strategy

1. Freeze Windows v0.5.2 behavior and representative fixtures.
2. Extract pure models, parsers, pricing, history math, and forecast math with parity tests.
3. Add versioned settings migration and SQLite repositories.
4. Build shared Avalonia settings, dashboard, details, and daily-usage views.
5. Build the custom-drawn orb and tray/menu-bar shell.
6. Complete Windows platform integration and compare against the released app.
7. Complete macOS platform integration, packaging, signing, and notarization.
8. Publish vNext only after both platform matrices pass; keep the WinForms release line available until then.

## Acceptance gates

- Domain and application tests run on Windows and macOS CI.
- The same fixtures produce identical quota, Token, price, forecast, and uniform-use reference results on both systems.
- Settings Save/Cancel, import/export, migration, and position restoration pass on both systems.
- Orb drag, topmost repair, click-through recovery, tray/menu-bar control, sleep/wake, and multi-monitor behavior pass on real hardware.
- Dark/light/system themes pass screenshot checks at 100%, 150%, and 200% effective scale where supported.
- Windows x64 and arm64 packages install or run portably without deleting existing user settings.
- macOS Apple Silicon package launches after signing/notarization checks and preserves settings across upgrades.
- No release claim is made for macOS until it has been run on a real Mac; cross-compilation alone is not acceptance evidence.

## Non-goals for the first vNext milestone

- Linux packaging.
- Cloud synchronization of settings or history.
- Reconstructing official quota percentages from Token counts.
- Native AOT before ordinary self-contained packages are stable.
- Replacing the released WinForms application before Windows parity is demonstrated.

## Primary references

- OpenAI Codex configuration reference: <https://developers.openai.com/codex/config-reference>
- Avalonia overview and supported desktop families: <https://docs.avaloniaui.net/docs/welcome>
- .NET support policy: <https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core>
