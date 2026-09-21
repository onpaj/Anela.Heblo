### task: wire-table-data-to-color-map

**Files:**
- Modify: `frontend/src/components/pages/ProductMarginSummary.tsx:170-207` (replace the body of `tableData`)

This task removes `tableData`'s own `findIndex`-based color derivation and reads `colorCode` from `productColorMap` instead. After this task, the regression test added in `add-regression-test` is expected to PASS.

- [ ] **Step 1: Replace the `tableData` useMemo body**

Replace the entire existing `tableData` useMemo (currently lines 170–207, from `const tableData = useMemo(() => {` through the closing `}, [data]);`) with:

```ts
  // Prepare table data using topProducts which already contain all M0-M2 data
  const tableData = useMemo(() => {
    if (!data?.topProducts) return [];

    return data.topProducts
      .map((product) => {
        const color = product.groupKey
          ? (productColorMap.get(product.groupKey) ?? DEFAULT_COLOR)
          : DEFAULT_COLOR;

        return {
          groupKey: product.groupKey || "",
          displayName: product.displayName || "",
          colorCode: color,
          totalMargin: product.totalMargin || 0,
          rank: product.rank || 0,

          // M0-M2 margin levels - amounts
          m0Amount: product.m0Amount || 0,
          m1Amount: product.m1Amount || 0,
          m2Amount: product.m2Amount || 0,

          // M0-M2 margin levels - percentages
          m0Percentage: product.m0Percentage || 0,
          m1Percentage: product.m1Percentage || 0,
          m2Percentage: product.m2Percentage || 0,

          // Pricing
          sellingPrice: product.sellingPrice || 0,
          purchasePrice: product.purchasePrice || 0,
        };
      })
      .sort((a, b) => a.rank - b.rank); // Sort by rank from backend
  }, [data?.topProducts, productColorMap]);
```

- [ ] **Step 2: Run the test file to confirm the regression test now passes**

Run: `cd frontend && CI=true npm test -- --testPathPattern=ProductMarginSummary`
Expected: PASS — all tests in `ProductMarginSummary.test.tsx` pass, including `"assigns the same color to a product in the chart legend and the table (no chart/table divergence)"` from `add-regression-test`.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/pages/ProductMarginSummary.tsx
git commit -m "refactor: read table row colors from productColorMap, fixing chart/table divergence"
```

---

