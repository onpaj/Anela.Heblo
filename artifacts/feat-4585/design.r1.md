# Design: StockOperationsPage test coverage

## Component Design
- `stockOperationsUtils.ts`: exports `isOperationStuck(operation, now = new Date())` using `differenceInMinutes` from date-fns with thresholds Submitted 5 / Pending 10 (strict greater-than), and `canRetry(state?)`. No React imports.
- `StockOperationsPage.tsx`: deletes the two inline helpers and imports them; all call sites (`isOperationStuck(operation)`, `canRetry(operation.state)`) unchanged.
- Page test harness: mocks for `useStockUpOperationsQuery` (returns `{data:{items:[],totalCount:0,...}, isLoading:false, error:null, refetch: jest.fn()}`), both mutation hooks, `CatalogAutocomplete`, `useScreenView`; renders inside `MemoryRouter`.

## Data Schemas
Test operation fixture: `{ id: 1, state: StockUpOperationState.X, createdAt: Date, submittedAt?: Date }` with a fixed `now = new Date("2026-01-01T12:00:00Z")` and offsets computed in minutes.
