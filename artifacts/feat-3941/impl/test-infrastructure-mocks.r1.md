# Implementation: test-infrastructure-mocks

## What was implemented
Restructured the two `jest.mock` factories at the top of
`ManufactureBatchCalculator.test.tsx` so later tasks can configure the
`useManufactureBatch` hook's return values (`getBatchTemplate`, `calculateBySize`,
`calculateByIngredient`) per test, and so tests can simulate a manual product
selection through `CatalogAutocomplete`.

- The `useManufactureBatch` mock now returns module-scoped `jest.fn()` refs
  (`mockGetBatchTemplate`, `mockCalculateBySize`, `mockCalculateByIngredient`)
  instead of inline `jest.fn().mockResolvedValue(...)` calls, so individual tests
  can call `.mockResolvedValueOnce(...)` / `.mockResolvedValue(...)` on them.
- The `CatalogAutocomplete` mock now renders the seeded `value.productName` (for
  assertions on what the calculator passes down as `value`) and a
  `data-testid="catalog-autocomplete-select"` button that invokes the real
  `onSelect` prop with a module-scoped `mockAutocompleteProduct`, exposed via the
  new `triggerProductSelect(product)` helper.
- Added a `beforeEach` that resets all three hook mocks to their previous default
  (`{ success: false }`) and clears `mockAutocompleteProduct`, so tests don't leak
  state into one another.
- Added a shared `testProduct` fixture (`CatalogItemDto` with
  `type: ProductType.SemiProduct`) for later tasks to reuse.
- Added the new imports required by the above: `fireEvent`, `waitFor`,
  `MemoryRouter`, `Routes`, `Route`, and `CatalogItemDto` / `ProductType` from
  `../../../api/generated/api-client`. `MemoryRouter`, `Routes`, `Route`, `waitFor`,
  `triggerProductSelect`, and `testProduct` are unused by the existing smoke test —
  they are scaffolding for later tasks in this feature, per the task spec.

No new test cases were added; the existing `describe('computePercentage helper', ...)`
block and the single `ManufactureBatchCalculator` smoke test were left untouched, as
instructed.

## Files created/modified
- `frontend/src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx` —
  restructured mock declarations, added `beforeEach` reset, `triggerProductSelect`
  helper, and `testProduct` fixture. Rest of the file (all `describe` blocks) unchanged.

## Tests
No new tests were added in this task. Verified the existing suite still passes
unchanged:
- `computePercentage helper` — 10 existing unit test cases (unchanged).
- `ManufactureBatchCalculator` — 1 existing smoke test (unchanged): renders without
  crashing and shows no `%` column header in the empty state.

## How to verify
```bash
cd frontend
npm install --legacy-peer-deps   # node_modules were not present in this worktree
CI=true npm test -- --watchAll=false ManufactureBatchCalculator.test.tsx
```
Expected: `Test Suites: 1 passed, 1 total`, `Tests: 11 passed, 11 total`.

Actual output obtained:
```
PASS src/components/pages/__tests__/ManufactureBatchCalculator.test.tsx (5.689 s)
  computePercentage helper
    ✓ (10 tests)
  ManufactureBatchCalculator
    ✓ renders without crashing and shows no percentage column header in empty state (113 ms)

Test Suites: 1 passed, 1 total
Tests:       11 passed, 11 total
```

## Notes
- `node_modules` did not exist in this worktree. Plain `npm ci` / `npm install`
  failed with an ERESOLVE peer-dependency conflict between `knip@5.88.1` (wants
  `@types/node >= 18`, resolved to `@types/node@26.2.0`) and the Jest 27 toolchain
  transitively requiring `@types/node@^16.18.108`. This is a pre-existing conflict
  in `frontend/package.json`, unrelated to this task's change. Installed with
  `npm install --legacy-peer-deps` to unblock running the test suite; did not
  modify `package.json` / `package-lock.json`. This same conflict would block
  `npm run build` if it were run without `--legacy-peer-deps`, so it may be worth
  flagging separately (not fixed here, per the "surgical changes" rule — out of
  scope for this task).
- `artifacts/feat-3941/state.json` showed as modified in `git status` (pipeline-
  managed file, not touched by me) — left untouched and not included in the commit,
  per the task's explicit `git add` instruction (only the test file).
- Did not run `npm run lint` / `npm run build` per this task's scope, which only
  specifies running the Jest suite for this file; several of the newly imported
  names (`MemoryRouter`, `Routes`, `Route`, `waitFor`, `triggerProductSelect`,
  `testProduct`) are intentionally unused until later tasks consume them, exactly
  as instructed in the task spec.

## Status
DONE
