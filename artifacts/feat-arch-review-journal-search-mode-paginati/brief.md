## Module
Journal

## Finding
`useSearchJournalEntries` is configured with `enabled: false`:

```ts
// frontend/src/api/hooks/useJournal.ts, line 62
enabled: false, // Only run when explicitly called
```

React Query therefore never re-runs the search query automatically when the query key changes. In `JournalList.tsx`, the pagination and sorting handlers only update state — they never call `.refetch()`:

```tsx
// Lines 247–255 — no refetch, so search results don't update
const handlePageChange = (newPage: number) => {
  if (newPage >= 1 && newPage <= totalPages) {
    setPageNumber(newPage);
  }
};

const handlePageSizeChange = (newPageSize: number) => {
  setPageSize(newPageSize);
  setPageNumber(1);
};

// Lines 237–243 — same issue for sorting
const handleSort = (column: string) => { ... setSortBy(column); ... };
```

**Consequence**: while in search mode (after clicking "Filtrovat"), clicking a different page, changing page size, or clicking a sort column header updates the visible page indicator and sort arrow — but the data on screen does not change. It remains the first page of the original search, silently.

The only way to get updated data is to click "Filtrovat" again. This means a user who searches, then navigates to page 2, is reading stale page-1 results without knowing it.

## Why it matters
Pagination and sorting are broken in a visible, user-facing feature. The bug is invisible in tests because the hook is typically tested in isolation (where `refetch` can be called directly), and the `enabled: false` pattern looks intentional at first glance.

## Suggested fix
Pass an `enabled` flag to the hook so React Query can auto-refetch when the query key changes:

```ts
// useJournal.ts
export const useSearchJournalEntries = (params: SearchJournalParams, enabled = false) => {
  return useQuery({
    queryKey: [...QUERY_KEYS.journal, "search", params],
    queryFn: async () => { ... },
    enabled,
  });
};
```

In `JournalList.tsx`, bind it to `isSearchMode`:

```tsx
const searchQuery = useSearchJournalEntries({ ...params }, isSearchMode);
```

This preserves the original intent (no search request on initial mount when `searchTextFilter` is empty and `isSearchMode` is false) and makes pagination/sorting work automatically when in search mode.

---
_Filed by daily arch-review routine on 2026-06-04._