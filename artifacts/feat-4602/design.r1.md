# Design: useUpdateManufactureOrderStatusMutation tests

Skip Design is true: no UI is involved, so UX/UI sections are omitted.

## Component Design
Test file `frontend/src/api/hooks/__tests__/useManufactureOrders.test.tsx`:
- Module mock of `../../client` keeping real `QUERY_KEYS`, with `getAuthenticatedApiClient` as `jest.fn()` returning `{ manufactureOrder_UpdateOrderStatus: mockUpdateStatus }`.
- Helper `createWrapper(queryClient)`.
- `describe("useUpdateManufactureOrderStatusMutation")` with tests:
  1. optimistic update applied while request pending, then success calls API with `(id, request)`.
  2. rejection restores previous cache and sets `isError`.
  3. empty cache: no throw, cache stays undefined, error path writes nothing.
  4. onSettled invalidates all/detail keys and calendar predicate (success and failure).
- `beforeEach`: new QueryClient (`retry:false`, `gcTime: Infinity`); `jest.clearAllMocks()`.

## Data Schemas
Detail cache entry: `{ order: { id: number; state: ManufactureOrderState } }`, key `["manufacture-orders","detail",id]`.
Mutation variables: `{ id: number; newState: ManufactureOrderState }`.
