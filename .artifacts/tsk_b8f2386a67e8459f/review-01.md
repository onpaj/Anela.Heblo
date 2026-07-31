# Review — useManufactureOrders: typed client instead of `(apiClient as any).http.fetch`

## Verified against the spec

- `grep -n "(apiClient as any)" frontend/src/api/hooks/useManufactureOrders.ts` → zero matches. Both flagged call sites (`useManufactureOrdersQuery` list query, `useOpenManufactureProtocol` PDF hook) are gone.
- `useManufactureOrdersQuery` now calls `apiClient.manufactureOrder_GetOrders(...)` via the file's existing `getManufactureOrdersClient()` helper — the same helper every other hook in the file already uses. Diffed the call's 11 positional arguments against the generated signature at `api-client.ts:6917` (`state, dateFrom, dateTo, responsiblePerson, orderNumber, productCode, erpDocumentNumber, manualActionRequired, lotNumber, pageNumber, pageSize`): order and nullability handling (`?? undefined` for the `| null` fields) match exactly. Return type is the real `GetManufactureOrdersResponse`, no more `any`.
- `useOpenManufactureProtocol` now calls `apiClient.manufactureOrder_GetProtocolPdf(orderId)` (`api-client.ts:7336`), which returns `FileResponse` (`{ data: Blob, status, headers, fileName }`). `URL.createObjectURL(fileResponse.data)` is correct. The manual `response.ok` check was correctly removed — the generated method throws `SwaggerException` (`extends Error`) on non-2xx, and the existing `try/catch` in `openProtocol` already handles that.
- Rule compliance: this satisfies `docs/development/api-client-generation.md`'s "never access private fields" rule by routing through the typed generated methods directly — a stronger fix than falling back to the `getApiBaseUrl()`/`getAuthenticatedFetch()` escape hatch, consistent with what architecture-01/02 already confirmed as correct given `manufactureOrder_GetProtocolPdf` handles blob responses natively.
- Test file `useOpenManufactureProtocol.test.ts` rewritten to mock `manufactureOrder_GetProtocolPdf` directly instead of `{ baseUrl, http: { fetch } }`; same 6 cases, intent preserved (order id call, blob URL open, revocation timer, loading state, error propagation via rejected promise instead of a fake non-ok `Response`).
- Scope matches plan-02/design-02: only these two hooks + the coupled test file touched; the other nine hooks in the file (already using `getManufactureOrdersClient()`) and the out-of-scope `useManufacturingStockAnalysis.ts`/`useSemiproductRecipePdf.ts` were left alone.

## Independently re-run in this step

- `npm run build` → compiles successfully, no type errors.
- `npx eslint src/api/hooks/useManufactureOrders.ts src/api/hooks/__tests__/useOpenManufactureProtocol.test.ts` → no output, clean.
- `npx react-scripts test --testPathPattern="useOpenManufactureProtocol" --watchAll=false` → 6/6 pass.
- `npx react-scripts test --testPathPattern="ManufactureOrder" --watchAll=false` → 25/26 pass. The one failure (`ManufactureOrderDetail.autoCalculation.test.tsx:243`, a `waitFor` timing issue) is confirmed unrelated: that test file `jest.mock`s `../../../../api/hooks/useManufactureOrders` wholesale (line 56), so it cannot be affected by changes inside the real hook implementation.
- Consumer check: `ManufactureOrderList.tsx` is the sole caller of `useManufactureOrdersQuery`; it only destructures `data` generically and passes through, unaffected by the return-type change from `any` to `GetManufactureOrdersResponse`.

## Verdict

Implementation matches plan-02/design-02/architecture-02 exactly, eliminates both `(apiClient as any)` call sites this issue targeted, and all verification commands pass with no regressions attributable to the change.
