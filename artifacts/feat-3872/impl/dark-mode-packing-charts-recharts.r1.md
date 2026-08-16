# Implementation: dark-mode-packing-charts-recharts

## What was implemented
Added runtime theme resolution to `PackingCharts.tsx` so the four Recharts-based charts (`ThroughputChart`, `CarrierMixChart`, `PackerLeaderboard`, `PackagesPerOrderChart`) and the shared `EmptyState` render correctly in Graphite dark mode. Recharts `stroke`/`fill`/tooltip styles are plain SVG/inline-style attributes, not Tailwind classes, so they can't be handled with `dark:` classes and instead resolve at render time via `useTheme()` + the `GRAPHITE` color constants, following the precedent in `BankStatementImportChart.tsx`.

Applied exactly the before/after edits specified in the task context:
- Added `useTheme` (from `contexts/ThemeContext`) and `GRAPHITE` (from `components/common/reactSelectDarkStyles`) imports.
- `EmptyState`: added `dark:text-graphite-muted` to the placeholder text.
- `ThroughputChart`, `CarrierMixChart`, `PackerLeaderboard`, `PackagesPerOrderChart`: each now calls `useTheme()` and computes `isDark` unconditionally at the top of the component, before the `data.length === 0` early return (Rules-of-Hooks compliance). Grid lines, axis strokes, and tooltip `contentStyle`/`itemStyle`/`labelStyle` swap to `GRAPHITE.border` / `GRAPHITE.muted` / `GRAPHITE.surface` / `GRAPHITE.text` in dark mode. The primary bar fill in `ThroughputChart` (`packageCount`) and `PackerLeaderboard` (`orderCount`) swaps to `GRAPHITE.accent` in dark mode, matching the established accent-color convention elsewhere in the module.

Left untouched per the out-of-scope list: `CARRIER_COLORS`, `OTHER_COLOR`, `sliceColor`, `buildCarrierSlices`, `MAX_CARRIERS`, `OTHER_LABEL`, `OTHER_KEY`, `CarrierSlice`, and the data-hook imports — as well as the secondary bar fills (`#93c5fd` in `ThroughputChart`, `#0ea5e9` in `PackagesPerOrderChart`) and the pie-slice colors in `CarrierMixChart`, which already clear contrast in dark mode per the task's verified contrast decisions.

## Files created/modified
- `frontend/src/components/baleni/statistics/PackingCharts.tsx` — added `useTheme`/`GRAPHITE` imports; themed `EmptyState`, and grid/axis/tooltip/primary-bar colors in `ThroughputChart`, `CarrierMixChart`, `PackerLeaderboard`, `PackagesPerOrderChart`.

## Tests
- `frontend/src/components/baleni/statistics/__tests__/PackingCharts.test.tsx` — existing test file, run unmodified. Covers `buildCarrierSlices` behavior (merging, sorting, "Ostatní" bucket rollup). Not color-related, so unaffected by this change. Result: 8/8 tests passed.

## How to verify
```bash
cd frontend && CI=true npm test -- --testPathPattern=PackingCharts   # 8/8 pass
cd frontend && npm run build                                        # compiles successfully, no TS errors
cd frontend && npm run lint                                         # no errors in PackingCharts.tsx, no react-hooks/rules-of-hooks violations anywhere
```
Manual/visual spot-check (not performed in this run — no dev instance available in this environment): toggle Graphite dark mode on the Baleni statistics page and confirm grid lines/axis labels are visible against the dark panel, the primary bars in "Průběh balení v čase" and "Baliči" render in cyan accent, and tooltips are not stark white boxes.

## Notes
- The pre-existing lint run reports 175 errors / 13 warnings across the frontend, but all are in unrelated test files (e.g. `testing-library/no-node-access`, `testing-library/no-wait-for-multiple-assertions` in various `__tests__` files, an `import/first` issue in `ThemeContext.test.tsx`). None reference `PackingCharts.tsx`, and no `react-hooks/rules-of-hooks` violation exists anywhere in the codebase after this change — confirmed the edits did not introduce any new lint findings.
- `artifacts/feat-3872/state.json` showed as modified in `git status` before this task started (pipeline-managed, not touched by this change) and was intentionally left out of the commit, per the exact `git add` command specified in the task.
- No deviations from the task context's exact before/after code were made; all edits matched by content rather than line number since the file's line numbers had not shifted from the spec's line numbers, but content matching was verified regardless.

## PR Summary
Adds theme-aware Recharts styling to `PackingCharts.tsx` so the packaging statistics charts (throughput, carrier mix, packer leaderboard, packages-per-order) render legibly in Graphite dark mode. Recharts colors are plain SVG attributes/inline styles rather than Tailwind classes, so they're resolved at render time via a new `useTheme()` call plus the shared `GRAPHITE` palette, mirroring the existing pattern in `BankStatementImportChart.tsx`. `useTheme()` is called unconditionally at the top of each component (before any early return) to satisfy the Rules of Hooks. Chart data/color-logic helpers (`CARRIER_COLORS`, `sliceColor`, `buildCarrierSlices`, etc.) are unchanged — only grid lines, axis strokes, tooltip styling, and the two primary accent-colored bars are themed.

### Changes
- `frontend/src/components/baleni/statistics/PackingCharts.tsx` — themed `EmptyState`, `ThroughputChart`, `CarrierMixChart`, `PackerLeaderboard`, `PackagesPerOrderChart` for dark mode.

## Status
DONE
