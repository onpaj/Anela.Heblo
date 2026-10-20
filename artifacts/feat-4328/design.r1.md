# Design: Fix React Query cache key for `useDqtRunDetail` pagination

## Component Design

This feature has no user-facing UI component — the sole consumer,
`DqtRunDetail.tsx`, renders identically before and after the change (it does
not currently pass explicit `resultPage`/`resultPageSize` and has no page
navigation control). The change is entirely internal to the React Query hook
layer.

### `dataQualityKeys` (query key factory)
- **Responsibility:** produce a unique, stable cache key per distinct query
  input, for every DQT query used under `frontend/src/api/hooks/useDataQuality.ts`.
- **Change:** `runDetail` gains two required parameters, `resultPage` and
  `resultPageSize`, both included in the returned key tuple alongside the
  existing `runId`.
- **Contract:**
  ```typescript
  runDetail(runId: string, resultPage: number, resultPageSize: number): readonly (string | number)[]
  ```
  Two calls with the same `runId` but different `resultPage` or
  `resultPageSize` MUST return unequal key tuples (by React Query's
  deep-equality key comparison). Two calls with all three arguments equal
  MUST return equal key tuples.

### `useDqtRunDetail` (hook)
- **Responsibility:** fetch a single DQT run's detail (including paginated
  drift/invoice results) and expose it as a React Query result, only when
  `runId` is present.
- **Change:** the internal `useQuery({ queryKey: ... })` call is updated to
  pass the hook's own `resultPage`/`resultPageSize` (with their existing
  defaults of `1`/`50`) into `dataQualityKeys.runDetail`, instead of only
  `runId`.
- **Contract (unchanged):**
  ```typescript
  useDqtRunDetail(
    runId: string | null,
    resultPage?: number,   // default 1
    resultPageSize?: number, // default 50
  ): UseQueryResult<GetDqtRunDetailResponse>
  ```
  Public signature, `enabled: !!runId` gating, `staleTime`/`gcTime` values,
  and the `queryFn`'s call to `apiClient.dataQuality_GetRunDetail` are all
  unchanged.

No new components, hooks, or files are introduced.

## Data Schemas

No schema changes. `GetDqtRunDetailRequest`/`GetDqtRunDetailResponse` (backend
contract, OpenAPI-generated client types) are unchanged — this fix only
affects the client-side React Query cache key, which is not part of any
request/response payload, database schema, or event payload.

For reference, the affected key shape:

```typescript
// Before (bug): only runId in the key
dataQualityKeys.runDetail(runId)
  → [...QUERY_KEYS.dataQuality, 'runs', runId, 'detail']

// After (fixed): runId + pagination in the key
dataQualityKeys.runDetail(runId, resultPage, resultPageSize)
  → [...QUERY_KEYS.dataQuality, 'runs', runId, 'detail', resultPage, resultPageSize]
```
