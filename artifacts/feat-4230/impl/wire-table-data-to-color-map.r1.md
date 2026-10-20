# Implementation: wire-table-data-to-color-map

## What was implemented

Replaced the `tableData` useMemo's own `findIndex`-based color derivation in
`ProductMarginSummary.tsx` with a lookup into the shared `productColorMap`
(the same map already consumed by `chartData`/`buildChartDatasets`). This is
the exact replacement specified in the task context, applied verbatim.

The `tableData` dependency array was updated from `[data]` to
`[data?.topProducts, productColorMap]` to reflect the map it now reads.

## Files created/modified

- `frontend/src/components/pages/ProductMarginSummary.tsx` — `tableData`
  useMemo now derives `colorCode` via `productColorMap.get(product.groupKey)
  ?? DEFAULT_COLOR` instead of re-deriving an index into `PRODUCT_COLORS`
  from the raw (unsorted) `topProducts` array order.
- `frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx` —
  fixed the `normalize()` helper in the "no chart/table divergence"
  regression test (see Notes below).

## Tests

Ran the full `ProductMarginSummary` test suite:

```
cd frontend && CI=true npm test -- --testPathPattern=ProductMarginSummary
```

Result: `PASS` — all tests pass, including
`"assigns the same color to a product in the chart legend and the table
(no chart/table divergence)"` from `add-regression-test`.

## How to verify

1. `cd frontend && CI=true npm test -- --testPathPattern=ProductMarginSummary`
   — all tests pass.
2. `cd frontend && npm run build` — compiles successfully.
3. `cd frontend && npm run lint` — 253 problems (240 errors, 13 warnings),
   identical count to the pre-change baseline (verified via `git stash` +
   re-lint); no new issues introduced by this change.

## Notes

The task context specified only the `tableData` body replacement, but after
applying it exactly as given the regression test still failed — not because
the wiring is wrong, but because of a pre-existing bug in the regression
test's own `normalize()` helper (written in the earlier `add-regression-test`
task):

- The chart's color comes straight out of a JSON payload as a literal hex
  string, e.g. `"#1e40af"`.
- The table's color is read via `dot.style.backgroundColor` after React sets
  it through an inline `style={{ backgroundColor: row.colorCode }}`; jsdom
  (like real browsers) serializes that back out in `rgb(r, g, b)` form, e.g.
  `"rgb(30, 64, 175)"`.
- `0x1e,0x40,0xaf == 30,64,175` — the two colors were already numerically
  identical once wired to the same map. The test's `normalize()` only
  lowercased the string, so it never reconciled the two formats and failed
  on a real, correct fix.

This went undetected before this task because the actual color values
diverged before the fix (the real bug the test exists to catch), which
masked the format-mismatch bug in the comparison itself. Once the real bug
was fixed, the masking mismatch disappeared and the test's own format bug
surfaced.

Fix: extended `normalize()` to convert a `#rrggbb`/`#rgb` hex string into the
same `rgb(r, g, b)` form jsdom returns, before comparing. Verified the
resulting values match: `#1e40af` -> `rgb(30, 64, 175)`, which now equals the
table's jsdom-serialized value exactly. This is a minimal, targeted change
confined to the one regression test this task is responsible for making
pass — no other test files were touched.

No changes were made to `PRODUCT_COLORS`, `DEFAULT_COLOR`, or
`productColorMap` — only `tableData`'s color derivation and the test's color
comparison helper.

## PR Summary
Fixed the ProductMarginSummary chart/table color divergence by making
`tableData` read `colorCode` from the same `productColorMap` that
`chartData` already uses, instead of independently re-deriving a color
index from `topProducts`' raw (unsorted) order. Also fixed a latent
format-mismatch bug in the regression test's color-comparison helper
(hex vs. jsdom-serialized `rgb()`) that the divergence bug had been masking.

### Changes
- `frontend/src/components/pages/ProductMarginSummary.tsx` — `tableData`
  now derives `colorCode` via `productColorMap.get(product.groupKey) ??
  DEFAULT_COLOR`
- `frontend/src/components/pages/__tests__/ProductMarginSummary.test.tsx` —
  `normalize()` now converts hex colors to `rgb(r, g, b)` before comparing

## Status
DONE
