## Review Result: CLEAN

### Blocking (correctness)
- None

### Advisory (cleanup)
- `frontend/src/components/baleni/statistics/BaleniStatistics.tsx:85-89` — The error-state title (`text-red-800`) and message (`text-red-700`) map to the same `dark:text-red-400` in dark mode, collapsing a visual hierarchy that light mode preserves (800 vs 700). Not a functional bug, but could differentiate with e.g. `dark:text-red-300` for the title if the hierarchy matters.
- `frontend/src/components/baleni/statistics/PackingCharts.tsx:106-108,146-148,192-194,225-227` — The same three-line `contentStyle`/`itemStyle`/`labelStyle` Tooltip theming block is duplicated verbatim across all four chart components. Could be extracted into a small shared `themedTooltipProps(isDark)` helper (or a `<ThemedTooltip>` wrapper) in this file to avoid the four-way duplication.
