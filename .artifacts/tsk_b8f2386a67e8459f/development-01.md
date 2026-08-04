# Development — useManufactureOrders: typed client instead of `(apiClient as any).http.fetch`

Implemented exactly as specified in design-02/plan-02, with no deviations.

> **Correction (post-review):** after this step was written, an unrelated PR (#3847) merged into
> `main` and changed `manufactureOrder_GetProtocolPdf`'s generated return type from
> `Promise<FileResponse>` (`{ data: Blob }`) to `Promise<GetManufactureProtocolResponse>`
> (`{ pdfBytes?: string; fileName?: string }`, base64). The code snippet and test description
> below reflect the pre-#3847 implementation and are now stale; the actual shipped code decodes
> `response.pdfBytes` via `atob`/`Uint8Array` into a `Blob`. See `useManufactureOrders.ts` and
> `useOpenManufactureProtocol.test.ts` for the current implementation.

## Files changed

### `frontend/src/api/hooks/useManufactureOrders.ts`

1. **`useManufactureOrdersQuery`** (list query) — replaced the manual `URLSearchParams` +
   `(apiClient as any).baseUrl` + `(apiClient as any).http.fetch(...)` + `response.json()` block
   with a single typed call:
   ```ts
   queryFn: async (): Promise<GetManufactureOrdersResponse> => {
     const apiClient = getManufactureOrdersClient();
     return await apiClient.manufactureOrder_GetOrders(
       request.state ?? undefined,
       request.dateFrom ?? undefined,
       request.dateTo ?? undefined,
       request.responsiblePerson ?? undefined,
       request.orderNumber ?? undefined,
       request.productCode ?? undefined,
       request.erpDocumentNumber ?? undefined,
       request.manualActionRequired ?? undefined,
       request.lotNumber ?? undefined,
       request.pageNumber ?? undefined,
       request.pageSize ?? undefined,
     );
   },
   ```
   Reuses the file's existing `getManufactureOrdersClient()` helper (already used by every other
   hook in the file) and the generated `manufactureOrder_GetOrders` method
   (`api-client.ts:6917`). `null` is normalized to `undefined` on every field (`pageNumber`/
   `pageSize` only accept `number | undefined`, not `| null`, per the generated signature).
   Query key and `staleTime` unchanged.

2. **`useOpenManufactureProtocol`** (PDF-open hook) — replaced `(apiClient as any).baseUrl` +
   `(apiClient as any).http.fetch(...)` + `response.blob()` with:
   ```ts
   const apiClient = getManufactureOrdersClient();
   const fileResponse = await apiClient.manufactureOrder_GetProtocolPdf(orderId);
   const blobUrl = URL.createObjectURL(fileResponse.data);
   ```
   `manufactureOrder_GetProtocolPdf` (`api-client.ts:7336`) throws a `SwaggerException` (which
   `extends Error`) on non-2xx/204 responses, so the existing `try/catch` already handles errors
   correctly — the manual `response.ok` check is no longer reachable and was removed.

3. Added `GetManufactureOrdersResponse` to the existing named import from
   `../generated/api-client` (needed as a type annotation now that it's used directly, not just
   re-exported).

No other hooks in the file were touched — the other nine already used
`getManufactureOrdersClient()` correctly.

### `frontend/src/api/hooks/__tests__/useOpenManufactureProtocol.test.ts`

Rewritten to mock `manufactureOrder_GetProtocolPdf` on the object returned by
`getAuthenticatedApiClient()`, instead of `{ baseUrl, http: { fetch } }`. Same 6 cases, same
intent:
- `'calls with the correct order id'` (renamed from `'calls the correct URL'`) — asserts
  `mockGetProtocolPdf` called with `42`, not a URL string.
- `'opens the blob URL in a new tab'` — unchanged assertions, resolves `{ data, status: 200 }`.
- `'schedules URL revocation after 10 seconds'` — unchanged.
- `'sets isLoading to true during fetch and false after'` — controlled promise now resolves a
  `{ data, status }` object instead of a controlled `blob()` promise.
- `'sets error when HTTP response is not ok'` — rejects with
  `new Error('An unexpected server error occurred.')` (the `SwaggerException` message) instead of
  mocking a `{ ok: false, status: 404 }` response; asserts the new message.
- `'sets error when fetch throws'` — unchanged.

No test file exists for `useManufactureOrdersQuery`; its contract is exercised transitively via
`ManufactureOrderList.tsx`, which is unaffected (same `GetManufactureOrdersResponse` shape).

## Verification performed

- `grep -n "(apiClient as any)" frontend/src/api/hooks/useManufactureOrders.ts` → zero matches
  (was 2 before).
- `cd frontend && npm run build` → compiles successfully, no type errors.
- `cd frontend && npm run lint` → no errors/warnings in the two changed files. (The full lint run
  reports ~175 pre-existing errors across many unrelated test files — confirmed pre-existing by
  inspection, not introduced by this change.)
- `npx react-scripts test --testPathPattern="useOpenManufactureProtocol" --watchAll=false` →
  6/6 pass.
- `npx react-scripts test --testPathPattern="ManufactureOrder" --watchAll=false` → 25/26 pass;
  the 1 failure (`ManufactureOrderDetail.autoCalculation.test.tsx:243`, a `waitFor` timing issue)
  reproduces identically on the pre-change baseline (`git stash` + rerun), confirming it's
  pre-existing and unrelated — that test file fully mocks `useManufactureOrders`, so it can't be
  affected by this change.

## Scope

Only `useManufactureOrdersQuery` and `useOpenManufactureProtocol` in
`useManufactureOrders.ts`, plus their coupled test file, were touched — matching plan-02/
design-02's scope boundary. `useManufacturingStockAnalysis.ts` (#3730) and
`useSemiproductRecipePdf.ts` (identical anti-pattern, different file) are explicitly out of scope
per the plan and were left untouched.

## How to verify

```
cd frontend
npm run build
npm run lint
npx react-scripts test --testPathPattern="useOpenManufactureProtocol" --watchAll=false
grep -n "(apiClient as any)" src/api/hooks/useManufactureOrders.ts   # expect no output
```
