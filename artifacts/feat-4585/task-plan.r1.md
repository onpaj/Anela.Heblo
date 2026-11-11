# StockOperationsPage Test Coverage Implementation Plan

**Goal:** Pin stuck thresholds, retry gate, and URL-param initialization of `StockOperationsPage` with tests, without changing behavior.
**Architecture:** Extract two pure helpers to `stockOperationsUtils.ts` (precedent: `orgChartUtils.ts`); test them directly; characterize URL init through a mocked-hook page render.
**Tech Stack:** React 18, TypeScript, Jest + React Testing Library (react-scripts), date-fns.

### task: extract-helpers

**Files:**
- Create: `frontend/src/pages/stockOperationsUtils.ts`
- Modify: `frontend/src/pages/StockOperationsPage.tsx` (lines ~147-157 `isOperationStuck`, ~203-210 `canRetry`)

- [ ] Create `stockOperationsUtils.ts` exporting `isOperationStuck(operation: { state?: StockUpOperationState; submittedAt?: Date | string; createdAt?: Date | string }, now: Date = new Date()): boolean` (body moved verbatim: Submitted && submittedAt -> `differenceInMinutes(now, new Date(submittedAt)) > 5`; Pending -> `differenceInMinutes(now, new Date(createdAt)) > 10`; else false) and `canRetry(state?: StockUpOperationState): boolean` (verbatim).
- [ ] In the page, delete both inline closures, add `import { isOperationStuck, canRetry } from "./stockOperationsUtils";`. Do not touch `getStuckMessage` or other code.
- [ ] Verify: `cd frontend && npm run build && npm run lint` pass.
- [ ] Commit: `refactor(frontend): extract stock operation stuck/retry helpers`

### task: test-helpers

**Files:**
- Create: `frontend/src/pages/__tests__/stockOperationsUtils.test.ts`

- [ ] Write tests with fixed `now = new Date("2026-01-01T12:00:00Z")` and a helper `minutesAgo(n)`:
  - Submitted: 5 min ago -> false, 6 -> true; no submittedAt -> false; createdAt old but submittedAt recent -> false.
  - Pending: 10 -> false, 11 -> true; old submittedAt ignored.
  - Completed and Failed, 1000 min old -> false.
  - `canRetry`: Failed/Submitted/Pending true; Completed false; undefined false.
- [ ] Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/pages/__tests__/stockOperationsUtils.test.ts` -> all pass.
- [ ] Sanity (mutation check, do not commit): change threshold 5 to 6 temporarily and confirm a test fails, then revert.
- [ ] Commit: `test(frontend): cover stock operation stuck and retry helpers`

### task: test-url-init

**Files:**
- Create: `frontend/src/pages/__tests__/StockOperationsPage.test.tsx`

- [ ] Mock `../../api/hooks/useStockUpOperations` (`useStockUpOperationsQuery` jest.fn returning `{data:{items:[],totalCount:0,totalPages:0,pageNumber:1}, isLoading:false, error:null, refetch:jest.fn()}`; both mutation hooks returning `{mutate:jest.fn(), mutateAsync:jest.fn(), isPending:false}`), `../../components/common/CatalogAutocomplete` (stub component), `../../telemetry/useScreenView` (no-op). Check the page's actual usage of `data` fields before finalizing the mock shape.
- [ ] Helper `renderAt(search: string)` wrapping the page in `MemoryRouter initialEntries={["/stock-operations" + search]}`; helper returns last call args of `useStockUpOperationsQuery`.
- [ ] Tests (use `waitFor` so the mount effect has flushed):
  - no params -> `state:"Active"`, `page:1`, `sortBy:"createdAt"`, `sortDescending:true`, `pageSize:50`.
  - full params (`state=Failed&sourceType=TransportBox&sourceId=42&productCode=ABC&documentNumber=D1&createdFrom=2026-01-01&createdTo=2026-01-31&sortBy=state&sortDescending=false&page=3`) -> matching args, `sourceId` number 42, dates are `Date`, `sortDescending` false, `page` 3.
  - `sourceType=All` -> `sourceType` undefined.
  - `sortDescending=true` -> true.
  - invalid `page=abc` and `sourceId=abc` -> characterization test asserting `Number.isNaN(...)`, with a comment that this pins current behavior (known gap, not fixed here).
- [ ] Run: `cd frontend && CI=true npx react-scripts test --watchAll=false src/pages/__tests__`; then `npm run build && npm run lint`.
- [ ] Commit: `test(frontend): cover StockOperationsPage URL param initialization`
