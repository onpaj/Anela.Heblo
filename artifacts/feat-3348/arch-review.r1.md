# Architecture Review: Fix Catalog Pagination Reset Race Condition

## Skip Design: true

## Architectural Fit Assessment

This is a pure frontend bug fix. No backend, API, database, or infrastructure components are involved. The change touches a single application file (`CatalogList.tsx`) and three E2E test files. It fits entirely within the existing React + React Router + React Query architecture and requires no new patterns, abstractions, or modules.

## Proposed Architecture

### Component Overview

`CatalogList.tsx` manages catalog filter, pagination, and sort state using a dual-sync pattern:

- **React state** drives the API query (via `useCatalogQuery`).
- **URL search params** are kept in sync with that state so the page is bookmarkable and back/forward navigation works.

The component currently has three `useEffect` hooks that interact with `useSearchParams`:

| Lines | Name | Dependency array | `setSearchParams` call |
|---|---|---|---|
| 188–206 | Synchronize filter state with URL parameters | `[productNameFilter, productCodeFilter, productTypeFilter, pageNumber, pageSize, sortBy, sortDescending, setSearchParams]` | `setSearchParams(params, { replace: true })` — no history entry |
| 210–243 | Handle browser back/forward navigation | `[searchParams]` (exhaustive-deps suppressed) | none — reads URL, sets state |
| 245–255 | Sync page number state from URL parameter changes | `[searchParams, pageNumber]` | none — reads URL, sets state |
| 258–275 | Sync URL parameter with page number state | `[pageNumber, searchParams, setSearchParams]` | `setSearchParams(newParams)` — **creates a history entry** |

### Key Design Decisions

#### Decision 1: Remove the "Sync URL parameter with page number state" effect (lines 258–275)

**Options considered:**

1. Remove effect 258–275 entirely.
2. Guard the effect with a ref-based "last committed page" to avoid the stale-closure window.
3. Rewrite `handleApplyFilters` to call `flushSync` before `setSearchParams` so state is committed before effects fire.

**Chosen approach:** Option 1 — remove effect 258–275.

**Rationale:** Effect 188–206 already covers every case the removed effect was handling:

- It depends directly on `pageNumber` in its dependency array, so it fires only after `setPageNumber(1)` has committed — no stale-value window.
- It rebuilds the full parameter set from all state in a single call, which is the correct single source of truth.
- It uses `{ replace: true }`, which satisfies NFR-1 (no extra history entries from internal sync).

The removed effect added a second, partial sync on the same state variable (`pageNumber`) with a different `setSearchParams` call that did not use `{ replace: true }`. This was redundant and introduced the race condition: when `handleApplyFilters` wrote a clean URL (no `page`), the removed effect immediately re-read a stale `pageNumber = 2` from the closure and wrote `page=2` back.

Removing it leaves a clean single-write-path: all URL writes from state changes flow through effect 188–206 with `{ replace: true }`.

#### Decision 2: `handlePageChange` requires no changes

**Options considered:**

1. Update `handlePageChange` to call `setSearchParams` directly in addition to `setPageNumber`.
2. Leave `handlePageChange` as-is and rely on effect 188–206 to sync the URL.

**Chosen approach:** Option 2 — no change to `handlePageChange`.

**Rationale:** The spec's statement that `handlePageChange` must "directly call `setSearchParams`" was based on the assumption that effect 258–275 was the only mechanism pushing page changes to the URL. That is incorrect: effect 188–206 at line 197 already writes `page=N` when `pageNumber !== 1`. Because that effect uses `{ replace: true }`, pagination navigation does not create extra history entries — this is the correct behavior for pagination within the same filter context. Browser back/forward restores prior filter state, not individual pagination steps, which is the expected UX. No change to `handlePageChange` is needed.

## Implementation Guidance

### Directory / Module Structure

```
frontend/src/components/pages/
  CatalogList.tsx                  -- remove lines 257–275 (the effect + blank line before it)

frontend/test/e2e/catalog/
  combined-filters.spec.ts         -- update assertions, remove bug comments
  pagination-with-filters.spec.ts  -- update assertions, remove bug comments
  text-search-filters.spec.ts      -- optional comment housekeeping only
```

### Interfaces and Contracts

No interface or contract changes. The `useCatalogQuery` hook signature, the `Pagination` component props, and the `GET /api/catalog` API are all unaffected.

### Data Flow

**Before fix — filter-apply with page=2 active:**

```
handleApplyFilters()
  setPageNumber(1)          -- schedules state update
  setSearchParams(params)   -- no `page` param; commits URL immediately
                            -- React schedules re-render
  [effect 258–275 fires with stale pageNumber=2 from previous render]
    --> setSearchParams({ page: 2 })   -- BUG: restores page=2
  [effect 188–206 fires with committed pageNumber=1]
    --> setSearchParams({ }, replace)  -- correct, but races with above
```

**After fix — same sequence:**

```
handleApplyFilters()
  setPageNumber(1)          -- schedules state update
  setSearchParams(params)   -- no `page` param; commits URL immediately
  [effect 188–206 fires with committed pageNumber=1]
    --> setSearchParams({ }, replace)  -- correct, no race
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Effect 188–206 fires on every filter/page/sort state change; with the redundant effect removed it is now the sole URL writer, which was always the intent — but a regression in its dependency array would silently break sync | Low | Existing unit tests cover URL sync; E2E nightly suite validates end-to-end behavior |
| Effect 245–255 ("Sync page number state from URL parameter changes") partially overlaps with the back/forward effect (210–243) and could fire redundant `setPageNumber` calls | Low | This effect is explicitly out of scope per the spec; it predates this fix and is not made worse by it. Document in `memory/gotchas/` for future cleanup. |
| Browser history behavior changes: effect 258–275 was writing history entries for page changes; effect 188–206 uses `replace` — pagination navigation will no longer create back-navigation steps within the same filter context | Low/Intentional | This is the correct UX. The spec confirms NFR-1 requires `{ replace: true }`. Back/forward still restores filter state via effect 210–243. |
| Spec amendment below contradicts one assertion in `pagination-with-filters.spec.ts` | Medium | See Specification Amendments. |

## Specification Amendments

The spec states at line 80–81: "the `handlePageChange` function … must be updated to directly call `setSearchParams` to maintain browser history entries for pagination navigation (since the removed effect was previously responsible for this)."

This is incorrect. Effect 188–206 already handles page-to-URL sync via `setSearchParams(params, { replace: true })` and has `pageNumber` in its dependency array. Adding a direct `setSearchParams` call to `handlePageChange` would create duplicate URL writes and would introduce history entries that the spec itself prohibits in NFR-1. No change to `handlePageChange` is required.

The spec's FR-3 and the `pagination-with-filters.spec.ts` assertion change (lines 113–137, from `toBe(1)` to `toBe(2)`) are correct and consistent with the fix.

## Prerequisites

- No migrations, environment changes, or infrastructure work needed.
- E2E suite (`./scripts/run-playwright-tests.sh`) must be run against staging after the change to verify the two previously-failing tests in `text-search-filters.spec.ts` now pass and that FR-3's `toBe(2)` assertion holds.
