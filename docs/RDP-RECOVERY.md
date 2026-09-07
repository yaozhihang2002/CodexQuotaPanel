# Windows RDP display recovery

Scope: v0.6.5 same-version RDP and visible-edge update. The sections below retain the local test evidence and its limits; see the v0.6.5 release notes for distribution details.

## Failure evidence and scope

The reported RDP failure had a native frame wider than the painted client content, with buttons failing at their visible positions. An earlier native probe found different DPI values for the retained, offscreen detail window and the orb. A later probe found consistent values after the problem disappeared. This is evidence of a display/session transition problem, but not a captured proof of the exact compositor failure.

The existing layout refresh retained the same native detail window. The new Windows-only recovery also invalidates that native surface, without restarting the quota/data coordinator.

## Recovery behavior

- A hidden top-level native monitor subscribes to current-session WTS notifications and display/work-area broadcasts.
- Disconnect/lock invalidates the detail surface; recovery waits for reconnect/unlock. A 750 ms debounce coalesces bursts and waits for open/collapse transitions to finish.
- A visible detail window is replaced after its replacement is prepared, with no additional entrance zoom. Quota presentation, application process, settings, and orb restore anchor remain in memory.
- A hidden invalid detail window is discarded. During RDP, the detail surface is also released after collapse and recreated on open, instead of retaining an offscreen HWND across sessions.
- A two-second display fingerprint/DPI check covers missed broadcasts. Native/render DPI mismatch recovery is rate-limited to 30 seconds.
- Recovery is automatic; no extra tray menu item is added.
- Local `display-recovery.log` records HWND, native/client dimensions, scaling, event reasons, and pointer positions inside the panel. It contains no quota amounts, tokens, account identifiers, or credentials, and rotates at 256 KiB with one retained backup.

## Verification, 2026-09-07

- Release build: zero warnings and errors.
- Domain 43, Application 18, Infrastructure 59, Windows Platform 12 checks passed.
- UI render matrix: 40 scenarios passed, including display sizing and persistent-window lifecycle. Recovery presentation explicitly resets its scale to 1 without an entrance animation.
- Final packaged Windows executable: three rounds of synthetic disconnect/reconnect and display broadcasts passed inside an actual RDP session at 100% DPI. Each round destroyed the prior HWND, retained the process and fixture quota, kept the orb position/restore anchor, and handled native mouse coordinates for collapse and orb reopening.
- Native capture of the isolated window showed a complete frame, client content, and bottom buttons.
- The native smoke harness is `tools/Test-RdpRecovery.ps1`. It refuses to target a process without `--isolated-smoke`; it sends notifications to that process only, never changes the OS session, and does not move the actual mouse.

Still required: a real RDP disconnect/reconnect between different display scaling configurations, including return to the local console. Synthetic notification tests do not validate graphics-device loss or every compositor transition. No macOS runtime test was performed for this Windows-only change.

## Local test build

The local portable test executable retains the numeric version 0.6.5 and has informational version `0.6.5-rdp-recovery-test`. It uses the existing .NET 10 runtime installed with the application. Exit the old application once before launching it, to avoid the single-instance gate activating the old process. Do not publish this test archive as a release asset.

## Visible-edge follow-up

The subsequent local `0.6.5-visible-edge-test` also fixes the gap caused by including invisible Windows resize borders in screen-edge placement. DWM visible bounds and native bounds are read in physical-pixel coordinates, with per-thread DPI context restored after measurement. Missing or stale measurements fall back to zero compensation rather than a hard-coded offset. Non-Windows placement retains zero compensation.

All three placement paths (open near orb, restore position, display recovery) use the same visible-edge constraints. Native window preparation now tracks actual completion instead of inferring it from HWND existence: Avalonia can allocate an HWND before Show. Reconstruction selects the monitor from the whole prior window, not its top-left invisible border, and restores position again after native preparation.

Verification: zero-warning solution build; 40 UI scenarios passed, including 100/125/150/200% physical-pixel inset geometry and negative monitor origins; Windows Platform 14 checks passed. `tools/Test-VisibleEdges.ps1` tested the packaged executable through five actual native window placements at 96 DPI: right, left, bottom-right, bottom-left, and right again. Every visible tested edge had a zero-pixel gap. Each placement also received a synthetic display change, rebuilt its HWND without changing its visible placement/monitor, and collapsed by native button coordinates without moving the orb. This is not a claim of real cross-DPI RDP reconnection validation.
