# Specification: Dark-mode support for Packing Statistics (Baleni)

## Summary
Add Graphite dark-mode styling to the three files that make up the packing-statistics screen — `BaleniStatistics.tsx`, `PackingCharts.tsx`, and `PackingHourHeatmap.tsx` — which currently contain zero `dark:` Tailwind variants and render as white-on-white/near-invisible when the app is in dark mode. This is a frontend-only, purely visual fix: no logic, props, data flow, or test files change.

## Background
ADR-006 (`docs/architecture/development_guidelines.md`, Accepted 2026-06-25) requires every frontend component that renders color to render correctly in both light and dark ("Graphite") mode, additively (`dark:` variants alongside existing light classes, never replacing them). The Baleni module has already applied this consistently everywhere except the `statistics/` subtree — sibling files in the same module have 1–19 `dark:` occurrences each (e.g. `BaleniHome.tsx`: 19, `PackingShipmentCreator.tsx`: 13, `ZasilkyFilters.tsx`: 7), confirming the established, additive-class convention this fix must follow rather than introducing a new pattern.

The Baleni screens run on a warehouse-floor kiosk where dark mode is a persisted (`localStorage`), first-class mode. Today the statistics page renders white `Panel`/`KpiCard` surfaces with light-gray text on the app's dark background (failing WCAG 2.1 AA contrast), the Recharts grid/axis strokes (`#f0f0f0`, `#6b7280`) are near-invisible against a dark surface, and the heatmap's empty-cell color falls back to an undefined CSS custom property (`--heatmap-empty`, never defined in `frontend/src/index.css` or the Tailwind config) which always resolves to its light-only fallback `#f1f5f9`, making empty and "some activity" cells indistinguishable from the dark background.

A directly applicable precedent already exists in the codebase: `frontend/src/components/charts/BankStatementImportChart.tsx` (closed issue #3761) solves the identical "Recharts colors must be theme-aware" problem using `useTheme()` from `frontend/src/contexts/ThemeContext.tsx` plus the `GRAPHITE` color-token object exported from `frontend/src/components/common/reactSelectDarkStyles.ts`. This fix should follow that same pattern for consistency.

## Functional Requirements

### FR-1: `BaleniStatistics.tsx` — theme the static/JSX surfaces
Add `dark:` variants to every raw Tailwind color utility in this file per the mapping table in `docs/design/dark-mode-conversion-guide.md`, without altering structure, logic, or text. Specifically:

- `Panel` (lines ~27–39): `bg-white` → `+ dark:bg-graphite-surface`; `border-border-light` → `+ dark:border-graphite-border`; `shadow-soft` → `+ dark:shadow-soft-dark`; `text-neutral-slate` (title) → `+ dark:text-graphite-text`; `text-neutral-gray` (subtitle) → `+ dark:text-graphite-muted`.
- `KpiCard` (lines ~41–54): same surface/border/shadow treatment as `Panel`; `text-neutral-gray` (label) → `+ dark:text-graphite-muted`; loading-pulse `bg-secondary-blue-pale` → `+ dark:bg-graphite-surface-2`; value `text-primary-blue` → `+ dark:text-graphite-accent`.
- Error state block (lines ~78–99): `bg-red-50`/`border-red-200` → status-pill dark mapping (`dark:bg-red-900/30`-equivalent surface, keep semantic red hue per the guide's status section — e.g. add `dark:bg-red-950/30 dark:border-red-900/50`); `text-red-600`/`text-red-800`/`text-red-700` → `dark:text-red-400`; retry button `bg-red-100 text-red-800 hover:bg-red-200` → add matching dark pill variants (`dark:bg-red-900/30 dark:text-red-300 dark:hover:bg-red-900/50`).
- Header (lines ~110–118): `text-neutral-slate` (h1) → `+ dark:text-graphite-text`; `text-primary-blue` icon → `+ dark:text-graphite-accent`; date-range subtitle `text-neutral-gray` → `+ dark:text-graphite-muted`.
- Range-preset buttons (lines ~122–134): active branch (`bg-secondary-blue-pale border-primary-blue text-primary-blue`) → add `dark:bg-graphite-accent/10 dark:border-graphite-accent dark:text-graphite-accent`; inactive branch (`bg-white border-border-light text-neutral-gray hover:bg-secondary-blue-pale`) → add `dark:bg-graphite-surface dark:border-graphite-border dark:text-graphite-muted dark:hover:bg-white/5`. Both ternary branches must be updated consistently (guide rule 4).
- Refresh button (lines ~135–142): `border-border-light text-neutral-gray hover:bg-secondary-blue-pale` → add `dark:border-graphite-border dark:text-graphite-muted dark:hover:bg-white/5`.
- KPI grid loading placeholder for cards uses `KpiCard`'s own `loading` branch — already covered above.
- Full-page loading state (lines ~172–178): `bg-white border border-border-light shadow-soft` → add `dark:bg-graphite-surface dark:border-graphite-border dark:shadow-soft-dark`; `text-primary-blue` spinner icon → `+ dark:text-graphite-accent`; `text-neutral-gray` label → `+ dark:text-graphite-muted`.

**Acceptance criteria:**
- Every `className` string in `BaleniStatistics.tsx` that sets a background, text, border, or shadow color has a corresponding `dark:` sibling per the mapping table (or already relies on a themed design-system class).
- No light-mode class is removed, reordered, or altered; only `dark:` classes are appended.
- No change to component props, state, hooks, conditional logic, or rendered text/markup structure.
- With the `dark` class present on `<html>` (Graphite theme active), the statistics page (KPI cards, panels, header, buttons, error and loading states) visually matches the Graphite palette used elsewhere in the Baleni module (verified by toggling `ThemeToggle` or via the Playwright dev instance per ADR-006's verification note).
- Existing `frontend/src/components/baleni/statistics/__tests__/BaleniStatistics.test.tsx` continues to pass unmodified (it asserts on text content and structure, not class strings).

### FR-2: `PackingCharts.tsx` — theme-aware Recharts colors
Recharts `stroke`/`fill` props are plain SVG attributes, not Tailwind classes, so they cannot take `dark:` variants (per dark-mode-conversion-guide.md rule 8, "leave chart library colors alone unless trivially a className" — this case is *not* trivial and needs runtime theme resolution, matching the precedent in `BankStatementImportChart.tsx`). Apply the same pattern:

- Import `useTheme` from `../../../contexts/ThemeContext` and derive `isDark = theme === "dark"` in each exported chart component (`ThroughputChart`, `CarrierMixChart`, `PackerLeaderboard`, `PackagesPerOrderChart`).
- Import the `GRAPHITE` token object from `../../common/reactSelectDarkStyles` (or a local equivalent) for grid/axis colors, matching the values already used by `BankStatementImportChart.tsx`:
  - `CartesianGrid stroke="#f0f0f0"` → `stroke={isDark ? GRAPHITE.border : "#f0f0f0"}` (lines 93, 165, 192).
  - `XAxis`/`YAxis stroke="#6b7280"` → `stroke={isDark ? GRAPHITE.muted : "#6b7280"}` (lines 94–95, 166, 172, 194, 199).
- Bar/Pie fill colors need a dark-mode-legible counterpart where the current hex would have insufficient contrast or clash with the dark surface. At minimum:
  - `Bar dataKey="packageCount" fill="#2563eb"` (line 106, throughput chart) and `Bar dataKey="orderCount" fill="#2563eb"` (line 175, packer leaderboard) → keep `#2563eb` in light mode; use `GRAPHITE.accent` (`#38BDF8`) in dark mode for better contrast against the dark surface, consistent with how `text-primary-blue` maps to `dark:text-graphite-accent` elsewhere in this module.
  - Secondary bars (`fill="#93c5fd"` line 107, `fill="#0ea5e9"` line 201) and the `CARRIER_COLORS`/`OTHER_COLOR` palette (lines 24, 32) may keep their existing hues if they already meet WCAG AA against the dark surface (`#202327`/`graphite-surface`); if contrast is insufficient, apply the same `isDark ? … : …` pattern. Verify each color's contrast against both `#FFFFFF`/`bg-white` (light) and `#202327`/`graphite-surface` (dark) as part of implementation, not assumed.
- Recharts `Tooltip` and `Legend` default styling (white background, dark text) is not addressed by props in this file today — confirm whether Recharts' default tooltip/legend already inherits acceptable contrast in dark mode (it typically renders a white box with a light border by default and will look like a light "flash" against the dark page, same defect class `BankStatementImportChart.tsx` fixed with a `content={<CustomTooltip />}` override). If default Tooltip/Legend contrast is inadequate in dark mode, add theme-aware `contentStyle`/`itemStyle`/`wrapperStyle` props (Recharts supports styling `Tooltip` via `contentStyle`/`itemStyle`/`labelStyle` without a full custom component) rather than a full custom tooltip component, to keep the change minimal.
- `EmptyState` (line 25–27): `text-neutral-gray` → add `dark:text-graphite-muted`.

**Acceptance criteria:**
- All four exported chart components read `theme` via `useTheme()` and select stroke/fill colors accordingly; no chart hardcodes a single light-only hex for grid lines or axis strokes.
- `CartesianGrid` and `XAxis`/`YAxis` strokes are visibly distinct from the dark background (`graphite-surface`/`graphite-bg`) when the Graphite theme is active — verified visually.
- Primary bar/line fill colors meet WCAG 2.1 AA contrast against `graphite-surface` in dark mode.
- Recharts default `Tooltip`/`Legend` chrome does not render as a stark white box against the dark page background when the Graphite theme is active.
- `buildCarrierSlices` and `sliceColor` pure functions are untouched (no behavior change, only the color values `sliceColor` returns may become theme-conditional).
- Existing `frontend/src/components/baleni/statistics/__tests__/PackingCharts.test.tsx` continues to pass unmodified.

### FR-3: `PackingHourHeatmap.tsx` — theme labels and fix the empty-cell color
- Header row hour labels (line ~60) and weekday labels (line ~71): `text-neutral-gray` → add `dark:text-graphite-muted`.
- Empty-state message (line ~49): `text-neutral-gray` → add `dark:text-graphite-muted`.
- Empty-cell background (lines 80–84): remove the dependency on the undefined `--heatmap-empty` custom property. Two acceptable approaches (implementer's choice, pick one and apply consistently):
  1. **Theme-aware inline color** (matches the `PackingCharts.tsx` pattern): import `useTheme`, and set the empty-cell color to `#f1f5f9` in light mode / a Graphite-appropriate near-surface tone (e.g. `GRAPHITE.surface2` or `GRAPHITE.hover`, `#272A30`/`#2E323A`) in dark mode, replacing the `var(--heatmap-empty, #f1f5f9)` fallback chain entirely.
  2. **Define the CSS custom property for both themes** in `frontend/src/index.css` (e.g. `:root { --heatmap-empty: #f1f5f9; }` and `.dark { --heatmap-empty: #272A30; }`), keeping the component's `var(--heatmap-empty, #f1f5f9)` reference as-is.
  Approach 1 is preferred for consistency with FR-2's pattern and to avoid introducing a new, single-use CSS variable convention not otherwise used in `index.css` (repo search found no other custom properties defined there); note the choice in the PR description if approach 2 is used instead.
- The intensity-scaled occupied-cell color (`rgba(37, 99, 235, ${0.15 + intensity * 0.85})`, line 84) uses the same blue (`#2563eb` = `rgb(37,99,235)`) in both themes. Confirm this reads correctly at both low and high intensity against the dark page background; if the low-intensity end (`alpha 0.15`) becomes indistinguishable from the new dark empty-cell color, adjust the dark-mode alpha floor or base hue (e.g. use `GRAPHITE.accent` as the RGB base in dark mode) so the low/high activity gradient stays perceptible — this is a visual judgment call to make during implementation, not a fixed formula.
- The `<td>`/`<th>` cell borders and spacing (`border-separate border-spacing-1`, `rounded`) are structural, not color, and need no change.

**Acceptance criteria:**
- No reference to the undefined `--heatmap-empty` custom property remains unresolved in dark mode — the empty-cell color is deterministic (not falling through to a light-only default) in both themes.
- Empty cells are visually distinguishable from occupied cells (all intensities) in both light and dark mode.
- Weekday/hour labels and the empty-data message are legible (WCAG AA) against the dark page background.
- No change to `cellKey`, `counts`/`maxCount`/`fromHour`/`toHour` memoized logic, or the component's props/data contract.
- Existing tests referencing `data-testid="packing-hour-heatmap"` (if any) continue to pass unmodified.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance impact expected — this is a styling-only change (added CSS classes and a `useTheme()` context read already used elsewhere in charts). No additional network calls, no new re-render triggers beyond the existing theme-context subscription pattern already proven in `BankStatementImportChart.tsx`.

### NFR-2: Security
Not applicable — no auth, data handling, or backend surface is touched. This is a pure frontend presentational change.

### NFR-3: Accessibility
Both light and dark renderings must meet WCAG 2.1 AA contrast ratios (4.5:1 for normal text, 3:1 for large text/graphical objects such as chart strokes and heatmap cells) per ADR-006. This must be spot-checked during implementation (e.g. browser devtools contrast checker) for: KPI card values/labels, panel titles/subtitles, chart axis/grid strokes, heatmap empty vs. low-intensity vs. high-intensity cells, and the error-state text.

### NFR-4: Consistency with existing module conventions
Follow the additive `dark:` utility-class pattern already used throughout `frontend/src/components/baleni/*` (e.g. `BaleniHome.tsx`) rather than introducing the `.card`/`.badge-*` design-system shorthand classes — the module has an established convention and this fix should match it, not diverge into a second pattern within the same module. For the two Recharts files, follow the `useTheme()` + `GRAPHITE` token pattern already established in `frontend/src/components/charts/BankStatementImportChart.tsx`.

## Data Model
None — this is a presentational-only change. No entities, DTOs, or API contracts are affected.

## API / Interface Design
No API changes. No new props are added to any of the three components or their exported sub-components (`ThroughputChart`, `CarrierMixChart`, `PackerLeaderboard`, `PackagesPerOrderChart`, `PackingHourHeatmap`) — all theme resolution happens internally via the existing `useTheme()` hook from `ThemeContext`, matching how `BankStatementImportChart.tsx` consumes it without adding a `theme` prop.

## Dependencies
- `frontend/src/contexts/ThemeContext.tsx` — existing `useTheme()` hook (no changes needed).
- `frontend/src/components/common/reactSelectDarkStyles.ts` — existing `GRAPHITE` color-token export, to be imported (not modified) by `PackingCharts.tsx` and optionally `PackingHourHeatmap.tsx`.
- `docs/design/dark-mode-conversion-guide.md` — mapping table and rules to follow for all raw-utility-class changes.
- `frontend/tailwind.config.js` — existing `graphite-*` color scale (no changes needed; all required tokens already exist).
- No new npm packages, no backend changes, no database/migration involvement.

## Out of Scope
- Any backend, API, or DTO changes — this is a frontend-only fix as directed by the brief.
- Adding a lint/CI rule to catch future light-only color utilities (referenced as a "recommended follow-up, not yet implemented" in ADR-006, but not part of this fix).
- Restructuring `Panel`/`KpiCard` into shared design-system components (`.card`, etc.) — out of scope per NFR-4; only additive `dark:` classes are in scope.
- Changes to any other Baleni module file not named in the brief (`BaleniHome.tsx`, `PackingShipmentCreator.tsx`, `zasilky/*`, etc. already have dark-mode support and are unaffected).
- Changes to test files (`__tests__/*.test.tsx`) per the dark-mode-conversion-guide's rule 7 — existing tests must pass without modification; if a test incidentally breaks due to a class-string assertion, that is a signal to reconsider the specific class change, not to edit the test.
- Any new heatmap color-intensity algorithm redesign beyond what's needed to keep the existing scale perceptible in dark mode (FR-3's alpha/base-hue adjustment is a minimal tweak, not a redesign).

## Open Questions
None.

## Status: COMPLETE
