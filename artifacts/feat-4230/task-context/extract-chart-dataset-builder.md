### task: extract-chart-dataset-builder

**Files:**
- Modify: `frontend/src/components/pages/ProductMarginSummary.tsx:66-167` (replace the body of `chartData`, remove the local `TOP_CHART_PRODUCTS` declaration, add a new module-level `buildChartDatasets` function)

This task removes `chartData`'s own color/sort logic and delegates dataset construction to a new plain helper function, wiring `chartData` to read colors from `productColorMap`. After this task, the regression test's chart-side assertions read from the corrected map, but the table side is still using the old logic — the regression test is expected to still FAIL after this task (table color remains wrong); that's corrected in the next task.

- [ ] **Step 1: Replace the `chartData` useMemo body**

Replace the entire existing `chartData` useMemo (currently lines 66–167, from `const chartData = useMemo(() => {` through the closing `}, [data]);`) with:

```ts
  const chartData = useMemo(() => {
    if (!data?.monthlyData || !data?.topProducts) return null;

    const labels = data.monthlyData.map((m) => m.monthDisplay);

    // Collect product display names
    const productDisplayNames = new Map<string, string>();
    data.monthlyData.forEach((month) => {
      month.productSegments?.forEach((segment) => {
        if (segment.groupKey && segment.displayName) {
          productDisplayNames.set(segment.groupKey, segment.displayName);
        }
      });
    });

    const datasets = buildChartDatasets(
      data.monthlyData,
      data.topProducts,
      productColorMap,
      productDisplayNames,
    );

    return { labels, datasets };
  }, [data, productColorMap]);
```

- [ ] **Step 2: Add the `buildChartDatasets` module-level helper function**

Add this function above the `ProductMarginSummary` component definition (i.e. above `const ProductMarginSummary: React.FC = () => {`, after the `DEFAULT_COLOR`/`TOP_CHART_PRODUCTS` constants):

```ts
function buildChartDatasets(
  monthlyData: NonNullable<
    ReturnType<typeof useProductMarginSummaryQuery>["data"]
  >["monthlyData"],
  topProducts: NonNullable<
    ReturnType<typeof useProductMarginSummaryQuery>["data"]
  >["topProducts"],
  productColorMap: Map<string, string>,
  productDisplayNames: Map<string, string>,
): any[] {
  const datasets: any[] = [];

  // A product is "Other" iff productColorMap did not give it a distinct top-N color.
  // Safe because DEFAULT_COLOR is never assigned to a top-N product (see productColorMap).
  const otherKeys = new Set(
    (topProducts || [])
      .filter(
        (p) => p.groupKey && productColorMap.get(p.groupKey) === DEFAULT_COLOR,
      )
      .map((p) => p.groupKey)
      .filter(Boolean),
  );

  // Add "Other" category first (will be at bottom of stack)
  if (otherKeys.size > 0) {
    datasets.push({
      label: "Ostatní produkty",
      data: (monthlyData || []).map((month) => {
        const otherMargin =
          month.productSegments?.reduce((sum, segment) => {
            if (segment.groupKey && otherKeys.has(segment.groupKey)) {
              return sum + (segment.marginContribution || 0);
            }
            return sum;
          }, 0) || 0;
        return otherMargin;
      }),
      backgroundColor: OTHER_COLOR,
      borderColor: OTHER_COLOR,
      borderWidth: 1,
    });
  }

  // Top products, ordered ascending by totalMargin (lowest first) so the highest-margin
  // product renders at the top of the stacked bar, exactly as before this refactor.
  const topProductEntries = (topProducts || [])
    .filter((p) => p.groupKey && !otherKeys.has(p.groupKey))
    .sort((a, b) => (a.totalMargin ?? 0) - (b.totalMargin ?? 0));

  topProductEntries.forEach((product) => {
    const productKey = product.groupKey;
    if (!productKey) return;
    const color = productColorMap.get(productKey) ?? DEFAULT_COLOR;
    const displayName = productDisplayNames.get(productKey) || productKey;

    datasets.push({
      label: displayName,
      data: (monthlyData || []).map((month) => {
        const segment = month.productSegments?.find(
          (s) => s.groupKey === productKey,
        );
        return segment?.marginContribution || 0;
      }),
      backgroundColor: color,
      borderColor: color,
      borderWidth: 1,
    });
  });

  return datasets;
}

```

- [ ] **Step 3: Run the full frontend build to confirm no syntax/type errors**

Run: `cd frontend && npm run build`
Expected: build succeeds (exit code 0).

- [ ] **Step 4: Run the test file to confirm chart-side behavior is intact**

Run: `cd frontend && CI=true npm test -- --testPathPattern=ProductMarginSummary`
Expected: the pre-existing tests (`renders chart with data`, `changes time window...`, etc.) still PASS — chart rendering/stacking is unchanged for the single-product case. The new regression test from `add-regression-test` still FAILS at this point (table-side assertions), because `tableData` has not been updated yet — this is expected and corrected in the next task.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/pages/ProductMarginSummary.tsx
git commit -m "refactor: extract buildChartDatasets helper, wire chartData to productColorMap"
```

---

