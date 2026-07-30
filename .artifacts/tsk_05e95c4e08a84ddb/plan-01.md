# Plan: Fix hardcoded light-mode colors in BankStatementImportChart (ADR-006 dark-mode compliance)

## Summary

`frontend/src/components/charts/BankStatementImportChart.tsx` renders its Recharts SVG primitives (grid, axes, weekend highlight, threshold reference line, data line, custom dot) with hardcoded light-mode hex colors. Because Recharts SVG props take literal values, not CSS classes, these colors don't respond to the Graphite dark theme, unlike the component's tooltip which already uses Tailwind `dark:` variants correctly. The fix makes the chart read the active theme via the existing `useTheme()` hook and select the color set at render time.

## Context

ADR-006 requires every frontend component that renders color to work correctly in both light and dark modes. This chart is used on a production monitoring page (`components/pages/BankStatementImportChart.tsx`, the Bank statement import overview) that operators check regularly. In dark mode today: grid lines are nearly invisible, weekend-highlight bands wash out, and the white dot outline used to flag problem days disappears against the dark surface — degrading a screen used for spotting import failures.

## Functional requirements

**FR-1 — Chart reads active theme.**
Component calls `useTheme()` from `../../contexts/ThemeContext` and derives `isDark = theme === 'dark'`.
- Acceptance: rendering the component inside a `ThemeProvider` with `theme='dark'` does not throw and produces dark-variant color values; with `theme='light'` produces the original light values.

**FR-2 — All hardcoded SVG color props become theme-derived.**
Replace every literal hex string identified in the finding with a value selected from a `colors` object computed per render from `isDark`:
| Element | Prop | Light | Dark |
|---|---|---|---|
| `CartesianGrid` (grid) | `stroke` | `#f0f0f0` | `#374151` |
| `XAxis` / `YAxis` (axis) | `stroke` | `#6b7280` | `#9ca3af` |
| `ReferenceArea` (weekendFill) | `fill` | `#e0f2fe` | `#0ea5e9` |
| `ReferenceLine` (threshold) | `stroke` + label `style.fill` | `#dc2626` | `#f87171` |
| `Line` (line) | `stroke` | `#3b82f6` | `#3b82f6` (unchanged, readable both modes) |
| `Line activeDot` | `fill` | `#3b82f6` | `#3b82f6` |
| `CustomDot` | `fill` | `#dc2626` | `#f87171` (match threshold color for consistency) |
| `CustomDot` | `stroke` (dot outline) | `#fff` | `#1f2937` |
- Acceptance: no bare hex literal remains inline on any Recharts prop in the file except inside the `colors` object definition itself; `grep -n '"#\|'"'"'#' BankStatementImportChart.tsx` (excluding the `colors` block) returns nothing.

**FR-3 — Visual behavior unchanged in light mode.**
Since the light-mode values in the `colors` map are identical to today's literals, existing light-mode rendering (including any snapshot/visual expectations) is unaffected.
- Acceptance: manual render in light mode is visually identical to current behavior.

**FR-4 — Legend swatches (lines 229–243) are out of scope.**
These use Tailwind utility classes (`bg-blue-500`, `bg-red-600`, `border-white`, `bg-sky-100`) not raw hex, and are not part of the reported finding (no `dark:` variants currently, but not flagged). Leave unchanged — do not expand scope beyond the finding.
- Acceptance: legend markup in the diff is untouched.

## Non-functional requirements

- **No new dependencies.** `useTheme` already exists in the codebase (`frontend/src/contexts/ThemeContext.tsx`); no new libraries or CSS-variable plumbing for Recharts.
- **No behavior change in light mode** (perf-neutral: one extra hook call and a small object literal per render, negligible).
- **Consistency with existing dark palette tokens** used elsewhere in the file's own tooltip (`graphite-surface`, `graphite-border`, `graphite-muted`, `graphite-text`) — pick the closest matching hex equivalents rather than inventing new colors that clash.

## Data model

No data model changes. Purely presentational — a `colors` lookup object derived from `Theme` (`"light" | "dark"`, already defined in `ThemeContext.tsx`).

## Interfaces

No API, prop, or public interface changes. `BankStatementImportChartProps` is untouched. The component gains an internal dependency on `useTheme()`, so it now requires a `ThemeProvider` ancestor at runtime — already guaranteed app-wide via `App.tsx`.

## Dependencies and scope

**In scope:** `frontend/src/components/charts/BankStatementImportChart.tsx` only, per the finding's explicit instruction ("No changes outside this one component file").

**Explicitly out of scope:**
- `frontend/src/components/charts/InvoiceImportChart.tsx` has the identical pattern (same hardcoded hex values at lines 87–88, 143, 147, 151, 162, 171, 185) but is a separate, unfiled finding — not touched here. Flagged under Open Questions for a possible follow-up arch-review item.
- Any test file — none currently exists for this component (verified: no `BankStatementImportChart.test.tsx` anywhere in the repo). Not adding new tests is consistent with existing coverage for this file; a smoke test could be added later but is not required to close this finding.
- Legend swatches (FR-4).

## Rough plan

1. Import `useTheme` from `../../contexts/ThemeContext` into `BankStatementImportChart.tsx`.
2. Inside the component body, call `const { theme } = useTheme(); const isDark = theme === 'dark';` and define the `colors` object per the FR-2 table.
3. Replace the 9 hardcoded hex occurrences (lines 112–113, 171, 175, 179, 194, 203, 209, 217, 220) with references into `colors`.
4. Run `npm run build` and `npm run lint` in `frontend/`.
5. Manually verify in-browser: toggle theme via `ThemeToggle` while viewing the Bank statement import page, confirm grid/axis/weekend band/threshold line/dot all remain visible and legible in both modes.

## Open questions

- **`InvoiceImportChart.tsx` has the same defect** — same hex values, same pattern. Since the finding restricts scope to one file, this is called out but not fixed here. Default: leave it; recommend filing a follow-up arch-review finding rather than silently expanding this task's scope.
- **Exact dark-mode hex values** for `weendFill`/`threshold`/`dotStroke` are design judgment calls (no formal dark palette token for chart-specific colors exists yet). Default: use the values proposed in the finding's suggested fix verbatim, since they already reference existing Tailwind/Graphite-adjacent tones (`blue-500/10`≈`#0ea5e9` at low opacity, `red-400`=`#f87171`, `graphite-surface`≈`#1f2937`).
- **Should `CustomDot`'s red use the same `colors.threshold` token or its own?** Default: reuse `colors.threshold` for both the reference-line and the dot, since both represent "below minimum threshold" semantics — reduces duplication over inventing a separate `dotFill` key.
