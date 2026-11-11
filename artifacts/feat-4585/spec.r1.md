# Specification: Unit tests for StockOperationsPage stuck/retry logic and URL init

## Summary
`frontend/src/pages/StockOperationsPage.tsx` has 0% line coverage. Add tests that pin the "stuck" thresholds, the retry-eligibility contract, and URL query-parameter initialization. Production behavior must not change.

## Background
Operators use this page to retry stock-up operations; a wrong stuck threshold or retry gate can cause duplicate Shoptet orders. `isOperationStuck` (Submitted > 5 min since `submittedAt`; Pending > 10 min since `createdAt`) and `canRetry` (Failed/Submitted/Pending true; Completed/undefined false) are closures inside the component, so they cannot be imported by a test today. The mount effect reads 9 URL params (state, sourceType, sourceId, productCode, documentNumber, createdFrom, createdTo, sortBy, sortDescending, page) and applies `parseInt` to `sourceId` and `page` without validation.

## Functional Requirements

### FR-1: Testable helpers
Extract `isOperationStuck` and `canRetry` verbatim into a pure module `frontend/src/pages/stockOperationsUtils.ts`; the page imports them. `isOperationStuck` gains an optional `now: Date = new Date()` parameter for determinism.
**Acceptance criteria:**
- Page behavior unchanged; `npm run build` and lint pass.

### FR-2: isOperationStuck tests
**Acceptance criteria:**
- Submitted with submittedAt 5 min ago -> false; 6 min ago -> true (strict `>`, using date-fns `differenceInMinutes` which floors).
- Submitted without `submittedAt` -> false.
- Pending created 10 min ago -> false; 11 min -> true.
- Pending ignores `submittedAt`; Submitted ignores `createdAt`.
- Completed and Failed -> false regardless of age.

### FR-3: canRetry tests
**Acceptance criteria:**
- true for Failed, Submitted, Pending; false for Completed and undefined.

### FR-4: URL initialization tests
Render the page in a `MemoryRouter` with mocked hooks/child components and assert what `useStockUpOperationsQuery` receives.
**Acceptance criteria:**
- All params valid -> query args reflect state, sourceType, sourceId (number), productCode, documentNumber, dates (Date), sortBy, sortDescending (boolean), page (number).
- `sourceType=All` is ignored.
- No params -> defaults (state "Active", page 1, sortBy createdAt, descending true).
- Invalid `page` / `sourceId` (e.g. "abc"): test pins the current behavior (NaN is passed) and is commented as characterization of a known gap; the bug is not fixed here.

## Non-Functional Requirements
### NFR-1: Performance
Tests run in well under a few seconds; use fixed `now` argument for helpers, no real timers.
### NFR-2: Security
None.

## Data Model
`StockUpOperationState` enum (Pending, Submitted, Completed, Failed) from the generated API client.

## API / Interface Design
`isOperationStuck(operation: {state?, submittedAt?, createdAt?}, now?: Date): boolean`; `canRetry(state?: StockUpOperationState): boolean`.

## Dependencies
Jest + React Testing Library (react-scripts), date-fns, react-router-dom.

## Out of Scope
Fixing NaN parsing; testing sorting/filters UI, mutations, getStuckMessage rendering; E2E.

## Open Questions
None.

## Status: COMPLETE
