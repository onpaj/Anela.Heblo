# Architecture Review: Fix React Query cache key for `useDqtRunDetail` pagination

## Skip Design: true

This is a backend-invisible, UI-invisible, cache-key-only correctness fix. No
new components, screens, layouts, or visual states are introduced — the single
existing consumer (`DqtRunDetail.tsx`) is unchanged and renders identically
before and after this fix. The designer phase is skipped.

## Architectural Fit Assessment

The finding is correct and the fix is a small, localized, one-file change that
aligns with an existing, well-established convention already used everywhere
else in `frontend/src/api/hooks/`: **every query-key factory that has
parameters affecting the response includes those parameters in the key.**

Evidence from the codebase (not a proposed pattern — the existing pattern):
- `useCatalog.ts:115` — `[...QUERY_KEYS.catalog, "list", params]`
- `useCatalog.ts:171` — `[...QUERY_KEYS.catalog, "detail", productCode, monthsBack]`
  (note: this is the closest analog — a detail-by-id query that also takes a
  secondary scalar parameter, and it puts that parameter in the key)
- `useBankStatements.ts:65` — `[...QUERY_KEYS.bankStatements, 'list', request]`
- `useBackgroundRefresh.ts:143` — `[...QUERY_KEYS.backgroundRefresh, "history", taskId, maxRecords]`
- `useArticles.ts` — `articleKeys.list(params)` / `articleKeys.detail(id)`

`dataQualityKeys.runDetail(runId)` (the one under review, at
`useDataQuality.ts:27-28`) is the outlier: it is the only "detail-style" key
in the hooks directory that accepts extra scalar parameters at the call site
(`resultPage`, `resultPageSize`) but drops them before they reach the key. This
is a straightforward omission relative to the codebase's own convention, not a
new architectural question — the fix is to bring this one factory in line with
what every sibling factory already does.

`dataQualityKeys.runs` in the same file (line 25-26) already does this
correctly — it takes the whole `GetDqtRunsParams` object and puts it in the
key. `runDetail` is the one spot that regressed.

## Proposed Architecture

### Component Overview

No new components. Change is confined to one file:

```
frontend/src/api/hooks/useDataQuality.ts
├── dataQualityKeys.runDetail(...)   ← factory signature gains 2 params
└── useDqtRunDetail(...)             ← useQuery() call site updated to match
```

Consumer (`DqtRunDetail.tsx:133`) is unaffected — it calls `useDqtRunDetail(runId)`
positionally and will continue to compile and behave identically, since
`resultPage`/`resultPageSize` remain optional hook parameters with unchanged
defaults (`1`, `50`).

### Key Design Decisions

#### Decision 1: Where do `resultPage`/`resultPageSize` live in the key tuple

**Options considered:**
1. Append `resultPage, resultPageSize` as trailing scalars in the tuple
   (matches `useBackgroundRefresh.ts:143`'s `taskId, maxRecords` and
   `useCatalog.ts:171`'s `productCode, monthsBack` style).
2. Bundle them into a single params object, e.g.
   `runDetail: (runId, params: { resultPage, resultPageSize })`
   (matches `dataQualityKeys.runs`'s object-params style).

**Chosen approach:** Option 1 — trailing scalar arguments, matching the spec's
proposed shape exactly:
```typescript
runDetail: (runId: string, resultPage: number, resultPageSize: number) =>
  [...QUERY_KEYS.dataQuality, 'runs', runId, 'detail', resultPage, resultPageSize] as const,
```

**Rationale:** `useDqtRunDetail`'s own signature is already three positional
scalars (`runId, resultPage, resultPageSize`), so the key factory's signature
should mirror the hook's signature 1:1 — this is the smallest possible diff,
requires no new type/interface, and matches the nearest analog in the codebase
(`useCatalog.ts`'s `detail(productCode, monthsBack)`), which is a
positional-scalar detail-key with one extra parameter, not an object-params
key. Introducing an object-params variant here would be inconsistent with that
nearest precedent and is unjustified extra surface for a 2-parameter case.

#### Decision 2: Scope of the fix

**Options considered:**
1. Fix only `runDetail`'s key (what the finding describes).
2. Broaden this into a general query-key-factory convention doc/lint rule
   across all hooks.

**Chosen approach:** Option 1, strictly scoped to `runDetail`.

**Rationale:** `dataQualityKeys.runs` and the rest of the hooks directory
already follow the correct convention (verified above) — there is no
systemic defect to fix, only this one factory. A repo-wide convention
doc/lint rule is out of proportion to a single-file bugfix and is explicitly
out of scope per the spec. If a reviewer wants a broader guard (e.g. an
ESLint rule for exhaustive query keys), that is a separate proposal, not a
prerequisite for this fix.

## Implementation Guidance

### Directory / Module Structure

No new files or directories. Edit in place:
- `frontend/src/api/hooks/useDataQuality.ts` — `dataQualityKeys.runDetail`
  (lines 27-28) and the `useQuery` call inside `useDqtRunDetail` (line 65).
- `frontend/src/api/hooks/__tests__/useDataQuality.test.ts` — extend to assert
  the key changes / a new fetch fires when `resultPage`/`resultPageSize`
  change (NFR-2 in the spec). The existing `describe('useDqtRunDetail', ...)`
  block (starting line 85) is the right place; it already exercises
  `useDqtRunDetail('run-1', 2, 25)`.

### Interfaces and Contracts

```typescript
// dataQualityKeys.runDetail: signature change
// before:
runDetail: (runId: string) =>
  [...QUERY_KEYS.dataQuality, 'runs', runId, 'detail'] as const,
// after:
runDetail: (runId: string, resultPage: number, resultPageSize: number) =>
  [...QUERY_KEYS.dataQuality, 'runs', runId, 'detail', resultPage, resultPageSize] as const,
```

```typescript
// useDqtRunDetail's useQuery call: key changes to match
// before:
queryKey: dataQualityKeys.runDetail(runId ?? ''),
// after:
queryKey: dataQualityKeys.runDetail(runId ?? '', resultPage, resultPageSize),
```

No other file references `dataQualityKeys.runDetail` (confirmed by search) —
this is not a breaking change for any other call site. No public API/DTO
contract changes; no OpenAPI client regeneration needed.

### Data Flow

Unchanged. `useDqtRunDetail(runId, resultPage, resultPageSize)` still calls
`apiClient.dataQuality_GetRunDetail(runId, resultPage, resultPageSize)` inside
`queryFn`. The only change is that React Query's internal cache lookup now
correctly treats `(runId, 1, 50)` and `(runId, 2, 50)` as distinct cache
entries, so a page change now reaches `queryFn` (and the network) instead of
being served from the page-1 cache entry.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Missed call site still using the 1-arg key shape elsewhere | Low | Confirmed via repo-wide search: `dataQualityKeys.runDetail` has exactly one call site (`useDataQuality.ts:65`); no other file imports/calls it. |
| Test doesn't actually catch a regression (assertion too weak) | Medium | Test must assert on the resulting `queryKey` array contents (or observe a second `queryFn`/API call after a page change), not just that params reach `apiClient.dataQuality_GetRunDetail` — the latter already passes today and did not catch this bug. |
| gcTime/staleTime tuning masking the fix in manual testing | Low | `staleTime: 30_000` only affects background refetch-on-mount behavior, not cache key identity; a distinct key is a distinct cache entry regardless of staleTime, so this doesn't interact with the fix. No change needed. |

## Specification Amendments

None. The spec's proposed `queryKey`/factory shape is architecturally correct
and matches existing codebase convention exactly (see Decision 1). No
additions needed beyond what NFR-2 already requires (test coverage proving
the key changes on page change, not just that params are forwarded to the API
client).

## Prerequisites

None. No migrations, config, or infrastructure changes. Implementation can
start immediately — this is a self-contained frontend hook fix plus a test
update in the same PR.
