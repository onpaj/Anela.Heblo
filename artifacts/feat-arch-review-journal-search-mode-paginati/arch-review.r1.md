I have enough context to write the architecture review. The fix matches established patterns in the codebase (`useInvoiceDetails`, `useManufactureDifficulty`, `useTransportBoxes`, `useManufactureOrderDetailQuery`, `useArticleTraceQuery`, and `useManufactureOrders` all use `enabled: boolean = true|false` as a hook parameter that gates `useQuery.enabled`). `useSearchJournalEntries` has exactly one production call site (`JournalList.tsx`), so the API change has minimal blast radius.

# Architecture Review: Fix Journal search pagination & sorting (auto-refetch on query-key change)

## Skip Design: true

## Architectural Fit Assessment

This is a surgical frontend bug fix that aligns cleanly with existing patterns in the codebase. There are no architectural concerns:

- **Convention precedent is solid.** The proposed `enabled: boolean = false` parameter is a well-established pattern in this repo. See `useInvoiceDetails(invoiceId, enabled = true)` (`useInvoiceClassification.ts:178`), `useManufactureDifficulty(..., enabled = true)`, `useManufactureOrderDetailQuery(id, enabled = true)`, `useArticleTraceQuery(id, enabled)`, and `useTransportBoxes` — all expose an `enabled` flag wired to `useQuery({ enabled })`. The new signature `useSearchJournalEntries(params, enabled = false)` slots in naturally; the only deviation is the default (`false` vs. the more common `true`), which is correct given the original "no request on mount" intent of the search hook.
- **Blast radius is bounded.** `useSearchJournalEntries` has exactly **one** production call site (`JournalList.tsx:189`). `useJournalEntriesByProduct` calls `journal_SearchJournalEntries` directly inside its own `useQuery`, so it is unaffected. Test files at `useJournal.test.ts:198, 385` and `JournalList.test.tsx:118, 238, 269, 310` rely on the existing one-arg signature; adding an optional second arg with a backward-compatible default does not break them.
- **No data-model, contract, or boundary impact.** Frontend-only change, same wire shape, same query key (`[...QUERY_KEYS.journal, "search", params]`), so cache hits across navigation are preserved.
- **Mutation invalidation already works.** `useCreateJournalEntry`, `useUpdateJournalEntry`, and `useDeleteJournalEntry` already invalidate `[...QUERY_KEYS.journal, "search"]` (`useJournal.ts:90, 118, 143`), so post-mutation freshness in search mode will continue to work via auto-refetch once `enabled` is `true`.

The fix corrects a behavioral asymmetry (default mode auto-refetches; search mode does not) without introducing a new pattern.

## Proposed Architecture

### Component Overview

```
┌──────────────────────────────────────────────────────────────────────┐
│ JournalList.tsx (page component)                                     │
│                                                                      │
│  state: searchTextFilter, isSearchMode, pageNumber, pageSize,        │
│         sortBy, sortDescending                                       │
│                                                                      │
│   ┌─────────────────────────────┐    ┌────────────────────────────┐  │
│   │ useJournalEntries(params)   │    │ useSearchJournalEntries(   │  │
│   │   enabled = true (implicit) │    │     params, isSearchMode)  │  │
│   │                             │    │   enabled = isSearchMode   │  │
│   └──────────────┬──────────────┘    └─────────────┬──────────────┘  │
│                  │                                  │                 │
│                  └────────── currentQuery ──────────┘                 │
│                  (selected by isSearchMode)                           │
│                  │                                                    │
│                  ▼                                                    │
│   table render, pagination footer, SortableHeader                     │
└──────────────────────────────────────────────────────────────────────┘
                  │
                  ▼ key change → React Query auto-refetch (when enabled)
┌──────────────────────────────────────────────────────────────────────┐
│ Backend: journal_GetJournalEntries / journal_SearchJournalEntries    │
└──────────────────────────────────────────────────────────────────────┘
```

The fix removes the asymmetry: both branches of `currentQuery` are now driven by React Query's `queryKey`-change auto-refetch mechanism. The conditional `enabled = isSearchMode` ensures only one of the two queries is live at a time.

### Key Design Decisions

#### Decision 1: Parameterize `enabled` rather than hardcode `enabled: true`

**Options considered:**
- **A.** Always-on (`enabled: true` or removing the line). Simplest, but fires an unwanted search request on mount when `searchTextFilter` is empty (it would call `journal_SearchJournalEntries(undefined, ...)`, returning unfiltered results that are then discarded). Wastes a backend round trip on every page load.
- **B.** Parameterize `enabled` with `false` default, bind to `isSearchMode` at the call site. Preserves the "no request on mount" guarantee; matches the existing repo pattern.
- **C.** Collapse `useJournalEntries` + `useSearchJournalEntries` into a single mode-aware hook. Cleaner conceptually but explicitly out of scope per the spec, and would be a much larger change.

**Chosen approach:** B — exactly as the spec proposes.

**Rationale:** Preserves backwards compatibility for any caller that opts out (none exist today, but the default protects future callers). Matches `useInvoiceDetails`, `useManufactureOrderDetailQuery`, `useTransportBoxQuery`, etc. The two-hook pattern stays, which keeps the diff surgical.

#### Decision 2: Keep the explicit `searchQuery.refetch()` in `handleApplyFilters`

**Options considered:**
- **A.** Remove it once auto-refetch is in place. Cleaner code, but breaks the UX contract when a user clicks **Filtrovat** without changing the input text (the query key is unchanged, so no auto-refetch fires).
- **B.** Keep it. Accepts a redundant request on the "text changed" path (auto-refetch + explicit refetch fire near-simultaneously; React Query coalesces in-flight requests with the same key, so the user-visible effect is still one render).

**Chosen approach:** B.

**Rationale:** "Click button → something visibly happens" is a stronger product invariant than the small efficiency win. React Query's in-flight de-duplication mitigates the cost in the common case. The spec captures this correctly in FR-6.

#### Decision 3: Do not memoize the `params` object

**Options considered:** Wrap `params` in `useMemo` to stabilize identity across renders.

**Chosen approach:** Pass a fresh object each render, as today.

**Rationale:** React Query serializes the query key for comparison; new object identity with structurally equal contents does not trigger a refetch. Adding `useMemo` would add ceremony without behavior change. Confirmed out of scope by the spec.

#### Decision 4: Pagination/sort handlers stay state-only

**Options considered:** Make `handlePageChange`/`handlePageSizeChange`/`handleSort` also call `searchQuery.refetch()` directly.

**Chosen approach:** Leave them as pure state setters. Refetch is driven entirely by `useQuery`'s key-change behavior once `enabled` is `true`.

**Rationale:** Single mechanism for data freshness (key-driven refetch) reduces the chance of regressing the same bug in a new handler later. It also keeps the handlers indistinguishable from their behavior in non-search mode, where they already work correctly.

## Implementation Guidance

### Directory / Module Structure

No new files. Two existing files are touched:

| File | Change |
|------|--------|
| `frontend/src/api/hooks/useJournal.ts` | Add `enabled: boolean = false` parameter to `useSearchJournalEntries`; forward to `useQuery({ enabled })`. Remove the hardcoded `enabled: false`. |
| `frontend/src/components/pages/Journal/JournalList.tsx` | Pass `isSearchMode` as the second argument at the existing call site (`line 189`). Nothing else changes. |

Tests:

| File | Change |
|------|--------|
| `frontend/src/api/hooks/__tests__/useJournal.test.ts` | Update the existing search test (`line 181`+) to either pass `enabled = true` or invoke `refetch()` (existing behavior). Add tests per NFR-4: (a) default `enabled` → no fetch on mount, (b) `enabled = true` → fetch on mount, (c) `enabled = true` + params change → auto-refetch. |
| `frontend/src/components/pages/Journal/__tests__/JournalList.test.tsx` | Add tests that simulate search mode → page change / sort click / page-size change and assert the search query mock is re-invoked with new params (or that the rendered rows reflect the new mock response). The existing `mockUseJournalHooks.useSearchJournalEntries.mockReturnValue(...)` pattern at lines 118/238/269/310 still applies; the new test will need to re-`mockReturnValue` between interactions to simulate the new page's data and assert the table re-renders. |

### Interfaces and Contracts

**Hook signature (the only public-API change):**

```ts
// frontend/src/api/hooks/useJournal.ts
export const useSearchJournalEntries = (
  params: SearchJournalParams,
  enabled?: boolean,    // default false — preserves current "no request on mount" semantics
) => UseQueryResult<...>
```

**Invariants the implementation must preserve:**

1. `queryKey: [...QUERY_KEYS.journal, "search", params]` — unchanged. Cache-by-params behavior is preserved.
2. When `enabled === false`, the hook does not run on mount and does not auto-refetch on key change. `.refetch()` is the only trigger. (Matches today's behavior, retained for backwards compatibility.)
3. When `enabled === true`, standard React Query semantics apply: runs on mount, auto-refetches on key change, integrates with `invalidateQueries` from sibling mutation hooks.
4. The `enabled` argument flows to `useQuery({ enabled })` literally — no AND-ing with other guards (unlike `useInvoiceDetails`, which additionally requires `!!invoiceId`). `SearchJournalParams.searchText` is allowed to be empty (the backend treats it as "no text filter"), so guarding on it would change semantics.

### Data Flow

**Search-mode pagination (the bug being fixed):**

```
user clicks "Page 2"
    └─> handlePageChange(2)
        └─> setPageNumber(2)
            └─> JournalList re-renders with pageNumber = 2
                └─> useSearchJournalEntries({ ..., pageNumber: 2 }, isSearchMode=true)
                    └─> queryKey changes (params is part of the key)
                        └─> React Query detects key change + enabled=true
                            └─> queryFn fires → journal_SearchJournalEntries(..., pageNumber=2, ...)
                                └─> isLoading flips → spinner (existing UI)
                                    └─> response arrives → data prop updates
                                        └─> table re-renders with page-2 rows
```

The same flow applies symmetrically to page-size changes and sort changes (each updates state that is part of `params`, which is part of the query key).

**Mode toggling:**

```
empty search + click "Filtrovat"   → isSearchMode stays false → entriesQuery active, searchQuery dormant
non-empty search + click "Filtrovat" → isSearchMode = true → searchQuery activates (auto-fetch)
click "Vymazat"                       → isSearchMode = false → searchQuery deactivates, entriesQuery shows
```

The transition between the two queries is observable via `currentQuery = isSearchMode ? searchQuery : entriesQuery` (`JournalList.tsx:197`) — unchanged.

**Mutation → invalidation → auto-refetch:**

```
user edits an entry → useUpdateJournalEntry.mutateAsync
    └─> onSuccess → queryClient.invalidateQueries([..., "search"])  (already in place)
        └─> with enabled=true, React Query refetches the active search query
            └─> table reflects the edit
```

(Today this only works via the explicit `searchQuery.refetch()` in `handleCloseModal`; after the fix, both mechanisms work, which is harmless.)

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Loading flicker on every pagination/sort click in search mode. | Low | Existing UI returns early on `loading` (`JournalList.tsx:287–296`), so each refetch will briefly show the spinner. This is consistent with non-search mode behavior. If perceived as regression, switch to a row-level skeleton or use `isFetching` instead of `isLoading` to keep the table visible during background refetch — out of scope for this fix. |
| Double-fire on **Filtrovat** when search text changed (auto-refetch + explicit `.refetch()`). | Low | React Query coalesces in-flight requests with the same query key, so at most one network request is in flight even if both triggers fire concurrently. Documented as acceptable in FR-6. |
| Empty-string `searchText` after clearing input would briefly run an unfiltered search if `isSearchMode` is still `true`. | Low | `handleClearFilters` sets `isSearchMode = false` before any re-render uses the new state. React batches state updates within the handler, so `searchQuery` is deactivated in the same commit that clears the filter. No spurious request. |
| Existing hook test (`useJournal.test.ts:181`) currently calls `result.current.refetch()` and will keep working, but the new default makes some assertions vacuous. | Low | Update the test alongside the implementation per NFR-4. New tests must include the negative case (`enabled = false` → no fetch on mount) to prevent regression of the original "no request on mount" intent. |
| Future call site of `useSearchJournalEntries` omits `enabled` and silently never fires. | Low | Default `false` was chosen specifically to preserve today's semantics for unaware callers. Add a one-line JSDoc comment on the hook explaining the flag's purpose so future callers see it. |
| Bug recurs if someone later adds a new param-driving state setter (e.g., a date filter) and again hardcodes `enabled: false`. | Low | The new tests at the component level (page change / sort / size → new data renders) act as a regression net. Recommend a brief note in `docs/architecture/development_guidelines.md` capturing the rule: "React Query hooks must rely on query-key changes for refetch; avoid `enabled: false` unless paired with a parameter that the call site can flip on." (Optional — not strictly required to merge.) |

## Specification Amendments

None required. The spec is internally consistent and matches the architectural constraints of the codebase. Two minor clarifications worth folding into the implementation PR description (not the spec):

1. **Test-file update required, not just additions.** The existing test at `useJournal.test.ts:181` will need its comment and explicit `refetch()` call adjusted (it can stay if the new default `enabled = false` is honored, but the comment "Search queries are disabled by default" should be updated to reflect that this is now a default, not a hardcoded constant).
2. **JSDoc on the new parameter** is recommended (one line) since the `false` default is unusual relative to peer hooks (`useInvoiceDetails`, `useManufactureDifficulty`, etc. all default to `true`). Without the comment, a developer scanning the signature could reasonably misread the default.

## Prerequisites

None. No migrations, config, infrastructure, feature flags, or backend changes. The fix can land in a single frontend PR.

- No `@tanstack/react-query` version bump (relies only on documented `enabled` semantics that have been stable since v3).
- No regeneration of the OpenAPI client.
- No environment variables.
- No Key Vault secrets.