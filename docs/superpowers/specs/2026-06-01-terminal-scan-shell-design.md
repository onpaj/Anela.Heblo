# Handoff: Terminal "Scan Shell"

> **Provenance & gap note (added 2026-06-01).** This file is the design handoff
> from the Claude design tool, placed verbatim. The bundle this README shipped in
> referenced supporting files — `spec.html` (the *authoritative* written spec:
> §5 TypeScript contracts, §8 per-workflow detail, §10 token table, §12 acceptance
> criteria) and a runnable prototype (`terminal.html`, `*.jsx`, `data.js`, `styles.css`).
> **Those files were NOT included in the delivered ZIP — only this README was.**
> The implementation plan (`docs/superpowers/plans/2026-06-01-terminal-scan-shell.md`)
> is therefore derived from this README plus the live codebase. Anywhere the plan
> says "confirm against spec.html", request the missing spec before finalizing the
> exact prop contract / acceptance wording.

## Overview
This bundle specifies a **shared UI skeleton for the Terminal module** — the touch-first, barcode-driven app that runs on the rugged handheld (≈5″ portrait, keyboard-wedge scanner). Today each Terminal workflow lays itself out, handles scanning, and shows feedback differently. The **Scan Shell** moves those concerns into one shared structure so all five workflows behave identically: a subject header at the top, a workflow body in the middle, an always-live scan strip and a single docked action at the bottom, and a full-bleed colour flash on every scan.

The goal of the implementation task is to **refactor the existing Terminal screens onto this shell** and **build the two unbuilt workflows** (Inventura, Identifikace šarže) on it.

## About the design files
The files in this bundle are **design references created in HTML/CSS/JS** — an interactive prototype plus a written spec. They are **not** production code to copy verbatim. The task is to **recreate the design inside the existing frontend codebase** (React + TypeScript + Tailwind, React Router, TanStack Query hooks) using its established patterns, components, and tokens.

Two reference documents, in priority order:

1. **`spec.html`** — the authoritative specification. Open it in a browser. It contains the architecture, the full component inventory (new / changed / retired, per file), TypeScript contracts, the keyboard-wedge focus model, the flash contract, a per-workflow breakdown, a design-token mapping to the existing Tailwind theme, a phased migration plan, acceptance criteria, and open questions. **Read this first.**
2. **`terminal.html`** — the runnable prototype that the spec describes. Open it to *see and feel* every pattern (scan flash, subject header, docked actions, bottom sheets, the wedge). It is the visual source of truth. Press the yellow SCAN buttons, type a code + Enter to simulate a scan, or tap the dark scan strip to pick a specific code (including error cases). A Tweaks panel (bottom-right) toggles layout variants — the chosen defaults are **grid home + docked actions**.

> The prototype is a self-contained React-via-Babel app for demonstration only. Do **not** lift its files into the repo. Reimplement the patterns with the codebase's real components and the live API hooks.

## Fidelity
**High-fidelity.** Colours, typography (Inter / JetBrains Mono), spacing, radii, and interactions are final and map directly onto the existing Tailwind theme (see the token table in `spec.html` §10). Recreate the UI to match, using the codebase's existing libraries (lucide-react icons, Tailwind tokens, `TransportBoxStateBadge`, etc.).

## Target codebase — what to touch
Paths are relative to `frontend/src/components/terminal/`.

| File | Action | Notes |
|---|---|---|
| `shell/` (new folder) | **add** | `ScanProvider.tsx`, `ScanShell.tsx`, `SubjectHeader.tsx`, `ScanStrip.tsx`, `DockedAction.tsx`, `FlashOverlay.tsx`, `BottomSheet.tsx`, `useScanScreen.ts`, `types.ts` |
| `TerminalLayout.tsx` | **change** | Keep the app-bar; wrap `<Outlet/>` in `<ScanProvider>`; mount `<FlashOverlay/>` + the wedge singleton here so they persist across routes; drop the `max-w-md` scroll container. |
| `TerminalHome.tsx` | **change** | 2-column grid tiles; remove `comingSoon` from stocktake & lot-id; add scan-first routing (state→workflow inference). |
| `TransportBoxCheck.tsx` | **change** | Render into `<ScanShell>`; body = existing detail tabs. |
| `TransportBoxReceive.tsx` | **change** | Render into `<ScanShell>`; split confirm/reject dock. |
| `box-fill/*` | **change** | Collapse the `scan → add-items` step machine into shell states; reuse `AmountEntrySheet` / `OverdraftSheet` via `BottomSheet`. Keep `useBoxFill` / `useSendBoxToTransit` logic. |
| Inventura screen | **add** | Net-new; replaces the `ComingSoonPage` stub at route `stocktake`. Needs a stocktake API (see Open questions). |
| Identifikace šarže screen | **add** | Net-new; replaces the `ComingSoonPage` stub at route `lot-identification`. Needs a lot-registration API. |
| `ScanInput.tsx` | **retire** | Its wedge logic moves into `ScanProvider` + `ScanStrip`. Remove after all workflows migrate. |
| `ComingSoonPage.tsx` | **retire** | Remove once both new screens ship. |

Reuse without change: `transport/box-detail/TransportBoxStateBadge.tsx` and `TransportBoxTypes.tsx` (`stateLabels`, `stateColors`) — `SubjectHeader` consumes them as-is. Keep `useScreenView` telemetry and `data-testid` conventions.

## The shell skeleton (zones)
Top → bottom, every workflow:

1. **App bar** — back-to-home, workflow title, connection status. *Persistent.*
2. **Subject header** — the box/lot "in hand": code + state badge + key facts. Empty-prompt before first scan.
3. **Body** — the **only** workflow-specific zone. Scrolls independently.
4. **Scan strip** — the persistent wedge surface: ready state → live typed characters + caret → last-code echo. *Persistent.*
5. **Docked action** — the single next step, thumb-zone. 1 button = full width, 2 = split. Optional FAB variant.
6. **Flash overlay** — full-bleed colour wash + glyph on every scan result. Non-blocking, auto-dismissing.

A workflow may only supply a **subject**, a **body**, **actions**, and a **scan handler** — never its own header, scan field, or feedback. That constraint is what enforces consistency. Full TypeScript prop contracts are in `spec.html` §5.

## Keyboard-wedge model (critical)
The handheld scanner is a keyboard wedge: it types the barcode into the focused field and appends a terminator. The shell must **always own a focused capture field**, except when a sheet needs the operator to type a quantity.

- **Singleton** hidden `<input inputMode="none">` in `ScanProvider` (not per-workflow). Captures keystrokes, never raises the soft keyboard, no caret.
- On the **terminator (Enter/Tab — confirm with DataWedge config)**, trim + uppercase the buffer, dispatch to the active screen's `onScan`, clear the field.
- **Refocus** on: mount, route change, blur (delayed), tap anywhere not on an input, plus a low-frequency safety interval.
- **Yield rule:** while a `BottomSheet` with an input is open, do **not** steal focus — reclaim on close. Guard every refocus by bailing if `document.activeElement` is another INPUT/TEXTAREA.
- Always `focus({ preventScroll: true })` — without it, focusing the off-screen field scrolls the shell and mis-positions overlays.
- **Hardware trigger:** if DataWedge is in keystroke-output mode with an Enter terminator, the physical trigger needs no app wiring — it types into the wedge. (If Intent output is used, add a listener in `ScanProvider` calling the same dispatch.)

## Flash feedback contract
Every `onScan` resolution ends in **exactly one** `flash()` call.

| Tone | Colour | Glyph | Use | Dwell |
|---|---|---|---|---|
| `ok` | Green `#10B981` | Check | resolved / added / confirmed | ~950 ms |
| `warn` | Amber `#F59E0B` | Alert | succeeded with caveat (overdraft, variance, wrong state) | ~950 ms |
| `err` | Red `#EF4444` | X | not found / invalid / rejected | ~1200 ms |

Non-blocking (`pointer-events:none`), one-at-a-time (new flash cancels the prior timer), `aria-live`. After fade, the scan strip keeps a quiet echo of the last code + tone. **Do not** gate the overlay's visibility on a CSS keyframe end-state (backgrounded tabs freeze animations) — render it visible by default, animate only as polish. This single `flash()` dispatch is the future attach point for audio/haptic cues.

## Workflows (summary — full detail in `spec.html` §8)
- **Kontrola boxu** *(refactor)* — subject = scanned box; body = Obsah / Historie tabs; optional "Naskenovat další" ghost dock.
- **Plnění boxu** *(refactor)* — no box → empty prompt; scan box → subject + "V boxu" list + tappable "Dostupné zásoby"; scanning a product opens `AmountEntrySheet` (stepper + quick chips); over-stock opens `OverdraftSheet` (add-with-negative / add-remaining, flash `warn`); primary dock "Odeslat do přepravy" disabled while empty.
- **Příjem boxu** *(refactor)* — subject + receivability banner + read-only contents; split dock "Zamítnout" / "Potvrdit příjem" (success, disabled when state ∉ {InTransit, Reserve, Quarantine}).
- **Inventura** *(net-new)* — session subject (counted/total); scan material lot → `CountSheet` (shows expected, computes signed variance live; flash `ok` if matches else `warn`); primary "Uložit inventuru".
- **Identifikace šarže** *(net-new)* — session subject (n registered); scan material → `RegisterSheet` (lot #, expiry, quantity); primary "Dokončit příjem".

## Home & scan-first routing
2-column grid of workflow tiles. The wedge is live on Home too — scanning a valid box infers the operation and navigates with the box preloaded:

| Scanned box state | Routes to |
|---|---|
| Receivable (InTransit / Reserve / Quarantine) | `receive` |
| Opened / New | `fill` |
| Anything else (Stocked, Closed…) | `check` |
| Unknown / invalid | stay on Home, `flash('err')` |

*(Validate this mapping with floor procedure before shipping.)*

## Design tokens
Reuse the existing Tailwind theme — see the full mapping table in `spec.html` §10. Already in the theme: `primary-blue` `#2563EB`, `secondary-blue-pale` `#EFF6FF`, `neutral-slate` `#0F172A`, `neutral-gray` `#64748B`, `border-light` `#E2E8F0`, `background-gray` `#F8FAFC`, `shadow-soft` / `shadow-hover`, `rounded-xl` (14px). **Add** a feedback scale (`success` `#10B981`/`#ECFDF5`, `warning` `#F59E0B`/`#FFFBEB`, `error` `#EF4444`/`#FEF2F2`) and a scan accent `#FACC15`. State-badge colours come from `stateColors` in `TransportBoxTypes.tsx` — no new tokens.

## Assets
No raster assets. Icons are lucide(-react) — already a dependency. Fonts: Inter (UI) + JetBrains Mono (codes), both Google Fonts.

## Migration plan (recommended order)
0. Foundation — `ScanProvider` + wedge singleton + `FlashOverlay` in `TerminalLayout` (no visual change).
1. Shell kit — build the `shell/` components in isolation.
2. Migrate **Kontrola boxu** (read-only, lowest risk).
3. Migrate **Příjem boxu** (introduces split dock + gating).
4. Migrate **Plnění boxu** (collapse step machine; sheet focus-yield).
5. **Home** grid + scan-first routing.
6. Build **Inventura** + **Identifikace šarže** (gated on backend APIs).
7. Cleanup — remove `ScanInput.tsx`, `ComingSoonPage.tsx`, dead step-machine code.

## Acceptance criteria
See `spec.html` §12 for the full checklist. Headlines: a focused capture field exists at all times and a typed code + terminator scans without tapping a field; focus returns to the wedge after every scan / blur / route change but yields to open sheet inputs; every scan produces exactly one correctly-toned flash that is non-blocking; all five workflows render zones A–E in identical positions; scanning a box on Home routes per the state map.

## Open questions (resolve with the team)
1. **Scanner terminator** — confirm DataWedge keystroke-output mode and terminator (Enter vs Tab).
2. **Backend APIs** for stocktake (materials-by-lot, expected qty, submit) and lot registration — do they exist? They gate Phase 6.
3. **Scan-first mapping** — validate the state→workflow precedence with floor procedure.
4. **Audio / haptic** — v1 or deferred to the post-flash hook point?
5. **"Naskenovat další"** — explicit reset button, or is the always-ready wedge enough?
6. **Offline / queueing** — any requirement to buffer scans/submissions on connectivity loss?
7. **Naming** — align proposed component/prop names with existing conventions.

## Files in this bundle
- `spec.html` + `spec.css` — the authoritative written specification (open `spec.html`).
- `terminal.html` — runnable interactive prototype (visual source of truth).
- `data.js`, `icons.jsx`, `shell.jsx`, `home.jsx`, `wf-box.jsx`, `wf-material.jsx`, `app.jsx`, `styles.css`, `tweaks-panel.jsx` — the prototype's supporting files (reference only; do not port).
