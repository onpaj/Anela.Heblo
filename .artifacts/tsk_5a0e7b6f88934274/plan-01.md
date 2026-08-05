# Plan — useSupplierSearch: migrate from hand-rolled useEffect/setTimeout to React Query

## Summary

`useSupplierSearch` (`frontend/src/api/hooks/useSuppliers.ts:9-46`) is the only hook among 80 files in `frontend/src/api/hooks/` that fetches data via a manual `useEffect` + `setTimeout` + three `useState` cells instead of `useQuery`. This plan migrates it to `useQuery`, matching the pattern already used by every sibling hook (`usePurchaseStockAnalysis.ts`, `useCatalogAutocomplete.ts`, `useMaterials.ts`), while keeping the hook's public signature and return shape unchanged so its sole consumer, `SupplierAutocomplete.tsx`, needs no changes.

## Context

Arch-review finding: this hook reinvents debouncing, loading, and error state that React Query already provides, gets no caching or request de-duplication (`GET /api/suppliers/search` re-fires on every mount/keystroke), and cannot be tested with the standard `QueryClientProvider`-wrapped hook harness documented in `docs/architecture/testing-strategy.md:172`. This is purely a plumbing change — the hook already uses the generated typed client (`apiClient.suppliers_SearchSuppliers`) and generated DTOs, so no contract/DTO work is needed (distinct from prior hook findings #3333, #3395, #3221, #2101, #1611).

The most directly comparable precedent in the repo is `useCatalogAutocomplete.ts`, which is a `useQuery` keyed on `[searchTerm, limit, ...]`, gated by `enabled: Boolean(searchTerm && searchTerm.length >= 2)`, returning `{ items: [] }` from `queryFn` itself when the term is too short. `CatalogAutocomplete.tsx` debounces at the *component* level (local `searchTerm`/`debouncedSearchTerm` state + `useEffect`/`setTimeout`) and passes the debounced value into the hook — but that pattern requires touching the consumer. Since the task explicitly asks not to change `SupplierAutocomplete`'s usage more than necessary, this plan keeps debouncing *inside* `useSupplierSearch` instead, so the consumer keeps calling `useSupplierSearch(searchTerm)` unmodified.

## Functional requirements

**FR-1: `useSupplierSearch` fetches via `useQuery`, not manual `useEffect`/`fetch`.**
- Acceptance: no `useEffect`, `setTimeout`, or manual `useState` for `suppliers`/`isLoading`/`error` remain in `useSuppliers.ts`; data fetching goes through `useQuery` from `@tanstack/react-query`, calling `apiClient.suppliers_SearchSuppliers(searchTerm, limit)` in `queryFn`.
- Acceptance: `getAuthenticatedApiClient()` is called synchronously (not `await`ed) inside `queryFn`, matching the convention in `useCatalogAutocomplete.ts`/`usePurchaseStockAnalysis.ts` (the current `await getAuthenticatedApiClient()` in the hand-rolled version is itself non-idiomatic here — confirm the function's actual signature is synchronous before dropping the `await`, since `SupplierAutocomplete.tsx` line 3 imports it the same way).

**FR-2: Debouncing is preserved with equivalent UX, without touching `SupplierAutocomplete.tsx`.**
- Acceptance: the hook still waits ~300ms after the last keystroke before issuing a request, implemented via an internal debounced-value state (local `useState`+`useEffect`+`setTimeout` inside `useSuppliers.ts`, or a small private helper in the same file) feeding the `useQuery` key/`queryFn`, not by adding debounce state to the component.
- Acceptance: when the *raw* (non-debounced) `searchTerm` drops below 2 characters (including empty string), the hook returns an empty `suppliers` array immediately — it must not wait out the debounce window before clearing results, matching current behavior (`useSuppliers.ts:15-18` clears synchronously before the `setTimeout` is even scheduled). This means the returned `suppliers` should be gated on the raw term's length at the hook-return level, independent of what the debounced query still has cached.
- Acceptance: the query itself is gated with `enabled: debouncedSearchTerm.length >= 2` so no request fires for short terms.

**FR-3: Query is keyed correctly and cached/de-duplicated.**
- Acceptance: `queryKey` includes a namespacing key plus the debounced search term and `limit` (e.g. `["suppliers", "search", debouncedSearchTerm, limit]`), so identical searches are served from cache and identical concurrent searches are de-duplicated by React Query — the two things the arch-review finding calls out as missing today.
- Acceptance: repeated identical searches within `staleTime` do not re-hit `GET /api/suppliers/search` (verify via a quick manual check or a hook test using a mocked API client with a call counter).

**FR-4: Smooth autocomplete via `keepPreviousData`.**
- Acceptance: `useQuery` is configured with `placeholderData: keepPreviousData` (v5 API, imported from `@tanstack/react-query`; project is on `@tanstack/react-query@^5.59.0` per `frontend/package.json:23`) so the previous result list stays visible (no flash-to-empty) while a new debounced search is in flight.

**FR-5: Public return shape of `useSupplierSearch` is preserved.**
- Acceptance: hook continues to return `{ suppliers, isLoading, error }` with the same types (`SupplierDto[]`, `boolean`, `string | null`) as before, so `SupplierAutocomplete.tsx` requires zero changes.
- Acceptance: `isLoading` reflects "a fetch is in flight" for the current debounced term — use React Query's `isFetching` (not bare `isLoading`, which with `keepPreviousData` would stay `false` on subsequent searches) so the spinner in `SupplierAutocomplete.tsx:158-159,175-179` still shows during every debounced re-search, not just the first one.
- Acceptance: `error` is derived as `queryError instanceof Error ? queryError.message : "Failed to search suppliers"` when a query error is present, else `null` — same fallback message as the current code (`useSuppliers.ts:34-36`).

## Non-functional requirements

- **No behavior regression for the sole consumer.** `SupplierAutocomplete.tsx` must continue to work exactly as before from a UX standpoint (debounce delay, immediate-clear-on-short-input, loading spinner, error display) — verify manually in the browser (e.g. via a page that uses `SupplierAutocomplete`, such as a purchase order form) since this is a UI-facing hook.
- **Match sibling hook conventions exactly**, not just "use useQuery somehow": query key shape, `enabled` gate style, and not `await`ing `getAuthenticatedApiClient()` should mirror `useCatalogAutocomplete.ts`.
- **No new dependencies.** `keepPreviousData` is already available in the installed `@tanstack/react-query` v5; no package changes needed.
- **Minimal blast radius per project convention** (CLAUDE.md "Surgical changes"): only `useSuppliers.ts` should change. Do not touch `SupplierAutocomplete.tsx`, `client.ts`'s `QUERY_KEYS`, or generated API client files unless something there turns out to block the migration.

## Data model

No data model changes. Existing generated types are reused as-is:
- `SupplierDto` (generated, re-exported from `useSuppliers.ts:6`)
- `SearchSuppliersResponse` (generated, re-exported from `useSuppliers.ts:6`) — shape `{ suppliers?: SupplierDto[] }`, consumed via `response.suppliers || []`.

## Interfaces

- No backend/API surface changes — same generated client method `apiClient.suppliers_SearchSuppliers(searchTerm, limit)` calling `GET /api/suppliers/search`.
- No change to `useSupplierSearch`'s call signature: `useSupplierSearch(searchTerm: string, limit: number = 10)`.
- No change to `SupplierAutocomplete`'s props or usage of the hook (`SupplierAutocomplete.tsx:29`).

## Dependencies and scope

**In scope:**
- Rewrite `frontend/src/api/hooks/useSuppliers.ts` to use `useQuery` + `placeholderData: keepPreviousData`, with internal debouncing, per FR-1..FR-5.

**Out of scope:**
- Any change to `SupplierAutocomplete.tsx` or other consumers (there are none besides this component).
- Any change to the generated API client, backend controller, or DTOs.
- The other previously-filed hook findings (#3333, #3395, #3221, #2101, #1611) — unrelated hooks, not touched here.
- Adding a new shared/reusable `useDebouncedValue` hook — there's no existing one in the codebase (confirmed: no `useDebounce` usage anywhere in `frontend/src`), and introducing a new shared abstraction for a single caller would be scope creep per the project's anti-premature-abstraction rule. Keep the debounce logic local and private to `useSuppliers.ts`.
- Adding a dedicated unit test file for the hook. None exists today for `useSuppliers.ts` or `SupplierAutocomplete.tsx`; the task is a pattern migration, not new test coverage. Flagged as an open question below in case the reviewer wants one added opportunistically since the migration is what makes testing possible.

## Rough plan

1. Rewrite `useSupplierSearch` in `frontend/src/api/hooks/useSuppliers.ts`:
   - Add local debounce state (`searchTerm` param → internal `debouncedSearchTerm` via `useState`/`useEffect`/`setTimeout`, 300ms, cleared on unmount/change — same timing as today).
   - Replace the `useState`+`useEffect`+`fetch` block with `useQuery({ queryKey: ["suppliers", "search", debouncedSearchTerm, limit], queryFn: ..., enabled: debouncedSearchTerm.length >= 2, placeholderData: keepPreviousData, staleTime: ... })`.
   - Derive `suppliers` for the return value: `[]` if the *raw* `searchTerm.length < 2`, else `data?.suppliers ?? []`.
   - Derive `isLoading` from `isFetching` (gated similarly so it isn't misleadingly `true` for short terms with no active query).
   - Derive `error` from the query's `error` object, falling back to the same message string as before.
2. Confirm `getAuthenticatedApiClient()`'s actual signature (sync vs. async) by reading `frontend/src/api/client.ts:276` before dropping the `await`, and adjust `queryFn` accordingly.
3. Manually verify in the browser: open a page using `SupplierAutocomplete` (search codebase for its usage — e.g. a purchase order form), type a query, confirm debounce/loading spinner/results/clear-on-short-input all behave as before, and confirm (via network tab) that repeating the same search doesn't re-fire the request.
4. Run `npm run build` and `npm run lint` in `frontend/` per project validation requirements; run any existing frontend test suite to confirm nothing else imports/relies on the old implementation details of this hook.
5. `dotnet build`/`dotnet format` are not applicable (frontend-only change) — skip, but confirm no BE files are touched.

## Open questions

- **Should a hook test be added as part of this change?** The arch-review finding explicitly notes this hook currently *can't* be tested with the standard `QueryClientProvider` harness — after migration it can be. Default: leave test-writing out of scope since the task is a pure refactor and none exists today for comparison hooks like `useCatalogAutocomplete`either; flag to the reviewer as a natural follow-up rather than bundling it in.
- **`staleTime`/`gcTime` values** aren't specified by the task. Default: don't override — inherit the global `QueryClient` defaults (`staleTime: 5min`, `gcTime: 10min`, `retry: 1` — set in `frontend/src/App.tsx:102-110`), same as most sibling hooks that don't override, unless a build/lint/manual check reveals a reason to (e.g. staleness causing visibly wrong dropdown results for fast-changing supplier data — unlikely, supplier lists change rarely).
- **Exact debounce implementation location** (component-embedded `useEffect` vs. a tiny private helper function within the same file) is left to the implementer; either satisfies FR-2 as long as `SupplierAutocomplete.tsx` isn't touched and timing/immediate-clear semantics match.
