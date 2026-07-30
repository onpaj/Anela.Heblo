# Design — useSupplierSearch: React Query migration

No UI section: `SupplierAutocomplete.tsx` and its rendered markup are unchanged by this task (confirmed by reading the component — it only consumes `{ suppliers, isLoading, error }` from the hook and has no dependency on how that data is produced). This is a pure data-layer refactor of one hook.

## Component design

### `useSupplierSearch(searchTerm: string, limit: number = 10)` — `frontend/src/api/hooks/useSuppliers.ts`

Internal structure, three layers:

```
raw searchTerm (from caller, every keystroke)
        │
        ▼
  [debounce layer]  300ms setTimeout, cancels on change/unmount
        │
        ▼
  debouncedSearchTerm (local useState)
        │
        ▼
  [useQuery layer]  keyed on debouncedSearchTerm + limit
        │
        ▼
  { data, isFetching, error } from React Query
        │
        ▼
  [derivation layer]  gates on RAW term length, not debounced
        │
        ▼
  return { suppliers, isLoading, error }
```

**1. Debounce layer (unchanged responsibility, same 300ms timing as today)**

```ts
const [debouncedSearchTerm, setDebouncedSearchTerm] = useState(searchTerm);

useEffect(() => {
  const timeoutId = setTimeout(() => setDebouncedSearchTerm(searchTerm), 300);
  return () => clearTimeout(timeoutId);
}, [searchTerm]);
```

This is the one piece of hand-rolled state the migration keeps — it's UX debounce logic, not data-fetching infrastructure, and React Query has no built-in debounce primitive. It stays private to this file (no new shared hook — confirmed no existing `useDebounce*` anywhere in `frontend/src`, and there's only one caller).

**2. Query layer** — mirrors `useCatalogAutocomplete.ts` exactly:

```ts
const query = useQuery({
  queryKey: ["suppliers", "search", debouncedSearchTerm, limit],
  queryFn: async () => {
    const apiClient = getAuthenticatedApiClient(); // sync, not awaited — confirmed client.ts:276
    return apiClient.suppliers_SearchSuppliers(debouncedSearchTerm, limit);
  },
  enabled: debouncedSearchTerm.length >= 2,
  placeholderData: keepPreviousData,
});
```

- Not added to the shared `QUERY_KEYS` registry in `client.ts` — that file defines one entry per *domain* (`catalog`, `journal`, etc.), and this hook is the only caller of `"suppliers"`. Adding a registry entry for a single-use key is scope creep the same way a shared debounce hook would be; the existing `"suppliers"` domains list doesn't have one and none is needed for one call site. Literal key array matches how `useCatalogAutocomplete` embeds its own sub-keys (`"autocomplete"`, `searchTerm`, ...) past the shared prefix.
- `queryFn` fires only when `enabled` is true, so it never runs for sub-2-char debounced terms — no manual short-circuit-and-return-empty-object needed inside `queryFn` (unlike `useCatalogAutocomplete`, which returns `{ items: [] }` itself for historical reasons); `enabled: false` plus the derivation layer below already produces the same externally-visible result.
- No `staleTime` override: inherits the global `QueryClient` default (5 min) set in `App.tsx`. Supplier lists change rarely; identical repeated searches within 5 minutes are served from cache, satisfying FR-3.

**3. Derivation layer** — reconciles React Query's data shape back to the hook's existing public contract, and preserves the one behavior React Query's `enabled` gate can't express on its own: clearing results immediately when the user deletes back below 2 characters, without waiting for the debounce window.

```ts
const suppliers = searchTerm.length < 2 ? [] : (query.data?.suppliers ?? []);
const isLoading = searchTerm.length >= 2 && query.isFetching;
const error = query.error
  ? query.error instanceof Error
    ? query.error.message
    : "Failed to search suppliers"
  : null;

return { suppliers, isLoading, error };
```

Why gate on the *raw* `searchTerm`, not `debouncedSearchTerm`: today's hook clears `suppliers` synchronously the instant the effect re-runs with a short term (`useSuppliers.ts:15-18`), before the 300ms timer even starts. If the derivation instead read `debouncedSearchTerm.length < 2`, a user who types "ab" (query fires) then quickly deletes to "a" would keep seeing stale `suppliers` from the previous query for up to 300ms — a regression. Gating on the raw term preserves the immediate-clear behavior exactly.

`isLoading` uses `isFetching` (not bare `isLoading`) because `placeholderData: keepPreviousData` makes React Query's own `isLoading` stay `false` after the first successful fetch — the arch-review's whole point is smooth re-searches, so the spinner must still reflect fetches for term 2, 3, 4, etc. It's additionally gated on `searchTerm.length >= 2` so the spinner doesn't show during the 300ms window after a short term while a stale `isFetching: true` from the previous (now-disabled) query could otherwise still be true.

### Return shape (unchanged, satisfies FR-5)

```ts
{ suppliers: SupplierDto[]; isLoading: boolean; error: string | null }
```

Identical field names and types to today — `SupplierAutocomplete.tsx` requires zero edits.

## Data schemas

No schema changes anywhere in this task — it's a client-side data-fetching mechanism swap only.

- **Request**: `apiClient.suppliers_SearchSuppliers(searchTerm: string, limit: number)` → `GET /api/suppliers/search?searchTerm=...&limit=...` (generated client method, untouched).
- **Response**: `SearchSuppliersResponse { suppliers?: SupplierDto[] }` (generated DTO, untouched); `SupplierDto` re-exported as-is from `useSuppliers.ts:6`.
- **React Query cache key** (new — this is the only "schema" this task introduces, and it's a cache key, not a wire format):
  `["suppliers", "search", debouncedSearchTerm: string, limit: number]`
  Chosen to mirror the literal-array style of `useCatalogAutocomplete`'s key rather than adding a single-use entry to the shared `QUERY_KEYS` object (see Component design above).

## Summary of file-level change

Only `frontend/src/api/hooks/useSuppliers.ts` changes: the `useState`×3 + `useEffect` + `setTimeout`-fetch body of `useSupplierSearch` is replaced by the three layers above (debounce `useState`/`useEffect` retained, fetch logic replaced by `useQuery`). Imports change from `{ useState, useEffect }` to add `useQuery`, `keepPreviousData` from `@tanstack/react-query`, and `getAuthenticatedApiClient` from `../client` (already imported). No other file changes.
