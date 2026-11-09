# Architecture Review: useBackgroundRefresh test coverage

## Skip Design: true

## Architectural Fit Assessment
Frontend-only, test addition plus a one-line robustness fix. Follows existing hook test convention in `frontend/src/api/hooks/__tests__/` (mock `../../client`, QueryClient wrapper with `retry: false`). `QUERY_KEYS` is exported from the same client module, so the mock must preserve it.

## Proposed Architecture

### Component Overview
Test file -> renderHook(hook) -> mocked `getAuthenticatedApiClient()` returning `{ baseUrl, http: { fetch: jest.fn() } }`.

### Key Design Decisions

#### Decision 1: Mocking boundary
**Options considered:** mock global fetch; mock client module.
**Chosen approach:** `jest.mock('../../client', () => ({ getAuthenticatedApiClient: jest.fn(), QUERY_KEYS: { backgroundRefresh: ['background-refresh'] } }))`.
**Rationale:** the code calls `http.fetch` on the client object, not global fetch; matches sibling tests.

#### Decision 2: Fix scope
**Chosen approach:** only change `forceRefreshTask` to `await response.json().catch(() => ({}))`, matching `runHydrationTier`. No shared helper (surgical changes rule).

## Implementation Guidance

### Directory / Module Structure
- New: `frontend/src/api/hooks/__tests__/useBackgroundRefresh.test.tsx` (or `.ts` with React.createElement).
- Edit: `frontend/src/api/hooks/useBackgroundRefresh.ts` (one line).

### Interfaces and Contracts
Fake response: `{ ok, status, json: jest.fn() }`. Mutations tested with `result.current.mutateAsync` / `mutate` + `waitFor(isError)`; assert `error.message`. Invalidation asserted via spy on `queryClient.invalidateQueries` with `{queryKey: ['background-refresh']}`.

### Data Flow
hook -> fetch fn -> mocked http.fetch -> response -> data / thrown Error -> react-query state.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| refetchInterval timers keep running after test | Low | Fresh QueryClient per test, unmount via RTL cleanup |
| Mock omits QUERY_KEYS -> undefined spread | Medium | Include QUERY_KEYS in mock factory |

## Specification Amendments
None.

## Prerequisites
None. Validation: `cd frontend && npx jest useBackgroundRefresh`, `npm run lint`, `npm run build`.
