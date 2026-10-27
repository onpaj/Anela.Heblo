# Architecture Review: StockOperationsPage test coverage

## Skip Design: true

## Architectural Fit Assessment
Frontend-only, test-focused. Precedent: `src/pages/orgChartUtils.ts` with `__tests__/orgChartUtils.test.ts` (pure helper next to page, test in `src/pages/__tests__/`). Page tests exist for other pages (e.g. `OvertimePage.test.tsx`) and mock hooks.

## Proposed Architecture

### Component Overview
StockOperationsPage.tsx -> imports { isOperationStuck, canRetry } from stockOperationsUtils.ts. Tests: `__tests__/stockOperationsUtils.test.ts` (pure) and `__tests__/StockOperationsPage.test.tsx` (render with mocks).

### Key Design Decisions

#### Decision 1: Extract helpers rather than render-only testing
**Options considered:** render page and probe DOM; export from component file; extract to utils module.
**Chosen approach:** extract to `stockOperationsUtils.ts`, moving code verbatim.
**Rationale:** brief asks for pure-function tests; matches orgChartUtils precedent; minimal diff. Add only the optional `now` parameter.

#### Decision 2: URL init via page render with mocked hooks
**Chosen approach:** `jest.mock` `../../api/hooks/useStockUpOperations`, `CatalogAutocomplete`, and `useScreenView`; wrap in `MemoryRouter initialEntries`; assert `useStockUpOperationsQuery` last call args.
**Rationale:** the logic lives in a mount effect; extracting it would be a larger refactor than the task warrants.

## Implementation Guidance

### Directory / Module Structure
- New: `frontend/src/pages/stockOperationsUtils.ts`
- New: `frontend/src/pages/__tests__/stockOperationsUtils.test.ts`
- New: `frontend/src/pages/__tests__/StockOperationsPage.test.tsx`
- Edit: `frontend/src/pages/StockOperationsPage.tsx` (remove two closures, add import)

### Interfaces and Contracts
As in spec. Keep the loosely typed `operation` parameter shape structural (`{ state?: StockUpOperationState; submittedAt?: Date | string; createdAt?: Date | string }`) to avoid `any` regressions.

### Data Flow
URL params -> mount effect -> state setters -> `useStockUpOperationsQuery` args (the assertion point).

## Risks and Mitigations
| Risk | Severity | Mitigation |
|------|----------|------------|
| Page imports many things (lucide, date-fns) making render fragile | Low | Mock hooks and autocomplete; mock mutations returning `{mutate: jest.fn(), isPending:false}` |
| Mount effect runs after first render so first query call has defaults | Low | Assert on the last call after effects flush (`waitFor`) |
| Tests accidentally change behavior | Low | Verbatim move; NaN behavior pinned not fixed |

## Specification Amendments
None.

## Prerequisites
None. Read `docs/testing/testing-strategy` equivalent: `docs/architecture/testing-strategy.md` for FE conventions. No process-doc impact (no sync/calculation changed).
