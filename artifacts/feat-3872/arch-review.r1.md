# Architecture Review: Dark-mode support for Packing Statistics (Baleni)

## Skip Design: false

## Architectural Fit Assessment

This is a pure retrofit of an established, actively-enforced pattern (ADR-006) onto three files that were missed when the rest of the `baleni` module was converted. It introduces no new architecture: the target files already sit inside the existing component tree (`frontend/src/components/baleni/statistics/`), consume the existing `ThemeContext` (`useTheme()`), and can reuse the existing `GRAPHITE` token object from `frontend/src/components/common/reactSelectDarkStyles.ts` that `BankStatementImportChart.tsx` already established as the cross-cutting precedent for "Recharts + dark mode."

Two integration points matter, and both already exist:

1. **Tailwind utility layer** — `BaleniStatistics.tsx`'s `Panel`/`KpiCard` are structurally identical (same props, same layout) to the `KpiCard` already themed in `frontend/src/components/baleni/BaleniHome.tsx` (verified: `bg-white dark:bg-graphite-surface border border-border-light dark:border-graphite-border rounded-xl p-6 shadow-soft dark:shadow-soft-dark`, etc., at `BaleniHome.tsx:45-53`). This is not just "a pattern to follow" — it is close to a literal copy-paste source for the class strings needed.
2. **Recharts theming layer** — `BankStatementImportChart.tsx:44-53` defines the canonical shape: `useTheme()` → `isDark` boolean → a local `colors` object keyed by chart concern (`grid`, `axis`, …) resolved from `GRAPHITE.*` tokens or hardcoded dark-safe hex, referenced directly in `stroke=`/`fill=` JSX props. `PackingCharts.tsx` should mechanically apply the same shape to its four exported chart components.

No new libraries, no new context, no new design tokens beyond one already-supported gap (`--heatmap-empty`, addressed below). Skip Design is `false` only in the narrow sense that this issue *is* itself design work (color mapping for existing components) — it introduces zero new UI structure, screens, or components.

## Proposed Architecture

### Component Overview

```
BaleniStatistics.tsx (container)
 ├─ Panel                     [local, unexported]  → Tailwind dark: variants only
 ├─ KpiCard                   [local, unexported]  → Tailwind dark: variants only
 ├─ error / loading states    [inline JSX]          → Tailwind dark: variants only
 ├─ PackingHourHeatmap.tsx    [default export]      → useTheme() + inline style resolution
 └─ PackingCharts.tsx
     ├─ ThroughputChart       [named export]  ┐
     ├─ CarrierMixChart       [named export]  │  each: useTheme() → isDark →
     ├─ PackerLeaderboard     [named export]  │  local `colors` object → stroke/fill props
     └─ PackagesPerOrderChart [named export]  ┘

Shared dependency (read-only, not modified):
  frontend/src/contexts/ThemeContext.tsx        → useTheme()
  frontend/src/components/common/reactSelectDarkStyles.ts → GRAPHITE token object
```

No component boundaries change. No new files are required — everything is edited in place across the three named files (plus the two dependency files, imported not modified).

### Key Design Decisions

#### Decision 1: Where do Recharts colors get resolved — per-chart-component `useTheme()` calls, or a single hook lifted to `PackingCharts.tsx` module scope / `BaleniStatistics.tsx`?

**Options considered:**
- (a) Call `useTheme()` once in `BaleniStatistics.tsx` and pass `isDark`/`colors` down as props to each chart component and to `PackingHourHeatmap`.
- (b) Call `useTheme()` independently inside each of the four exported chart components in `PackingCharts.tsx`, and independently again inside `PackingHourHeatmap.tsx`.

**Chosen approach:** (b) — independent `useTheme()` calls per component, exactly mirroring `BankStatementImportChart.tsx`, which calls it once inside a single top-level chart component (there is only one there) rather than threading it from a parent.

**Rationale:** The spec's own API/Interface Design section is explicit: *"No new props are added to any of the three components or their exported sub-components... all theme resolution happens internally via the existing `useTheme()` hook."* This also keeps `ThroughputChart`, `CarrierMixChart`, `PackerLeaderboard`, and `PackagesPerOrderChart` independently usable/testable without a prop-drilling dependency on their parent, consistent with how `BankStatementImportChart.tsx` is consumed as a self-contained unit elsewhere. `useTheme()` reads from React context, so four calls in four sibling components cost nothing extra beyond four context reads — no measurable overhead, no prop-drilling churn if a chart is later reused elsewhere.

#### Decision 2: How to eliminate the broken `--heatmap-empty` custom property

**Options considered:**
1. Theme-aware inline color via `useTheme()` in `PackingHourHeatmap.tsx`, replacing the `var(--heatmap-empty, #f1f5f9)` reference entirely.
2. Define `--heatmap-empty` for both `:root` and `.dark` in `frontend/src/index.css`.

**Chosen approach:** Option 1 (theme-aware inline color, same pattern as Decision 1).

**Rationale:** A `grep -rn "heatmap-empty" frontend/src/` confirms this custom property is referenced in exactly one place (`PackingHourHeatmap.tsx:83`) and defined nowhere. Introducing a single-use CSS custom property convention in `index.css` for one component, when the rest of the codebase's dark-mode story is 100% JS/Tailwind-class-driven (`ThemeContext` + `dark:` classes + the `GRAPHITE` object for inline styles), would be a second, inconsistent mechanism for exactly the same problem `PackingCharts.tsx` solves one file away. Keeping everything on the `useTheme()` + inline-style pattern means a future reader of this subtree sees one mechanism, not two.

#### Decision 3: Should the color-map object (`GRAPHITE`) be imported directly by `PackingCharts.tsx`/`PackingHourHeatmap.tsx`, or should a `baleni`-local copy be made?

**Options considered:**
- (a) Import `GRAPHITE` directly from `frontend/src/components/common/reactSelectDarkStyles.ts`.
- (b) Duplicate the token values locally in `PackingCharts.tsx` (as `BankStatementImportChart.tsx` does for two colors not in `GRAPHITE`: `'#f87171'` threshold, `'#e0f2fe'`/`'#0ea5e9'` weekend fill).

**Chosen approach:** (a) — import `GRAPHITE` directly for all values it already covers (`border`, `muted`, `accent`, `surface`, `surface-2`, `hover`). Only introduce a new inline hex value (not in `GRAPHITE`) for colors `GRAPHITE` doesn't define, following `BankStatementImportChart.tsx`'s own precedent of mixing `GRAPHITE.*` tokens with a few local hardcoded dark-safe hex values where no token exists.

**Rationale:** `reactSelectDarkStyles.ts` already exports `GRAPHITE` as a public, reusable token object (its docstring: "mirrors tailwind.config.js `graphite` scale") consumed outside its original react-select context by `BankStatementImportChart.tsx`. This is exactly the reuse the spec calls for; introducing a second copy of the same six-color object would create silent drift risk (someone updates one copy and not the other) for zero benefit. Do **not** rename or move `reactSelectDarkStyles.ts` — despite the generic `GRAPHITE` name being a slightly awkward fit for a file named after react-select, renaming is out of scope and would touch its existing react-select consumers unnecessarily.

## Implementation Guidance

### Directory / Module Structure

No new files, no new directories. All work happens in-place in:
- `frontend/src/components/baleni/statistics/BaleniStatistics.tsx`
- `frontend/src/components/baleni/statistics/PackingCharts.tsx`
- `frontend/src/components/baleni/statistics/PackingHourHeatmap.tsx`

New imports only (no new dependencies to `package.json` — both are existing internal modules):
```ts
// PackingCharts.tsx and PackingHourHeatmap.tsx
import { useTheme } from "../../../contexts/ThemeContext";
import { GRAPHITE } from "../../common/reactSelectDarkStyles";
```
(Relative path depth verified: `statistics/` is 3 levels below `src/`, matching the `../../../contexts` / `../../common` used by sibling `baleni/*` files.)

### Interfaces and Contracts

No public interface changes. Explicitly preserved, per the spec and verified against source:
- `PackingHourHeatmapProps { data: HourBucket[] }` — unchanged.
- `ThroughputChart`, `CarrierMixChart`, `PackerLeaderboard`, `PackagesPerOrderChart` prop signatures — unchanged (each takes only its `data` prop today; no `theme` prop is added).
- `buildCarrierSlices(data): CarrierSlice[]` and `sliceColor(slice, index): string` — pure functions, signatures unchanged; `sliceColor`'s *return value* may become theme-conditional only if `CARRIER_COLORS`/`OTHER_COLOR` turn out to fail contrast in dark mode (see NFR-3 below) — this requires `sliceColor` to close over `isDark` or take it as an argument, which is an internal implementation change, not a contract change, since it's not exported for direct testing outside `buildCarrierSlices`. Note: `sliceColor` is not currently exported — confirm during implementation whether it needs to become theme-parameterized at all; §PackingCharts.tsx bar/pie colors below defaults to "leave palette as-is unless contrast fails."
- `cellKey`, `counts`/`maxCount`/`fromHour`/`toHour` memoized values in `PackingHourHeatmap.tsx` — untouched; only the `style={{ backgroundColor: ... }}` expression at line 80-85 changes.

### Data Flow

No data flow changes — this is presentation-only. The only new runtime read is `useTheme()` (existing `ThemeContext`, already subscribed to `<html class="dark">` toggling elsewhere in the app), invoked independently in each of: `ThroughputChart`, `CarrierMixChart`, `PackerLeaderboard`, `PackagesPerOrderChart`, `PackingHourHeatmap`. `BaleniStatistics.tsx` itself does **not** need `useTheme()` — its color changes are pure Tailwind `dark:` class additions resolved by the CSS cascade via the `.dark` class already on `<html>`, with zero JS-level theme awareness required (this matches how `Panel`/`KpiCard`'s sibling in `BaleniHome.tsx` is themed, with no `useTheme()` import at all in that file for its Tailwind-only parts).

Concretely, per file:

- **`BaleniStatistics.tsx`**: no new hooks. Every `className` template literal or string gets `dark:` siblings appended per the mapping table in `docs/design/dark-mode-conversion-guide.md`, exactly matching `BaleniHome.tsx`'s existing `KpiCard`. The ternary at lines 126-130 (active/inactive range-preset button) must get `dark:` added to **both** branches per guide rule 4 — this is the one place in the file most likely to be done inconsistently by a mechanical find-replace.
- **`PackingCharts.tsx`**: `const { theme } = useTheme(); const isDark = theme === "dark";` added at the top of each of the 4 exported components; a local `colors` object (or direct `isDark ? GRAPHITE.x : "#hex"` ternaries inline, as `BankStatementImportChart.tsx` does via its `colors` object) resolves `CartesianGrid stroke`, `XAxis`/`YAxis stroke`, and primary `Bar fill` values. `EmptyState` gets one `dark:` class appended (`text-neutral-gray dark:text-graphite-muted`).
- **`PackingHourHeatmap.tsx`**: `useTheme()` added; the `backgroundColor` ternary at line 80-85 changes from `var(--heatmap-empty, #f1f5f9)` to an explicit `isDark ? <dark-tone> : "#f1f5f9"`; header/weekday `<th>`/`<td>` labels and the empty-state message get `dark:text-graphite-muted` appended.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Ternary/branch inconsistency: only one branch of a conditional className (e.g. the active/inactive range-preset buttons in `BaleniStatistics.tsx:126-130`, or the empty/occupied heatmap cell branch) gets a `dark:` variant, leaving a half-themed control | Medium | Guide rule 4 already calls this out explicitly. Implementer should grep the diff for every `? "..." : "..."` / `isDark ? ... : ...` after editing and confirm both sides were touched. |
| Chart bar/pie/carrier colors (`#2563eb`, `#93c5fd`, `#0ea5e9`, `CARRIER_COLORS`, `OTHER_COLOR`) may fail WCAG AA against `graphite-surface` (`#202327`) even though they're fine against white — spec explicitly defers this judgment to implementation ("Verify each color's contrast... not assumed") | Medium | Do the contrast check with a real tool during implementation (browser devtools contrast checker, or a contrast-ratio script) against `#202327` before merging, not just visually. If any fail, swap to `GRAPHITE.accent` (`#38BDF8`) or another already-defined dark-safe token rather than inventing new hex values. |
| Recharts default `Tooltip`/`Legend` chrome (white box, dark text) renders as a bright flash against the dark page if left unstyled — `BankStatementImportChart.tsx` solved this with a full `CustomTooltip` component, which is more than this fix's minimal-change goal calls for | Low-Medium | Spec already prefers the lighter-weight `contentStyle`/`itemStyle`/`labelStyle` props over a full custom tooltip component "to keep the change minimal" — follow that; only escalate to a custom tooltip component if `contentStyle` alone can't achieve adequate contrast (e.g. because default text color isn't overridable via those props for all sub-elements). |
| `PackingHourHeatmap`'s intensity-scaled occupied-cell color (`rgba(37, 99, 235, ${0.15 + intensity*0.85})`) may become visually indistinguishable from the new dark empty-cell tone at low intensity, defeating the heatmap's purpose | Medium | Spec flags this as a judgment call, not a fixed formula. Implementer should visually verify the full intensity gradient (0%, ~15%, ~50%, 100%) against the chosen dark empty-cell color before merging; if the bottom of the range collapses, raise the alpha floor or swap the base RGB to `GRAPHITE.accent`'s RGB equivalent in dark mode only. |
| Regression risk to the two existing test files (`BaleniStatistics.test.tsx`, `PackingCharts.test.tsx`) | Low | Verified directly: neither test file contains any `className`, `toHaveClass`, `color`, or `dark`-related assertions (confirmed via grep of both files — zero matches), so purely additive class/style changes cannot break them. No test file edits are in scope or needed. |
| Scope creep into `.card`/`.badge-*` design-system shorthand classes, diverging from the `baleni` module's established raw-Tailwind convention | Low | NFR-4 already explicitly forbids this; guide rule 2 (skip elements already on design-system classes) doesn't apply here since none of the three files use those classes today — stay on raw `dark:` utilities throughout. |

## Specification Amendments

The spec is implementation-ready as written and well-grounded against the actual precedent code. Two small clarifications worth folding in, both non-blocking:

1. **Add explicit note that `PackingHourHeatmap.tsx` has no dedicated test file today** (only `BaleniStatistics.test.tsx` and `PackingCharts.test.tsx` exist under `__tests__/`) — the spec's FR-3 acceptance criterion "Existing tests referencing `data-testid=\"packing-hour-heatmap\"`... continue to pass unmodified" is technically satisfied vacuously (the `data-testid` is asserted against indirectly via `BaleniStatistics.test.tsx`, if at all) — worth confirming during implementation that no test elsewhere asserts on this testid before treating it as covered.
2. **`useTheme()` per-component overhead is negligible but not literally zero** — NFR-1 states "no additional... re-render triggers beyond the existing theme-context subscription pattern," which is accurate: each of the 4 chart components + heatmap will re-render once on theme toggle (as they don't today, since they currently have zero theme dependency), which is expected and desired — not a regression, just worth stating plainly rather than "no re-render triggers" verbatim, since a toggle-triggered re-render is precisely the intended new behavior.

No changes to FR-1/FR-2/FR-3 scope, acceptance criteria, or the out-of-scope list are needed — they're consistent with the codebase as read.

## Prerequisites

None. All dependencies already exist and require no setup:
- `ThemeContext.tsx` and its `useTheme()` hook are already wired into the app root (confirmed toggling `<html class="dark">` and persisting to `localStorage` under key `anela-theme`).
- `GRAPHITE` token object is already exported and stable in `reactSelectDarkStyles.ts`.
- `tailwind.config.js` already defines the full `graphite` color scale (`bg`, `surface`, `surface-2`, `hover`, `chrome`, `border`, `border-strong`, `text`, `muted`, `faint`, `accent`, `accent-strong`, `accent-ink`) and `shadow-soft-dark` — no config changes needed.
- No migration, no backend/API change, no feature flag gating required (dark mode is a persisted user preference already live in production, not a flag).
