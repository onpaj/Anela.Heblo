# Design: Unify product color-mapping logic in ProductMarginSummary.tsx

This feature introduces no new or changed user-facing UI — the spec (FR-5) requires the rendered chart and table to remain visually identical to current behavior for existing data. The architecture review confirms `Skip Design: true` for the same reason. Accordingly, this document contains no UX/UI section (per the designer's own instruction: skip UX/UI sections entirely when there is no user-facing UI design work, rather than writing placeholder content) and covers only component design and data shapes, both purely internal to one existing file.

## Component Design

All elements below live inside the single existing component file `frontend/src/components/pages/ProductMarginSummary.tsx`. No new files or exported modules are introduced (per the architecture review's Decision 1 — no extraction, since there is exactly one consumer).

### `productColorMap` (new internal `useMemo`)
- **Responsibility:** The single, canonical source of truth for `groupKey → color` assignment. Replaces the two independent color-derivation blocks currently inside `chartData` and `tableData`.
- **Signature (conceptual):** `(topProducts: TopProduct[] | undefined) => Map<string, string>`
- **Behavior:**
  - Returns an empty `Map` if `data?.topProducts` is falsy.
  - Sorts a copy of `topProducts` descending by `totalMargin` (using `?? 0` for missing values).
  - Assigns the first `TOP_CHART_PRODUCTS` (15) entries, in sorted order, `PRODUCT_COLORS[index % PRODUCT_COLORS.length]`, keyed by `groupKey` (entries with a falsy `groupKey` are skipped, matching current guard behavior).
  - Assigns every entry beyond index `TOP_CHART_PRODUCTS - 1` the value `DEFAULT_COLOR`.
- **Dependency array:** `[data?.topProducts]`.
- **Consumed by:** `chartData` and `tableData` (both read-only via `Map.get`).

### `TOP_CHART_PRODUCTS` (constant, hoisted to module scope)
- **Responsibility:** Single named constant for "how many products get a distinct palette color before falling back to `DEFAULT_COLOR`." Currently a local `const` inside the old `chartData` body (value `15`); this design hoists it to module scope, alongside `PRODUCT_COLORS`/`DEFAULT_COLOR`/`OTHER_COLOR`, so both `productColorMap` and the chart's top-15/"Other" split (see `buildChartDatasets` below) share one definition.

### `buildChartDatasets` (new internal module-level helper function, not a hook)
- **Responsibility:** Pure function that constructs the Chart.js `datasets` array (the "Ostatní produkty" aggregate series plus one series per top product), replacing the current inline logic at lines ~108–165 of `chartData`. Extracted so that no single `useMemo` body in the file exceeds the project's ~50-line guideline (FR-4).
- **Signature (conceptual):**
  ```ts
  function buildChartDatasets(
    monthlyData: MonthlyMarginData[],
    topProducts: TopProduct[],
    productColorMap: Map<string, string>,
    productDisplayNames: Map<string, string>,
  ): ChartDataset[]
  ```
- **Behavior (unchanged from current, just relocated and re-sourced for color):**
  1. Determine "Other" membership per product via `productColorMap.get(groupKey) === DEFAULT_COLOR` (architecture review Decision 3 — avoids a second independent sort of `topProducts`).
  2. If any products are "Other," push one dataset for `"Ostatní produkty"` (label), summing `marginContribution` across all "Other" `groupKey`s per month, colored `OTHER_COLOR`.
  3. For the remaining ("top") products, push one dataset per product, ordered ascending by `totalMargin` (lowest first) so Chart.js renders the highest-margin product at the top of the stack — this ordering is preserved exactly as today, it is not new behavior.
  4. Each top-product dataset uses `productColorMap.get(productKey) ?? DEFAULT_COLOR` for `backgroundColor`/`borderColor`, and `productDisplayNames.get(productKey) ?? productKey` for `label`.
- **Not memoized independently** — called from inside `chartData`'s own `useMemo`, since its inputs are a subset of `chartData`'s existing dependencies (no independent memoization value; see architecture review Decision 2 rationale).

### `chartData` (existing `useMemo`, modified)
- **Responsibility:** Unchanged from today — produces `{ labels, datasets }` for the Chart.js `<Chart>` component.
- **Change:** No longer computes its own sorted-by-margin list for color assignment, no longer contains the inline dataset-construction loop (delegated to `buildChartDatasets`). Reads colors exclusively via `productColorMap`.
- **Dependency array:** `[data, productColorMap]` (adds `productColorMap`; architecture review flags this as the most likely subtle bug spot if omitted).

### `tableData` (existing `useMemo`, modified)
- **Responsibility:** Unchanged from today — produces the array of row view-models for the detail table, sorted by backend `rank`.
- **Change:** No longer calls `data.topProducts!.findIndex(...)` to derive `colorCode`. Reads `colorCode` via `productColorMap.get(product.groupKey || "") ?? DEFAULT_COLOR`.
- **Dependency array:** `[data?.topProducts, productColorMap]` (tightened from `[data]`, since this memo only reads `data.topProducts`, not the rest of `data`).

### Unaffected elements (explicitly out of scope, unchanged)
`chartOptions`, `formatCurrency`, `productDisplayNames` construction (still built from `data.monthlyData`, unchanged), the loading/error/empty-state JSX, the controls (time window / grouping mode / margin level selectors), `PRODUCT_COLORS`/`OTHER_COLOR`/`DEFAULT_COLOR` values.

## Data Schemas

No API/DTO/backend schema changes. This section documents the one new internal (non-exported, non-DTO) shape introduced:

```ts
// Internal only — not a DTO, not exported, purely local to ProductMarginSummary.tsx.
// A plain Map, consistent with the existing productDisplayNames Map already used in this file.
type ProductColorMap = Map<string /* groupKey */, string /* hex color */>;
```

All other consumed shapes (`data.topProducts[]`, `data.monthlyData[]`, their fields) are pre-existing generated API-client types from `useProductMarginSummaryQuery` (`frontend/src/api/hooks/useProductMarginSummary.ts`) and are unchanged by this design — no field is added, removed, or retyped.
