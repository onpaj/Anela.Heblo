# Architecture Review: useUpdateManufactureOrderStatusMutation tests

## Skip Design: true
Test-only change; no UI.

## Architectural Fit Assessment
Fits the existing convention in `frontend/src/api/hooks/__tests__/` (e.g. `useDeletePackageMutation.test.ts`, `useSetProductPrice.test.ts`): `jest.mock("../../client", () => ({ ...jest.requireActual("../../client"), getAuthenticatedApiClient: jest.fn() }))`, a fresh `QueryClient` per test (`retry:false`), and `renderHook` with a `QueryClientProvider` wrapper. `QUERY_KEYS.manufactureOrders` = `["manufacture-orders"]` (client.ts:494), so the real keys are retained via requireActual.

## Proposed Architecture

### Component Overview
Single test file `frontend/src/api/hooks/__tests__/useManufactureOrders.test.tsx` -> hook -> mocked `getAuthenticatedApiClient()` returning `{ manufactureOrder_UpdateOrderStatus: jest.fn() }` -> real QueryClient.

### Key Design Decisions

#### Decision 1: Observe optimistic state via a deferred promise
**Chosen approach:** mock API returns a manually controlled promise; assert cache is updated while pending (waitFor on `getQueryData`), then resolve/reject. Rationale: deterministic proof of optimistic behavior and of rollback.

#### Decision 2: Seed cache with setQueryData; spy invalidateQueries
Detail key `["manufacture-orders","detail",id]`. Spy `queryClient.invalidateQueries` for onSettled assertions (all key, detail key, one call with a `predicate` function; test predicate against `["manufacture-orders","calendar",...]` true and `["manufacture-orders","list"]` false).

## Implementation Guidance
### Directory / Module Structure
Only the new test file. No production changes.
### Interfaces and Contracts
Request: `{ id: number, newState: ManufactureOrderState }` (import enum from generated api-client; verify enum member names there). Cache: `{ order: { id, state } }`.
### Data Flow
mutate -> onMutate (cancel, snapshot, set) -> API -> success/error -> onError rollback -> onSettled invalidate.

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| invalidateQueries refetch races with seeded data (no observers so no refetch) | Low | No active query observers; assert via spy |
| Issue says `previousOrders`; code uses `previousOrder` | Low | Test against actual code |
| Rollback guard `context?.previousOrder` skips when cache was empty | Low | Covered by FR-3 |

## Specification Amendments
None.

## Prerequisites
None.
