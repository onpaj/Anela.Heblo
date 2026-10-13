# Implementation: extract-chart-dataset-builder

## What was implemented
Extracted the dataset-construction logic previously inlined in the `chartData`
`useMemo` in `ProductMarginSummary.tsx` into a new module-level
`buildChartDatasets` helper function, and rewired `chartData` to source colors
from the shared `productColorMap` (introduced by the prior
`build-product-color-map` task) instead of recomputing its own separate
top-N/color assignment from scratch.

Previously `chartData` independently re-sorted `topProducts` by
`totalMargin`, re-sliced the top 15, and re-assigned palette colors by index
inside its own closure (`chartProductColorMap`), duplicating the same
"who is top-N and what color are they" logic that `productColorMap` already
computes canonically. Now `buildChartDatasets` derives the "is this product
'Other'" test from whether `productColorMap` assigned it `DEFAULT_COLOR`
(safe because `productColorMap` never assigns `DEFAULT_COLOR` to a top-N
product — see the comment on `productColorMap`), and reads each top product's
color directly from `productColorMap`. Sort order within the stacked bar
(ascending by `totalMargin` so the highest-margin product renders at the top
of the stack) and the "Other" aggregation logic are preserved exactly as
before.

The module-level `TOP_CHART_PRODUCTS` constant was **not** removed, since it
is still consumed by `productColorMap` (added by the previous task) to decide
the top-N cutoff; `buildChartDatasets` itself never references it directly,
receiving pre-computed colors instead. The task context's line numbers
(66-167) reflected the file's state before `build-product-color-map` added
`productColorMap`, which shifted the `chartData` block to lines 91-191 in the
current file; the actual code content targeted was verified to be identical
before editing.

## Files created/modified
- `frontend/src/components/pages/ProductMarginSummary.tsx` — added the
  module-level `buildChartDatasets(monthlyData, topProducts, productColorMap,
  productDisplayNames)` helper above the component definition; replaced the
  body of the `chartData` `useMemo` to build `productDisplayNames` and
  delegate dataset construction to `buildChartDatasets`, with `productColorMap`
  added to the memo's dependency array.

## Tests
No new tests were added by this task (per task context, this is a pure
refactor). Verified against the existing test suite:
- `frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx`

## How to verify
1. `cd frontend && npm run build` — succeeds (exit code 0), no type/syntax
   errors.
2. `cd frontend && CI=true npm test -- --testPathPattern=ProductMarginSummary`
   — all pre-existing tests pass (`renders loading state`, `renders error
   state`, `renders empty state when no data`, `renders chart with data`,
   `changes time window when dropdown is selected`, `displays summary
   information correctly`, `has proper page structure following layout
   standards`). The regression test added by `add-regression-test`
   (`assigns the same color to a product in the chart legend and the table`)
   still **fails**, as expected: it now fails only on the table-side
   assertion (table still returns `rgb(59, 130, 246)` / `#3B82F6` from its
   own unrelated `PRODUCT_COLORS[index % ...]` logic instead of the
   corrected chart color `#1e40af` from `productColorMap`). This is
   documented in the task context as expected and is fixed by the next task,
   `wire-table-data-to-color-map`.
3. `cd frontend && npm run lint` — no new errors introduced.
   `ProductMarginSummary.tsx` itself produces zero lint findings; the
   pre-existing repo-wide lint error count (240 errors, mostly
   `testing-library/no-node-access` in unrelated test files) is unchanged
   before and after this change (confirmed via `git stash`/`git stash pop`
   comparison).

## Notes
- No local `TOP_CHART_PRODUCTS` declaration was removed, contrary to the
  task context's literal wording, because the only `TOP_CHART_PRODUCTS`
  declaration in the file is the shared module-level constant that
  `productColorMap` (from the prior task) still depends on; removing it
  would break that task's code. The task context's step 1/2 code snippets
  (the actual functional requirement) were followed verbatim and do not
  reference `TOP_CHART_PRODUCTS` from within `buildChartDatasets` or the new
  `chartData` body, so this deviation does not affect behavior.
- `buildChartDatasets` return type is `any[]` per the task context's exact
  snippet (matching the existing `datasets: any[]` pattern already used
  elsewhere in this file).

## Status
DONE
