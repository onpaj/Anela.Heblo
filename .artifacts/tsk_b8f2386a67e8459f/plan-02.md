# Plan (final) — useManufactureOrders: replace `(apiClient as any).http.fetch` with typed/sanctioned calls

This is the consolidated plan after design-01 and architecture-01 both reviewed plan-01 without
finding anything to change. Architecture's verdict was **"Approved as designed, no changes
required"** — every factual claim (generated method signatures, response shapes, sole consumer,
test coupling) was independently re-verified against the current source and held. This document
is the implementation-ready plan; it does not alter FR-1/FR-2/FR-3 from plan-01, it folds in the
architecture review's confirmations and the one flagged-but-out-of-scope observation.

## Summary

`frontend/src/api/hooks/useManufactureOrders.ts` has two hooks — `useManufactureOrdersQuery`
(list query) and `useOpenManufactureProtocol` (PDF-open hook) — that bypass the generated typed
API client and instead reach into NSwag-private fields (`(apiClient as any).baseUrl`,
`(apiClient as any).http.fetch`), unlike every other hook in the same file. Fix both by routing
through the already-generated `manufactureOrder_GetOrders` / `manufactureOrder_GetProtocolPdf`
methods via the file's existing `getManufactureOrdersClient()` helper, and rewrite the one test
file coupled to the old raw-fetch shape.

## Context

`docs/development/api-client-generation.md` (Enforcement Rule 3) bans `(apiClient as any)`
private-field access because NSwag can rename `.http`/`.baseUrl` on regeneration with no
compile-time signal — a backend rename of a `GetManufactureOrdersResponse`/`ManufactureOrderDto`
field currently compiles clean and breaks only at runtime. This file already has the correct
generated methods available and nine other hooks in it already call them correctly through
`getManufactureOrdersClient()`. Only these two hooks are holdouts.

Verified facts (re-confirmed against current source in this step):
- `useManufactureOrdersQuery` (`useManufactureOrders.ts:59-93`, current source) hand-builds a
  `URLSearchParams` string and calls `(apiClient as any).http.fetch(urlWithParams, …)`, then
  `return await response.json()` — untyped `any` result feeding `ManufactureOrderList.tsx`, the
  largest/most-filtered query in the slice (11 params).
- `useOpenManufactureProtocol` (`useManufactureOrders.ts:409-435`, current source) uses
  `(apiClient as any).baseUrl` + `(apiClient as any).http.fetch` + `response.blob()` for the
  protocol PDF.
- `manufactureOrder_GetOrders(state, dateFrom, dateTo, responsiblePerson, orderNumber,
  productCode, erpDocumentNumber, manualActionRequired, lotNumber, pageNumber, pageSize):
  Promise<GetManufactureOrdersResponse>` exists at `api-client.ts:6917`, same positional order the
  hand-built `URLSearchParams` currently appends in.
- `manufactureOrder_GetProtocolPdf(id): Promise<FileResponse>` exists at `api-client.ts:7336`;
  `FileResponse = { data: Blob; status: number; fileName?: string; headers?: {...} }` —
  `fileResponse.data` is a drop-in `Blob` replacement for the current `response.blob()`.
- `GetManufactureOrdersResponse` (`api-client.ts:28155`) exposes `orders`, `totalCount`,
  `pageNumber`, `pageSize`, `totalPages` — exactly what `ManufactureOrderList.tsx:90-92`
  destructures. Grepped the whole frontend: it is the sole consumer of `useManufactureOrdersQuery`.
  No consumer-side changes needed.
- `SwaggerException extends Error` (`api-client.ts:43178`) and non-2xx responses reject via
  `throwException(...)` (`api-client.ts:43202`) with message `"An unexpected server error
  occurred."` — the existing `catch (err) { err instanceof Error ? err : new Error(...) }` pattern
  in `useOpenManufactureProtocol` already handles this correctly since `SwaggerException` *is* an
  `Error`; only the now-impossible `response.ok` branch is dropped.
- `getManufactureOrdersClient()` (`useManufactureOrders.ts:50-54`) already exists and is reused by
  every other hook in the file — reuse it for both fixes, no new helper.
- `frontend/src/api/hooks/__tests__/useOpenManufactureProtocol.test.ts` mocks
  `getAuthenticatedApiClient()` returning `{ baseUrl, http: { fetch } }` and asserts on raw
  fetch args/return shape across 6 cases (URL/args, blob-open, revoke-after-10s, loading state,
  HTTP-not-ok error, fetch-throw error). It is tightly coupled to the current implementation and
  must be rewritten to mock `manufactureOrder_GetProtocolPdf` directly.
- No dedicated test file exists for `useManufactureOrdersQuery`; its contract is exercised
  transitively through `ManufactureOrderList.tsx`, whose destructured fields don't change.

### On the escape-hatch alternative (resolved by architecture-01, no ambiguity remains)

The task description named `getApiBaseUrl()` + `getAuthenticatedFetch()` as a fallback "if the
typed method cannot be used directly for blob handling." The doc itself scopes that escape hatch
to cases where "an endpoint's business outcome cannot yet be expressed through the generated
client" (e.g., status-code branching the controller doesn't support yet). `GetProtocolPdf` is a
plain 200/206-success-or-throw download and the generated method already returns the blob via
`FileResponse.data` — there is no such gap. **Decision: call `manufactureOrder_GetProtocolPdf`
directly** (the doc's primary "✅ CORRECT" pattern), not the escape hatch.

## Functional requirements

**FR-1 — `useManufactureOrdersQuery` calls the typed client**
Replace the manual URL/query-string construction and `(apiClient as any).http.fetch` call with
`getManufactureOrdersClient().manufactureOrder_GetOrders(...)`, passing the 11 filter params from
`GetManufactureOrdersRequest` in the method's declared order, normalizing `null` → `undefined` on
every field (the generated method throws if passed `null` for `pageNumber`/`pageSize`).
- Acceptance: no `(apiClient as any)` or manual `URLSearchParams`/`fetch` remains in this hook.
- Acceptance: `queryFn` return type is inferred as `Promise<GetManufactureOrdersResponse>` (no
  `any`).
- Acceptance: `ManufactureOrderList.tsx` continues to compile and read `data.orders`,
  `data.totalCount`, `data.totalPages` unchanged, with no edits to that file.

**FR-2 — `useOpenManufactureProtocol` calls the typed client**
Replace `(apiClient as any).baseUrl` + `(apiClient as any).http.fetch` + `response.blob()` with
`getManufactureOrdersClient().manufactureOrder_GetProtocolPdf(orderId)`, building the blob URL
from the returned `FileResponse.data`.
- Acceptance: no `(apiClient as any)` remains in this hook.
- Acceptance: existing user-visible behavior preserved — opens the PDF blob in a new tab, revokes
  the object URL after 10s, surfaces fetch/HTTP errors via the hook's `error` state.
- Acceptance: the `try/catch` catches the thrown `SwaggerException`/network error directly (the
  typed method throws rather than returning a non-ok `Response`); the manual `response.ok` check
  is removed as it is no longer reachable.

**FR-3 — Update the coupled test**
Rewrite `useOpenManufactureProtocol.test.ts` to mock `getAuthenticatedApiClient()` returning an
object exposing a mocked `manufactureOrder_GetProtocolPdf`, instead of `{ baseUrl, http: { fetch
} }`, preserving the same 6 cases' intent (correct order ID passed, blob opened in new tab, object
URL revoked after 10s, loading state, error surfaced on rejection) with one intentional
assertion change: the "HTTP not ok" case now asserts on `SwaggerException`'s message ("An
unexpected server error occurred.") instead of the hand-written `"HTTP error! status: 404"`
string, since that construction no longer exists in the hook.
- Acceptance: `npm test -- useOpenManufactureProtocol` passes, 6/6 cases green.

## Non-functional requirements

- No change to runtime behavior visible to end users (same PDF-open flow, same order list
  filtering/pagination), except the one accepted error-message-string change in FR-3 (not
  currently rendered anywhere with the specific status code, per architecture-01's check).
- No new `any` types introduced; the query result must be typed as `GetManufactureOrdersResponse`.
- Match existing code style in the file (arrow functions, existing `manufactureOrderKeys`
  query-key structure, existing error-handling shape in `useOpenManufactureProtocol`).

## Data model

No data model changes — this is a client-call routing fix. Relevant existing types (all already
defined, none introduced): `GetManufactureOrdersRequest` (local interface,
`useManufactureOrders.ts:24-36`), `GetManufactureOrdersResponse`, `ManufactureOrderDto`,
`FileResponse` (all in `frontend/src/api/generated/api-client.ts`).

## Interfaces

No API contract changes. Both hooks call existing generated client methods against existing
endpoints (`GET /api/manufactureorder`, `GET /api/manufactureorder/{id}/protocol.pdf`) — same HTTP
requests, routed through the typed client instead of manual fetch.

## Dependencies and scope

- Depends on: generated client already exposing `manufactureOrder_GetOrders` and
  `manufactureOrder_GetProtocolPdf` (confirmed present in current `api-client.ts`; no client
  regeneration needed).
- In scope: `useManufactureOrders.ts` (both flagged hooks) and
  `useOpenManufactureProtocol.test.ts`.
- Out of scope: `useManufacturingStockAnalysis.ts` (tracked separately, issue #3730);
  `useSemiproductRecipePdf.ts`, which architecture-01 found contains the **identical**
  `(apiClient as any).baseUrl`/`.http.fetch` anti-pattern for a PDF download — flagged for a
  separate issue, not folded into this change, per the task's own "fixed individually per owning
  hook file" convention; any other `(apiClient as any)` usage elsewhere in the codebase; any
  behavior/UX change to the order list or protocol PDF viewer.

## Rough plan

1. In `useManufactureOrdersQuery`, replace the manual `fullUrl`/`URLSearchParams`/
   `(apiClient as any).http.fetch`/`response.json()` block with a single call to
   `getManufactureOrdersClient().manufactureOrder_GetOrders(request.state ?? undefined,
   request.dateFrom ?? undefined, request.dateTo ?? undefined, request.responsiblePerson ??
   undefined, request.orderNumber ?? undefined, request.productCode ?? undefined,
   request.erpDocumentNumber ?? undefined, request.manualActionRequired ?? undefined,
   request.lotNumber ?? undefined, request.pageNumber ?? undefined, request.pageSize ??
   undefined)`.
2. In `useOpenManufactureProtocol`, replace the manual fetch block with
   `const apiClient = getManufactureOrdersClient(); const fileResponse = await
   apiClient.manufactureOrder_GetProtocolPdf(orderId);` then build the blob URL from
   `fileResponse.data`; keep the existing `try/catch`/`setIsLoading`/`setError` structure, dropping
   the now-unreachable manual `response.ok` check.
3. Update `useOpenManufactureProtocol.test.ts` to mock the generated client's
   `manufactureOrder_GetProtocolPdf` (via the already-mocked `getAuthenticatedApiClient`)
   returning `FileResponse`-shaped values, adjusting assertions from raw fetch-URL/args checks to
   method-call checks, including the `SwaggerException`-message change in the HTTP-error case.
4. Run `cd frontend && npm run build && npm run lint` and the targeted Jest suite (`npm test --
   useOpenManufactureProtocol`, plus any `ManufactureOrderList` tests) to confirm no regressions.
5. Confirm zero remaining `(apiClient as any)` occurrences:
   `grep -n "(apiClient as any)" frontend/src/api/hooks/useManufactureOrders.ts`.
6. File a follow-up issue for `useSemiproductRecipePdf.ts` (same anti-pattern, different file) —
   do not fix it as part of this change.

## Open questions

- None. Every factual claim underpinning this plan (generated method signatures, response shapes,
  sole consumer, exception behavior, test coupling) was independently re-verified against the
  current repository state across plan-01, design-01, and architecture-01; no ambiguity remains
  for implementation.
