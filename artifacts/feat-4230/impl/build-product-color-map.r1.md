# Implementation: build-product-color-map

## What was implemented

Added the canonical `productColorMap` `useMemo` in `ProductMarginSummary.tsx`,
hoisted `TOP_CHART_PRODUCTS` to module scope, and left `chartData`/`tableData`
unwired to it (per the task context, wiring happens in the next two tasks:
`extract-chart-dataset-builder` and `wire-table-data-to-color-map`).

- `TOP_CHART_PRODUCTS = 15` is now a module-scope constant, declared right
  after `DEFAULT_COLOR`, matching the value previously hardcoded inline
  inside the `chartData` memo.
- The new `productColorMap` memo builds a `Map<groupKey, color>` sorted
  descending by `totalMargin`, assigning a distinct palette color to the top
  `TOP_CHART_PRODUCTS` products and `DEFAULT_COLOR` to the rest. It depends
  only on `data?.topProducts`.
- The pre-existing `chartData` memo still builds its own local color map for
  now (as instructed — this task doesn't rewire it). Its local variable was
  renamed from `productColorMap` to `chartProductColorMap` to avoid
  colliding with the new outer `productColorMap` memo of the same name; all
  three internal references (`.set(...)` at the top-products loop and
  `.get(...)` at the dataset-building loop) were updated to match. This is a
  pure rename with no behavior change — `chartData`'s output is identical to
  before.

## Files created/modified

- `frontend/src/components/pages/ProductMarginSummary.tsx` — hoisted
  `TOP_CHART_PRODUCTS`, added the new `productColorMap` memo, renamed the
  pre-existing local map inside `chartData` to `chartProductColorMap` to
  avoid a naming collision.

## Tests

No new tests added by this task (the regression test was added in the prior
`add-regression-test` task). Ran the existing test suite for this component:

```
CI=true npx react-scripts test src/components/pages/__tests__/ProductMarginSummary.test.tsx --watchAll=false
```

Result: 7 passed, 1 failed — the regression test
`assigns the same color to a product in the chart legend and the table (no
chart/table divergence)` still fails, exactly as expected per the task
context (the new map isn't consumed by `tableData` yet, so the divergence
bug is unchanged by this task).

## How to verify

1. `cd frontend && npm run build` — succeeds (exit 0), with an expected
   `@typescript-eslint/no-unused-vars` warning on `productColorMap` (it has
   no consumers yet).
2. `CI=true npx react-scripts test src/components/pages/__tests__/ProductMarginSummary.test.tsx --watchAll=false`
   — 7/8 tests pass; the color-divergence regression test still fails as
   expected.

## Notes

No deviations from the task context. The only addition beyond the literal
task-context diff was the `chartProductColorMap` rename inside `chartData`,
which was necessary to avoid a duplicate `const productColorMap` declaration
in the same component scope — not mentioned explicitly in the task context
but required for the file to compile, and scoped to a pure rename with
identical runtime behavior.

## PR Summary
Added a canonical `productColorMap` memo to `ProductMarginSummary.tsx`, computed once from `topProducts` sorted by `totalMargin`, as the future single source of truth for chart/table color assignment. This task intentionally does not wire it into `chartData` or `tableData` yet — that lands in the next two tasks — so the previously-added regression test proving the chart/table color divergence continues to fail as expected.

### Changes
- `frontend/src/components/pages/ProductMarginSummary.tsx` — hoisted `TOP_CHART_PRODUCTS` to module scope, added `productColorMap` useMemo, renamed the pre-existing in-`chartData` local map to `chartProductColorMap` to avoid a name collision (no behavior change)

## Status
DONE
