# CodexQuotaPanel vNext experience specification

This specification makes “feature complete, smoother, and more distinctive” measurable. Visual novelty never overrides legibility, low idle cost, or the compact desktop-tool character.

## Product direction: ambient instrument

The interface should feel like a small precision instrument that happens to live on the desktop:

- calm and almost invisible when usage is stable;
- immediately readable when quota pressure changes;
- dark optical orb independent of the settings theme;
- mint/cyan live data, warm translucent planning data, and restrained alert colors;
- compact typography with generous line height rather than oversized windows;
- depth from controlled translucency, hairline borders, and light falloff instead of heavy gradients or decorative glass everywhere.

The memorable element is the relationship between **actual pace** and **safe pace**, not a decorative animation system.

## Feature parity floor

vNext does not replace the released Windows application until it includes:

- adaptive single/dual quota rings and selectable ring roles/colors;
- official quota snapshots, reset times, reset credits, and refresh state;
- 24-hour exact observed trends with hover inspection and the even-use guide;
- conservative runway forecast with confidence handling;
- current-cycle daily Token chart and model/tier/cost details;
- three activity styles and five continuous intensity states, including an idle frozen state;
- dark, light, and system themes plus Simplified Chinese and English;
- topmost, native drag, click-through, position lock, optional edge snap, opacity, size, and type scale;
- tray/menu-bar controls, alerts, quiet hours, restart, update checks, import/export, diagnostics, and settings migration;
- Windows upgrade behavior that preserves settings and position.

Features that were explicitly rejected stay rejected: no reset-credit nodes on the orb and no weather-system metaphor.

## New design improvements

### Pace halo

A very thin, optional halo around the primary ring summarizes actual pace versus the even-use guide:

- cool neutral: safely ahead of the reference;
- mint: close to the reference;
- warm amber: meaningfully behind the reference;
- alert red only when both remaining quota and forecast justify it.

The halo must not add another number or compete with the two quota rings. It fades out when the comparison is unavailable.

### Temporal scrubber

Trend hover becomes a shared time cursor. At one time position it shows:

- actual remaining quota;
- even-use reference quota;
- delta from plan;
- observed Token usage for that local day when available.

Keyboard left/right navigation and screen-reader text expose the same information without a pointer.

### Quiet intelligence

- When quota data and activity are unchanged, the orb stops continuous animation and consumes no animation frames.
- A data change produces one short, subtle transition rather than restarting the entire visual tree.
- Forecast uncertainty changes wording and opacity before it changes color.
- Offline or unavailable states retain the last timestamp and avoid pretending that a stale value is live.

### Focus transition

Opening details uses a short origin-aware transition from the orb center:

1. the orb contracts and fades without showing an opaque square;
2. after the orb is visually gone, the detail surface expands with its final rounded clip already applied;
3. collapse runs the exact reverse path and restores the original orb coordinates.

The animation is cancellable, respects reduced-motion settings, and never resizes the native window on every frame.

## Motion and performance budgets

These are acceptance targets, not marketing claims:

- settings window warm open: visual first frame within 250 ms on the reference Windows PC;
- tab switch after first construction: next complete frame within 50 ms, with no mixed old/new page frame;
- orb drag: native window movement; no data parsing, layout rebuild, history persistence, or animation-cache regeneration during drag;
- resize: only the visible page participates in live layout;
- animation: compositor/property changes where possible; no per-frame control-tree reconstruction;
- idle state: no continuous render timer when values are unchanged;
- data refresh: file-system notification plus bounded coalescing, with polling only as recovery;
- all background operations are cancellable and publish immutable state snapshots;
- no UI-thread file scanning, SQLite query, pricing aggregation, or forecast calculation.

Frame-time and launch targets must be recorded on both a 60 Hz and a high-refresh display. A faster timer is not accepted as proof of smoother motion.

## Rendering rules

- Draw the orb in one custom render pass.
- Keep final corner/ellipse clips active from the first visible frame.
- Cache geometry and text layouts until DPI, size, language, theme, or displayed content changes.
- Use device-independent coordinates and let the platform compositor map to physical pixels once.
- Snap hairlines deliberately at each render scale; do not mix logical and physical scaling.
- Avoid transparent native child controls over a transparent top-level window.
- Use a single invalidation scheduler that merges repeated state changes into one frame.

## Theme system

The settings and detail surfaces share semantic tokens instead of hard-coded colors:

- `Canvas`, `Surface`, `SurfaceRaised`, `Border`, `Text`, `TextMuted`;
- `LivePrimary`, `LiveSecondary`, `PlanGuide`, `Warning`, `Critical`;
- `OrbBase`, `OrbEdge`, `OrbReflection`, `Shadow`.

Light theme uses a warm mineral canvas rather than pure white. The orb defaults to the same dark optical base in every theme, without a white perimeter. Custom orb background remains available.

## Accessibility and user control

- full keyboard navigation for settings and detail inspection;
- reduced-motion mode follows the operating system and can be overridden;
- minimum contrast is checked for both themes and all status colors;
- text scale never causes title/description overlap or clipped descenders;
- color is never the only signal for pressure, connectivity, or selection;
- click-through always has a tray/menu-bar escape and the existing global recovery route on Windows.

## Visual acceptance matrix

Each milestone captures deterministic screenshots for:

- Windows and macOS;
- dark, light, and system themes;
- Chinese and English;
- single and dual quota windows;
- 100%, 150%, and 200% effective scaling where supported;
- default and maximum supported text scale;
- idle, normal, warning, critical, offline, and stale-data states;
- settings pages, detail panel, daily usage details, tray/menu-bar menu, and every activity style.

Screenshots supplement interaction tests; they do not replace real drag, resize, sleep/wake, topmost, and click-through validation.
