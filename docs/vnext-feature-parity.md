# vNext formal-release parity gate

The WinForms application under `work/` is the behavioral baseline. A vNext
package is not a release candidate until every required row below has executable
evidence. A screenshot-only prototype does not satisfy this gate.

Status vocabulary:

- **verified**: deterministic check, rendered interaction matrix, or native Windows smoke evidence exists.
- **hosted verified**: GitHub-hosted Windows/macOS check passed on the committed tree.
- **device pending**: the implementation and hosted checks pass, but final acceptance needs a real user device.

## User-facing parity

| Area | Required behavior | Gate and evidence |
| --- | --- | --- |
| Orb | transparent compact orb, adaptive one/two rings, configurable colors/background/opacity/size, consumption feedback | **verified** — single/dual 100/150/200% render matrix and native orb smoke |
| Window lifecycle | click orb to open details, reverse collapse, no position jump, no square/white intermediate frame | **verified** — interaction transition checks plus native open/collapse coordinate smoke |
| Placement | free drag, optional edge snap and lock, persisted per-display position, DPI/work-area clamping | **verified** — application placement checks and native negative-coordinate/DPI logic |
| Window modes | always-on-top, click-through, hide/show orb, restore previous startup view | **verified** — native Windows extended-style smoke and application-state checks |
| Hover | readable quota peek with reset time and source state | **verified** — bilingual tooltip/render checks |
| Details | quota windows, trend and even-use guide, forecast/confidence, reset-card expiry, refresh/source status | **verified** — dashboard render and domain forecast/reset-credit checks |
| Usage details | current-cycle totals, per-day values, model/service-tier breakdown, API-equivalent cost disclaimer | **verified** — usage render plus pricing/attribution checks |
| Trend interaction | 24-hour exact-enough segmented history and pointer tooltips for actual/reference values | **verified** — 24-hour storage, pointer selection and paired-value checks |
| Settings | general, appearance, interaction, notifications, data/about; immediate preview, Save without close, Cancel rollback | **verified** — all five pages in zh/dark and en/light plus atomic draft tests |
| Personalization | Chinese/English, system/dark/light, UI scale, orb size, opacity, background, ring colors, three flame styles | **verified** — 28-scenario UI matrix including 150% font and three five-state flame styles |
| Alerts | warning/critical thresholds, quiet hours, optional sound, once-per-cycle silence, editable click-through reminder | **verified** — alert/reminder renders and cycle-dismiss application checks |
| Tray/menu bar | quota-aware icon, details, orb toggle, refresh, click-through, settings, help, restart and exit | **verified** — menu construction/state tests; native host smoke on Windows |
| Keyboard | global recovery shortcut to disable click-through or restore the orb | **hosted verified** — registration result passed Windows and macOS platform mutation checks |
| Data tools | settings import/export, clear trend history, sanitized diagnostics, restore defaults | **verified** — settings/data tool checks and rendered data/about page |
| Updates | startup/manual GitHub prerelease-aware check with same-version suppression and no automatic executable download | **verified** — version comparison/cache/no-download application checks |
| Recovery | atomic settings, backup recovery, normal-state restart, single-instance/restart behavior | **verified** — corrupt-primary backup recovery, single-instance and restart checks |

## Platform and packaging parity

| Area | Windows | macOS |
| --- | --- | --- |
| topmost/click-through | **verified** with native window styles | **hosted verified** platform adapter; real-device interaction pending |
| startup integration | **hosted verified** | **hosted verified** |
| tray/menu bar | **verified** locally | **hosted verified** construction/package; real-device interaction pending |
| global recovery shortcut | **hosted verified** no-conflict registration | **hosted verified** no-conflict registration |
| multi-display and DPI | **verified** at 100/150/200% and negative-coordinate logic | **hosted verified** 1x/2x rendering; real Retina device pending |
| preserved settings migration | **verified** through legacy and schema migration checks | **hosted verified** platform-neutral store |
| native package | Windows Setup/MSI/portable produced from one payload; administrative extraction/hash verified | **hosted verified** `.app`/ZIP/DMG, signature structure, integrity and mount contents |

## Release evidence

- **Local complete:** 21 domain, 10 application, 43 infrastructure, 4 Windows platform, and 37 UI scenarios.
- **Local complete:** real-user-environment App Server quota read; isolated JSONL first-index worker; single-instance and no-residual-child smoke.
- **Local complete:** Windows 100/150/200% render matrix, settings large-font render, native topmost/click-through and open/collapse placement smoke.
- **Local complete:** 15-minute active refresh/index smoke with one UI instance, bounded memory, and no child process left after exit.
- **Local complete:** one self-contained payload reused by portable/MSI/Setup; MSI administrative extraction reproduces the exact payload hash.
- **Hosted complete:** Windows/macOS build, logic, platform mutation and UI matrix on commit `e8a3caf` (Actions `32950339033`).
- **Hosted complete:** macOS `.app`, ZIP and DMG signature/integrity/mount-content verification (Actions `32950520588`).
- **Candidate-install gate:** clean Windows install/upgrade/uninstall and settings retention must be exercised in an isolated Windows environment so the installed formal release is not disturbed.
- **Public-release gate:** real macOS Retina behavior, Developer ID signing and notarization remain device/account requirements and are not implied by hosted CI.
