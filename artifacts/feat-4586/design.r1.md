# Design: PurchaseOrderForm test coverage

## Component Design
Single test file `frontend/src/components/pages/__tests__/PurchaseOrderForm.test.tsx`.

- Mocks: `../../../contexts/PurchasePlanningListContext` (`usePurchasePlanningList`), `../../../api/client` (`getAuthenticatedApiClient`), purchase-order hooks from `../../../api/hooks/usePurchaseOrders`, and stubs for `PurchaseOrderHeader`, `PurchaseOrderLines`, `MaterialResolver`.
- Helper `renderForm(items, opts)` renders `<PurchaseOrderForm isOpen onClose onSuccess />` inside a QueryClientProvider if required.
- `describe("supplier name matching")` with `it.each` over [selectedSupplier, planningSupplier, shouldMatch] rows covering s.r.o., "s. r. o.", a.s., spol. s.r.o., case, unrelated, substring.
- `describe("planning list filtering")`: no supplier, toggle show-all, supplier selected.
- `describe("handleAddFromPlanningList")`: exact match, no match (fallback price 0), non-ok response, fetch rejection, first-line replace vs append, `removeItem` called with productCode.

## Data Schemas
Planning item: `{ productCode: string; productName: string; supplier: string }`.
API response: `{ materials: MaterialForPurchaseDto[] }` with `productCode`, `productName`, `lastPurchasePrice`.
fetch mock: `{ ok: boolean, status: number, json: () => Promise<...> }`.
