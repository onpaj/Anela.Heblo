# Specification: Unify product color-mapping logic in ProductMarginSummary.tsx

## Summary
`ProductMarginSummary.tsx` currently computes a product → chart/legend color mapping twice, independently, inside two separate `useMemo` blocks (`chartData` and `tableData`). The two computations use different, inconsistently-expressed sort/index logic, creating a latent risk that the chart legend and the detail table assign different colors to the same product. This change extracts a single canonical `productColorMap` `useMemo` that both `chartData` and `tableData` read, eliminating the duplication and guaranteeing color consistency. As a secondary cleanup, the now-oversized `chartData` `useMemo` (101 lines) is split so no single memo exceeds the project's 50-line guideline.

## Background
`frontend/src/components/pages/ProductMarginSummary.tsx` renders a stacked bar chart plus a detail table for product margin data. Both views color-code products consistently with the product's rank by total margin, using a shared 15-color palette (`PRODUCT_COLORS`) plus a gray fallback (`DEFAULT_COLOR`/`OTHER_COLOR`).

Today:
- `chartData` (lines 66–167) explicitly sorts `[...data.topProducts]` descending by `totalMargin`, then assigns `PRODUCT_COLORS[index % length]` to the top 15, grouping the remainder into an "Ostatní produkty" ("Other") series colored `OTHER_COLOR`.
- `tableData` (lines 170–207) does **not** re-sort; it colors each product using `data.topProducts.findIndex(...)`, i.e. the index in the array as returned by the API, falling back to `DEFAULT_COLOR` only when the item isn't found (which cannot happen, since it iterates the same array) — so it never actually uses the "not in top 15" fallback color, unlike `chartData`.

This means: if the backend's `topProducts` response order ever differs from strict descending `totalMargin` order (e.g. a future backend change, a tie-breaking change, or grouping-mode-specific ordering), the chart legend and the table will silently disagree on which color represents which product — a data-integrity/visual-consistency bug that is currently latent, not observed, but real. This was flagged by the daily arch-review routine as a code-duplication / consistency risk affecting the Analytics module.

There is no known current production bug report tied to this — this is a proactive refactor closing a correctness risk plus a maintainability issue (one algorithm change today requires touching two places).

## Functional Requirements

### FR-1: Single canonical color-mapping source
Introduce one `useMemo`-derived value, `productColorMap: Map<string, string>`, that is the sole place computing the product → color assignment. It must:
- Sort a copy of `data.topProducts` descending by `totalMargin` (using `?? 0` for missing values, matching existing `??`/`||` normalization already used elsewhere in the file).
- Assign the top `TOP_CHART_PRODUCTS` (15, keep existing named constant) products, in that sorted order, colors from `PRODUCT_COLORS[index % PRODUCT_COLORS.length]`, keyed by `groupKey`.
- Assign all products beyond the top 15 the fallback color (`DEFAULT_COLOR`; see FR-3 on reconciling `DEFAULT_COLOR`/`OTHER_COLOR`).
- Skip entries with a falsy `groupKey` (matches current guard behavior in `chartData`).
- Depend only on `data?.topProducts` (i.e. `useMemo(..., [data?.topProducts])`), not the whole `data` object, since that is the only field it reads — this is a tightening of the current `chartData`/`tableData` deps (`[data]`) but is behaviorally safe since nothing else the memo reads changes independently of `topProducts` in this dataset shape (confirm during implementation that `topProducts` identity changes whenever the query result changes; if the API hook returns a new `data` object per fetch, as `useProductMarginSummaryQuery` does, `data?.topProducts` will also be a new reference per fetch, so this is safe).

**Acceptance criteria:**
- There is exactly one place in the file that reads `PRODUCT_COLORS[index % PRODUCT_COLORS.length]`.
- For any given `data.topProducts` input, the color assigned to a `groupKey` in the chart legend and in the table's colored dot are identical for every product, including products beyond the top 15.
- The existing "Ostatní produkty" ("Other") aggregate bar in the chart keeps using its own distinct gray (`OTHER_COLOR`), not `productColorMap`.

### FR-2: `chartData` and `tableData` consume the shared map
Both `chartData` and `tableData` `useMemo` blocks look up colors via `productColorMap.get(groupKey) ?? DEFAULT_COLOR` (or `|| DEFAULT_COLOR`, matching existing file convention — see FR-3) instead of performing their own sort/index/color derivation.

**Acceptance criteria:**
- `chartData` no longer contains a `sort` call to derive `sortedByTotalMargin` for color purposes, nor its own `PRODUCT_COLORS[index % ...]` assignment loop; it reads colors from `productColorMap`.
- `tableData` no longer calls `data.topProducts!.findIndex(...)` to derive a color; it reads colors from `productColorMap`.
- `chartData` still needs `sortedByTotalMargin` (or equivalent) for the *"Other" grouping* (which products fall in/out of the top 15) and for stacking order — that non-color-related use of the sorted list may remain, but must not be a second independent copy of the sort used for coloring. Prefer deriving the top-15/other split from `productColorMap` (e.g. `productColorMap.get(key) !== DEFAULT_COLOR`) or from a single shared `sortedByTotalMargin` value computed once and reused by both the color map and the chart's other logic — implementer's choice, as long as there is exactly one sort-by-`totalMargin` computation feeding both color assignment and the top-15/other split, and it is *not duplicated by `tableData`* (`tableData` does not need the sorted list at all — its rows are ordered by backend `rank`, which is unaffected by this change).

### FR-3: Reconcile `DEFAULT_COLOR` vs `OTHER_COLOR` fallback usage
`DEFAULT_COLOR` and `OTHER_COLOR` are currently defined as the same literal value (`"#9CA3AF"`) but used for conceptually different things: `OTHER_COLOR` colors the chart's aggregate "Other" stacked-bar series; `DEFAULT_COLOR` is the per-product fallback for a product not found in the top-15 map. Keep both named constants as-is (do not merge them — they represent different semantic uses even though their current values match) and use `DEFAULT_COLOR` for `productColorMap`'s fallback / any per-product lookup miss, and continue using `OTHER_COLOR` only for the "Ostatní produkty" aggregate dataset. This matches current file behavior; call out explicitly so implementation doesn't merge or rename these constants as an unrequested cleanup.

**Acceptance criteria:**
- `OTHER_COLOR` and `DEFAULT_COLOR` both still exist as separate named constants after the change.
- No behavior change to which literal color value is shown in either the "Other" bar or the fallback-color case (both remain `#9CA3AF`).

### FR-4: Reduce `chartData` useMemo below ~50 lines
Per the project's method-length guideline (flagged in the finding: `chartData` is 101 lines, lines 66–167), extract the chart dataset-construction logic (currently lines 108–165: building the "Other" dataset entry and the per-top-product dataset entries) into a separate `useMemo` or a plain helper function, so that:
- The `productColorMap` `useMemo` is its own small memo (see FR-1).
- The remaining `chartData` `useMemo` (labels + delegating to dataset construction) is reasonably sized, and the extracted dataset-construction logic is either its own `useMemo` (if it needs independent memoization) or a plain function/helper called from within `chartData` (if it doesn't need to be memoized separately) — implementer's choice based on whether the extracted logic benefits from independent memoization; a plain helper function is preferred as the simpler option unless there's a clear performance reason for a separate memo, since it does not need its own dependency array and keeps the split easy to reason about.
- No individual `useMemo` body introduced or retained by this change should be intentionally left over ~50 lines; some latitude is acceptable if splitting further would harm readability more than it helps (this is a guideline, not a hard gate enforced by tooling in this repo).

**Acceptance criteria:**
- `chartData`'s own `useMemo` body is materially shorter than the current 101 lines.
- No behavior change to the rendered chart (dataset order, labels, colors, stacking, tooltip data) as a result of this extraction alone.

### FR-5: No visual or behavioral regression
This is a pure refactor. The rendered chart (bars, stacking order, "Other" grouping, legend labels/colors, tooltips) and the rendered table (row order, per-row values, per-row color dot) must be pixel-for-pixel/value-for-value identical to current behavior for every existing `data.topProducts` ordering that is already consistent with descending `totalMargin` (i.e. the common case today). The only behavior that is allowed, and expected, to change is the latent inconsistency bug itself: if `data.topProducts` from the backend were ever *not* pre-sorted by descending `totalMargin`, the table's color-per-product would now match the chart's (previously it would not) — this is the intended fix, not a regression.

**Acceptance criteria:**
- Existing component/UI behavior for the current (already totalMargin-sorted) backend response order is unchanged.
- No new console warnings/errors introduced.
- No new/removed/reordered chart datasets, table columns, or table rows.

## Non-Functional Requirements

### NFR-1: Performance
No meaningful performance regression. The refactor should not increase the number of `O(n)` or `O(n log n)` passes over `data.topProducts`/`data.monthlyData` beyond what's already done today — ideally it reduces them, since today the sort-by-`totalMargin` and the `findIndex` scan are each recomputed independently; after the change there should be a single sort feeding both consumers. `useMemo` dependency arrays must be correct (no stale-closure bugs, no unnecessary re-computation on unrelated state changes such as `selectedMarginLevel` changes that don't affect `topProducts` shape — though in practice changing any selector triggers a new query result, so this is a minor optimization, not a correctness requirement).

### NFR-2: Code quality / maintainability
This is the primary driver of the change: a single source of truth for product→color mapping, and no `useMemo` body over roughly 50 lines (per the project's general method-length guideline). No new `any`-typed values introduced beyond what already exists in the file (the file already uses `datasets: any[]`; this change does not need to fix that pre-existing typing, per "surgical changes" — do not touch unrelated code).

### NFR-3: No API/contract changes
This is a frontend-only, single-file (plus optionally a small extracted local helper/hook within the same directory) presentation-logic refactor. No backend, DTO, or OpenAPI contract changes are needed or in scope.

## Data Model
No data model changes. Existing types consumed, unchanged:
- `data.topProducts`: array of product-group summaries with `groupKey`, `displayName`, `totalMargin`, `rank`, `m0Amount`/`m1Amount`/`m2Amount`, `m0Percentage`/`m1Percentage`/`m2Percentage`, `sellingPrice`, `purchasePrice` (from `useProductMarginSummaryQuery`'s response type).
- `data.monthlyData`: array of monthly buckets with `monthDisplay`, `productSegments` (each with `groupKey`, `displayName`, `marginContribution`, `percentage`, `averageMarginPerPiece`, `averageSellingPriceWithoutVat`, `unitsSold`, `productCount`, `averageMaterialCosts`, `averageLaborCosts`), and `totalMonthMargin`.

New in-component type (not a DTO, purely local): `productColorMap: Map<string, string>` — a `groupKey → hex color` map, internal to the component (or an extracted local hook's return value). This is not exposed as an API/DTO type, so the "DTOs are classes, never records" rule does not apply — it's a plain `Map`, consistent with the existing `productColorMap`/`productDisplayNames` local `Map` usage already in the file.

## API / Interface Design
No REST/API changes. This is purely an internal refactor of `frontend/src/components/pages/ProductMarginSummary.tsx`. Suggested internal shape (implementer has latitude on exact extraction boundaries per FR-1/FR-4, as long as FR-1–FR-5 acceptance criteria hold):

```ts
// Either as a useMemo inside the component, or extracted to a local hook
// e.g. frontend/src/components/pages/useProductColorMap.ts (co-located, not a shared/global hook
// unless a second consumer emerges — keep it local per "surgical changes" until reuse is proven needed).
const productColorMap = useMemo(() => {
  if (!data?.topProducts) return new Map<string, string>();
  const TOP_CHART_PRODUCTS = 15; // reuse existing constant name/value
  const sorted = [...data.topProducts].sort(
    (a, b) => (b.totalMargin ?? 0) - (a.totalMargin ?? 0),
  );
  const map = new Map<string, string>();
  sorted.forEach((p, i) => {
    if (p.groupKey) {
      map.set(
        p.groupKey,
        i < TOP_CHART_PRODUCTS
          ? PRODUCT_COLORS[i % PRODUCT_COLORS.length]
          : DEFAULT_COLOR,
      );
    }
  });
  return map;
}, [data?.topProducts]);
```

`chartData` and `tableData` then do `productColorMap.get(groupKey) ?? DEFAULT_COLOR` wherever they currently derive color independently.

Whether this becomes a standalone extracted hook file or stays as a `useMemo` directly in `ProductMarginSummary.tsx` is an implementation decision for the developer/architect phase — the brief's suggested fix names it `useProductColorMap`, but a co-located `useMemo` is equally acceptable and simpler; no other component currently needs this mapping, so extracting a shared/exported hook is not required by this spec (extract only if the architecture review calls for it, e.g. for testability).

## Dependencies
None. No new npm packages, no backend changes, no other components depend on or need to change alongside this file today (confirmed no other file imports `PRODUCT_COLORS`, `DEFAULT_COLOR`, or `OTHER_COLOR` from `ProductMarginSummary.tsx` — verify during implementation that this remains true before assuming a fully local change).

## Out of Scope
- Any change to the actual color palette (`PRODUCT_COLORS` values), the top-N threshold (15), or the fallback/"Other" color values.
- Any change to chart type, stacking behavior, tooltip content/formatting, or table columns/layout.
- Any change to `chartOptions`, `formatCurrency`, the loading/error/empty-state JSX, or the controls (time window / grouping mode / margin level selectors).
- Any backend/API/DTO changes.
- Fixing the pre-existing `datasets: any[]` typing looseness (noted but not in scope — "surgical changes" rule; may be worth a follow-up finding if the architect wants to flag it separately).
- Adding new automated tests beyond what's needed to verify this refactor, unless the architect/designer/planner phases determine the existing test coverage for this file is insufficient to safely verify FR-5 (no regression) — if so, adding a focused unit test for `productColorMap`/color consistency between chart and table is in scope as a natural extension of "no regression," not as a general test-coverage initiative for the file.

## Open Questions
None.

## Status: COMPLETE
