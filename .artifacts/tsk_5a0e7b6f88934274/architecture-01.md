# Architecture review — useSupplierSearch: React Query migration

## Verdict

**Approved as designed.** The plan and design in `plan-01.md`/`design-01.md` were checked against the actual source of every hook/component/config they cite. All cited line numbers, signatures, and conventions match current code. No invariant in this codebase is violated; no changes to the design are required before implementation.

## Alignment with existing patterns

Verified directly against source, not just against the design doc's claims:

- **`getAuthenticatedApiClient()` is synchronous** (`frontend/src/api/client.ts:276-279`, returns `ApiClient` not `Promise<ApiClient>`). The design's decision to call it un-awaited inside `queryFn` is correct and matches `useCatalogAutocomplete.ts:25` and `usePurchaseStockAnalysis.ts:52`. The current hand-rolled code's `await getAuthenticatedApiClient()` (`useSuppliers.ts:26`) is itself the non-idiomatic outlier being fixed.
- **`useQuery` + local `queryFn` gate instead of a global `QUERY_KEYS` entry** is an established, not just tolerated, pattern: `usePurchaseStockAnalysis.ts:38-43` defines its own private `stockAnalysisKeys` object rather than extending the shared `QUERY_KEYS` (`client.ts:474-...`), for the same reason the design gives (single-domain, single-caller key). The design's literal-array key (`["suppliers", "search", debouncedSearchTerm, limit]`) is consistent with this precedent — arguably even simpler than `usePurchaseStockAnalysis`'s factory object, which is fine given there's only one hook and one query shape here.
- **`enabled` gate on a length check** mirrors `useCatalogAutocomplete.ts:36` (`enabled: Boolean(searchTerm && searchTerm.length >= 2)`) exactly.
- **No `staleTime` override** is a defensible default — it inherits `App.tsx:105` (`staleTime: 5 * 60 * 1000`), same as several sibling hooks that don't override (e.g. `useCatalogAutocomplete` *does* override to 5 min explicitly, which is actually identical to the global default — so omitting it here changes nothing observable).
- **Return-shape preservation**: confirmed `SupplierAutocomplete.tsx:29` only destructures `{ suppliers, isLoading }` (not `error`, though it's also returned and used at line 166 via the `error` *prop*, not the hook's `error` — worth noting but not a design flaw, see Risks) and has zero dependency on how the hook fetches. The design correctly leaves this file untouched.
- **Testing harness assumption**: `docs/architecture/testing-strategy.md:170-189` confirms the standard hook/component test pattern wraps in `QueryClientProvider`. Migrating unblocks this hook for that harness, as the task states.

## Proposed architecture (as designed)

Three-layer structure inside `useSuppliers.ts`, no other files touched:

1. **Debounce layer** — private `useState`/`useEffect`/`setTimeout` (300ms) producing `debouncedSearchTerm`. This is the one piece of hand-rolled state kept intentionally, since React Query has no built-in debounce primitive and the task forbids pushing this responsibility into `SupplierAutocomplete.tsx`.
2. **Query layer** — `useQuery({ queryKey: ["suppliers","search",debouncedSearchTerm,limit], queryFn, enabled: debouncedSearchTerm.length >= 2, placeholderData: keepPreviousData })`.
3. **Derivation layer** — reconciles React Query's shape back to `{ suppliers, isLoading, error }`, critically gating `suppliers`/`isLoading` on the **raw** `searchTerm`, not the debounced one, to preserve today's synchronous-clear-on-short-input behavior.

This is the right shape. The key architectural decision worth calling out explicitly: **gating the derivation on the raw term, not the debounced term, is load-bearing and non-obvious** — it's the only way to keep `enabled`'s async gate from introducing a regression (stale results lingering up to 300ms after the user deletes back below 2 characters). The design's own trace-through of this (design-01.md:78) is correct; implementers should not "simplify" this to gate on `debouncedSearchTerm` for symmetry, as that reintroduces the bug.

## Implementation guidance

- Change is confined to `frontend/src/api/hooks/useSuppliers.ts`. Imports move from `{ useState, useEffect }` to `{ useState, useEffect }` (debounce layer still needs both) plus `{ useQuery, keepPreviousData }` from `@tanstack/react-query`.
- `keepPreviousData` is not used anywhere else in `frontend/src` today (confirmed via grep) — this introduces it as a first instance in the codebase. That's not a conflict with any convention (it's a documented v5 export already available via the installed `@tanstack/react-query@^5.59.0`), and the task's own "Suggested direction" explicitly asks for it, so this is expected new-but-idiomatic surface, not scope creep.
- `isLoading` must be derived from `query.isFetching`, not `query.isLoading` — the design already gets this right and explains why (`keepPreviousData` makes bare `isLoading` false after the first successful fetch, which would break the dropdown spinner on the 2nd+ search). Flag this as a common mistake to catch in review if the implementer free-hands it differently than the design.
- Error fallback message must stay `"Failed to search suppliers"` to match current UX (`useSuppliers.ts:34-36`).

## Risks and mitigations

1. **`error` field is currently unused by the consumer for the hook's own error state** — `SupplierAutocomplete.tsx` receives `error` as a *prop* (parent-supplied validation error, line 19/166), not from `useSupplierSearch`'s return value at all; the hook's own `{ error }` is silently dropped by the destructure at line 29 (`const { suppliers, isLoading } = useSupplierSearch(searchTerm)`). This means the migration's error-handling work is inert today (was already inert before the migration too) — not a defect in the design, since FR-5 correctly preserves the field for shape-parity, but implementers shouldn't expect to see an error state surface in the UI during manual verification. Not a blocker; just don't spend verification time hunting for an error UI that isn't wired up.
2. **React StrictMode double-effect in dev** could cause the debounce `useEffect` to schedule/cancel twice on mount — pre-existing behavior of the current code too (it has the identical effect shape today), so this is a non-issue, not a new risk introduced by the migration.
3. **ESLint `react-hooks/exhaustive-deps`** (part of `react-app` config, `frontend/.eslintrc`) will likely warn (not error, under CRA's config) if the debounce effect's dependency array omits something — implementer should keep `[searchTerm]` only (not `limit`), matching the design; this is intentional (limit changes don't need re-debouncing) and consistent with the original code's behavior for the fetch trigger.

No prerequisites block starting implementation. The plan/design already correctly scoped out a shared `useDebounce` hook, a `QUERY_KEYS.suppliers` registry entry, and new test files as out-of-scope, each for reasons consistent with this codebase's conventions (single call site, per-domain key registry pattern that already tolerates local key objects, no existing test precedent for sibling hooks).

## Summary

Design and plan are implementation-ready as written. No architectural changes requested.
