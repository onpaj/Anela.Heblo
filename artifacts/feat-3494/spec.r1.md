# Specification: Replace raw `http.fetch` bypass in `useFinancialOverviewQuery` with the typed generated client method

## Summary
`frontend/src/api/hooks/useFinancialOverview.ts` currently builds the FinancialOverview request URL by hand and calls `(apiClient as any).http.fetch(...)` directly, bypassing the typed NSwag-generated `ApiClient` method and its `as any` type-safety escape hatches. The generated client already exposes a fully typed method, `financialOverview_GetFinancialOverview(months, includeStockData, excludedDepartments, includeCurrentMonth)`, that performs the identical query-string construction, GET request, and response parsing/error handling. This change replaces the manual fetch block with a direct call to that generated method, removing both `as any` casts and restoring the client's built-in auth/error-handling pipeline for this endpoint.

## Background
An automated daily architecture-review routine flagged this file (see `artifacts/feat-3494/brief.md`, filed 2026-07-05) because it reaches into `ApiClient`'s internal `http` property — an NSwag implementation detail not part of the public contract — instead of calling the generated typed method for the endpoint. This:
1. **Bypasses middleware** — `getAuthenticatedApiClient()` (in `frontend/src/api/client.ts`) wires a custom `http.fetch` implementation that attaches auth headers, handles 401 redirect-to-login, resets the auth-recovery counter, and shows centralized error toasts (`extractErrorMessage`, `globalToastHandler`). Calling `.http.fetch` directly *does* still run through this wired object (since `apiClient.http` is that same authenticated fetcher) — but it does so by reaching past the typed method entirely, which is fragile and defeats the purpose of code generation.
2. **Is fragile to regeneration** — `http` is an NSwag-emitted internal property name. A future template change or NSwag version bump could rename or restructure it with no compile error at this call site, silently breaking the FinancialOverview page.
3. **Is type-unsafe** — both the URL construction and the response parsing (`await response.json() as GetFinancialOverviewResponse`) are manually re-implemented and asserted, rather than relying on the generated `GetFinancialOverviewResponse.fromJS(...)` deserialization and its compile-time parameter checking.

Investigation of the current codebase confirms:
- The generated client (`frontend/src/api/generated/api-client.ts`, lines 3809–3860) already contains a method for this exact endpoint: `financialOverview_GetFinancialOverview(months, includeStockData, excludedDepartments, includeCurrentMonth): Promise<GetFinancialOverviewResponse>`. **Note the actual generated name differs from the brief's suggested `financialOverviewGet`** — NSwag emits `{controllerName}_{ActionName}` style names, and for this controller/action it is `financialOverview_GetFinancialOverview`.
- The method's parameter order and query-string semantics (`months`, `includeStockData`, `excludedDepartments[]`, `includeCurrentMonth`) exactly match what the hook currently builds by hand — no NSwag template or OpenAPI spec change is needed.
- The generated method throws (via `throwException`) a `SwaggerException` (which `extends Error`) or a `ProblemDetails`-carrying exception on non-200/204 responses, and resolves with a fully-typed `GetFinancialOverviewResponse` (via `GetFinancialOverviewResponse.fromJS`) on 200 — compatible with the hook's existing `useQuery<GetFinancialOverviewResponse, Error>` signature.
- This "call the generated method directly, no manual fetch/try-catch" pattern is the codebase-wide convention: every other hook in `frontend/src/api/hooks/*.ts` (e.g. `useWarehouseStatistics.ts`, `useProductMarginSummary.ts`, `useConfiguration.ts`, `useManufactureSettings.ts`) does `return apiClient.someMethod(args)` with no wrapping.

## Functional Requirements

### FR-1: Replace manual URL/fetch construction with the generated client method
In `frontend/src/api/hooks/useFinancialOverview.ts`, the `queryFn` of `useFinancialOverviewQuery` must call `apiClient.financialOverview_GetFinancialOverview(months, includeStockData, excludedDepartments, includeCurrentMonth)` instead of building `URLSearchParams`, computing `fullUrl`, and calling `(apiClient as any).http.fetch(...)`.

The replacement `queryFn` body:
```typescript
queryFn: async () => {
  const apiClient = getAuthenticatedApiClient();
  return await apiClient.financialOverview_GetFinancialOverview(
    months,
    includeStockData,
    excludedDepartments,
    includeCurrentMonth,
  );
},
```

Notes:
- `excludedDepartments` is passed as-is (the hook's parameter already defaults to `[]`). Passing an empty array produces the same query string as passing `undefined` (the generated method's `forEach` over an empty array appends nothing), so no `excludedDepartments.length > 0 ? excludedDepartments : undefined` ternary is required — this differs from the brief's suggested snippet but is behaviorally equivalent and simpler. See Open Questions for confirmation.
- No manual `response.ok` check, `response.json()` call, or `as GetFinancialOverviewResponse` cast is needed — the generated method already returns a parsed, typed `GetFinancialOverviewResponse` on success and throws on failure.

**Acceptance criteria:**
- `frontend/src/api/hooks/useFinancialOverview.ts` no longer contains `as any` anywhere.
- `frontend/src/api/hooks/useFinancialOverview.ts` no longer imports or references `URLSearchParams` for this purpose (the manual query-string block is removed in its entirety).
- `frontend/src/api/hooks/useFinancialOverview.ts` no longer calls `.http.fetch` directly.
- `useFinancialOverviewQuery`'s public signature (`months`, `includeStockData`, `excludedDepartments`, `includeCurrentMonth`, all with the same defaults) is unchanged.
- `useFinancialOverviewQuery`'s return type (`UseQueryResult<GetFinancialOverviewResponse, Error>`) is unchanged.
- The re-exported generated types (`GetFinancialOverviewResponse`, `MonthlyFinancialDataDto`, `FinancialSummaryDto`, `StockChangeDto`, `StockSummaryDto`) are unchanged.
- The `queryKey`, `staleTime`, and `gcTime` values are unchanged.

### FR-2: Preserve request semantics exactly
The resulting HTTP request (method, path, query parameters, and their encoding) issued to `GET /api/FinancialOverview` must be byte-for-byte identical to what the current manual implementation produces for the same input arguments, for every combination of inputs currently exercised by callers of `useFinancialOverviewQuery` (see `frontend/src/pages` / components using this hook for current call sites).

**Acceptance criteria:**
- For `months = 6, includeStockData = true, excludedDepartments = [], includeCurrentMonth = false` (the hook's defaults), the request URL query string matches `months=6&includeStockData=true&includeCurrentMonth=false` (no `excludedDepartments` params), consistent with both the old and new implementation.
- For a non-empty `excludedDepartments` array, e.g. `["Sales", "Marketing"]`, the request includes repeated `excludedDepartments=Sales&excludedDepartments=Marketing` params (same as the old implementation's `params.append` loop and the generated method's `forEach`).
- Manual verification (browser network tab or equivalent) against a running/staging backend confirms the FinancialOverview page renders identical data before and after the change, for at least one call with default params and one call with `excludedDepartments` populated (if the UI exposes a department-exclusion filter).

### FR-3: Preserve error behavior for the consuming UI
Whatever component(s) consume `useFinancialOverviewQuery`'s `error` field must continue to receive an `Error`-compatible object on failure (network error, non-2xx HTTP status), with no behavioral regression in how errors are displayed.

**Acceptance criteria:**
- On a simulated/forced non-200 response (e.g. temporarily point `apiUrl` at an invalid endpoint, or use existing test tooling to force a 403/500), `useFinancialOverviewQuery`'s `error` is a truthy object whose `message` is a non-empty string, matching current behavior (previously `new Error(\`Failed to fetch financial overview: ${response.statusText}\`)`; now a `SwaggerException` or `ProblemDetails`-derived exception from the generated client, both of which are `Error` instances with a populated `message`).
- No TypeScript compilation errors are introduced (`useQuery<GetFinancialOverviewResponse, Error>` remains satisfied because `SwaggerException extends Error`).
- Global 401 handling (auto-redirect-to-login via `globalAuthRedirectHandler`) and global error-toast behavior (`globalToastHandler`) continue to fire for this endpoint exactly as they do today, since both are wired into `getAuthenticatedApiClient()`'s `http.fetch` implementation, which the generated method still calls internally (`this.http.fetch(url_, options_)` inside `financialOverview_GetFinancialOverview`).

## Non-Functional Requirements

### NFR-1: Performance
No performance impact expected — the change removes one layer of hand-rolled fetch/parse logic and replaces it with the equivalent generated code path, which performs the same number of network round-trips (one GET request) and equivalent JSON parsing (`GetFinancialOverviewResponse.fromJS` vs. the old `response.json() as ...` cast). `staleTime` (5 min) and `gcTime` (10 min) are unchanged, so caching behavior and request frequency are unaffected.

### NFR-2: Security
No change in security posture. Authentication continues to flow through `getAuthenticatedApiClient()`'s wired `http.fetch`, which attaches the `Authorization` bearer token (or E2E test token) exactly as before. No new data is exposed or logged; this is a pure refactor of the call site with no change to what is sent over the wire or how responses are handled from a security perspective.

## Data Model
No data model changes. This is a refactor of an existing hook's internal implementation; it uses only pre-existing generated types:
- `GetFinancialOverviewResponse` (and its nested `MonthlyFinancialDataDto`, `FinancialSummaryDto`, `StockChangeDto`, `StockSummaryDto`) — unchanged, defined in `frontend/src/api/generated/api-client.ts` starting at line 20378.
- No backend/API contract changes. No OpenAPI spec regeneration is required, since the generated client already contains the needed method.

## API / Interface Design
No public API surface changes.

- **Backend endpoint** (unchanged): `GET /api/FinancialOverview?months={number}&includeStockData={bool}&excludedDepartments={string}&excludedDepartments={string}...&includeCurrentMonth={bool}`.
- **Generated client method** (already exists, unchanged by this task): `ApiClient.financialOverview_GetFinancialOverview(months: number | null | undefined, includeStockData: boolean | undefined, excludedDepartments: string[] | null | undefined, includeCurrentMonth: boolean | undefined): Promise<GetFinancialOverviewResponse>` — `frontend/src/api/generated/api-client.ts:3809`.
- **Hook signature** (unchanged): `useFinancialOverviewQuery(months = 6, includeStockData = true, excludedDepartments = [], includeCurrentMonth = false): UseQueryResult<GetFinancialOverviewResponse, Error>` — `frontend/src/api/hooks/useFinancialOverview.ts`.
- **Internal change only**: the `queryFn` implementation inside `useFinancialOverviewQuery`, as described in FR-1.

## Dependencies
- `frontend/src/api/generated/api-client.ts` — the NSwag-generated client; specifically its `financialOverview_GetFinancialOverview` method (line 3809) and `GetFinancialOverviewResponse` class (line 20378). No regeneration needed; the method already exists and matches the required shape.
- `frontend/src/api/client.ts` — `getAuthenticatedApiClient()`, which wires auth headers, 401 handling, and error toasts into `ApiClient`'s `http.fetch`. No changes needed here; the generated method already routes through it.
- `@tanstack/react-query` — `useQuery` usage is unchanged.
- No backend changes required.
- No new npm packages.

## Out of Scope
- Any change to the backend `FinancialOverviewController` or its OpenAPI annotations.
- Any change to the NSwag generation config/template.
- Any change to `GetFinancialOverviewResponse` or related DTOs' shape.
- Any change to how `useFinancialOverviewQuery` is consumed by UI components (props, rendering logic, filters).
- Any change to `queryKey`, `staleTime`, `gcTime`, or retry behavior.
- Fixing or altering the general-purpose `getAuthenticatedFetch()` / `authenticatedFetch()` helpers in `client.ts` (these remain valid for endpoints without a generated typed method, per the doc comment already in that file); this task only touches the one call site that has a typed method available and is used unnecessarily via raw fetch.
- Broader audit of other hooks for similar `(apiClient as any)` patterns — this spec covers only `useFinancialOverview.ts` as filed in the brief. (A grep across `frontend/src/api/hooks/*.ts` during investigation found no other occurrences of `.http.fetch` outside `useFinancialOverview.ts` and `client.ts` itself, so no sibling instances were identified, but a full repo-wide audit was not performed as part of this task.)

## Open Questions
None. (Investigation confirmed the generated method exists with a matching signature, so no NSwag/OpenAPI changes are needed, and the empty-array-vs-undefined behavior for `excludedDepartments` was verified to be equivalent by reading the generated method's implementation — both produce zero `excludedDepartments` query params.)

## Status: COMPLETE
