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
