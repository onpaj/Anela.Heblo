# Code Review: extract-chart-dataset-builder

## Summary
The `chartData` useMemo was cleanly reduced to building `productDisplayNames`
and delegating to the new module-level `buildChartDatasets` helper, which now
sources colors from the shared `productColorMap` (via the `DEFAULT_COLOR`
sentinel to detect "Other" products) instead of recomputing its own top-N
sort/slice/color assignment. The diff matches the task context's Step 1/2
code snippets verbatim, `productColorMap` was correctly added to the memo's
dependency array, and stacking order / "Other" aggregation semantics are
preserved.

## Review Result: PASS

### task: extract-chart-dataset-builder
**Status:** PASS

Verified:
- `npm run build` succeeds with no type/syntax errors.
- `CI=true npm test -- --testPathPattern=ProductMarginSummary`: all
  pre-existing tests pass unchanged (chart rendering/time-window/summary/
  layout tests). The `add-regression-test` regression test still fails as
  expected — now failing only on the table-side color assertion
  (`#3B82F6` vs `#1e40af`), confirming the chart side already reads the
  corrected color from `productColorMap` and the table side is correctly
  left untouched for the next task (`wire-table-data-to-color-map`).
- `npm run lint`: zero findings in the modified file; repo-wide pre-existing
  lint error count is unchanged before/after (confirmed via
  stash/pop comparison in the impl summary).
- The module-level `TOP_CHART_PRODUCTS` constant was correctly left in place
  rather than removed, since `productColorMap` (added by the prior task)
  still depends on it; removing it as the task context's prose literally
  suggested would have broken that prior task's code. The actual functional
  code snippets in the task context (which are the binding part of the spec)
  do not reference `TOP_CHART_PRODUCTS` from the new code, so this deviation
  is correct and does not conflict with the spec's intent.
- `otherKeys`/dataset construction logic is a faithful mechanical
  transcription of the task context's Step 2 snippet.

No blocking issues found.

## Docs to Update
(None — internal refactor of an existing component, no public behavior,
CLI, or documented API changed.)

## Overall Notes
This task deliberately leaves the regression test red on the table-side
assertion; that is expected per the task context and is resolved by the
next task, not a defect of this one.
