# Specification: Remove Manual `.refetch()` Calls from JournalList

## Summary
Remove three manual `.refetch()` invocations from `JournalList.tsx` that fire React Query refetches immediately after `setState`, violating the documented contract of `useSearchJournalEntries` and causing double network requests with stale-data flashes. Rely on React Query's automatic key-change refetch behavior, which is already in place.

## Background
The Journal module's list page (`frontend/src/components/journal/JournalList.tsx`) currently mixes two refetch mechanisms: React Query's automatic key-change refetch and manual `.refetch()` calls. Because React state updates are batched and asynchronous, calling `.refetch()` immediately after `setState` fires the network request with the **previous** query parameters. React then re-renders, the query key actually changes, and React Query fires a **second** request with the correct parameters.

This produces two observable defects:
1. **Stale-data flash** — the first (wrong-parameter) response lands in the cache before the correct response, briefly painting incorrect rows in the table.
2. **Duplicate network traffic** — every filter apply, filter clear, and modal close generates an unnecessary request.

The `useSearchJournalEntries` hook's JSDoc (`frontend/src/api/hooks/useJournal.ts` lines 46–51) explicitly warns callers against this pattern. Three call sites in `JournalList.tsx` already violate it; leaving them in place normalizes the anti-pattern for future callers.

## Functional Requirements

### FR-1: Remove manual refetch from `handleApplyFilters`
Delete the `await searchQuery.refetch()` and `await entriesQuery.refetch()` calls at `JournalList.tsx` lines 214–216. The existing `setSearchTextFilter`, `setIsSearchMode`, and `setPageNumber` state updates already change the React Query key(s); React Query will automatically refetch with the correct parameters on the next render.

**Acceptance criteria:**
- No `.refetch()` calls remain inside `handleApplyFilters`.
- Applying a filter triggers exactly one network request (verifiable in browser DevTools → Network).
- The journal entry table updates to the filtered results without a stale-data flash.
- The function signature and external behavior (UX-visible side effects) are unchanged.

### FR-2: Remove manual refetch from `handleClearFilters`
Delete the `await entriesQuery.refetch()` call at `JournalList.tsx` lines 233–237. The existing `setSearchTextInput`, `setSearchTextFilter`, `setIsSearchMode`, and `setPageNumber` state updates already drive the query key change.

**Acceptance criteria:**
- No `.refetch()` calls remain inside `handleClearFilters`.
- Clearing filters triggers exactly one network request.
- The table returns to the unfiltered/browse view without a stale-data flash.

### FR-3: Remove manual refetch from `handleCloseModal`
Delete the `searchQuery.refetch()` and `entriesQuery.refetch()` calls at `JournalList.tsx` lines 282–287. Cache invalidation after a create/update/delete is already handled by `queryClient.invalidateQueries` inside the `onSuccess` callbacks of `useCreateJournalEntry`, `useUpdateJournalEntry`, and `useDeleteJournalEntry`. The modal close itself does not require any explicit refetch.

**Acceptance criteria:**
- No `.refetch()` calls remain inside `handleCloseModal`.
- After creating a new entry and closing the modal, the new entry appears in the list (driven by mutation `onSuccess` → `invalidateQueries`).
- After editing an existing entry and closing the modal, the updated entry is reflected in the list.
- After deleting an entry and closing the modal, the entry is no longer in the list.
- Closing the modal without saving (cancel) does not trigger any network request beyond what was already implicit before the change.

### FR-4: Preserve unchanged behavior in all other paths
No other functions, hooks, or components in `JournalList.tsx` should be modified beyond removing the three refetch calls and any now-unused `async`/`await` keywords or variable destructuring that becomes dead code as a direct result.

**Acceptance criteria:**
- The diff is limited to the three identified locations plus mechanical follow-ups (e.g., dropping `async` on a handler that no longer awaits anything).
- No changes to `useJournal.ts`, the mutation hooks, or any other Journal module file are made.
- Pagination, sorting, search-mode toggling, and modal open/edit/delete flows continue to work as before.

## Non-Functional Requirements

### NFR-1: Performance
- Eliminate one redundant HTTP request per filter apply, filter clear, and post-mutation modal close.
- No regression in perceived responsiveness — the React Query key-change path must produce a network request promptly after state commits (this is the default React Query behavior; no configuration changes required).

### NFR-2: Correctness
- The journal list must never display results computed from stale filter parameters, even transiently. The fix must remove the stale-data flash window entirely.

### NFR-3: Maintainability
- The hook contract documented in `useJournal.ts` JSDoc is honored by all current callers, removing a misleading precedent.

### NFR-4: Test coverage
- Existing unit tests for `JournalList.tsx` must continue to pass.
- Add or update tests (Jest + React Testing Library) that assert exactly one network call per filter-apply, filter-clear, and post-mutation modal close sequence, using a mocked React Query client or fetch mock.

## Data Model
No data model changes. This is a frontend refactor that affects only client-side query orchestration.

## API / Interface Design
No backend API changes. No changes to hook signatures, component props, or public exports.

**Internal call-flow change (per affected handler):**

Before:
1. `setState(…)` (1..N times)
2. `await someQuery.refetch()` ← fires with stale params
3. React re-renders, key changes, React Query fires again with new params

After:
1. `setState(…)` (1..N times)
2. React re-renders, key changes, React Query fires exactly once with new params

## Dependencies
- React Query (`@tanstack/react-query`) — relies on existing `queryKey` change detection.
- Existing hooks `useSearchJournalEntries`, `useJournalEntries`, `useCreateJournalEntry`, `useUpdateJournalEntry`, `useDeleteJournalEntry` in `frontend/src/api/hooks/useJournal.ts` — no modifications expected.

## Out of Scope
- Refactoring or restructuring `JournalList.tsx` beyond the three minimal edits.
- Changing the `useSearchJournalEntries` or `useJournalEntries` hook internals.
- Adjusting the mutation hooks' `onSuccess` invalidation behavior.
- Auditing other modules for the same anti-pattern (this spec is scoped to the Journal list).
- Documentation updates beyond the code itself; the existing JSDoc warning already covers the rationale.
- Behavioral changes to pagination, search debounce, or modal lifecycle outside the three refetch removals.

## Open Questions
None.

## Status: COMPLETE