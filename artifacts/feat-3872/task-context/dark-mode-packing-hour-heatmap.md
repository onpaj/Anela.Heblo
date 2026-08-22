### task: dark-mode-packing-hour-heatmap

**Context:** `frontend/src/components/baleni/statistics/PackingHourHeatmap.tsx` renders a weekday × hour activity heatmap as a plain HTML `<table>` with per-`<td>` inline `style={{ backgroundColor }}`. It has two dark-mode defects:
1. No `dark:` classes on the hour/weekday `<th>`/`<td>` labels or the empty-state message (`text-neutral-gray` is illegible against a dark page).
2. The empty-cell color reads `var(--heatmap-empty, #f1f5f9)` — this CSS custom property is referenced nowhere else in the codebase and is defined nowhere in `frontend/src/index.css`, so it always resolves to the light-only fallback `#f1f5f9`, making empty and low-activity cells visually indistinguishable from the dark page background.

The fix follows the same `useTheme()` + `GRAPHITE` pattern as task `dark-mode-packing-charts-recharts` (see that task for the full `GRAPHITE` object definition and import paths) rather than defining the CSS variable, so this subtree has one dark-mode mechanism, not two.

**Occupied-cell contrast finding (verified, apply as specified — do not re-derive):** the existing occupied-cell formula `rgba(37, 99, 235, ${0.15 + intensity * 0.85})` (blue `#2563eb` base) computed against the panel's dark background (`graphite-surface`, `#202327`) tops out at only ≈2.9:1 contrast even at full intensity (`alpha=1`), and at the low end (`alpha=0.15`) is visually indistinguishable from the proposed empty-cell color (`GRAPHITE.surface2`, `#272A30`) — contrast ratio ≈1.07:1. Two changes are needed for dark mode specifically (light mode keeps the exact current formula, unchanged):
- Swap the base hue from `#2563eb` (`rgb(37,99,235)`) to `GRAPHITE.accent` (`#38BDF8` = `rgb(56,189,248)`), which reaches ≈6.9:1 contrast at full intensity against `#202327`.
- Raise the alpha floor from `0.15` to `0.35` in dark mode, so the lowest-intensity occupied cell is still perceptibly brighter than the empty cell.

Apply the following edits to `frontend/src/components/baleni/statistics/PackingHourHeatmap.tsx`:

- [ ] **Add imports** after the existing import (currently line 2, before line 4 `interface PackingHourHeatmapProps`):
  ```tsx
  // before
  import React from "react";
  import { HourBucket } from "../../../api/hooks/usePackingStatistics";

  interface PackingHourHeatmapProps {
  // after
  import React from "react";
  import { HourBucket } from "../../../api/hooks/usePackingStatistics";
  import { useTheme } from "../../../contexts/ThemeContext";
  import { GRAPHITE } from "../../common/reactSelectDarkStyles";

  interface PackingHourHeatmapProps {
  ```
- [ ] **Compute `isDark`** inside the component, immediately after the existing `data` destructure (currently line 18, before the `counts` memo on line 19):
  ```tsx
  // before
  const PackingHourHeatmap: React.FC<PackingHourHeatmapProps> = ({ data }) => {
    const counts = React.useMemo(() => {
  // after
  const PackingHourHeatmap: React.FC<PackingHourHeatmapProps> = ({ data }) => {
    const { theme } = useTheme();
    const isDark = theme === "dark";
    const counts = React.useMemo(() => {
  ```
- [ ] **Empty-state message** (currently lines 47–50):
  ```tsx
  // before
  if (data.length === 0) {
    return (
      <p className="text-sm text-neutral-gray italic">Žádná data k zobrazení.</p>
    );
  }
  // after
  if (data.length === 0) {
    return (
      <p className="text-sm text-neutral-gray italic dark:text-graphite-muted">Žádná data k zobrazení.</p>
    );
  }
  ```
- [ ] **Hour header labels** (currently line 60):
  ```tsx
  // before
  <th key={hour} className="text-xs font-normal text-neutral-gray text-center w-7">
  // after
  <th key={hour} className="text-xs font-normal text-neutral-gray text-center w-7 dark:text-graphite-muted">
  ```
- [ ] **Weekday labels** (currently line 71):
  ```tsx
  // before
  <td className="text-xs text-neutral-gray pr-1 text-right">{label}</td>
  // after
  <td className="text-xs text-neutral-gray pr-1 text-right dark:text-graphite-muted">{label}</td>
  ```
- [ ] **Cell background color** (currently lines 76–87) — replace the undefined CSS variable with the theme-aware inline color, and use the adjusted dark-mode formula for occupied cells:
  ```tsx
  // before
  return (
    <td
      key={hour}
      className="h-7 w-7 rounded"
      title={`${label} ${hour}:00 — ${count} balíků`}
      style={{
        backgroundColor:
          count === 0
            ? "var(--heatmap-empty, #f1f5f9)"
            : `rgba(37, 99, 235, ${0.15 + intensity * 0.85})`,
      }}
    />
  );
  // after
  return (
    <td
      key={hour}
      className="h-7 w-7 rounded"
      title={`${label} ${hour}:00 — ${count} balíků`}
      style={{
        backgroundColor:
          count === 0
            ? isDark
              ? GRAPHITE.surface2
              : "#f1f5f9"
            : isDark
              ? `rgba(56, 189, 248, ${0.35 + intensity * 0.65})`
              : `rgba(37, 99, 235, ${0.15 + intensity * 0.85})`,
      }}
    />
  );
  ```

Do not change `cellKey`, `counts`, `maxCount`, `fromHour`, `toHour`, `WEEKDAY_LABELS`, `DEFAULT_FROM_HOUR`/`DEFAULT_TO_HOUR`, the component's props (`PackingHourHeatmapProps { data: HourBucket[] }`), or the `title` tooltip text — only the four edits above.

**Verification steps:**

- [ ] Confirm no test file exists for this component today (only `BaleniStatistics.test.tsx` and `PackingCharts.test.tsx` exist under `__tests__/`, so there is no dedicated suite to run for this file):
  ```bash
  ls frontend/src/components/baleni/statistics/__tests__/
  ```
  Expected: `BaleniStatistics.test.tsx` and `PackingCharts.test.tsx` only — no `PackingHourHeatmap.test.tsx`. Since `BaleniStatistics.tsx` renders `PackingHourHeatmap` (via the `Panel title="Vytížení podle hodin"` block), re-run the `BaleniStatistics` suite as an integration smoke check:
  ```bash
  cd frontend && CI=true npm test -- --testPathPattern=BaleniStatistics
  ```
  Expected: passes unmodified (no assertions on this component's classes or inline styles).
- [ ] Run the build to confirm no TypeScript errors:
  ```bash
  cd frontend && npm run build
  ```
  Expected: build succeeds with no new errors.
- [ ] Run the linter:
  ```bash
  cd frontend && npm run lint
  ```
  Expected: no new lint errors.
- [ ] Manual/visual spot-check (if a dev instance is available): toggle Graphite dark mode and confirm (a) hour/weekday labels are legible, (b) empty cells (`GRAPHITE.surface2`) are visibly distinct from the panel background (`graphite-surface`) they sit on, and (c) the occupied-cell gradient from lowest to highest activity is perceptibly ordered and distinct from the empty-cell color at every step, not just at the high end.
- [ ] Run the full frontend suite one final time to confirm the three-file change set is clean together:
  ```bash
  cd frontend && CI=true npm test && npm run build && npm run lint
  ```
  Expected: all pass with zero new failures/errors.
- [ ] Commit:
  ```bash
  git add frontend/src/components/baleni/statistics/PackingHourHeatmap.tsx
  git commit -m "Fix undefined --heatmap-empty variable and theme heatmap for dark mode"
  ```
