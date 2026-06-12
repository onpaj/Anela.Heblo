## Module
Journal

## Finding
`JournalList.tsx` calls `.refetch()` manually on the React Query objects immediately after setting React state in three places:

- `handleApplyFilters` (line 214–216): `await searchQuery.refetch()` / `await entriesQuery.refetch()` after `setSearchTextFilter`, `setIsSearchMode`, `setPageNumber`.
- `handleClearFilters` (line 233–237): `await entriesQuery.refetch()` after `setSearchTextInput`, `setSearchTextFilter`, `setIsSearchMode`, `setPageNumber`.
- `handleCloseModal` (line 282–287): `searchQuery.refetch()` / `entriesQuery.refetch()` after the modal close.

The `useSearchJournalEntries` hook's own JSDoc (`frontend/src/api/hooks/useJournal.ts`, lines 46–51) explicitly warns against this:

> "callers should rely on key-change refetch rather than calling `.refetch()` manually after changing state"

The reason is timing: React state updates are batched and asynchronous. When `.refetch()` is called immediately after `setState`, the query key has **not yet changed** — the refetch fires with the old parameters. React then re-renders with the new state, the key changes, and React Query fires a second fetch with the correct parameters. The result is two network requests: one with stale parameters (which may briefly populate the table with wrong data) and one with the correct parameters.

## Why it matters
- **Stale data flash**: the first refetch returns old results into the query cache, which React Query displays before the second (correct) fetch completes.
- **Double requests**: every filter apply, clear, and modal close sends an extra network request.
- **Contract violation**: the hook's documentation is the contract between the hook and its callers. Three callers already violate it, increasing the risk that future callers follow the same pattern.

## Suggested fix
Remove all manual `.refetch()` calls from `handleApplyFilters`, `handleClearFilters`, and `handleCloseModal`. React Query will automatically re-fetch when the query key changes (driven by the state updates that are already there). The `enabled` parameter on `useSearchJournalEntries` already gates the search query; the browse query has no `enabled` guard and will refetch on any key change.

For `handleCloseModal`, if a refetch after save is genuinely needed, `onSuccess` in the mutation's `useCreateJournalEntry` / `useUpdateJournalEntry` / `useDeleteJournalEntry` hooks already calls `queryClient.invalidateQueries`, which is the correct mechanism.

---
_Filed by daily arch-review routine on 2026-06-10._