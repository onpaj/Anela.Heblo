# Implementation: add-regression-test

## What was implemented

Added a new regression test to `ProductMarginSummary.test.tsx` that reproduces the
chart/table color-divergence bug on the current, unmodified component. The test
supplies `topProducts` in an order that does **not** match descending `totalMargin`
(the shape that exposes the bug: `chartData` re-sorts by margin, while `tableData`
colors by raw array position), renders the component, reads the color the chart
assigned to each product from the mocked `Chart`'s `data-chart-data` payload, reads
the color the table assigned from the rendered color dot next to each product name,
and asserts the two must match for both products and that the two products must not
share a color.

## Files created/modified

- `frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx` — added the
  test case `"assigns the same color to a product in the chart legend and the table
  (no chart/table divergence)"` at the end of the `describe("ProductMarginSummary", ...)`
  block, exactly as specified in the task context.

## Tests

- The new test was run in isolation via
  `cd frontend && CI=true npm test -- --testPathPattern=ProductMarginSummary`.
- Result: **FAILS** as expected — `expect(normalize(tableColorFor("High Margin
  Product"))).toBe(normalize(chartColorByLabel["High Margin Product"]))` fails
  (`Expected: "#1e40af"`, `Received: "rgb(59, 130, 246)"`), proving the chart and
  table currently disagree on which color belongs to "High Margin Product" when
  `topProducts` isn't pre-sorted by margin descending.
- All 350 other pre-existing test suites in the run still passed (2988 passed / 1
  failed / 5 skipped, out of 2994 total) — no regressions to existing coverage.

## How to verify

1. `cd frontend`
2. `npm ci --legacy-peer-deps` (this environment's lockfile has a pre-existing
   `knip` vs. jest peer-dependency conflict that requires `--legacy-peer-deps`;
   unrelated to this task)
3. `CI=true npm test -- --testPathPattern=ProductMarginSummary`
4. Confirm the new test fails with a color mismatch, and every other test in the
   file/suite still passes.

## Notes

- This task intentionally does not touch any production code — it only adds a
  failing test that documents the bug, per the task context's Goal. Later tasks in
  this feature (`build-product-color-map`, `extract-chart-dataset-builder`,
  `wire-table-data-to-color-map`) are expected to make this test pass.
- `npm ci` alone fails in this environment due to a pre-existing lockfile conflict
  between `knip@5.88.1` (wants `@types/node@>=18`, resolves to `26.6.2`) and
  `jest@27.5.1`'s toolchain (via `jest-watch-typeahead`). This is unrelated to this
  change; `--legacy-peer-deps` was used only to install dependencies locally to run
  the test and was not persisted anywhere (no `.npmrc` or lockfile change made).

## PR Summary
Added a regression test to `ProductMarginSummary.test.tsx` proving that the chart
and the summary table currently assign different colors to the same product when
the API's `topProducts` array isn't pre-sorted by margin descending. This is the
first task of the fix for issue #4230 (duplicated, divergent color-assignment logic
across two `useMemo` blocks); it deliberately fails against the current component
and will be turned green by the follow-up tasks that unify the color assignment.

### Changes
- `frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx` — added
  `"assigns the same color to a product in the chart legend and the table (no
  chart/table divergence)"` test case

## Status
DONE
