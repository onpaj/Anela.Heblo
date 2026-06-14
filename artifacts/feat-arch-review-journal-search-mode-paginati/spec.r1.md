# Specification: Fix Journal search pagination & sorting (auto-refetch on query key change)

## Summary
While viewing journal entries in search mode, changing the page number, page size, or sort column updates the UI affordances but does not refresh the displayed data — the user silently keeps reading page 1 of the original search. This spec covers a surgical fix that enables React Query's automatic refetch-on-key-change behavior for the search hook while preserving the original "no request on mount" intent.

## Background
`JournalList.tsx` uses two React Query hooks to back the same grid:

- `useJournalEntries` — default browse mode; runs automatically on mount and on key change.
- `useSearchJournalEntries` — search mode (active once the user clicks **Filtrovat** with non-empty search text); hardcoded with `enabled: false` at `frontend/src/api/hooks/useJournal.ts:62`.

The `enabled: false` flag was introduced so the search query would not fire on initial render. Side-effect: React Query also stops auto-refetching when the query key changes. In `frontend/src/components/pages/Journal/JournalList.tsx`, the pagination handlers `handlePageChange` (lines 247–251), `handlePageSizeChange` (lines 253–256), and the sorting handler `handleSort` (lines 237–244) only update local state — they never call `searchQuery.refetch()`. The only paths that do refetch the search query are `handleApplyFilters` (clicking **Filtrovat**) and `handleCloseModal`.

**Consequence (user-visible bug):** in search mode, clicking page 2/3/…, changing page size, or clicking a sortable header updates the pagination indicator and sort arrows but leaves the underlying data on screen as page 1 of the originally searched result set — a silent data-staleness bug. Default (non-search) mode is unaffected because `useJournalEntries` has no `enabled: false` and React Query auto-refetches it whenever the params/key change.

The bug was not caught by tests because the hook is exercised in isolation (where `refetch()` can always be invoked directly), and the `enabled: false` pattern reads as intentional.

## Functional Requirements

### FR-1: Search hook accepts an `enabled` flag
`useSearchJournalEntries` must accept an optional `enabled` parameter (default `false`, to preserve the current "no request on mount" guarantee for any existing/future caller that does not opt in) and forward it to React Query's `enabled` option.

**Acceptance criteria:**
- Hook signature becomes `useSearchJournalEntries(params: SearchJournalParams, enabled?: boolean)`.
- When `enabled` is omitted or `false`, the hook behaves exactly as today: it does not run on mount and does not auto-refetch on query-key change; an explicit `.refetch()` is the only way to fire the request.
- When `enabled` is `true`, the hook runs immediately and auto-refetches when any value in `params` (and hence the query key) changes.
- The `queryKey` is unchanged (`[...QUERY_KEYS.journal, "search", params]`), so cached results keyed by prior params are still reused on back-navigation.

### FR-2: `JournalList` binds the `enabled` flag to `isSearchMode`
The page must pass `isSearchMode` to the hook so the search query becomes active iff the user has applied a non-empty search filter.

**Acceptance criteria:**
- `useSearchJournalEntries({ ...params }, isSearchMode)` is called from `JournalList.tsx`.
- Initial mount with empty search text and `isSearchMode === false`: no search request is issued (matches today's behavior).
- After the user clicks **Filtrovat** with non-empty text, `isSearchMode` flips to `true`, the search query becomes active, and the data renders.
- After the user clicks **Vymazat filtry** (or applies an empty filter), `isSearchMode` flips back to `false`, the search query becomes inactive, and the default `useJournalEntries` data is shown.

### FR-3: Pagination updates data in search mode
When in search mode, clicking a different page number or pressing the prev/next chevron must trigger a backend request with the new `pageNumber` and render the new page's data.

**Acceptance criteria:**
- In search mode, calling `handlePageChange(newPage)` causes `params.pageNumber` to change → the query key changes → React Query refetches → the table renders the new page.
- The pagination indicator and the visible rows are consistent (no silent staleness).
- Existing guard `newPage >= 1 && newPage <= totalPages` remains in place.
- Behavior is identical in default (non-search) mode (no regression).

### FR-4: Page-size change updates data in search mode
When in search mode, choosing a different page size must reset to page 1 and fetch fresh data with the new page size.

**Acceptance criteria:**
- `handlePageSizeChange(newPageSize)` sets `pageSize` and resets `pageNumber` to 1.
- In search mode, the resulting query-key change triggers a refetch; the table shows the first page at the new size.
- In default mode, behavior is unchanged.

### FR-5: Sorting updates data in search mode
When in search mode, clicking a sortable column header must trigger a backend re-query with the new sort column / direction and render the re-sorted data.

**Acceptance criteria:**
- `handleSort(column)` continues to (a) toggle direction if the same column is clicked, or (b) switch column and reset direction to ascending.
- In search mode, the resulting `sortBy` / `sortDescending` state change feeds into `params`, the query key changes, React Query refetches, and the displayed rows reflect the new order.
- Sort-arrow indicator in `SortableHeader` remains consistent with the actually-rendered data.
- In default mode, behavior is unchanged.

### FR-6: `handleApplyFilters` no longer needs an explicit refetch in the search path
With FR-1+FR-2 in place, calling `searchQuery.refetch()` inside `handleApplyFilters` becomes redundant whenever the new `searchTextInput` differs from the prior `searchTextFilter` (the key changes and React Query auto-fetches). However, when the user types the same text and clicks **Filtrovat** again (key unchanged), an explicit `.refetch()` is still required to fulfill the implicit "the button does something" UX contract.

**Acceptance criteria:**
- `handleApplyFilters` retains the explicit `await searchQuery.refetch()` call in the search-text branch so that re-clicking **Filtrovat** with unchanged text still refreshes the data.
- When `searchTextInput` differs from `searchTextFilter`, the auto-refetch (FR-1) and the explicit `.refetch()` together still produce a single coherent rendered result; an extra request on this path is acceptable.
- The non-search branch (`await entriesQuery.refetch()`) is unchanged.

### FR-7: Existing modal-close refetch behavior preserved
`handleCloseModal` (lines 275–284) calls `searchQuery.refetch()` or `entriesQuery.refetch()` after the edit modal closes. This behavior is independently useful (force-refresh after an edit even if params didn't change) and must be preserved.

**Acceptance criteria:**
- No change to `handleCloseModal`.
- After the fix, closing the modal still triggers a fresh fetch of the current view.

### FR-8: Loading and error states cover the new auto-refetch paths
`currentQuery.isLoading` and `currentQuery.error` already drive the page-level spinner and error banner (lines 287–308). These must continue to work for auto-refetches triggered by pagination/sort/page-size changes in search mode.

**Acceptance criteria:**
- The existing loading/error UI fires for refetches triggered by query-key change in search mode (React Query default behavior).
- No new top-level error or empty-state branches are introduced; surgical change only.

## Non-Functional Requirements

### NFR-1: Performance
- One additional backend request per pagination/page-size/sort interaction in search mode is expected and desired; no batching, debouncing, or request-coalescing is required for this fix.
- React Query's existing caching by query key must remain — repeated visits to a previously-fetched page (e.g., navigating page 2 → page 3 → page 2) reuse cached data per default React Query behavior.
- No measurable regression in default-mode rendering (the default-mode code path is untouched).

### NFR-2: Security
- No new endpoints, no new data exposure, no auth changes. The fix only affects when an existing authenticated endpoint is called from the frontend.

### NFR-3: Backwards compatibility (hook API)
- The new `enabled` parameter is optional with a `false` default, preserving the existing semantics for any caller (current or future) that does not pass it. Current call sites other than `JournalList` (if any) must be unaffected.

### NFR-4: Testability
- The change must be covered by frontend unit tests that detect the original bug (i.e., assert that changing `pageNumber`, `pageSize`, `sortBy`, or `sortDescending` while in search mode causes a new request and updates the rendered rows).
- Tests for the hook in isolation must cover: (a) `enabled = false` does not fire on mount, (b) `enabled = true` fires on mount, (c) `enabled = true` auto-refetches when `params` changes.

## Data Model
No data-model changes. The fix is frontend-only and does not alter the wire contract, request shape, response shape, or any persisted entity.

## API / Interface Design

### Hook signature (changed)
File: `frontend/src/api/hooks/useJournal.ts`

```ts
export const useSearchJournalEntries = (
  params: SearchJournalParams,
  enabled: boolean = false,
) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.journal, "search", params],
    queryFn: async () => {
      const client = await getAuthenticatedApiClient();
      return await client.journal_SearchJournalEntries(
        params.searchText || undefined,
        params.dateFrom || undefined,
        params.dateTo || undefined,
        params.productCodePrefix || undefined,
        params.tagIds || undefined,
        params.createdByUserId || undefined,
        params.pageNumber,
        params.pageSize,
        params.sortBy,
        params.sortDirection,
      );
    },
    enabled,
  });
};
```

### Call site (changed)
File: `frontend/src/components/pages/Journal/JournalList.tsx`

```tsx
const searchQuery = useSearchJournalEntries(
  {
    searchText: searchTextFilter,
    pageNumber,
    pageSize,
    sortBy,
    sortDirection: sortDescending ? "DESC" : "ASC",
  },
  isSearchMode,
);
```

No other UI or controller code is touched. Backend endpoints (`journal_SearchJournalEntries`, `journal_GetJournalEntries`) are unchanged.

## Dependencies
- React Query (`@tanstack/react-query`) — already a project dependency; relies on its standard `enabled` flag and query-key-based refetch behavior. No version bump.
- Generated API client (`SearchJournalEntryDto`, `journal_SearchJournalEntries`) — unchanged.

## Out of Scope
- Backend changes to the journal search endpoint.
- Adding new filter fields beyond `searchText` to the page UI (date range, tags, user, product code prefix are already on the hook param shape but not all are wired to UI — keeping that scope as-is).
- Refactoring the dual-hook pattern (`useJournalEntries` + `useSearchJournalEntries`) into a single hook.
- Memoization of the `params` object passed to the hook. React Query serializes the query key, so a new object identity each render is fine.
- Restructuring the loading/error UI, redesigning pagination, or any visual changes.
- Removing the explicit `searchQuery.refetch()` call inside `handleApplyFilters` (kept per FR-6 so re-clicking **Filtrovat** with unchanged text still does something visible).
- Any related "stale on mutation" concerns in non-search mode (existing `invalidateQueries` in the mutation hooks already covers that).

## Open Questions
None.

## Status: COMPLETE