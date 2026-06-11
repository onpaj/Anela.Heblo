---

# Implementation: Remove Manual `.refetch()` Calls from JournalList

## What was implemented

Removed all 5 manual `.refetch()` call sites from three handlers in `JournalList.tsx`:
- `handleApplyFilters`: dropped the `if/else refetch` block + `async`
- `handleClearFilters`: dropped the `await entriesQuery.refetch()` + `async`
- `handleCloseModal`: dropped the `if/else refetch` block

React Query's existing key-change refetch mechanism handles all refetching automatically. Post-mutation freshness continues to be handled by `queryClient.invalidateQueries` in the mutation hooks' `onSuccess` callbacks.

Three existing tests that asserted `mockRefetch.toHaveBeenCalled()` were rewritten to assert hook re-invocation with correct params (the pattern established at JournalList.test.tsx:485–528). One new regression test locks in the `handleCloseModal` no-refetch contract.

## Files created/modified

- `frontend/src/components/pages/Journal/JournalList.tsx` — removed 3 refetch blocks, dropped `async` from 2 handlers (~20 line reduction)
- `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx` — rewrote 3 tests + added 1 regression test; fixed `testing-library/no-wait-for-multiple-assertions` lint rule in new test

## Tests

- 22 tests total, all passing
- 3 rewritten tests: assert `useSearchJournalEntries`/`useJournalEntries` re-invocation with correct params using `mock.calls`
- 1 new regression test: "does not call refetch on entriesQuery or searchQuery when the modal closes" — was red before source fix, green after

## How to verify

From `frontend/`:
```bash
node_modules/.bin/react-scripts test --testPathPattern=JournalList.test.tsx --watchAll=false
# Expected: 22 passed, 0 failed
```

Check no `.refetch(` calls remain:
```bash
grep -n "\.refetch(" src/components/pages/Journal/JournalList.tsx
# Expected: no output
```

Surgical diff check:
```bash
git diff --stat main...HEAD -- frontend/src/components/pages/Journal/
# Expected: only JournalList.tsx and __tests__/JournalList.test.tsx
```

## Notes

- The implementer subagent batched all 4 test changes into one commit instead of 4 separate commits — the code is correct, just the commit granularity differs from the plan
- A `testing-library/no-wait-for-multiple-assertions` lint violation was introduced in the new regression test (two `expect` inside `waitFor`) and fixed before final commit — this is a new lint error vs. baseline, corrected immediately
- All other lint errors in the output are pre-existing in unrelated files; the two changed files are lint-clean
- Worktree required `npm install --legacy-peer-deps` as `node_modules` was absent (package includes `@microsoft/applicationinsights-web` not in a stale symlink)

## PR Summary

Removed three manual `.refetch()` invocations from `JournalList.tsx` that fired React Query refetches immediately after `setState`, violating the hook contract and causing double network requests with stale-data flashes. React Query's automatic key-change refetch already handles all cases; the manual calls were redundant anti-patterns.

Three existing tests that asserted `mockRefetch.toHaveBeenCalled()` were rewritten to assert hook re-invocation with correct params (matching the pattern already established in the file for sort/pagination tests). One new regression test verifies `handleCloseModal` no longer triggers refetch.

### Changes
- `frontend/src/components/pages/Journal/JournalList.tsx` — removed refetch blocks from `handleApplyFilters`, `handleClearFilters`, `handleCloseModal`; dropped now-dead `async` from first two handlers
- `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx` — rewrote 3 tests from `mockRefetch.toHaveBeenCalled()` to hook `mock.calls` param assertions; added regression test for `handleCloseModal`

## Status

DONE