# Specification: Unit tests for useBackgroundRefresh raw HTTP layer

## Summary
Add Jest unit tests for `frontend/src/api/hooks/useBackgroundRefresh.ts` (0% coverage, 67 lines), covering success and `!response.ok` paths of all five fetch functions, and fix the inconsistent error handling in `forceRefreshTask` (unguarded `response.json()` on error).

## Background
The hook file uses a hand-written HTTP layer (`(apiClient as any).http.fetch`) instead of generated client methods. `forceRefreshTask` calls `await response.json()` on error without `.catch`, so a non-JSON error body (e.g. HTML 502) throws a SyntaxError that masks the HTTP status. `runHydrationTier` already uses `.catch(() => ({}))`.

## Functional Requirements

### FR-1: Query fetch function tests
Cover via hooks `useBackgroundTasks`, `useTaskHistory`, `useAllHistory`, `useTaskStatus`.
**Acceptance criteria:**
- Success: correct URL (`${baseUrl}/api/backgroundrefresh/...`), GET method, `Accept: application/json`, returned JSON exposed as `data`.
- `taskId` is URI-encoded; default `maxRecords` (50 history, 100 all) and custom values appear in the query string.
- `!response.ok`: query errors with message `Failed to fetch background tasks: 500`, `Failed to fetch task history: <status>`, `Failed to fetch all history: <status>`, `Failed to fetch task status: <status>` respectively.
- `useTaskHistory(null)` and `useTaskStatus(null)` do not call fetch.

### FR-2: forceRefreshTask tests and fix
**Acceptance criteria:**
- Success: POST to `/tasks/{id}/force-refresh`, resolves, invalidates `QUERY_KEYS.backgroundRefresh`.
- Error with JSON body `{error: "msg"}` throws `msg`.
- Error with JSON body lacking `error` throws `Failed to force refresh task: <status>`.
- Error with non-JSON body (json() rejects) throws `Failed to force refresh task: <status>` (requires production fix: `await response.json().catch(() => ({}))`).

### FR-3: runHydrationTier tests
**Acceptance criteria:**
- Success: POST `/tiers/{tier}/run`, invalidates queries.
- Error JSON `{error}` -> that message; non-JSON -> `Failed to run hydration tier {tier}: <status>`.

## Non-Functional Requirements
### NFR-1: Performance
Tests run fast, no real timers/network; refetch intervals must not leak (QueryClient per test, unmount).
### NFR-2: Security
No change.

## Data Model
None.

## API / Interface Design
No API change. Only change to production code: one-line `.catch(() => ({}))` in `forceRefreshTask`. Hook signatures unchanged.

## Dependencies
Jest, @testing-library/react, @tanstack/react-query, mocking of `../../client` (`getAuthenticatedApiClient`), per `useCarrierCooling.test.ts` pattern.

## Out of Scope
Refactoring the raw layer to a shared helper; UI components (`BackgroundTasksCard`, `TaskHistoryModal`); backend.

## Open Questions
None.

## Status: COMPLETE
