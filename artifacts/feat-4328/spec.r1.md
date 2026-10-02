# Specification: Fix React Query cache key for `useDqtRunDetail` pagination

## Summary
`useDqtRunDetail` in `frontend/src/api/hooks/useDataQuality.ts` accepts `resultPage`
and `resultPageSize` parameters and forwards them to the API call, but the React
Query `queryKey` it uses (`dataQualityKeys.runDetail(runId)`) only encodes `runId`.
Because React Query treats an unchanged key as a cache hit, changing page while
viewing a run's drift/invoice results returns the previously cached page instead
of firing a new request. This spec covers making the query key key off the full
set of parameters that affect the response.

## Background
- `useDqtRunDetail` is defined at `frontend/src/api/hooks/useDataQuality.ts:59-74`.
- The query key factory lives in the same file at `dataQualityKeys.runDetail`
  (line 27-28) and currently takes only `runId`.
- The backend handler (`GetDqtRunDetailHandler.cs`) and `GetDqtRunDetailRequest`
  already accept and apply `ResultPage`/`ResultPageSize`, and
  `GetDqtRunDetailResponse` carries `TotalDriftResults`, confirming pagination
  is a supported, intended server-side behavior — the frontend cache key is
  simply incomplete relative to it.
- The only current caller, `frontend/src/components/data-quality/DqtRunDetail.tsx`
  (line 133), calls `useDqtRunDetail(runId)` with no explicit page args, so it
  always uses the defaults (`resultPage = 1`, `resultPageSize = 50`) and never
  triggers the bug today. The bug is latent: it will silently break the first
  pagination UI built against this hook, with no failing test today to catch it,
  which is exactly why this is being fixed proactively rather than waiting for
  a bug report.
- Existing unit tests: `frontend/src/api/hooks/__tests__/useDataQuality.test.ts`
  already exercises `useDqtRunDetail('run-1', 2, 25)` and asserts params are
  passed to `apiClient.dataQuality_GetRunDetail`, but does not currently assert
  anything about the query key contents.

## Functional Requirements

### FR-1: Query key includes pagination parameters
`dataQualityKeys.runDetail` must accept `resultPage` and `resultPageSize` in
addition to `runId`, and include all three in the returned key tuple, so that
two calls with the same `runId` but different `resultPage`/`resultPageSize`
produce different React Query cache keys.

**Acceptance criteria:**
- `dataQualityKeys.runDetail(runId, resultPage, resultPageSize)` returns a key
  that differs whenever `resultPage` or `resultPageSize` differs, for the same
  `runId`.
- `dataQualityKeys.runDetail('run-1', 1, 50)` !== `dataQualityKeys.runDetail('run-1', 2, 50)`
  (as JSON-serialized/deep-equality comparison, matching how React Query compares keys).

### FR-2: `useDqtRunDetail` passes pagination params into its own query key
The `useQuery` call inside `useDqtRunDetail` must call
`dataQualityKeys.runDetail(runId ?? '', resultPage, resultPageSize)` (using the
hook's own resolved `resultPage`/`resultPageSize`, including their defaults),
so that calling the hook with a different page produces a cache miss and a
real network request rather than silently replaying the previous page's cached
data.

**Acceptance criteria:**
- Given the hook is rendered with `(runId, 1, 50)` and then re-rendered with
  `(runId, 2, 50)`, a new `queryFn` invocation occurs (i.e. `dataQuality_GetRunDetail`
  is called again with page `2`), instead of returning the cached page-1 result.
- Given the hook is re-rendered with the same `(runId, resultPage, resultPageSize)`
  triple within the existing `staleTime` window, no new request fires (cache
  behavior for unchanged params is preserved — this is a targeted fix to the
  key, not a change to caching/staleness policy).
- The `enabled: !!runId` gating behavior is unchanged.

## Non-Functional Requirements

### NFR-1: Backward compatibility
No change to the hook's public call signature
(`useDqtRunDetail(runId, resultPage?, resultPageSize?)`) or to
`GetDqtRunDetailResponse`/API contract. This is purely a cache-key correctness
fix internal to the hook and its key factory. The single existing caller
(`DqtRunDetail.tsx`) requires no changes.

### NFR-2: Test coverage
The existing test suite (`useDataQuality.test.ts`) must be extended (or a new
test added) to assert that changing `resultPage`/`resultPageSize` between
renders results in a distinct query key / a new fetch, so this regression
cannot silently reoccur. This is the acceptance test that proves FR-1/FR-2.

## Data Model
No data model changes. No new entities. This only affects the shape of the
client-side React Query key tuple, which is not persisted or serialized
anywhere outside the React Query cache.

## API / Interface Design
- **Before:**
  ```typescript
  runDetail: (runId: string) =>
    [...QUERY_KEYS.dataQuality, 'runs', runId, 'detail'] as const,
  ```
- **After:**
  ```typescript
  runDetail: (runId: string, resultPage: number, resultPageSize: number) =>
    [...QUERY_KEYS.dataQuality, 'runs', runId, 'detail', resultPage, resultPageSize] as const,
  ```
- **`useDqtRunDetail` call site change:**
  ```typescript
  queryKey: dataQualityKeys.runDetail(runId ?? '', resultPage, resultPageSize),
  ```
  (replacing `dataQualityKeys.runDetail(runId ?? '')`).
- No REST endpoint, request/response DTO, or route changes. No changes to
  `GetDqtRunDetailRequest`/`GetDqtRunDetailHandler.cs`/`GetDqtRunDetailResponse`
  on the backend — this is a frontend-only fix.

## Dependencies
- `@tanstack/react-query` (already in use, no version change needed).
- No dependency on other in-flight features. This is an isolated hook-level
  bugfix inside `frontend/src/api/hooks/useDataQuality.ts`.

## Out of Scope
- Building an actual pagination UI/control for `DqtRunDetail.tsx` (the current
  component does not expose page navigation; that remains a separate future
  feature).
- Any change to `useDqtRuns` or its `dataQualityKeys.runs` key factory (that
  hook already includes its params object in the key and is not affected by
  this finding).
- Any backend change — the backend already correctly applies pagination.
- Broader React Query key-factory conventions/refactor beyond this one key.

## Open Questions
None.

## Status: COMPLETE
