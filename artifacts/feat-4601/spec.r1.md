# Specification: Test coverage for useSubmitStockTaking

## Summary
Add unit tests for the `useSubmitStockTaking` mutation hook in `frontend/src/api/hooks/useStockTaking.ts` (currently 26.7% line coverage). Tests must lock in the `softStockTaking` cache-update guard and the error path. No production code changes.

## Background
The existing test file `frontend/src/api/hooks/__tests__/useStockTaking.test.tsx` covers only `useStockTakingHistory`. In `useSubmitStockTaking`, `onSuccess` optimistically writes quantities into the React Query cache only when `variables.softStockTaking` is falsy, then always invalidates catalog queries. `onError` shows a toast via `useToast().showError`. Regressions here are silent.

## Functional Requirements

### FR-1: Hard stock-taking updates the cache
With `softStockTaking: false` (and when omitted), after success the catalog detail cache (`[...QUERY_KEYS.catalog, "detail", productCode, 1]`) has `item.stock.available` and `item.stock.eshop` equal to `targetAmount`, and matching items in `[...QUERY_KEYS.catalog, "inventory"]` queries are updated while other products are untouched.
**Acceptance criteria:**
- Detail cache seeded with stock reflects new quantities after mutation.
- Inventory list item with matching productCode updated; non-matching unchanged.
- Cache entries lacking `item.stock` / `items` are returned unchanged.
- API called with a `SubmitStockTakingRequest` with `softStockTaking` defaulting to false.

### FR-2: Soft stock-taking does not write the cache
With `softStockTaking: true`, `setQueryData`/`setQueriesData` are not invoked and seeded cache values keep their original quantities (no optimistic write), while invalidation still occurs.
**Acceptance criteria:**
- Seeded detail and inventory data retain original quantities (observed via spies on `setQueryData`/`setQueriesData` or by preventing refetch).
- `invalidateQueries` is still called for the catalog keys.

### FR-3: Error path
When the API rejects, no cache write occurs, `showError` is called with title "Chyba při inventarizaci" and the error message (or the Czech fallback when message is empty).
**Acceptance criteria:**
- Cache unchanged; `setQueryData`/`setQueriesData` not called; `showError` called with `{ duration: 5000 }`.

## Non-Functional Requirements
### NFR-1: Performance
Tests run in well under a few seconds each; no real network.
### NFR-2: Security
None; no secrets or real API calls.

## Data Model
None. Cache shapes: detail `{ item: { stock: { available, eshop } } }`, inventory `{ items: [{ productCode, stock }] }`.

## API / Interface Design
Test-only. Mock `../../client` (`getAuthenticatedApiClient`, with `QUERY_KEYS` preserved via `jest.requireActual`) and `../../../contexts/ToastContext` (`useToast`), following `useManufactureStockTaking.test.tsx`.

## Dependencies
Jest, @testing-library/react `renderHook`, @tanstack/react-query.

## Out of Scope
Changes to `useStockTaking.ts`; `useStockTakingHistory` tests; E2E.

## Open Questions
None.

## Status: COMPLETE
