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

