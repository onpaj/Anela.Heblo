# Design (final) — useManufactureOrders: typed client instead of `(apiClient as any).http.fetch`

This is the consolidated design after plan-02 folded in architecture-01's review of design-01.
Architecture's verdict on design-01 was **"Approved as designed, no changes required"** — every
signature, response shape, exception-handling claim, and test-coupling claim was independently
re-verified against the current source and held. I re-verified the same facts again directly
against `frontend/src/api/hooks/useManufactureOrders.ts`,
`frontend/src/api/generated/api-client.ts`, and
`frontend/src/api/hooks/__tests__/useOpenManufactureProtocol.test.ts` in this step; nothing has
drifted (git status is clean, no prior implementation work exists). This document carries
design-01's content forward unchanged in substance, reorganized as the implementation-ready
design for plan-02's FR-1/FR-2/FR-3.

No UI changes. This is an internal data-access refactor: two hooks change their implementation;
their public signatures and return shapes stay identical, so no downstream component
(`ManufactureOrderList.tsx`, protocol-open buttons) needs to change. UX/UI section omitted.

## Component design

### 1. `useManufactureOrdersQuery` (list query hook)

**File:** `frontend/src/api/hooks/useManufactureOrders.ts:59-93` (current).

**Boundary/responsibility — unchanged:** a `useQuery` hook taking a `GetManufactureOrdersRequest`
filter object, resolving to the order list page. Sole caller: `ManufactureOrderList.tsx` (verified
sole consumer by grep across the whole frontend).

**Current implementation (to be replaced):**
```ts
queryFn: async () => {
  const apiClient = getAuthenticatedApiClient();
  const relativeUrl = `/api/manufactureorder`;
  const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
  const params = new URLSearchParams();
  /* ...11 manual params.append calls... */
  const urlWithParams = params.toString() ? `${fullUrl}?${params.toString()}` : fullUrl;
  const response = await (apiClient as any).http.fetch(urlWithParams, {
    method: 'GET',
    headers: { 'Content-Type': 'application/json' },
  });
  if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
  return await response.json();
}
```

**New implementation:**
```ts
export const useManufactureOrdersQuery = (request: GetManufactureOrdersRequest = {}) => {
  return useQuery({
    queryKey: manufactureOrderKeys.list(request),
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
    staleTime: 1000 * 60 * 5,
  });
};
```

- Drops: `fullUrl`/`URLSearchParams` construction, `(apiClient as any).baseUrl`,
  `(apiClient as any).http.fetch`, the manual `response.ok` check, `response.json()`.
- Reuses `getManufactureOrdersClient()` — already defined at
  `useManufactureOrders.ts:51-54` (`getAuthenticatedApiClient()` cast to `GeneratedApiClient`) and
  already used by every other hook in the file (`useManufactureOrderDetailQuery`,
  `useCreateManufactureOrderMutation`, `useManufactureOrderCalendarQuery`, etc.). No new helper.
- `queryFn`'s return type becomes `Promise<GetManufactureOrdersResponse>` — a typed class instance
  replacing the previous implicit `any` from `response.json()`.
- Parameter order/nullability verified directly against the generated signature at
  `api-client.ts:6917`:
  `manufactureOrder_GetOrders(state: ManufactureOrderState | null | undefined, dateFrom: Date | null | undefined, dateTo: Date | null | undefined, responsiblePerson: string | null | undefined, orderNumber: string | null | undefined, productCode: string | null | undefined, erpDocumentNumber: string | null | undefined, manualActionRequired: boolean | null | undefined, lotNumber: string | null | undefined, pageNumber: number | undefined, pageSize: number | undefined): Promise<GetManufactureOrdersResponse>`.
  Note `pageNumber`/`pageSize` accept `number | undefined` only — **not** `| null` — unlike the
  other nine parameters, so `request.pageNumber ?? undefined` (not bare `request.pageNumber`) is
  required for those two fields to avoid passing `null` where the generated method's type (and,
  per plan-02, its runtime behavior) rejects it.
- Query key (`manufactureOrderKeys.list(filters)`) and `staleTime` (5 min) are unchanged — only
  `queryFn`'s body changes.

### 2. `useOpenManufactureProtocol` (imperative PDF-open hook)

**File:** `frontend/src/api/hooks/useManufactureOrders.ts:410-437` (current).

**Boundary/responsibility — unchanged:** returns `{ openProtocol(orderId), isLoading, error }`.
`openProtocol` fetches the protocol PDF, opens it in a new tab as a blob URL, revokes the URL
after 10s.

**Current implementation (to be replaced):**
```ts
const openProtocol = async (orderId: number) => {
  setIsLoading(true);
  setError(null);
  try {
    const apiClient = getAuthenticatedApiClient();
    const relativeUrl = `/api/manufactureorder/${orderId}/protocol.pdf`;
    const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
    const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });
    if (!response.ok) throw new Error(`HTTP error! status: ${response.status}`);
    const blob = await response.blob();
    const blobUrl = URL.createObjectURL(blob);
    window.open(blobUrl, '_blank', 'noopener,noreferrer');
    setTimeout(() => URL.revokeObjectURL(blobUrl), 10000);
  } catch (err) {
    const error = err instanceof Error ? err : new Error(String(err));
    console.error('Failed to open manufacture protocol PDF:', error);
    setError(error);
  } finally {
    setIsLoading(false);
  }
};
```

**New implementation:**
```ts
const openProtocol = async (orderId: number) => {
  setIsLoading(true);
  setError(null);
  try {
    const apiClient = getManufactureOrdersClient();
    const fileResponse = await apiClient.manufactureOrder_GetProtocolPdf(orderId);
    const blobUrl = URL.createObjectURL(fileResponse.data);
    window.open(blobUrl, '_blank', 'noopener,noreferrer');
    setTimeout(() => URL.revokeObjectURL(blobUrl), 10000);
  } catch (err) {
    const error = err instanceof Error ? err : new Error(String(err));
    console.error('Failed to open manufacture protocol PDF:', error);
    setError(error);
  } finally {
    setIsLoading(false);
  }
};
```

- Drops: `(apiClient as any).baseUrl`, `(apiClient as any).http.fetch`, the manual `response.ok`
  check, `response.blob()`.
- Verified `manufactureOrder_GetProtocolPdf(id: number): Promise<FileResponse>`
  (`api-client.ts:7343`) and its internal `processManufactureOrder_GetProtocolPdf` (verified in
  this step, `api-client.ts:7354-7370`): status `200` or `206` → resolves
  `{ fileName, data: blob, status, headers }`; any other status except `204` → rejects via
  `throwException("An unexpected server error occurred.", status, ...)`; `204` → resolves `null`.
  `FileResponse.data` is a direct drop-in replacement for the current `response.blob()`.
- `throwException` constructs and throws a `SwaggerException` (`extends Error`,
  `api-client.ts:43178`, verified in architecture-01), so the existing
  `catch (err) { err instanceof Error ? err : new Error(String(err)) }` pattern already handles it
  correctly — no catch-block restructuring beyond dropping the now-unreachable `response.ok`
  branch (there is no raw `Response` in scope anymore, only a resolved `FileResponse` or a
  rejected promise).
- `error.message` on a thrown NSwag exception is no longer the hand-written
  `"HTTP error! status: ${status}"` string — it becomes `"An unexpected server error occurred."`
  for a non-2xx/204 HTTP response, or the transport error's own message for a network failure.
  This is a visible, intentional test-assertion change (below); observable behavior (isLoading/
  error state transitions, no `window.open` call on error) is unchanged. Not currently rendered
  anywhere with the specific status code (checked by architecture-01), so no user-facing string
  changes beyond the hook's internal `error.message`.

### Escape-hatch question — resolved, no ambiguity

The task named `getApiBaseUrl()` + `getAuthenticatedFetch()` as a fallback "if the typed method
cannot be used directly for blob handling." `docs/development/api-client-generation.md` scopes
that escape hatch to cases where "an endpoint's business outcome cannot yet be expressed through
the generated client" (e.g. status-code branching the controller doesn't yet support).
`GetProtocolPdf` is a plain 200/206-success-or-throw download and the generated method already
returns the blob via `FileResponse.data` — there is no such gap. **Decision, carried forward
unchanged: call `manufactureOrder_GetProtocolPdf` directly**, the doc's primary "✅ CORRECT"
pattern, not the escape hatch.

## Data schemas

No wire-format or DTO changes — same two endpoints (`GET /api/manufactureorder`,
`GET /api/manufactureorder/{id}/protocol.pdf`), same request/response bodies. What changes is
which TypeScript layer constructs the request and parses the response.

**`GetManufactureOrdersRequest`** (local interface, unchanged, `useManufactureOrders.ts:24-36`):
```ts
export interface GetManufactureOrdersRequest {
  state?: ManufactureOrderState | null;
  dateFrom?: Date | null;
  dateTo?: Date | null;
  responsiblePerson?: string | null;
  orderNumber?: string | null;
  productCode?: string | null;
  erpDocumentNumber?: string | null;
  manualActionRequired?: boolean | null;
  lotNumber?: string | null;
  pageNumber?: number | null;
  pageSize?: number | null;
}
```
Maps 1:1 onto `manufactureOrder_GetOrders`'s 11 positional parameters, in the same order.

**`GetManufactureOrdersResponse`** (generated class, `api-client.ts:28155`):
```ts
class GetManufactureOrdersResponse extends BaseResponse {
  orders?: ManufactureOrderDto[];
  totalCount?: number;
  pageNumber?: number;
  pageSize?: number;
  totalPages?: number;
}
```
Exactly what `ManufactureOrderList.tsx:90-92` destructures (`data?.orders`, `data?.totalCount`,
`data?.totalPages`) — no consumer-side schema change.

**`FileResponse`** (generated interface, `api-client.ts:43171`):
```ts
interface FileResponse {
  data: Blob;
  status: number;
  fileName?: string;
  headers?: { [name: string]: any };
}
```
Replaces the raw `Response` object as the value `openProtocol` works with; only `.data` is used.

## Test design

`frontend/src/api/hooks/__tests__/useOpenManufactureProtocol.test.ts` (read in full this step,
6 `test()` cases, current content confirmed unchanged from plan-02/architecture-01's description)
mocks `getAuthenticatedApiClient()` → `{ baseUrl: 'http://localhost:5001', http: { fetch:
mockFetch } }` and asserts on `mockFetch`'s call args and a `{ ok, blob }`-shaped resolved value.
Since `openProtocol` no longer touches `.http.fetch` or `.baseUrl`, this file is rewritten to mock
the generated client's `manufactureOrder_GetProtocolPdf` method directly, at the same
`getAuthenticatedApiClient()` mock boundary (consistent with how `getManufactureOrdersClient()` is
implemented — a thin cast, so no new mock scaffolding is needed):

```ts
jest.mock('../../client');
const mockGetAuthenticatedApiClient = getAuthenticatedApiClient as jest.MockedFunction<
  typeof getAuthenticatedApiClient
>;

let mockGetProtocolPdf: jest.Mock;

beforeEach(() => {
  mockGetProtocolPdf = jest.fn();
  mockGetAuthenticatedApiClient.mockReturnValue({
    manufactureOrder_GetProtocolPdf: mockGetProtocolPdf,
  } as any);

  URL.createObjectURL = jest.fn().mockReturnValue('blob:mock-url');
  URL.revokeObjectURL = jest.fn();
  window.open = jest.fn();
  jest.useFakeTimers();
});
```

Per-test-case mapping (same 6 cases, same intent, new mock surface):

| Current test | Change |
|---|---|
| `'calls the correct URL'` | Rename intent to "calls with the correct order id"; `mockGetProtocolPdf.mockResolvedValueOnce({ data: new Blob(['pdf']), status: 200 })`, then `expect(mockGetProtocolPdf).toHaveBeenCalledWith(42)` — no URL string; the generated method takes `id: number`. |
| `'opens the blob URL in a new tab'` | `mockGetProtocolPdf.mockResolvedValueOnce({ data: mockBlob, status: 200 })`; assert `URL.createObjectURL`/`window.open` as before. |
| `'schedules URL revocation after 10 seconds'` | Same fake-timer pattern; resolve `mockGetProtocolPdf` with `{ data: blob, status: 200 }`. |
| `'sets isLoading to true during fetch and false after'` | Resolve `mockGetProtocolPdf` via a controlled promise (`{ data: blob, status: 200 }`) instead of a controlled `blob()` promise. |
| `'sets error when HTTP response is not ok'` | Since the typed method throws instead of resolving a non-ok `Response`: `mockGetProtocolPdf.mockRejectedValueOnce(new Error('An unexpected server error occurred.'))`; assert `result.current.error?.message === 'An unexpected server error occurred.'`, drop the `'HTTP error! status: 404'` assertion (that string is no longer constructed anywhere in the hook) and drop the `status: 404` mock field (no longer meaningful — the mock now rejects instead of resolving). |
| `'sets error when fetch throws'` | Unchanged intent: `mockGetProtocolPdf.mockRejectedValueOnce(new Error('Network failure'))`; assert `result.current.error?.message === 'Network failure'`. |

No test file exists today for `useManufactureOrdersQuery` (confirmed — none is being added); its
contract (`GetManufactureOrdersResponse` shape flowing into `ManufactureOrderList.tsx`) is
exercised transitively by that component's existing tests, if any, which are unaffected since the
returned shape is unchanged.

## Verification

- `cd frontend && npm run build && npm run lint` — must be clean; confirms no remaining `any` from
  this change and no lint violations.
- `npm test -- useOpenManufactureProtocol` — rewritten suite passes, 6/6.
- `grep -n "(apiClient as any)" src/api/hooks/useManufactureOrders.ts` — zero matches after the
  fix (two matches currently: line 65 in `useManufactureOrdersQuery`, line 421 in
  `useOpenManufactureProtocol`).

## Scope boundary (carried from plan-02, restated for implementers of this design)

In scope: `useManufactureOrders.ts` (`useManufactureOrdersQuery`, `useOpenManufactureProtocol`
only — the other nine hooks in the file already use `getManufactureOrdersClient()` correctly and
are untouched) and `useOpenManufactureProtocol.test.ts`. Out of scope:
`useManufacturingStockAnalysis.ts` (issue #3730, different file); `useSemiproductRecipePdf.ts`
(identical anti-pattern, flagged by architecture-01 for a separate follow-up issue, not folded in
here); any other `(apiClient as any)` usage elsewhere in the codebase; any behavior/UX change to
the order list or protocol PDF viewer.
