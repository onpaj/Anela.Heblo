# Architecture assessment (final) — useManufactureOrders: typed client instead of `(apiClient as any).http.fetch`

## Verdict

**Approved as designed, no changes required.** design-02 is a consolidation of design-01 plus
architecture-01's confirmations, with no new decisions and no changed approach. I re-verified
every load-bearing claim independently against the current source in this step (git tree is
clean, `f1877021` HEAD, no implementation work exists yet) rather than trusting the prior
artifacts' citations. Nothing has drifted. The design is implementation-ready as written.

## What I re-verified against the actual codebase

- **Current state of both hooks** — read `frontend/src/api/hooks/useManufactureOrders.ts` in
  full. `useManufactureOrdersQuery` (lines 58–97) and `useOpenManufactureProtocol`'s `openProtocol`
  (lines 410–439) match design-02's "current implementation" blocks exactly, including the two
  `(apiClient as any)` sites (line 64 `.baseUrl`, line 82 `.http.fetch` for the list query; line
  420 `.baseUrl`, line 421 `.http.fetch` for the PDF hook). `getManufactureOrdersClient()`
  (lines 51–55) exists and is already used by all nine other hooks in the file — the design reuses
  it rather than introducing anything new.
- **`manufactureOrder_GetOrders` signature** — `api-client.ts:6917`:
  `(state: ManufactureOrderState | null | undefined, dateFrom: Date | null | undefined, dateTo: Date | null | undefined, responsiblePerson: string | null | undefined, orderNumber: string | null | undefined, productCode: string | null | undefined, erpDocumentNumber: string | null | undefined, manualActionRequired: boolean | null | undefined, lotNumber: string | null | undefined, pageNumber: number | undefined, pageSize: number | undefined): Promise<GetManufactureOrdersResponse>`.
  Confirms the design's positional-argument order and, critically, that `pageNumber`/`pageSize`
  reject `null` (no `| null` in their type) while the other nine parameters accept it — the
  design's `?? undefined` normalization on those two fields specifically is required, not
  decorative.
- **`manufactureOrder_GetProtocolPdf` and its blob/error handling** — `api-client.ts:7336–7375`.
  Confirmed the full body: status `200`/`206` → resolves
  `{ fileName, data: blob, status, headers }` via `response.blob()`; any other status except `204`
  → `throwException("An unexpected server error occurred.", status, ...)`; `204` → resolves
  `null`. `FileResponse` (`api-client.ts:43171-43176`) is exactly `{ data: Blob; status: number;
  fileName?: string; headers?: {...} }` — `fileResponse.data` is a direct, correct replacement for
  today's `response.blob()`.
- **`SwaggerException`** — `api-client.ts:43178-43200`: `extends Error`, constructor sets
  `this.message = message` directly. It genuinely *is* an `Error` instance, so the existing
  `catch (err) { err instanceof Error ? err : new Error(String(err)) }` pattern in
  `useOpenManufactureProtocol` requires zero changes to keep working — confirms design-02's claim
  that no catch-block restructuring is needed beyond deleting the now-unreachable `response.ok`
  branch.
- **`GetManufactureOrdersResponse` shape** — `api-client.ts:28155`: `extends BaseResponse`, has
  `orders`, `totalCount`, `pageNumber`, `pageSize`, `totalPages`. Cross-checked against the sole
  consumer `ManufactureOrderList.tsx:90-92`, which destructures `data?.orders`, `data?.totalCount`,
  `data?.totalPages` — exactly what the generated response exposes. Grepped the whole `frontend/src`
  tree for `useManufactureOrdersQuery`; `ManufactureOrderList.tsx` is the only consumer besides the
  hook file itself. No downstream change needed.
- **The coupled test file** — read `useOpenManufactureProtocol.test.ts` in full (137 lines, 6
  `test()` cases). It mocks `getAuthenticatedApiClient()` to return
  `{ baseUrl: 'http://localhost:5001', http: { fetch: mockFetch } }` and asserts directly on
  `mockFetch`'s call args and a hand-shaped `{ ok, status, blob }` object — confirmed tightly
  coupled to the current raw-fetch implementation exactly as design-02's rewrite table assumes,
  case by case (URL/args, blob-open, revoke-after-10s, loading state, `'HTTP error! status: 404'`
  on non-ok, `'Network failure'` on throw). design-02's mock-boundary choice
  (`getAuthenticatedApiClient()` returning an object exposing `manufactureOrder_GetProtocolPdf`
  directly) is consistent with how `getManufactureOrdersClient()` itself is implemented — a thin
  cast over the same function — so no new mock scaffolding is required, as claimed.
- **The doc's escape-hatch criterion, verbatim** —
  `docs/development/api-client-generation.md:252-274`: "Reach for these helpers only when an
  endpoint's business outcome cannot yet be expressed through the generated client — for example,
  an `If-Match`-based update returning HTTP 412 Precondition Failed before the controller has been
  annotated..." and rule 3, "**NEVER use `(apiClient as any)`**... use public helper functions
  instead." `GetProtocolPdf` has no unexpressed status-code branching — it's a plain
  200/206-success-or-throw download the generated method already models correctly via
  `FileResponse`. design-02's decision to call `manufactureOrder_GetProtocolPdf` directly (not the
  `getApiBaseUrl()`/`getAuthenticatedFetch()` escape hatch) is the doc's primary "✅ CORRECT"
  path, matching the rule's own stated scope exactly — not a shortcut, not an overreach.

Every citation in design-02 (line numbers, signatures, shapes) checked out against current source
with only trivial line-number drift (e.g. `GetProtocolPdf` at `api-client.ts:7336` vs. design-02's
`7343`/`7354` — same function, generated file has shifted a few lines since design-02 was written,
substance identical). No claim was found to be stale or wrong.

## Alignment with existing patterns

- Reuses `getManufactureOrdersClient()`, the file's own established convention (9/11 hooks already
  use it) — no new abstraction, no new helper file, no new client wrapper.
- Both endpoints stay behind `getAuthenticatedApiClient()` — auth headers, base URL resolution, and
  global error-toast wiring (if any) don't change; only the call surface moves from raw `fetch` to
  typed generated methods hitting the identical URLs (`/api/manufactureorder` and
  `/api/ManufactureOrder/{id}/protocol.pdf`, confirmed unchanged from `api-client.ts:7337`).
- Matches `docs/development/api-client-generation.md`'s enforcement rules exactly: typed client for
  standard calls, escape hatch reserved for genuine status-code-branching gaps, no `(apiClient as
  any)` anywhere in the result.

## Scope boundary

In scope and correctly bounded: `useManufactureOrders.ts` (the two named hooks only) and
`useOpenManufactureProtocol.test.ts`. Correctly out of scope per the task's own "fixed
individually" convention: `useManufacturingStockAnalysis.ts` (already tracked as #3730, different
file) and `useSemiproductRecipePdf.ts` (identical anti-pattern, confirmed present by architecture-01,
correctly deferred to its own follow-up issue rather than folded in here — re-confirming that
call: bundling it would silently expand this task's diff beyond the filed issue's boundary).

## Risks

- **Low**, unchanged from architecture-01's assessment. No wire-format, auth, or routing change;
  no client regeneration needed (both generated methods already exist); no backend change.
- The one user-visible behavioral delta — `SwaggerException`'s fixed `"An unexpected server error
  occurred."` message replacing the hand-written `"HTTP error! status: ${status}"` string on a
  non-2xx/206 protocol-PDF fetch — is confined to `useOpenManufactureProtocol`'s internal `error`
  state. Confirmed (again, by grep) that no component renders that message string today, so this
  is safe to accept as a byproduct of adopting the typed client, not a regression requiring
  mitigation.

## Prerequisites before implementation begins

None. Both generated methods already exist in `api-client.ts`; no schema, backend, or client
regeneration work is needed before starting.

## Implementation guidance (unchanged from architecture-01, reconfirmed against current source)

1. `useManufactureOrdersQuery`: replace the manual `URLSearchParams` / `(apiClient as
   any).http.fetch` / `response.json()` block with
   `getManufactureOrdersClient().manufactureOrder_GetOrders(...)`, applying `?? undefined` to
   every filter field (required specifically for `pageNumber`/`pageSize`, which reject `null`).
   `queryFn`'s return type becomes `Promise<GetManufactureOrdersResponse>`.
2. `useOpenManufactureProtocol`: replace `(apiClient as any).baseUrl` +
   `(apiClient as any).http.fetch` + `response.blob()` with
   `getManufactureOrdersClient().manufactureOrder_GetProtocolPdf(orderId)`, using
   `fileResponse.data` for `URL.createObjectURL`. Drop the `response.ok` check — the typed method
   throws a `SwaggerException` (an `Error`) on non-2xx/206 instead.
3. Rewrite `useOpenManufactureProtocol.test.ts` to mock `getAuthenticatedApiClient()` returning an
   object exposing a mocked `manufactureOrder_GetProtocolPdf`, per design-02's case-by-case table;
   update the non-ok-response test's expected message to `'An unexpected server error occurred.'`.
4. No changes needed anywhere else — `ManufactureOrderList.tsx` and all other consumers are
   unaffected.
5. Validation gate: `cd frontend && npm run build && npm run lint`, then
   `npm test -- useOpenManufactureProtocol` (6/6 passing), then
   `grep -n "(apiClient as any)" src/api/hooks/useManufactureOrders.ts` returning zero matches.
