# Unify Product Color-Mapping Logic in ProductMarginSummary.tsx Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the two independent, duplicated product→color derivations in `ProductMarginSummary.tsx` (one in the `chartData` memo, one in the `tableData` memo) with a single canonical `productColorMap` memo that both consume, eliminating the latent chart/table color-divergence bug and shrinking the oversized `chartData` memo below the project's ~50-line guideline.

**Architecture:** Everything stays inside the single existing file `frontend/src/components/pages/ProductMarginSummary.tsx` (per arch-review Decision 1 — no new file, single consumer). A new `productColorMap` `useMemo` (keyed off `data?.topProducts`, sorted descending by `totalMargin`, top 15 get `PRODUCT_COLORS[index % length]`, rest get `DEFAULT_COLOR`) becomes the sole place that reads `PRODUCT_COLORS`. `chartData`'s dataset-construction logic is pulled into a plain module-level helper `buildChartDatasets(...)` (not independently memoized — arch-review Decision 2) which determines top-15/"Other" membership via `productColorMap.get(groupKey) === DEFAULT_COLOR` rather than re-sorting (arch-review Decision 3). `tableData` reads `colorCode` from `productColorMap` instead of `findIndex`-ing the raw array.

**Tech Stack:** React 18 + TypeScript, `react-chartjs-2`/`chart.js`, `@tanstack/react-query`, Jest + React Testing Library (`react-scripts test`), ESLint (`npm run lint`).

---

## Reference: current file state (before this plan)

`frontend/src/components/pages/ProductMarginSummary.tsx`, relevant existing ranges (read in full before starting — do not skip this):
- Lines 19–39: `PRODUCT_COLORS`, `OTHER_COLOR`, `DEFAULT_COLOR` module-level constants.
- Lines 66–167: `chartData` useMemo (contains the color logic to remove, plus dataset construction to extract).
- Lines 170–207: `tableData` useMemo (contains the color logic to remove).

`frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx`: existing test file, 212 lines, single-product `mockData`. Do not remove or break any existing test in this file.

---

### task: add-regression-test

**Files:**
- Modify: `frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx`

This task writes a test that reproduces the chart/table color-divergence bug described in the brief, on the **current, unmodified** component. It must fail before any production code changes, proving the bug is real, then must pass once later tasks land the fix.

- [ ] **Step 1: Write the failing test**

Add this new test data constant and test case at the end of the `describe("ProductMarginSummary", ...)` block in `frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx` (after the last existing `it(...)`, before the closing `});` of the `describe`):

```tsx
  it("assigns the same color to a product in the chart legend and the table (no chart/table divergence)", () => {
    // topProducts is intentionally NOT sorted by totalMargin descending — this is the
    // shape that previously caused chartData (which re-sorts) and tableData (which used
    // the raw array order) to disagree on which color belongs to which product.
    const divergentMockData = {
      monthlyData: [
        {
          year: 2024,
          month: 3,
          monthDisplay: "Bře 2024",
          productSegments: [
            {
              groupKey: "PROD_LOW",
              displayName: "Low Margin Product",
              marginContribution: 500,
              percentage: 25,
              colorCode: "#000000",
              averageMarginPerPiece: 50,
              unitsSold: 10,
              averageSellingPriceWithoutVat: 100,
              averageMaterialCosts: 20,
              averageLaborCosts: 10,
              isOther: false,
            },
            {
              groupKey: "PROD_HIGH",
              displayName: "High Margin Product",
              marginContribution: 1500,
              percentage: 75,
              colorCode: "#000000",
              averageMarginPerPiece: 150,
              unitsSold: 10,
              averageSellingPriceWithoutVat: 300,
              averageMaterialCosts: 60,
              averageLaborCosts: 30,
              isOther: false,
            },
          ],
          totalMonthMargin: 2000,
        },
      ],
      // Order deliberately does NOT match descending totalMargin: PROD_LOW (500) is listed
      // before PROD_HIGH (1500).
      topProducts: [
        { groupKey: "PROD_LOW", displayName: "Low Margin Product", totalMargin: 500, rank: 2 },
        { groupKey: "PROD_HIGH", displayName: "High Margin Product", totalMargin: 1500, rank: 1 },
      ],
      totalMargin: 2000,
      timeWindow: "current-year",
      fromDate: "2024-01-01T00:00:00",
      toDate: "2024-12-31T23:59:59",
    };

    mockUseProductMarginSummary.mockReturnValue({
      data: divergentMockData,
      isLoading: false,
      error: null,
    } as any);

    const { container } = render(<ProductMarginSummary />, {
      wrapper: createWrapper(),
    });

    // Read the color the chart assigned to each product from the mocked Chart's
    // data-chart-data JSON payload (datasets[].label / backgroundColor).
    const chartEl = screen.getByTestId("chart");
    const chartPayload = JSON.parse(
      chartEl.getAttribute("data-chart-data") || "{}",
    );
    const chartColorByLabel: Record<string, string> = {};
    for (const dataset of chartPayload.datasets) {
      chartColorByLabel[dataset.label] = dataset.backgroundColor;
    }

    // Read the color the table assigned to each product from the rendered color dot
    // (the small rounded div immediately preceding the product name in each row).
    const tableColorFor = (displayName: string): string => {
      const nameEl = screen.getByText(displayName);
      const row = nameEl.closest("tr");
      if (!row) throw new Error(`No <tr> found for ${displayName}`);
      const dot = row.querySelector("div.rounded-full") as HTMLElement | null;
      if (!dot) throw new Error(`No color dot found for ${displayName}`);
      return dot.style.backgroundColor.toLowerCase();
    };

    const normalize = (hex: string) => hex.toLowerCase();

    expect(normalize(tableColorFor("High Margin Product"))).toBe(
      normalize(chartColorByLabel["High Margin Product"]),
    );
    expect(normalize(tableColorFor("Low Margin Product"))).toBe(
      normalize(chartColorByLabel["Low Margin Product"]),
    );
    // The two products must not have been assigned the same color as each other.
    expect(
      normalize(tableColorFor("High Margin Product")),
    ).not.toBe(normalize(tableColorFor("Low Margin Product")));

    // Reference implementation avoids the JSDOM container variable being unused.
    expect(container).toBeTruthy();
  });
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && CI=true npm test -- --testPathPattern=ProductMarginSummary`
Expected: FAIL — the new test `"assigns the same color to a product in the chart legend and the table (no chart/table divergence)"` fails, because on the current (unmodified) component, `chartData` sorts by `totalMargin` descending (`High Margin Product` → `PRODUCT_COLORS[0]`, `#1E40AF`) while `tableData` colors by raw array position (`Low Margin Product` → `PRODUCT_COLORS[0]`, `#1E40AF`) — the two `expect(...).toBe(...)` color-equality assertions fail. All other existing tests in the file must still PASS.

- [ ] **Step 3: Commit**

```bash
git add frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx
git commit -m "test: add failing regression test for chart/table color divergence"
```

---

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

### task: final-validation

**Files:**
- No file changes in this task — verification only.

This task runs the full validation suite required before declaring the change done, per project convention (`CLAUDE.md` → Validation before completion: FE `npm run build` + `npm run lint`, all touched tests pass). No backend files are touched by this feature, so BE validation (`dotnet build`/`dotnet format`) is not applicable and may be skipped.

- [ ] **Step 1: Run the full frontend build**

Run: `cd frontend && npm run build`
Expected: exit code 0, no TypeScript errors.

- [ ] **Step 2: Run lint**

Run: `cd frontend && npm run lint`
Expected: exit code 0. If lint reports a pre-existing issue unrelated to this change (e.g. the pre-existing `datasets: any[]` typing), do not fix it — out of scope per spec (`Out of Scope` section); only fix lint issues on lines this plan's tasks touched.

- [ ] **Step 3: Run the full frontend test suite (not just this one file), to confirm no unrelated regression**

Run: `cd frontend && CI=true npm test -- --watchAll=false`
Expected: all tests pass, including every test in `ProductMarginSummary.test.tsx`.

- [ ] **Step 4: Manually re-read the final `chartData`/`tableData`/`productColorMap`/`buildChartDatasets` code in `frontend/src/components/pages/ProductMarginSummary.tsx` and confirm against spec FR-1–FR-5**

Checklist (confirm each, no code changes expected unless a gap is found):
- FR-1: exactly one place reads `PRODUCT_COLORS[index % PRODUCT_COLORS.length]` (inside `productColorMap`). ✅ if true.
- FR-2: neither `chartData` nor `tableData` independently sorts/indexes `data.topProducts` for color purposes anymore. ✅ if true.
- FR-3: `DEFAULT_COLOR` and `OTHER_COLOR` both still exist as separate named constants, both still `#9CA3AF`. ✅ if true.
- FR-4: no single `useMemo` body in the file is anywhere near the old 101-line `chartData` length. ✅ if true.
- FR-5: single-product existing tests still pass unchanged (confirmed in Step 3).

If any checklist item fails, fix it before considering the plan complete — do not commit a partial fix as done.

- [ ] **Step 5: Commit (only if Step 4 required a fix; otherwise this task has nothing new to commit)**

```bash
git status --short frontend/
# If clean, no commit needed for this task.
# If Step 4 required a fix:
git add frontend/src/components/pages/ProductMarginSummary.tsx
git commit -m "fix: address final-validation checklist gap"
```

---

## Self-Review

**1. Spec coverage:**
- FR-1 (single canonical color-mapping source) → `build-product-color-map` task.
- FR-2 (`chartData`/`tableData` consume the shared map) → `extract-chart-dataset-builder` + `wire-table-data-to-color-map` tasks.
- FR-3 (`DEFAULT_COLOR`/`OTHER_COLOR` reconciliation — keep both, no merge) → explicitly preserved in `build-product-color-map` (Step 1 keeps both constants) and `extract-chart-dataset-builder` (helper still uses `OTHER_COLOR` for the "Other" dataset, `DEFAULT_COLOR` for per-product fallback).
- FR-4 (reduce `chartData` below ~50 lines) → `extract-chart-dataset-builder` task (dataset construction moved to `buildChartDatasets`).
- FR-5 (no visual/behavioral regression) → verified by existing single-product tests staying green throughout (`extract-chart-dataset-builder` Step 4, `wire-table-data-to-color-map` Step 2, `final-validation` Step 3), plus the explicit FR-5 checklist item in `final-validation` Step 4.
- NFR-1 (performance: single sort, no redundant passes) → arch-review Decision 3, implemented via `otherKeys`/`topProductEntries` derived from `productColorMap` in `buildChartDatasets`, no second independent `[...data.topProducts].sort(by totalMargin)` call anywhere.
- NFR-2 (maintainability, ~50-line guideline) → covered by FR-4 task.
- NFR-3 (no API/contract changes) → no task touches backend, `contracts/`, or DTOs — confirmed by file list across all tasks (frontend-only).
- Architecture review Decision 1 (no new file/hook) → all tasks modify only `ProductMarginSummary.tsx` and its co-located test file.
- Architecture review Decision 2 (plain helper, not a second memo) → `buildChartDatasets` is a plain function, not wrapped in its own `useMemo`.
- Architecture review Decision 3 (`=== DEFAULT_COLOR` membership test, not re-sort) → implemented exactly this way in `buildChartDatasets`.
- Architecture review Specification Amendment 3 (extend test coverage for multi-product color consistency) → `add-regression-test` task.

No spec or arch-review requirement was found without a corresponding task.

**2. Placeholder scan:** No "TBD"/"TODO"/"implement later" strings. Every code step shows complete, copy-pasteable code, not a description of what to write. Every test step shows the actual assertions, not "add appropriate assertions."

**3. Type consistency:** `productColorMap: Map<string, string>` is defined once in `build-product-color-map` and referenced with that exact name and type in `extract-chart-dataset-builder` and `wire-table-data-to-color-map`. `buildChartDatasets` is defined with that exact name/signature in `extract-chart-dataset-builder` and called with that exact name in the same task's `chartData` body (not referenced elsewhere). `TOP_CHART_PRODUCTS`, `DEFAULT_COLOR`, `OTHER_COLOR`, `PRODUCT_COLORS` are used consistently by their existing names throughout — no renamed constants introduced without updating every reference in this plan.
