# vNext iteration 1: executable foundation

This checkpoint turns the approved architecture into a runnable, independently testable baseline. It does not replace the released WinForms application.

## Included

- .NET 10 domain, application, infrastructure, shared Avalonia UI, Windows platform, macOS platform, and desktop host projects.
- Immutable AppState with separate quota, history, orb, settings, theme, and window state.
- SettingsDraftSession semantics:
  - preview changes are temporary;
  - Save commits without closing the editor;
  - Cancel restores the state captured when settings opened.
- Official quota and locally observed usage are separate domain types.
- Adaptive quota-window filtering and uniform-use reference math.
- CODEX_HOME first, then ~/.codex, through an injectable resolver.
- Atomic JSON settings writes.
- One-pass custom orb rendering and a compact ambient-instrument preview.
- Windows and macOS GitHub Actions jobs that build, run deterministic checks, render the UI without a display, and upload the rendered PNG.

## Local evidence

- Release build: 11 projects, 0 warnings, 0 errors.
- Domain checks: 6.
- Application checks: 10.
- Infrastructure checks: 4.
- Headless visual render: 1 PNG at 980 x 620.

## Deliberately deferred

- Real Codex quota and JSONL adapters.
- SQLite migrations and history repositories.
- Full settings pages and localization.
- Native Windows/macOS tray, click-through, topmost repair, startup, and notifications.
- Real-hardware drag, resize, sleep/wake, DPI, and accessibility acceptance.
- Packaging, signing, notarization, and release replacement.

## Next gate

Iteration 2 migrates representative v0.5.2 fixtures and conservative forecast/pricing semantics, then adds Chinese/English, dark/light, single/dual-ring, and scale variants to the visual matrix.
