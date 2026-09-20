### task: build-product-color-map

**Files:**
- Modify: `frontend/src/components/pages/ProductMarginSummary.tsx:19-39` (constants block)
- Modify: `frontend/src/components/pages/ProductMarginSummary.tsx:66-167` (inside the `chartData` useMemo — this task only adds the new memo, it does not yet rewire `chartData`/`tableData` to use it)

This task adds the canonical `productColorMap` memo and hoists `TOP_CHART_PRODUCTS` to module scope, without yet wiring `chartData`/`tableData` to consume it (that happens in the next two tasks). The regression test added in the previous task is expected to still FAIL after this task — that is correct, do not be alarmed.

- [ ] **Step 1: Hoist `TOP_CHART_PRODUCTS` to module scope**

In `frontend/src/components/pages/ProductMarginSummary.tsx`, add this constant directly after the existing `DEFAULT_COLOR` constant (currently line 39):

```ts
const DEFAULT_COLOR = "#9CA3AF"; // Gray for products not in top 15
const TOP_CHART_PRODUCTS = 15;
```

- [ ] **Step 2: Add the `productColorMap` useMemo**

Immediately inside the component, before the existing `chartData` useMemo (currently starting at line 66), add:

```ts
  // Single canonical productKey -> color mapping, consumed by both chartData and
  // tableData. Sorted descending by totalMargin; top TOP_CHART_PRODUCTS get a distinct
  // palette color, the rest fall back to DEFAULT_COLOR. This map is never itself
  // reassigned DEFAULT_COLOR for a top-N product, so `=== DEFAULT_COLOR` is a safe way
  // to test "is this product in the top N" elsewhere in this file (see buildChartDatasets).
  const productColorMap = useMemo(() => {
    const map = new Map<string, string>();
    if (!data?.topProducts) return map;
    const sorted = [...data.topProducts].sort(
      (a, b) => (b.totalMargin ?? 0) - (a.totalMargin ?? 0),
    );
    sorted.forEach((product, index) => {
      if (product.groupKey) {
        map.set(
          product.groupKey,
          index < TOP_CHART_PRODUCTS
            ? PRODUCT_COLORS[index % PRODUCT_COLORS.length]
            : DEFAULT_COLOR,
        );
      }
    });
    return map;
  }, [data?.topProducts]);

```

- [ ] **Step 3: Run the full frontend build to confirm no syntax/type errors**

Run: `cd frontend && npm run build`
Expected: build succeeds (exit code 0). `productColorMap` is currently unused by `chartData`/`tableData`, so expect a possible ESLint `no-unused-vars` warning at this intermediate step — that's fine, it is resolved in the next two tasks; the build itself must still succeed.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/components/pages/ProductMarginSummary.tsx
git commit -m "refactor: add canonical productColorMap memo (not yet wired)"
```

---

