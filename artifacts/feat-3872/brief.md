## Module
Packaging (Baleni) Workflow — module-map part #9

## Finding
`frontend/src/components/baleni/statistics/` — the packing-statistics screen — contains **zero** `dark:` Tailwind variants across all three of its files:

```
0  frontend/src/components/baleni/statistics/BaleniStatistics.tsx
0  frontend/src/components/baleni/statistics/PackingCharts.tsx
0  frontend/src/components/baleni/statistics/PackingHourHeatmap.tsx
```

Every other component in the same `Owns:` tree has dark-mode support (1 to 19 `dark:` occurrences — e.g. `BaleniHome.tsx`: 19, `PackingShipmentCreator.tsx`: 13, `ZasilkyFilters.tsx`: 7), so this subtree is an outlier within its own module, not a codebase-wide gap.

Concrete evidence:
- `frontend/src/components/baleni/statistics/BaleniStatistics.tsx:32-54` — `Panel` and `KpiCard` use `bg-white`, `border-border-light`, `text-neutral-slate`, `text-neutral-gray` with no `dark:` sibling anywhere in the file.
- `frontend/src/components/baleni/statistics/PackingCharts.tsx:24,32,93-107,165-175,192-201` — every Recharts `stroke`/`fill` is a hardcoded light-mode hex (`stroke="#f0f0f0"`, `stroke="#6b7280"`, `fill="#2563eb"`, etc.), not theme-aware.
- `frontend/src/components/baleni/statistics/PackingHourHeatmap.tsx:60,71,81-84` — header/weekday labels use `text-neutral-gray` with no `dark:` variant, and the empty-cell background falls back to `var(--heatmap-empty, #f1f5f9)`; `--heatmap-empty` is not defined anywhere in `frontend/src/index.css` or the Tailwind config, so it always resolves to the light fallback `#f1f5f9` (near-white) regardless of theme.

## Rule
`docs/architecture/development_guidelines.md`, ADR-006 (Accepted, 2026-06-25): "Every frontend component that renders color (background, text, border, ring, shadow, divider, icon, status) must render correctly in both light and dark mode." This is an established, actively-enforced class — prior accepted instances include #3761 (Bank chart), #3518 (GridLayouts), #3479 (Leaflet), #3457 (OrgChart), #3440 (Dashboard), all closed/fixed.

## Why it matters
The Baleni screens run on a warehouse-floor kiosk where the Graphite dark theme is a first-class, persisted (`localStorage`) mode, not a niche preference. With `Panel`/`KpiCard` unstyled for dark mode, the whole statistics page renders white cards with near-white/gray text on the app's dark surface — failing WCAG 2.1 AA contrast exactly as ADR-006 requires. The `CartesianGrid`/axis strokes are near-invisible against a dark background, and the heatmap's empty-cell color (`#f1f5f9`) is indistinguishable from — or brighter than — the intended dark surface, breaking the chart's core visual affordance (low vs. high activity) precisely where the tool that depends on it is used.

## Suggested direction
Bring `BaleniStatistics.tsx`'s `Panel`/`KpiCard` onto the design-system surface/text tokens (`.card`, `graphite-surface`, `graphite-text`/`muted`) the rest of the module already uses, per `docs/design/dark-mode-conversion-guide.md`. For the Recharts elements, resolve stroke/fill colors from the active theme (e.g. via `useTheme()`) rather than hardcoded hex, and define `--heatmap-empty` for both themes rather than relying on an undefined custom property's light-only fallback. Do not implement the fix here — this issue only records the finding.

---
_Filed by arch-review of module-map part #9._

