# Code Review: wire-table-data-to-color-map

## Summary
The `tableData` useMemo was replaced exactly as specified in the task
context, reading `colorCode` from the shared `productColorMap` instead of
re-deriving it from `topProducts`' raw array order. The regression test from
`add-regression-test` now passes. A pre-existing bug in that test's color
comparison helper (hex vs. jsdom-serialized `rgb()` format) was fixed as a
necessary, narrowly-scoped consequence of validating this task's acceptance
criterion; it was not a wiring bug.

## Review Result: PASS

### task: wire-table-data-to-color-map
**Status:** PASS

Verified:
- `tableData`'s color derivation now matches the task context's specified
  replacement verbatim (`productColorMap.get(product.groupKey) ??
  DEFAULT_COLOR`), and the dependency array correctly reflects the map it
  reads (`[data?.topProducts, productColorMap]`).
- No other logic in `tableData` was changed (field mapping, sort by rank,
  defaults) — confirmed by diff.
- `CI=true npm test -- --testPathPattern=ProductMarginSummary` passes, all
  351 suites / 2989 tests (5 skipped), including the divergence regression
  test.
- `npm run build` compiles successfully.
- `npm run lint` shows the identical problem count (253: 240 errors, 13
  warnings) before and after this change (confirmed via `git stash`
  comparison) — no new lint issues introduced.
- The additional edit to the test file's `normalize()` helper is in scope:
  it does not change what the test asserts (same-color-as-chart,
  different-color-from-each-other), only how two equivalent color string
  representations (`#rrggbb` vs. `rgb(r, g, b)`) are compared. The
  underlying color values were independently verified to match
  (`#1e40af` == `rgb(30, 64, 175)`), so this is a comparison-format fix, not
  a weakening of the assertion.

## Docs to Update
(none — this is an internal component refactor with no public behaviour,
API, or operational change)

## Overall Notes
None. The task's acceptance criterion ("regression test now passes") is met
for the right reason: the actual chart/table color divergence bug is fixed,
verified against real numeric color equality, not by loosening the test.
