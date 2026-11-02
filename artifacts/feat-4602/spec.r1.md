# Specification: Unit tests for useUpdateManufactureOrderStatusMutation optimistic update and rollback

## Summary
Add a Jest/React Testing Library test file for `frontend/src/api/hooks/useManufactureOrders.ts` covering `useUpdateManufactureOrderStatusMutation` (optimistic `onMutate`, `onError` rollback, empty-cache guard). No production code changes are required.

## Background
Issue #4602 (coverage-gap, 31% line coverage). The hook caches order detail under `manufactureOrderKeys.detail(id)`. `onMutate` cancels in-flight queries, snapshots `previousOrder`, and optimistically sets `order.state = newState` (guard `if (!old) return old;`). `onError` restores the snapshot if present. `onSettled` invalidates list, detail and calendar queries. No test file exists for this hook. Note: the issue text mentions `previousOrders`; the actual code uses `previousOrder` on the detail key.

## Functional Requirements

### FR-1: Successful mutation updates cache optimistically
**Acceptance criteria:**
- With a seeded detail cache `{order:{id, state:A}}`, calling mutate with `newState: B` causes the cache to show state B before the API promise resolves.
- API client `manufactureOrder_UpdateOrderStatus(id, request)` is called with correct args.

### FR-2: Failed mutation rolls back
**Acceptance criteria:**
- When the API rejects, the detail cache returns to the original object (state A).
- `mutation.isError` becomes true.

### FR-3: Empty cache guard
**Acceptance criteria:**
- With no cached detail, mutate does not throw; `getQueryData` for the detail key remains undefined after onMutate; on API failure no rollback write occurs (cache stays undefined).

### FR-4: onSettled invalidation
**Acceptance criteria:**
- After success and after failure, `invalidateQueries` is called for the `all` key, the detail key, and a calendar predicate.

## Non-Functional Requirements
### NFR-1: Performance
Tests are fast, no real timers or network.
### NFR-2: Security
No secrets; API client fully mocked.

## Data Model
Cache shape: `{ order: { state: ManufactureOrderState, ... } }` under `["manufacture-orders","detail",id]`.

## API / Interface Design
New file `frontend/src/api/hooks/__tests__/useManufactureOrders.test.tsx`, mocking `../../client` (`getAuthenticatedApiClient`) like `useCatalog.test.ts`. `QUERY_KEYS` must remain available (use `jest.requireActual` partial mock or mock only `getAuthenticatedApiClient`).

## Dependencies
@tanstack/react-query, @testing-library/react (existing).

## Out of Scope
Other hooks in the file; production code changes; E2E.

## Open Questions
None.

## Status: COMPLETE
