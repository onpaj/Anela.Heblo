# Implementation: update-e2e-test-assertions

## What was implemented

Updated three locations across two E2E test files to reflect the now-correct behavior after removing the pagination race condition from CatalogList.tsx. Removed all KNOWN BUG / KNOWN APPLICATION BUG comment blocks and updated assertions to match the fixed application behavior.

## Files created/modified

- `frontend/test/e2e/catalog/combined-filters.spec.ts` — removed 7-line KNOWN APPLICATION BUG comment block, changed `expect(currentPage).toBe(2)` to `toBe(1)`, updated "despite pagination bug" comment, updated console.log
- `frontend/test/e2e/catalog/pagination-with-filters.spec.ts` — removed 5-line file-level TODO comment block, removed 17-line KNOWN BUG block + debug `if` block in "stays on page 2 with filter" test (changed `toBe(1)` → `toBe(2)`), removed 11-line KNOWN BUG block in "page size change resets to page 1" test (changed `toBe(2)` → `toBe(1)`), updated console.logs

## Tests

No new tests — the existing test assertions themselves were the change.

## How to verify

Run `./scripts/run-playwright-tests.sh --grep "catalog"` against staging:
- "should reset page to 1 when any filter changes" must pass with `toBe(1)`
- "should stay on page 2 after applying filter and clicking page 2" must pass with `toBe(2)`
- "page size change resets to page 1" must pass with `toBe(1)`

## Notes

Also removed the 5-line file-level TODO comment at the top of `pagination-with-filters.spec.ts` that announced all tests in the file assert buggy behaviour — this was the umbrella comment for all the now-removed bug workarounds.

## PR Summary

Fixes the pagination reset inconsistency in the catalog list by removing a race-condition `useEffect` and updating all E2E test assertions to match the corrected behavior.

### Changes

- `frontend/src/components/pages/CatalogList.tsx` — removed the "Sync URL parameter with page number state" useEffect (lines 257-275) that fired with stale `pageNumber=2` when `searchParams` updated first, causing it to re-write `page=2` after `handleApplyFilters` reset it to 1
- `frontend/test/e2e/catalog/combined-filters.spec.ts` — removed KNOWN APPLICATION BUG block, fixed `toBe(2)` → `toBe(1)` assertion
- `frontend/test/e2e/catalog/pagination-with-filters.spec.ts` — removed KNOWN BUG blocks in two tests, fixed `toBe(1)` → `toBe(2)` for "stays on page 2" test and `toBe(2)` → `toBe(1)` for "page size change resets" test
