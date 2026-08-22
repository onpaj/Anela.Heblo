# Implementation: dark-mode-packing-hour-heatmap

## What was implemented
Added runtime theme resolution to `PackingHourHeatmap.tsx` following the same `useTheme()` + `GRAPHITE` pattern used in `dark-mode-packing-charts-recharts`, so this subtree has one dark-mode mechanism rather than two. Fixed two defects:
1. The weekday/hour labels and empty-state message used `text-neutral-gray` with no `dark:` class, illegible against the dark page.
2. Empty cells read `var(--heatmap-empty, #f1f5f9)`, a CSS custom property defined nowhere in the codebase, so it always resolved to the light-only fallback and was visually indistinguishable from the dark page background.

Applied exactly the before/after edits specified in the task context:
- Added `useTheme` (from `contexts/ThemeContext`) and `GRAPHITE` (from `components/common/reactSelectDarkStyles`) imports.
- Computed `isDark` immediately after the `data` destructure, before the `counts` memo.
- Empty-state message, hour header labels, and weekday labels: added `dark:text-graphite-muted`.
- Cell background: replaced the undefined CSS variable with `GRAPHITE.surface2` for empty cells in dark mode, and swapped the occupied-cell base hue from `#2563eb` to `GRAPHITE.accent` (`#38BDF8`) with the alpha floor raised from `0.15` to `0.35` in dark mode only — light mode keeps the exact original formula unchanged.

## Files created/modified
- `frontend/src/components/baleni/statistics/PackingHourHeatmap.tsx` — added `useTheme`/`GRAPHITE` imports; themed labels, empty-state message, and cell background colors (light-mode formula unchanged).

## Tests
- No dedicated test file exists for this component (only `BaleniStatistics.test.tsx` and `PackingCharts.test.tsx` exist under `__tests__/`). `BaleniStatistics.tsx` renders `PackingHourHeatmap`, so the `BaleniStatistics` suite was run as an integration smoke check. Result: 5/5 tests passed, no assertions on this component's classes or inline styles.

## How to verify
```bash
cd frontend && CI=true npm test -- --testPathPattern=BaleniStatistics   # 5/5 pass
cd frontend && npm run build                                           # compiles successfully, no TS errors
cd frontend && npm run lint                                            # no errors in PackingHourHeatmap.tsx
```
Manual/visual spot-check (not performed in this run — no dev instance available in this environment): toggle Graphite dark mode and confirm hour/weekday labels are legible, empty cells (`GRAPHITE.surface2`) are visibly distinct from the panel background, and the occupied-cell gradient is perceptibly ordered and distinct from the empty-cell color at every intensity step.

## Notes
- The pre-existing lint run reports errors across the frontend, but none reference `PackingHourHeatmap.tsx` — confirmed the edit did not introduce any new lint findings.
- No deviations from the task context's exact before/after code were made.

## PR Summary
Adds theme-aware styling to `PackingHourHeatmap.tsx` so the weekday × hour activity heatmap on the Baleni statistics page renders legibly in Graphite dark mode, and replaces an undefined `--heatmap-empty` CSS variable (which always fell back to its light-only default) with a proper `GRAPHITE`-based dark-mode cell color. Occupied-cell contrast in dark mode is raised via a cyan accent base hue and a higher alpha floor so low-intensity cells stay visually distinct from empty ones; the light-mode formula is unchanged.

### Changes
- `frontend/src/components/baleni/statistics/PackingHourHeatmap.tsx` — themed labels, empty-state message, and cell background colors for dark mode.

## Status
DONE
