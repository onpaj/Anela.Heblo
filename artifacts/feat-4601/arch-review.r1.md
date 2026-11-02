# Architecture Review: Test coverage for useSubmitStockTaking

## Skip Design: true
Test-only change, no UI.

## Architectural Fit Assessment
Fits the existing pattern of hook tests under `frontend/src/api/hooks/__tests__/`. Sibling `useManufactureStockTaking.test.tsx` already mocks `ToastContext`; follow it.

## Proposed Architecture

### Component Overview
Extend `useStockTaking.test.tsx` with a new `describe('useSubmitStockTaking')` block. Real `QueryClient` per test (retry false, mutations retry false), seeded with `setQueryData`, wrapped in `QueryClientProvider`. Mocked: API client, ToastContext.

### Key Design Decisions
#### Decision 1: Mock scope
**Options considered:** mock `client` wholesale (existing file does `jest.mock('../../client')`, which auto-mocks `QUERY_KEYS` to... objects retained, but verify) vs partial mock.
**Chosen approach:** `jest.mock('../../client', () => ({ ...jest.requireActual('../../client'), getAuthenticatedApiClient: jest.fn() }))` only if automock breaks `QUERY_KEYS`; otherwise keep the existing automock. Verify by running.
**Rationale:** the hook builds keys from `QUERY_KEYS.catalog`; keys must be real arrays.

#### Decision 2: Observing the guard
**Chosen approach:** spy on `queryClient.setQueryData` and `setQueriesData` (call counts) plus assert seeded data. Prevent invalidation refetch from clobbering assertions by seeding with no active observers (invalidate only marks stale), so cache values are readable after `waitFor(isSuccess)`.
**Rationale:** spies detect inversion of the guard deterministically; data assertions verify behavior.

## Implementation Guidance
### Directory / Module Structure
Only `frontend/src/api/hooks/__tests__/useStockTaking.test.tsx` changes. The wrapper factory must expose the `QueryClient` (refactor `createWrapper` to accept/return a client, minimally).

### Interfaces and Contracts
Mock `stockTaking_SubmitStockTaking` on the api client; `useToast` returns `{ showError: jest.fn() }`.

### Data Flow
mutate -> `submitStockTaking` -> api mock -> onSuccess (guard) -> setQueryData/setQueriesData -> invalidateQueries; on reject -> onError -> showError.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Existing `jest.mock('../../client')` automock affects QUERY_KEYS | Medium | Verify; switch to requireActual partial mock |
| Existing tests rely on current `createWrapper` | Low | Keep its signature compatible |

## Specification Amendments
None.

## Prerequisites
None. Run: `cd frontend && npx jest src/api/hooks/__tests__/useStockTaking.test.tsx`.
