# Plan — useManufactureOrders: replace `(apiClient as any).http.fetch` with typed/sanctioned calls

## Summary

`frontend/src/api/hooks/useManufactureOrders.ts` has two hooks that reach into NSwag-private
fields (`(apiClient as any).baseUrl`, `(apiClient as any).http.fetch`) instead of using the
generated typed client or the sanctioned escape hatch. Fix both by routing through the
generated `manufactureOrder_GetOrders` / `manufactureOrder_GetProtocolPdf` methods, matching
every sibling hook already in this file.

## Context

`docs/development/api-client-generation.md` bans `(apiClient as any)` private-field access
(Enforcement Rule 3) because NSwag can rename `.http`/`.baseUrl` on regeneration with no
compile-time signal. This file already has the correct generated methods available
(`manufactureOrder_GetOrders` at `api-client.ts:6917`, `manufactureOrder_GetProtocolPdf` at
`api-client.ts:7336`) — nine other hooks in the same file already call the generated client
correctly via `getManufactureOrdersClient()`. Only `useManufactureOrdersQuery` and
`useOpenManufactureProtocol` are the holdouts.

Verified during planning:
- `manufactureOrder_GetOrders(state, dateFrom, dateTo, responsiblePerson, orderNumber,
  productCode, erpDocumentNumber, manualActionRequired, lotNumber, pageNumber, pageSize):
  Promise<GetManufactureOrdersResponse>` — positional params, in the same order the current
  hand-built `URLSearchParams` appends them.
- `GetManufactureOrdersResponse` has `orders`, `totalCount`, `pageNumber`, `pageSize`,
  `totalPages` — exactly what `ManufactureOrderList.tsx:90-92` destructures
  (`data?.orders`, `data?.totalCount`, `data?.totalPages`). Swapping in the typed method is a
  drop-in replacement; no consumer changes needed.
- `manufactureOrder_GetProtocolPdf(id): Promise<FileResponse>` where
  `FileResponse = { data: Blob; status: number; fileName?: string; headers?: {...} }` — the
  typed method already returns a `Blob` (`.data`), so it can replace the manual fetch+blob()
  call directly; the doc's "escape hatch" (`getApiBaseUrl()` + `getAuthenticatedFetch()`) is
  a fallback for cases where blob handling can't go through the typed client, but here it can.
- `getManufactureOrdersClient()` (`useManufactureOrders.ts:51-55`) already exists and is used
  by every other hook in the file — reuse it for both fixes instead of introducing a new
  helper.
- An existing test, `frontend/src/api/hooks/__tests__/useOpenManufactureProtocol.test.ts`,
  mocks `getAuthenticatedApiClient()` returning `{ baseUrl, http: { fetch } }` and asserts the
  exact URL/args passed to that raw fetch. This test is tightly coupled to the current
  implementation and must be rewritten to mock the generated client method instead.
- No dedicated test file exists for `useManufactureOrdersQuery` today (checked
  `frontend/src/api/hooks/__tests__/`); `ManufactureOrderList.tsx` consumes it, and its own
  tests (if any) should keep passing since the returned shape doesn't change.

## Functional requirements

**FR-1 — `useManufactureOrdersQuery` calls the typed client**
Replace the manual URL/query-string construction and `(apiClient as any).http.fetch` call
with `getManufactureOrdersClient().manufactureOrder_GetOrders(...)`, passing the 11 filter
params from `GetManufactureOrdersRequest` in the method's declared order.
- Acceptance: no `(apiClient as any)` or manual `URLSearchParams`/`fetch` remains in this hook.
- Acceptance: `queryFn` return type is inferred as `GetManufactureOrdersResponse` (no `any`).
- Acceptance: `ManufactureOrderList.tsx` continues to compile and read `data.orders`,
  `data.totalCount`, `data.totalPages` unchanged.

**FR-2 — `useOpenManufactureProtocol` calls the typed client**
Replace `(apiClient as any).baseUrl` + `(apiClient as any).http.fetch` with
`getManufactureOrdersClient().manufactureOrder_GetProtocolPdf(orderId)`, and build the blob
URL from the returned `FileResponse.data` (a `Blob`).
- Acceptance: no `(apiClient as any)` remains in this hook.
- Acceptance: existing behavior preserved — opens the PDF blob in a new tab, revokes the
  object URL after 10s, surfaces fetch/HTTP errors via the hook's `error` state.
- Acceptance: since the typed method throws (via NSwag's generated exception handling) rather
  than returning a non-ok `Response`, the `try/catch` must catch that thrown error instead of
  checking `response.ok`/`response.status`.

**FR-3 — Update the coupled test**
Rewrite `useOpenManufactureProtocol.test.ts` to mock
`getManufactureOrdersClient()`/`manufactureOrder_GetProtocolPdf` (or the underlying
`getAuthenticatedApiClient` typed method) instead of asserting on raw `http.fetch` args, so it
reflects the new implementation while preserving the same behavioral assertions (correct
order ID passed, blob opened in new tab, object URL revoked after 10s, loading state,
error-state on failure).
- Acceptance: `npm test -- useOpenManufactureProtocol` passes.

## Non-functional requirements

- No change to runtime behavior visible to end users (same PDF-open flow, same order list
  filtering/pagination).
- No new `any` types introduced; the query result must be typed as `GetManufactureOrdersResponse`.
- Match existing code style in the file (arrow functions, existing `manufactureOrderKeys`
  query-key structure, existing error-handling shape in `useOpenManufactureProtocol`).

## Data model

No data model changes — this is a client-call routing fix. Relevant existing types:
`GetManufactureOrdersRequest` (local interface, `useManufactureOrders.ts:25-37`),
`GetManufactureOrdersResponse`, `ManufactureOrderDto`, `FileResponse` (all in
`frontend/src/api/generated/api-client.ts`).

## Interfaces

No API contract changes. Both hooks call existing generated client methods against existing
endpoints (`GET /api/ManufactureOrder`, `GET /api/ManufactureOrder/{id}/protocol.pdf`) —
same HTTP requests, routed through the typed client instead of manual fetch.

## Dependencies and scope

- Depends on: generated client already exposing `manufactureOrder_GetOrders` and
  `manufactureOrder_GetProtocolPdf` (confirmed present, no regeneration needed).
- In scope: `useManufactureOrders.ts` (both flagged hooks) and the one coupled test file.
- Out of scope: `useManufacturingStockAnalysis.ts` (tracked separately, issue #3730), any
  other `(apiClient as any)` usages elsewhere in the codebase, any behavior/UX change to the
  order list or protocol PDF viewer.

## Rough plan

1. In `useManufactureOrdersQuery`, replace the manual `fullUrl`/`URLSearchParams`/
   `(apiClient as any).http.fetch`/`response.json()` block with a single call to
   `getManufactureOrdersClient().manufactureOrder_GetOrders(request.state ?? undefined,
   request.dateFrom ?? undefined, request.dateTo ?? undefined, request.responsiblePerson ??
   undefined, request.orderNumber ?? undefined, request.productCode ?? undefined,
   request.erpDocumentNumber ?? undefined, request.manualActionRequired ?? undefined,
   request.lotNumber ?? undefined, request.pageNumber ?? undefined, request.pageSize ??
   undefined)`, matching the generated method's parameter order/nullability.
2. In `useOpenManufactureProtocol`, replace the manual fetch block with
   `const apiClient = getManufactureOrdersClient(); const fileResponse = await
   apiClient.manufactureOrder_GetProtocolPdf(orderId);` then build the blob URL from
   `fileResponse.data`; keep the existing `try/catch`/`setIsLoading`/`setError` structure,
   dropping the now-unneeded manual `response.ok` check (the typed method throws on non-2xx).
3. Update `useOpenManufactureProtocol.test.ts` to mock the generated client's
   `manufactureOrder_GetProtocolPdf` (via the already-mocked `getAuthenticatedApiClient`)
   returning a `FileResponse`-shaped object, and adjust assertions from raw fetch-URL/args
   checks to method-call checks, keeping the same 6 test cases' intent (correct order ID,
   blob opened, object URL revoked after 10s, loading state, error surfaced on rejection).
4. Run `cd frontend && npm run build && npm run lint` and the targeted Jest suite
   (`npm test -- useOpenManufactureProtocol` and any `ManufactureOrderList` tests) to confirm
   no regressions.
5. Confirm no other file references the removed manual-fetch code path (grep for
   `(apiClient as any)` in this file to verify zero remaining occurrences).

## Open questions

- None — the generated methods, response shapes, and consumer usage were all verified
  directly against the current codebase during planning; no ambiguity remains for the
  implementation step.
