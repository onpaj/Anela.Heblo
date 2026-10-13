## Module
Analytics

## Finding
`frontend/src/components/pages/ProductMarginSummary.tsx` contains two independent `useMemo` blocks that each implement color assignment for products from `PRODUCT_COLORS`, deriving from overlapping but inconsistently expressed sort/index logic:

**chartData useMemo (lines 66–167):**
```ts
const sortedByTotalMargin = [...data.topProducts].sort(
  (a, b) => (b.totalMargin || 0) - (a.totalMargin || 0)
);
topProductsForChart.forEach((product, index) => {
  productColorMap.set(product.groupKey, PRODUCT_COLORS[index % PRODUCT_COLORS.length]);
});
```

**tableData useMemo (lines 170–207):**
```ts
const productIndex = data.topProducts!.findIndex(
  (tp) => tp.groupKey === product.groupKey,
);
const color = productIndex >= 0
  ? PRODUCT_COLORS[productIndex % PRODUCT_COLORS.length]
  : DEFAULT_COLOR;
```

Both compute a product → color mapping, but do so separately. `chartData` re-sorts `data.topProducts` explicitly; `tableData` relies on the original response order. If the backend's sort order ever differs from the descending totalMargin sort, the two blocks will assign different colors to the same product in the chart legend vs. the table, breaking visual consistency.

The `chartData` useMemo is also 101 lines long (lines 66–167), well over the 50-line method guideline.

## Why it matters
Color assignment is presentation-layer business logic, but it is duplicated across two useMemo blocks in the same file. A change to the coloring algorithm (palette, top-N threshold, fallback color) must be applied in both places. A divergence in sort order between chart and table is an existing silent risk.

## Suggested fix
Extract a single `useProductColorMap` hook (or a `useMemo` that both downstream memos read) that builds the canonical `productKey → color` mapping once:

```ts
const productColorMap = useMemo(() => {
  if (!data?.topProducts) return new Map<string, string>();
  const TOP_N = 15;
  const sorted = [...data.topProducts].sort((a, b) => (b.totalMargin ?? 0) - (a.totalMargin ?? 0));
  const map = new Map<string, string>();
  sorted.forEach((p, i) => {
    if (p.groupKey) map.set(p.groupKey, i < TOP_N ? PRODUCT_COLORS[i] : DEFAULT_COLOR);
  });
  return map;
}, [data?.topProducts]);
```

Then both `chartData` and `tableData` use `productColorMap.get(groupKey) ?? DEFAULT_COLOR`, eliminating the duplication and guaranteeing consistency.

The chart dataset construction logic (currently lines 108–165) can be further extracted into a separate `useMemo` or utility function to keep each block under 50 lines.

---
_Filed by daily arch-review routine on 2026-09-19._
