# Specification: Fix Catalog Pagination Reset Race Condition

## Summary

Applying a text or code filter on the catalog list page is intended to reset pagination to page 1, but a race condition in `CatalogList.tsx` restores the stale page number after `handleApplyFilters` sets it to 1. The decision is to fix the application so that filter application correctly resets to page 1. All tests that currently document "stays on page 2" as a known bug must be updated to assert the correct (page 1) behavior and have their bug-comment scaffolding removed.

## Background

The nightly E2E run (2026-06-25, run 28147951139) surfaced two failures in `text-search-filters.spec.ts` (lines 142 and 267). Both tests navigate to page 2 and then apply a filter, expecting the URL to revert to page 1. They fail because the page stays on 2.

The root cause is a **race condition across two `useEffect` hooks** in `CatalogList.tsx`:

1. `handleApplyFilters()` (line 108) sets `pageNumber` state to 1 and calls `params.delete("page")` before calling `setSearchParams(params)`. This is the correct intent.
2. The "Sync URL parameter with page number state" effect (line 258) watches `[pageNumber, searchParams, setSearchParams]`. When `setSearchParams` is called from `handleApplyFilters`, React schedules a re-render. The effect fires with the **old** `pageNumber = 2` (the state update from `setPageNumber(1)` hasn't committed yet in that render cycle). Because `currentPageNumber (1, from fresh URL) !== pageNumber (2, stale state)`, the `else if` branch at line 269 writes `page=2` back to the URL, undoing the reset.

The same root cause produces three additional known-bug assertions in the test suite:

| File | Lines | Current assertion | What it should be |
|---|---|---|---|
| `combined-filters.spec.ts` | 149–178 | `expect(currentPage).toBe(2)` | `toBe(1)` |
| `pagination-with-filters.spec.ts` | 113–137 | `expect(currentPage).toBe(1)` (accidentally correct via a different bug path — confirmed by comment "resets to page 1 after clicking page 2") | leave as `toBe(1)`, remove bug comment |
| `pagination-with-filters.spec.ts` | 292–307 | `expect(currentPage).toBe(2)` | `toBe(1)` |

The two failing tests in `text-search-filters.spec.ts` already assert `toBe(1)` via `validatePageResetToOne()` and must **not** change.

## Functional Requirements

### FR-1: Applying a text or code filter resets pagination to page 1

When the user applies a product-name or product-code filter (via Enter key or the Filter button), the URL must not contain a `page` parameter after the filter is committed, which renders the catalog starting at page 1.

**Acceptance check:** Navigate to `?page=2`, apply any text filter, observe URL — the `page` query param must be absent (equivalent to page 1).

### FR-2: Changing page size resets pagination to page 1

When the user changes the page-size selector while on page N > 1, the catalog must reset to page 1. This was already the stated intent of `handlePageSizeChange` (line 181–184) but was defeated by the same race condition.

**Acceptance check:** Navigate to `?page=2`, change the page-size select — `page` param must be absent after update.

### FR-3: Clicking to page 2 under an active filter stays on page 2

Once the race condition is fixed, navigating to page 2 while a filter is active must remain stable on page 2. The `pagination-with-filters.spec.ts` test at lines 113–137 currently documents "resets to page 1" as a bug. After the fix this navigation must stay on page 2, so the assertion at line 137 changes from `toBe(1)` to `toBe(2)`.

**Acceptance check:** Apply a filter with enough results for multiple pages, click the page 2 control, observe URL contains `page=2` and remains there without further changes.

### FR-4: Test suite is internally consistent — no contradictory bug comments

All "KNOWN BUG", "documented pagination reset bug", and related TODO comments in the catalog E2E suite must be removed. Assertions must reflect the correct, post-fix behavior.

## Non-Functional Requirements

### NFR-1: No new history entries from internal state synchronisation

The fix must not introduce extra browser-history entries. The existing `{ replace: true }` pattern used in the first `useEffect` (line 188–206) must be preserved or extended to the rewritten sync logic.

### NFR-2: Browser back/forward navigation continues to work

Navigating back/forward between pages must still restore the correct page number and filter state via the "Handle browser back/forward navigation" effect (line 210–243). This effect must not be changed.

### NFR-3: No regression to existing passing tests

All currently-passing tests in the catalog E2E suite must continue to pass after the change.

## Data Model

No data model changes. This is a pure frontend state-management fix.

## API / Interface Design

No API changes. The backend pagination API (`GET /api/catalog?page=N&...`) is unaffected.

### CatalogList.tsx — exact changes required

**Remove the "Sync URL parameter with page number state" effect (lines 258–275 inclusive).**

This is the problematic effect. Its job (keeping the URL in sync with `pageNumber` state) is already covered by the first `useEffect` at lines 188–206, which rebuilds the full parameter set from all state variables and calls `setSearchParams(params, { replace: true })`. That effect fires correctly after the state update from `setPageNumber(1)` has committed because it depends on `pageNumber` directly.

The effect at line 245–255 ("Sync page number state from URL parameter changes") also overlaps with the second effect at 210–243 and should be evaluated for removal to avoid redundant state updates. However, since removing it is not strictly required to fix the race condition and carries independent regression risk, it is **out of scope** for this feature.

After removing the effect at lines 258–275, the `handlePageChange` function (line 174–179) will still work correctly: it calls `setPageNumber(newPage)`, which triggers the first `useEffect` (line 188–206) to write `page=N` to the URL via `setSearchParams(params, { replace: true })`.

### E2E test changes required

**`frontend/test/e2e/catalog/combined-filters.spec.ts` — test "should reset page to 1 when any filter changes" (line 149):**

- Change `expect(currentPage).toBe(2)` (line 170) to `expect(currentPage).toBe(1)`.
- Remove the multi-line "KNOWN APPLICATION BUG" comment block (lines 164–168).
- Remove the trailing `console.log` line that references the pagination bug (line 178).
- Update the test's `console.log` on success to remove the "(with documented pagination reset bug)" qualifier.

**`frontend/test/e2e/catalog/pagination-with-filters.spec.ts` — test at lines 113–137 (page 2 navigation with filter):**

- Change `expect(currentPage).toBe(1)` (line 137) to `expect(currentPage).toBe(2)`.
- Remove the "KNOWN BUG: Pagination with filters causes automatic reset to page 1" comment block (lines 113–124).
- Remove the conditional `if (currentPage === 1 && ...)` branch that logs "APPLICATION BUG CONFIRMED" (lines 127–130), since the bug will no longer manifest.
- Remove the `// Current buggy behavior` comment (line 135) and the `// When bug is fixed` TODO comment (line 136).
- Update the trailing `console.log` to remove the "(with documented pagination reset bug)" qualifier.

**`frontend/test/e2e/catalog/pagination-with-filters.spec.ts` — test at lines 292–307 (page size change resets pagination):**

- Change `expect(currentPage).toBe(2)` (line 307) to `expect(currentPage).toBe(1)`.
- Remove the "KNOWN BUG: Page size changes don't reset pagination to page 1" comment block (lines 292–300).
- Remove the `// Current buggy behavior` and `// When bug is fixed` TODO comments (lines 305–306).
- Update the trailing `console.log` to remove the "(with documented pagination reset bug - stays on page 2)" qualifier.

**`frontend/test/e2e/catalog/text-search-filters.spec.ts` — tests at lines 142 and 267:**

No changes to assertions. The stale skip comments at lines 139–141 and 265–266 (which already note "Fix confirmed") may be removed as housekeeping but are not required.

## Dependencies

- `frontend/src/components/pages/CatalogList.tsx` — sole application file to change.
- `frontend/test/e2e/catalog/combined-filters.spec.ts` — test update.
- `frontend/test/e2e/catalog/pagination-with-filters.spec.ts` — test update.
- `frontend/test/e2e/helpers/catalog-test-helpers.ts:325` — referenced in the brief as the validation helper; no changes expected there.

## Out of Scope

- The redundant "Sync page number state from URL parameter changes" effect (lines 245–255). It overlaps with the back/forward navigation effect but does not cause the regression and removing it carries independent risk.
- Any backend changes.
- The `handlePageChange` function: it works correctly once the race-condition effect is removed.
- Any UI or design changes to the catalog page.
- Any changes to E2E tests outside the three files named above.

## Open Questions

None.

## Status: COMPLETE
