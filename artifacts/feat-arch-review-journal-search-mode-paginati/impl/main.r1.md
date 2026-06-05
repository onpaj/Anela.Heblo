Final code review: **APPROVED**. Writing the output summary.

---

# Implementation: Fix Journal search pagination & sorting (auto-refetch on query-key change)

## What was implemented

Surgical two-file fix that eliminates a silent data-staleness bug: in journal search mode, changing the page number, page size, or sort column updated UI affordances but did not refetch data — users silently kept seeing page 1 of the original search.

**Root cause:** `useSearchJournalEntries` had `enabled: false` hardcoded, which prevented React Query's automatic key-change refetch. Pagination/sort handlers in `JournalList` were correct (pure state setters), but the query never reacted to those state changes in search mode.

**Fix:** Parameterized `enabled` on the hook (default `false`, preserving backwards compatibility), then passed `isSearchMode` at the single call site so the query becomes live exactly when — and only when — the user has applied a non-empty search filter.

## Files created/modified

- `frontend/src/api/hooks/useJournal.ts` — Added `enabled: boolean = false` parameter to `useSearchJournalEntries`; forwarded to `useQuery({ enabled })`; added JSDoc explaining the default and key-change refetch semantics
- `frontend/src/components/pages/Journal/JournalList.tsx` — Passed `isSearchMode` as second arg to `useSearchJournalEntries`; no other change
- `frontend/src/api/hooks/__tests__/useJournal.test.ts` — 3 new tests (no-fetch-on-mount guard, fetch-on-mount with `enabled=true`, auto-refetch on params change) + comment update on the existing explicit-refetch test
- `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx` — 3 new component regression tests (pagination, sort, page-size in search mode) + lint fixes (`no-wait-for-multiple-assertions`)

## Tests

- `useJournal.test.ts` — 17 tests, all passing; 4 cover `useSearchJournalEntries` (including 3 new tests for the `enabled` parameter behavior)
- `JournalList.test.tsx` — 21 tests, all passing; 3 new regression tests assert that changing page, sort, or page-size in search mode causes the hook to be re-invoked with the new params and `enabled=true`
- Overall journal test suite: **38/38 PASS**

## How to verify

```bash
cd frontend
# Hook tests
CI=true npm test -- --testPathPattern="src/api/hooks/__tests__/useJournal" --no-coverage

# Component tests (includes 3 regression tests)
CI=true npm test -- --testPathPattern="src/components/pages/Journal/__tests__/JournalList" --no-coverage

# Build
npm run build
```

## Notes

- `handleApplyFilters` retains its explicit `searchQuery.refetch()` call (FR-6 — so re-clicking Filtrovat with unchanged text still refreshes data). With `enabled=true` in place, an identical-text re-filter causes both auto-refetch + explicit refetch; React Query coalesces them.
- `handleCloseModal` and all pagination/sort handlers are untouched (FR-7, Decision 4 in arch review).
- 144 pre-existing lint warnings in the project; none introduced by this change.
- The `positional-index` approach in the auto-refetch test (`lastCallArgs[6]`) is noted as brittle if the API signature changes, but matches codebase conventions.

## PR Summary

Fixed a silent data-staleness bug where changing page, page size, or sort column while in journal search mode updated UI affordances but left the table showing stale page-1 data.

The root cause was `enabled: false` hardcoded on `useSearchJournalEntries`, which blocked React Query's automatic key-change refetch. The fix parameterizes `enabled` (default `false` to preserve backwards compatibility for any caller that omits it) and passes `isSearchMode` at the sole call site in `JournalList.tsx` — so the query is live only when the user has applied a non-empty filter, and React Query auto-refetches on every pagination/sort/page-size state change.

All existing handlers (`handleApplyFilters`, `handleCloseModal`) are untouched. Three new component-level regression tests directly cover the three bug scenarios (page change, sort change, page-size change in search mode).

### Changes
- `frontend/src/api/hooks/useJournal.ts` — parameterized `enabled` on `useSearchJournalEntries` with JSDoc
- `frontend/src/components/pages/Journal/JournalList.tsx` — pass `isSearchMode` as second arg at call site
- `frontend/src/api/hooks/__tests__/useJournal.test.ts` — 3 new hook-level tests + comment update
- `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx` — 3 new component regression tests + lint fixes

## Status
DONE