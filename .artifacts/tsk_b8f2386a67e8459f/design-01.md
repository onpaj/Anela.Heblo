# Design — useManufactureOrders: typed client instead of `(apiClient as any).http.fetch`

No UI changes. This is an internal data-access refactor: two hooks in
`frontend/src/api/hooks/useManufactureOrders.ts` change their implementation, their public
signatures and return shapes stay identical, so no component downstream (`ManufactureOrderList.tsx`,
protocol-open buttons, etc.) needs to change. UX/UI section omitted.

## Component design

### 1. `useManufactureOrdersQuery` (list query hook)

**Boundary/responsibility — unchanged:** a `useQuery` hook that takes a `GetManufactureOrdersRequest`
filter object and resolves to the order list page. Callers (`ManufactureOrderList.tsx`) are untouched.

**Internal implementation — changed:**

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
  `(apiClient as any).http.fetch`, manual `response.ok` check, `response.json()`.
- Reuses `getManufactureOrdersClient()` (already defined in this file, line 51), matching every
  other hook.
- `queryFn` return type becomes `Promise<GetManufactureOrdersResponse>` (class instance with typed
  fields), replacing the previous implicit `any`.
- Parameter order/nullability matches `manufactureOrder_GetOrders`'s generated signature exactly
  (verified against `api-client.ts:6917`):
  `(state, dateFrom, dateTo, responsiblePerson, orderNumber, productCode, erpDocumentNumber,
  manualActionRequired, lotNumber, pageNumber, pageSize)`. `pageNumber`/`pageSize` accept
  `number | undefined` only (not `null` — the generated method throws `"cannot be null"` if passed
  `null`), so `request.pageNumber ?? undefined` is required, not `request.pageNumber` directly,
  to normalize `null` → `undefined`.
- Query key (`manufactureOrderKeys.list(filters)`) and `staleTime` are unchanged — only `queryFn`'s
  body changes.

### 2. `useOpenManufactureProtocol` (imperative PDF-open hook)

**Boundary/responsibility — unchanged:** returns `{ openProtocol(orderId), isLoading, error }`.
`openProtocol` fetches the protocol PDF, opens it in a new tab as a blob URL, and revokes the URL
after 10s.

**Internal implementation — changed:**

```ts
export const useOpenManufactureProtocol = () => {
  const [isLoading, setIsLoading] = useState(false);
  const [error, setError] = useState<Error | null>(null);

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

  return { openProtocol, isLoading, error };
};
```

- Drops: `(apiClient as any).baseUrl`, `(apiClient as any).http.fetch`, manual `response.ok` check,
  `response.blob()`.
- `manufactureOrder_GetProtocolPdf(id): Promise<FileResponse>` (`api-client.ts:7336`) already does
  the blob extraction internally and throws (`throwException`, surfaced as a rejected promise) on
  non-2xx/206 status — so the existing `try/catch` becomes the sole error path; no `response.ok`
  check is needed or possible (there is no raw `Response` in scope anymore).
  `FileResponse.data` is the `Blob`, used directly in `URL.createObjectURL`.
- `error.message` on a thrown NSwag exception is no longer the hand-written
  `"HTTP error! status: ${status}"` string — it becomes NSwag's own message (typically
  `"An unexpected server error occurred."` for non-2xx, or the network error's own message for a
  transport failure). This is a visible test-assertion change (see below); the *behavior*
  (isLoading/error state transitions, no `window.open` call) is unchanged.

## Data schemas

No wire-format or DTO changes — same two endpoints, same request/response bodies. What changes is
which TypeScript layer constructs the request and parses the response.

**`GetManufactureOrdersRequest`** (local interface, unchanged, `useManufactureOrders.ts:25-37`):
`state`, `dateFrom`, `dateTo`, `responsiblePerson`, `orderNumber`, `productCode`,
`erpDocumentNumber`, `manualActionRequired`, `lotNumber`, `pageNumber`, `pageSize` — all
optional/nullable. Maps 1:1 onto `manufactureOrder_GetOrders`'s 11 positional parameters.

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
Already what `ManufactureOrderList.tsx` destructures (`data?.orders`, `data?.totalCount`,
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

`frontend/src/api/hooks/__tests__/useOpenManufactureProtocol.test.ts` currently mocks
`getAuthenticatedApiClient()` → `{ baseUrl, http: { fetch: mockFetch } }` and asserts on
`mockFetch`'s call args/return shape (`{ ok, blob }`). It must instead mock the generated client's
`manufactureOrder_GetProtocolPdf` method directly, since `openProtocol` no longer touches
`.http.fetch` or `.baseUrl`.

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
  ...
});
```

Per-test-case mapping (same 6 cases, same intent, new mock surface):
- *"calls the correct URL"* → renamed to assert the typed call instead:
  `expect(mockGetProtocolPdf).toHaveBeenCalledWith(42);` (no more URL string — the generated
  method takes `id: number`, not a URL).
- *"opens the blob URL in a new tab"* → `mockGetProtocolPdf.mockResolvedValueOnce({ data: mockBlob, status: 200 })`,
  then assert `URL.createObjectURL`/`window.open` as before.
- *"schedules URL revocation after 10 seconds"* → same fake-timer pattern, resolve
  `mockGetProtocolPdf` with `{ data: blob, status: 200 }`.
- *"sets isLoading true during fetch and false after"* → resolve `mockGetProtocolPdf` via a
  controlled promise instead of a controlled `blob()` promise.
- *"sets error when HTTP response is not ok"* → since the typed method throws instead of resolving
  a non-ok `Response`, change to `mockGetProtocolPdf.mockRejectedValueOnce(new Error('An unexpected server error occurred.'))`
  and assert `result.current.error?.message` equals that thrown message (drop the
  `'HTTP error! status: 404'` string assertion — that construction no longer exists in the hook).
- *"sets error when fetch throws"* → unchanged intent, `mockGetProtocolPdf.mockRejectedValueOnce(new Error('Network failure'))`.

No test file exists today for `useManufactureOrdersQuery`; none is being added — its contract
(`GetManufactureOrdersResponse` shape into `ManufactureOrderList.tsx`) is exercised transitively by
that component's existing tests, if any, which are unaffected since the returned shape is unchanged.

## Verification

- `cd frontend && npm run build && npm run lint` — must be clean, confirms no remaining `any` from
  this change and no lint violations.
- `npm test -- useOpenManufactureProtocol` — rewritten suite passes.
- `grep -n "(apiClient as any)" src/api/hooks/useManufactureOrders.ts` — zero matches after the fix.
