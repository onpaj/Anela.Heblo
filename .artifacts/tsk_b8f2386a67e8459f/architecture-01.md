# Architecture assessment — useManufactureOrders: typed client instead of `(apiClient as any).http.fetch`

## Verdict

**Approved as designed, no changes required.** This is a mechanical, low-risk refactor with no
architectural decisions of consequence beyond the two already made in plan-01/design-01: route
`useManufactureOrdersQuery` through `manufactureOrder_GetOrders`, and route
`useOpenManufactureProtocol` through `manufactureOrder_GetProtocolPdf` directly (not the escape
hatch). I independently re-verified every factual claim the plan/design rely on against the
current source; all of them hold.

## Alignment with existing patterns

- `frontend/src/api/hooks/useManufactureOrders.ts:51-55` already defines
  `getManufactureOrdersClient()` (`getAuthenticatedApiClient()` cast to `GeneratedApiClient`) and
  nine other hooks in the same file (`useManufactureOrderDetailQuery`,
  `useCreateManufactureOrderMutation`, `useManufactureOrderCalendarQuery`, etc.) already use it.
  The design reuses this helper rather than introducing a new one — correct, matches the file's
  own convention, no new abstraction needed.
- Verified `manufactureOrder_GetOrders`'s generated signature directly
  (`api-client.ts:6917`): `(state, dateFrom, dateTo, responsiblePerson, orderNumber, productCode,
  erpDocumentNumber, manualActionRequired, lotNumber, pageNumber, pageSize):
  Promise<GetManufactureOrdersResponse>`. Matches the plan's positional-argument list exactly,
  including the `pageNumber`/`pageSize` null-vs-undefined distinction (the generated method
  `throw`s if passed `null` — `?? undefined` in the design is required, not decorative).
- Verified `manufactureOrder_GetProtocolPdf(id): Promise<FileResponse>` (`api-client.ts:7336`)
  and `FileResponse` (`api-client.ts:43171`: `{ data: Blob; status: number; fileName?: string;
  headers?: {...} }`). The typed method does the blob extraction and content-disposition parsing
  internally — the design's `URL.createObjectURL(fileResponse.data)` is a direct, correct
  replacement for the current `response.blob()`.
- Verified `ManufactureOrderList.tsx:90-92` destructures `data?.orders`, `data?.totalCount`,
  `data?.totalPages` from `useManufactureOrdersQuery`'s result — exactly the fields
  `GetManufactureOrdersResponse` exposes. Grepped the whole frontend for other consumers of
  `useManufactureOrdersQuery`; `ManufactureOrderList.tsx` is the only one. No consumer-side
  changes needed, as claimed.
- Verified `SwaggerException extends Error` (`api-client.ts:43178`) and `throwException(...)`
  (`api-client.ts:43202`) — non-2xx responses reject with a `SwaggerException` whose `.message`
  is `"An unexpected server error occurred."`. The existing `catch (err) { err instanceof Error
  ? err : new Error(...) }` pattern in `useOpenManufactureProtocol` already handles this
  correctly since `SwaggerException` *is* an `Error` — no catch-block restructuring needed beyond
  dropping the now-impossible `response.ok` branch, as the design states.

## On the escape-hatch question

The task description offered `getApiBaseUrl()` + `getAuthenticatedFetch()` as a fallback "if the
typed method cannot be used directly for blob handling." I checked `docs/development/api-client-generation.md`'s
own framing of that escape hatch: it says to reach for it **"only when an endpoint's business
outcome cannot yet be expressed through the generated client — for example, an `If-Match`-based
update returning HTTP 412 before the controller is annotated for it."** `GetProtocolPdf` has no
such status-code-branching requirement (it's a plain 200/206-success-or-throw download), and the
generated method already returns the blob via `FileResponse.data`. The design's choice to call
`manufactureOrder_GetProtocolPdf` directly — the doc's primary "✅ CORRECT" pattern, not the
escape hatch — is the right call, not a shortcut. Reaching for the escape hatch here would have
been the wrong choice: it would silently drop the doc's own `getAuthenticatedApiClient()` typed
path for a case that doesn't need status-code branching.

## Test coupling

The existing `useOpenManufactureProtocol.test.ts` mocks `getAuthenticatedApiClient()` to return
`{ baseUrl, http: { fetch: mockFetch } }` and asserts on raw fetch args — I read the full file
and confirmed it is tightly coupled to the current implementation exactly as plan-01 describes
(6 cases: URL/args, blob-open, revoke-after-10s, loading state, HTTP-not-ok error, fetch-throw
error). design-01's rewrite mocks `getAuthenticatedApiClient()` to return an object exposing
`manufactureOrder_GetProtocolPdf` directly — this is consistent with how
`getManufactureOrdersClient()` is implemented (a thin cast over `getAuthenticatedApiClient()`),
so mocking at the `getAuthenticatedApiClient` boundary is correct and requires no new mock
scaffolding. The "HTTP error! status: 404" → "An unexpected server error occurred." message
change in that one test case is a genuine, expected behavioral difference (verified above via
`SwaggerException`), not a design oversight.

## Observation — not a blocker for this task

`frontend/src/api/hooks/useSemiproductRecipePdf.ts` contains the **identical** anti-pattern
(`(apiClient as any).baseUrl` + `(apiClient as any).http.fetch` for a PDF blob download, same
shape as `useOpenManufactureProtocol` before the fix). Per the task's stated convention ("fixed
individually" per owning hook file, referencing closed issues #3494/#3442/#3333/#3395/#2500/#1826
and open #3730 for a different file), this is out of scope here and should be filed as its own
issue rather than folded into this change — flagging it so it isn't mistaken for a second
"sanctioned" precedent when it is in fact the same bug living in a different file.

## Implementation guidance (confirms plan-01/design-01, no deviation)

1. `useManufactureOrdersQuery`: replace the manual `URLSearchParams`/`(apiClient as
   any).http.fetch`/`response.json()` block with `getManufactureOrdersClient().manufactureOrder_GetOrders(...)`
   using `?? undefined` normalization on every filter field, matching the verified parameter
   order. `queryFn`'s return type becomes `Promise<GetManufactureOrdersResponse>`.
2. `useOpenManufactureProtocol`: replace `(apiClient as any).baseUrl` +
   `(apiClient as any).http.fetch` + `response.blob()` with
   `getManufactureOrdersClient().manufactureOrder_GetProtocolPdf(orderId)`, using
   `fileResponse.data` as the `Blob` for `URL.createObjectURL`. Drop the `response.ok` check —
   the typed method throws on non-2xx/206.
3. Rewrite `useOpenManufactureProtocol.test.ts` to mock `getAuthenticatedApiClient()` returning
   an object with a mocked `manufactureOrder_GetProtocolPdf`, preserving all 6 existing
   assertions' intent (update the error-message assertion to match `SwaggerException`'s message).
4. No changes needed to `ManufactureOrderList.tsx` or any other consumer.
5. Standard validation gate applies: `cd frontend && npm run build && npm run lint`, then the
   targeted Jest suite, then `grep -n "(apiClient as any)" src/api/hooks/useManufactureOrders.ts`
   returning zero matches.

## Risks

- **Low.** Both endpoints are already called through the same base client
  (`getAuthenticatedApiClient()`); only the call surface changes from raw `fetch` to typed
  methods hitting the identical URLs. No wire-format, auth, or routing change.
- The one behavior-visible change (SwaggerException's generic error message replacing the
  hand-written `"HTTP error! status: ${status}"` string) is confined to the `error` state
  surfaced by `useOpenManufactureProtocol`, which is not currently rendered with the specific
  status code anywhere I found — reasonable to accept as noted in design-01.

## Prerequisites before implementation begins

None. No client regeneration needed (both methods already exist in the generated client), no
backend changes, no schema changes.
