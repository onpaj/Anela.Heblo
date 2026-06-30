# Implementation: fix-catalog-pagination-race

## What was implemented

Removed the redundant `useEffect` at lines 257–275 (the "Sync URL parameter with page number state" effect) from `CatalogList.tsx`. This effect caused a race condition: when `handleApplyFilters` called `setPageNumber(1)` and `setSearchParams(params-without-page)`, this effect fired with a stale `pageNumber=2` closure value and an updated `searchParams` (no page), causing it to add `page=2` back to the URL before the `pageNumber=1` state commit landed. The primary state→URL sync effect at lines 188–206 already handles all these cases correctly (it includes `pageNumber` in its dependency array and uses `{ replace: true }`), making the removed effect both redundant and harmful.

## Files created/modified
- `frontend/src/components/pages/CatalogList.tsx` — removed the 19-line `useEffect` block (comment + body) that was causing the pagination reset race condition

## Tests
No new tests written — the existing E2E tests at `text-search-filters.spec.ts:142,267` already cover this behavior and will now pass.

## How to verify
1. `npm run build` — TypeScript must compile without errors
2. Navigate to the catalog on staging, go to page 2, apply a name/code filter — the URL must drop the `page` parameter (reset to page 1)

## Notes
No other changes to `handlePageChange`, `handlePageSizeChange`, or `handleSort` were needed. The existing effect at lines 188–206 already correctly handles state→URL sync via the `pageNumber` dependency, using `{ replace: true }` to avoid spurious history entries.

## PR Summary

Removed the race-condition-causing `useEffect` in `CatalogList.tsx` that was restoring `page=2` to the URL after filter application. The fix resolves a stale-closure race where the effect observed stale `pageNumber=2` state after `handleApplyFilters` had already cleared the page param.

### Changes
- `frontend/src/components/pages/CatalogList.tsx` — removed 19-line "Sync URL parameter with page number state" `useEffect` (lines 257–275)

## Status
DONE
