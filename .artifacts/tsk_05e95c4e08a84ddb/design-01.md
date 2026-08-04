# Design: Theme-aware colors for BankStatementImportChart

Scope confirmed against current source (`frontend/src/components/charts/BankStatementImportChart.tsx`, 249 lines) and `frontend/src/contexts/ThemeContext.tsx`. No structural, layout, or interaction changes — this is a color-source change only, confined to one file.

## UX/UI

No new UI, layout, or interaction is introduced. The chart's DOM structure, sizing, tooltip behavior, and legend are untouched. The only visible effect is that six SVG-rendered elements switch color set when `ThemeContext`'s `theme` flips, matching what the Tailwind-styled tooltip already does.

Visual states (same layout, two color sets):

```
Light theme                              Dark theme
┌─────────────────────────────┐          ┌─────────────────────────────┐
│ Import banky - přehled...    │          │ Import banky - přehled...    │
│                               │          │                               │
│  ┆░░░░┆         ┆░░░░┆        │          │  ┆▓▓▓▓┆         ┆▓▓▓▓┆        │ ← weekend band
│  │┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈│  │          │  │┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈┈│  │ ← grid (#f0f0f0 → #374151)
│  │  ╭╮      ●(red/white)  │  │          │  │  ╭╮      ●(red/graphite) │  │ ← CustomDot outline
│  │╌╌│╰─────╌╌╌╌╌╌╌╌╌╌╌╌╌╌│  │          │  │╌╌│╰─────╌╌╌╌╌╌╌╌╌╌╌╌╌╌│  │ ← threshold ReferenceLine
│  ╰───────────────────────╯  │          │  ╰───────────────────────╯  │
│  (axis text #6b7280)         │          │  (axis text #9ca3af)         │
│ [blue] importů  [red] práh…  │          │ [blue] importů  [red] práh…  │ ← legend (Tailwind, unchanged)
└─────────────────────────────┘          └─────────────────────────────┘
```

No wireframe changes are needed beyond this — the legend already carries its own Tailwind classes and is explicitly out of scope (FR-4).

## Component design

Single component, single file: `BankStatementImportChart.tsx`. No new components, no new files, no prop changes.

**Responsibility boundary added:** the component becomes theme-aware for its own rendering only. It reads `useTheme()` and derives a plain color lookup used exclusively by the Recharts SVG props inside its own JSX — it does not push theme state to `CustomDot`, `weekendPeriods`, or any other collaborator beyond what's needed to read `colors` from the enclosing closure.

Internal structure after the change:

1. **Imports** — add `import { useTheme } from '../../contexts/ThemeContext';` alongside the existing recharts/date-fns/api imports.
2. **Theme derivation + color map** — declared once at the top of the function body (before `chartData`), so both `CustomDot` (defined later, closes over it) and the JSX (further down) share the same reference without recomputation:
   ```tsx
   const { theme } = useTheme();
   const isDark = theme === 'dark';
   const colors = {
     grid: isDark ? '#374151' : '#f0f0f0',
     axis: isDark ? '#9ca3af' : '#6b7280',
     weekendFill: isDark ? '#0ea5e9' : '#e0f2fe',
     threshold: isDark ? '#f87171' : '#dc2626',
     line: '#3b82f6',
     dotStroke: isDark ? '#1f2937' : '#fff',
   };
   ```
3. **`CustomDot`** (closure, lines ~104–119) — `fill` becomes `colors.threshold`, `stroke` becomes `colors.dotStroke`. Unchanged: `strokeWidth`, `r`, early-return-null logic.
4. **JSX tree** (unchanged shape — `LineChart` → `CartesianGrid` / `XAxis` / `YAxis` / `Tooltip` / `ReferenceArea[]` / `ReferenceLine` / `Line`):
   - `CartesianGrid.stroke` → `colors.grid`
   - `XAxis.stroke`, `YAxis.stroke` → `colors.axis`
   - `ReferenceArea.fill` (mapped per weekend period) → `colors.weekendFill`
   - `ReferenceLine.stroke` → `colors.threshold`; `ReferenceLine.label.style.fill` → `colors.threshold`
   - `Line.stroke` → `colors.line`; `Line.activeDot.fill` → `colors.line`
5. **Untouched:** `CustomTooltip` (already Tailwind `dark:`-aware), `weekendPeriods` memo, legend markup (lines 227–246), all data transforms, all props/exports.

No new abstraction (e.g., a shared `useChartColors` hook) is warranted — this is the only chart in scope for this finding, and `InvoiceImportChart.tsx`'s identical defect is an explicitly separate, unfiled follow-up (see plan's Open Questions). Introducing a shared hook now would touch a file outside this finding's stated scope ("No changes outside this one component file").

## Data schemas

No DB, API, request/response, or event payload changes — this is a pure rendering fix. `BankStatementImportChartProps` and `ChartDataPoint` are unchanged.

The only new data shape is the in-memory `colors` object, local to the component (not exported, not persisted):

```ts
type ChartColors = {
  grid: string;        // CartesianGrid stroke
  axis: string;         // XAxis / YAxis stroke
  weekendFill: string;  // ReferenceArea fill
  threshold: string;    // ReferenceLine stroke + label.style.fill; also CustomDot fill
  line: string;         // Line stroke + activeDot.fill (same value both themes)
  dotStroke: string;    // CustomDot outline stroke
};
```

Values per theme (from the plan's FR-2 table, carried forward verbatim):

| key | light | dark |
|---|---|---|
| `grid` | `#f0f0f0` | `#374151` |
| `axis` | `#6b7280` | `#9ca3af` |
| `weekendFill` | `#e0f2fe` | `#0ea5e9` |
| `threshold` | `#dc2626` | `#f87171` |
| `line` | `#3b82f6` | `#3b82f6` |
| `dotStroke` | `#fff` | `#1f2937` |

This map is derived fresh each render from `isDark`; it is not memoized (`React.useMemo`) since it's a small object literal recomputed only when `theme` changes re-renders the component via context — consistent with how the existing `CustomTooltip` and `CustomDot` closures are already redefined per render.
