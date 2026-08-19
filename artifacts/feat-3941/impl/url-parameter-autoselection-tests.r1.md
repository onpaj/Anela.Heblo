# Implementation: url-parameter-autoselection-tests

## What was implemented

Added a `URL parameter auto-selection` describe block to
`ManufactureBatchCalculator.test.tsx`, exercising FR-3: the mount-time
`useEffect` (lines 117–136) that auto-selects a product from
`?productCode=X&batchSize=Y` in the URL. The single test case:

- Renders the component inside a `MemoryRouter` with
  `initialEntries={['/manufacturing/batch-calculator?productCode=URLPROD&batchSize=500']}`
  routed through `Routes`/`Route`.
- Confirms `getBatchTemplate` is called with the URL's `productCode`
  (`'URLPROD'`).
- Confirms `calculateBySize` is called with the URL's `batchSize` (`500`),
  not the template's `newBatchSize` (`1000`) — verifying the precedence
  documented in `handleProductSelect` (lines 78–92): URL `batchSize` wins
  whenever `parseFloat(urlBatchSize) > 0`.
- Asserts the batch size input displays `500` and does not display `1000`.
- Asserts `selectedProduct.productName` is seeded from the URL's
  `productCode` and stays `'URLPROD'` (via the mocked
  `catalog-autocomplete-value` testid) — unchanged existing behavior per
  spec.r1.md FR-3's fourth acceptance criterion.

A code comment documents the one FR-3 case intentionally left uncovered:
re-triggering auto-selection when `selectedProduct` is already set is
guarded by `!selectedProduct`, but deterministically testing it would
require pausing the async `handleProductSelect` chain mid-flight, which is
racy with React Testing Library's async utilities. This was called out as
"not covered" in spec.r1.md, so no test was forced for it.

## Files created/modified

- `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx` — added the `URL parameter auto-selection` describe block (one test case) at the end of the file, per the task-context's exact instructions.

## Tests

- `ManufactureBatchCalculator.test.tsx` — new test:
  `auto-selects the product from ?productCode&batchSize and overrides the
  template default batch size`.

## How to verify

```bash
cd frontend
npm install --legacy-peer-deps   # node_modules were not present in this worktree
CI=true npm test -- --watchAll=false ManufactureBatchCalculator.test.tsx
```

Result:

```
PASS src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx (5.085 s)
...
Test Suites: 1 passed, 1 total
Tests:       17 passed, 17 total
```

All 17 tests pass, exactly matching the task-context's expected output
(17 passed, 17 total).

## Notes

- `node_modules` did not exist in this worktree. Installed with
  `npm install --legacy-peer-deps`, matching the flag used by
  `.github/workflows/ci-feature-branch.yml` and by the prior
  `test-infrastructure-mocks` and `computepercentage-infinity-tests` tasks
  on this same feature (see their `impl/*.r1.md` notes) — a plain
  `npm install`/`npm ci` fails on an existing peer-dependency conflict
  between `@types/node` versions (`knip` vs. the root project), unrelated
  to this change.
- No deviations from the task-context's specified code — the added block
  is byte-for-byte the snippet given in Step 1.
- Did not run `npm run lint` / `npm run build` — out of scope for this
  task, which only touches the test file with no source changes.

## PR Summary
Added a test covering FR-3's URL-driven product auto-selection: navigating to `?productCode=X&batchSize=Y` auto-selects the product on mount and the URL's `batchSize` overrides the template's `newBatchSize` default. This closes one of the two remaining coverage gaps identified in the feature's spec for `ManufactureBatchCalculator.tsx`.

### Changes
- `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx` — added `URL parameter auto-selection` describe block with one test case

## Status
DONE
