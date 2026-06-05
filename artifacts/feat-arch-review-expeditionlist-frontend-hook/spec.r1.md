# Specification: Eliminate `as any` Casts in `useExpeditionListArchive.ts` by Using Generated Typed Client

## Summary
Five hooks/helpers in `frontend/src/api/hooks/useExpeditionListArchive.ts` reach into private fields of the NSwag-generated `ApiClient` via `(apiClient as any).baseUrl` and `(apiClient as any).http.fetch`, bypassing the typed client entirely. Investigation shows the generated client already exposes typed methods for all four API calls (`expeditionListArchive_GetDates`, `expeditionListArchive_GetByDate`, `expeditionListArchive_Reprint`, `expeditionList_RunFix`), so this is a pure call-site migration — no backend or codegen changes are required. The remaining URL-construction helper (`getExpeditionListDownloadUrl`) will use the public `getApiBaseUrl()` helper.

## Background
`docs/development/api-client-generation.md` explicitly forbids `(apiClient as any).baseUrl` and `(apiClient as any).http.fetch`:

> **❌ AVOID**: `(apiClient as any).baseUrl` and `(apiClient as any).http.fetch`
> These reach into private fields of the NSwag-generated class. If NSwag renames those fields, the code breaks at runtime with no compile-time warning. Use `getApiBaseUrl()` and `getAuthenticatedFetch()` from `./client` instead.

`useExpeditionListArchive.ts` is the only file in the codebase that still violates this rule (5 occurrences across `useExpeditionDates`, `useExpeditionListsByDate`, `useReprintExpeditionList`, `useRunExpeditionListPrintFix`, and `getExpeditionListDownloadUrl`). The arch-review routine flagged it on 2026-06-03 as a type-safety regression: there is no compile-time check that the bypassed endpoints match backend contracts, no guarantee that auth/error interceptors apply, and any rename of the generated client's internal `.http` field breaks all five call sites at runtime.

Two relevant findings during scoping reduce the work to a pure refactor:

1. The generated client (`frontend/src/api/generated/api-client.ts`) already contains typed methods for every backend endpoint these hooks call:
   - `expeditionListArchive_GetDates(page, pageSize)` → `GetExpeditionDatesResponse`
   - `expeditionListArchive_GetByDate(date)` → `GetExpeditionListsByDateResponse`
   - `expeditionListArchive_Reprint(request)` → `ReprintExpeditionListResponse`
   - `expeditionList_RunFix()` → `RunExpeditionListPrintFixResponse`
   - `expeditionListArchive_Download(blobPath)` → `FileResponse`
2. The public escape-hatch helpers `getApiBaseUrl()` and `getAuthenticatedFetch()` already exist in `frontend/src/api/client.ts`. The brief's "expose `baseUrl` publicly" suggestion is already satisfied — call sites simply need to use these helpers.

The brief's premise that "these endpoints don't have generated typed methods" is incorrect; this assumption is corrected in the requirements below.

## Functional Requirements

### FR-1: Replace `useExpeditionDates` with typed client call
Rewrite `useExpeditionDates(page, pageSize)` to call `getAuthenticatedApiClient().expeditionListArchive_GetDates(page, pageSize)`. Drop manual URL construction, `URLSearchParams` building, and the raw `fetch` call.

**Acceptance criteria:**
- The hook contains no `as any` cast.
- The hook contains no string literal `"/api/expedition-list-archive/dates"`.
- The hook calls `client.expeditionListArchive_GetDates(page, pageSize)`.
- React Query `queryKey`, `staleTime`, and the `(page = 1, pageSize = 20)` defaults are preserved.
- Return type remains assignable to existing `GetExpeditionDatesResponse` consumers (callers do not need code changes).
- Loading the `ExpeditionListArchive` page in the running app fetches the same data as today and renders identically.

### FR-2: Replace `useExpeditionListsByDate` with typed client call
Rewrite `useExpeditionListsByDate(date)` to call `getAuthenticatedApiClient().expeditionListArchive_GetByDate(date)`.

**Acceptance criteria:**
- No `as any` cast in the hook.
- No manual `encodeURIComponent` of the date — the generated client handles URL encoding.
- `enabled: !!date` guard and `staleTime` preserved.
- Return type assignable to existing `GetExpeditionListsByDateResponse` consumers.
- Selecting a date in the UI loads the same items list as today.

### FR-3: Replace `useReprintExpeditionList` with typed client call
Rewrite the mutation to call `getAuthenticatedApiClient().expeditionListArchive_Reprint(new ReprintExpeditionListRequest({ blobPath }))`.

**Acceptance criteria:**
- No `as any` cast.
- The mutation imports `ReprintExpeditionListRequest` from `../generated/api-client` and instantiates it (NSwag-generated requests require class construction, not a plain literal — see `useArticles.ts` for canonical pattern).
- `onSuccess` query invalidation of `QUERY_KEYS.expeditionListArchive` is preserved.
- Existing error-message extraction behavior (rejecting with `errorMessage` from the response when present, else the HTTP status) is preserved — falling back to the generated client's standard `SwaggerException` handling is acceptable provided the user-visible toast message remains equivalent.
- Triggering "Reprint" on a list in the UI produces the same success/error UX as today.

### FR-4: Replace `useRunExpeditionListPrintFix` with typed client call
Rewrite the mutation to call `getAuthenticatedApiClient().expeditionList_RunFix()`.

**Acceptance criteria:**
- No `as any` cast.
- The mutation returns the typed `RunExpeditionListPrintFixResponse`.
- Existing error-handling behavior is preserved (see FR-3 caveat).
- The "Run print fix" action in the UI behaves identically to today.

### FR-5: Fix `getExpeditionListDownloadUrl` to use the public base-URL helper
`getExpeditionListDownloadUrl(blobPath)` constructs an authenticated download URL used as an `href` for downloads — it does not need a typed RPC method. Replace `(apiClient as any).baseUrl` with `getApiBaseUrl()` from `./client`.

**Acceptance criteria:**
- No `as any` cast.
- No reference to `getAuthenticatedApiClient()` (this helper builds a URL only — instantiating the client is unnecessary).
- `blobPath` is still per-segment URL-encoded (`blobPath.split("/").map(encodeURIComponent).join("/")`) so multi-segment paths like `2026/06/03/file.pdf` resolve correctly.
- The download link in the UI opens the same file as today.

### FR-6: Lint rule / guardrail (regression prevention)
Add an ESLint rule (or extend an existing one) to fail builds when source files in `frontend/src/` contain `(apiClient as any)` or any cast that reads `.http` / `.baseUrl` off the generated client. Scoped to `frontend/src/**/*.{ts,tsx}` excluding `frontend/src/api/generated/**`.

**Acceptance criteria:**
- A new lint rule (e.g. `no-restricted-syntax` matching `TSAsExpression > TSAnyKeyword` paired with an identifier matching `apiClient`, or a custom rule) flags any future reintroduction.
- `npm run lint` passes after the refactor.
- Manually reintroducing `(apiClient as any).http.fetch` in a sandbox file causes `npm run lint` to fail with a clear error message naming the violation and pointing to the sanctioned helpers.

## Non-Functional Requirements

### NFR-1: Behavior parity
No user-visible behavioral change. All five call sites must produce the same network requests (same HTTP method, path, query string, body, headers) and the same React Query cache keys before and after the change.

### NFR-2: Type safety
After the refactor, `tsc --noEmit` (run as part of `npm run build`) must succeed. The hook file must contain zero `as any` casts and zero references to `.http` or `.baseUrl` on the generated client.

### NFR-3: Test coverage
Existing unit/integration tests that touch this hook file must continue to pass. If no tests exist for these hooks today (the file appears uncovered based on naming conventions), add minimal RTL/React Query tests for at least `useExpeditionDates` and `useReprintExpeditionList` that mock the generated client and assert the typed methods are called with the expected arguments. Target: each refactored hook has ≥1 happy-path test.

### NFR-4: Bundle size
No measurable bundle-size increase. The refactor removes manual `fetch` code and reuses methods that are already in the generated client bundle, so net size should be neutral or slightly smaller.

### NFR-5: Auth interceptor coverage
After the change, all five endpoints must go through `getAuthenticatedApiClient()`'s `authenticatedHttp.fetch`, which means they get: auth-header injection, 401 redirect handling, global error-toast handling, and E2E test cookie credentials. Today, `(apiClient as any).http.fetch` already happens to use the same `authenticatedHttp` (because `getAuthenticatedApiClient()` passes it to `new ApiClient(...)`), so behavior should be equivalent — but this becomes guaranteed-by-type rather than guaranteed-by-coincidence.

## Data Model
No data-model changes. All DTOs already exist in both the backend (`backend/src/Anela.Heblo.Application/Features/ExpeditionList*`) and the generated TypeScript client.

The hook file's locally-declared interfaces (`ExpeditionListItemDto`, `GetExpeditionDatesResponse`, `GetExpeditionListsByDateResponse`, `ReprintExpeditionListRequest`, `ReprintExpeditionListResponse`) duplicate types that already exist in `frontend/src/api/generated/api-client.ts`. The refactor must:
- Keep the locally-declared interfaces **for now** (so consuming components compile unchanged), OR
- Re-export the generated types under the same names if structural compatibility holds.

Decision: re-export from the generated client where structurally identical; keep local types only if structural differences exist (e.g. `string` vs `Date`). The local DTOs were almost certainly hand-written because someone bypassed the generated client — they should drop in cleanly.

## API / Interface Design
No API surface changes. All five hook signatures stay identical so consumers (`ExpeditionListArchivePage` and similar) need zero changes:

```typescript
useExpeditionDates(page?: number, pageSize?: number): UseQueryResult<GetExpeditionDatesResponse>
useExpeditionListsByDate(date: string): UseQueryResult<GetExpeditionListsByDateResponse>
useReprintExpeditionList(): UseMutationResult<ReprintExpeditionListResponse, Error, ReprintExpeditionListRequest>
useRunExpeditionListPrintFix(): UseMutationResult<RunExpeditionListPrintFixResponse, Error, void>
getExpeditionListDownloadUrl(blobPath: string): string
```

## Dependencies
- `frontend/src/api/client.ts` — `getAuthenticatedApiClient`, `getApiBaseUrl`, `QUERY_KEYS` (all existing exports, no changes required).
- `frontend/src/api/generated/api-client.ts` — `ApiClient` typed methods + `ReprintExpeditionListRequest` (existing, regenerated on build).
- `@tanstack/react-query` — `useQuery`, `useMutation`, `useQueryClient` (unchanged).
- ESLint configuration in `frontend/` for FR-6.

No new packages, no backend changes, no OpenAPI changes.

## Out of Scope
- **OpenAPI/NSwag changes.** The brief suggested adding endpoints to the OpenAPI spec, but the endpoints are already generated. No backend or codegen work is required.
- **Making `baseUrl` a public property of `ApiClient`.** Not needed; `getApiBaseUrl()` already exposes it via the public helper exported from `client.ts`.
- **A new `apiFetch(relativeUrl, init)` typed wrapper.** The brief proposed this as a stopgap; with FR-1 through FR-5 the wrapper is unnecessary because no call site needs hand-rolled `fetch` anymore. `getAuthenticatedFetch()` already covers the escape-hatch case.
- **Refactoring other hooks.** This file is the only known offender per the arch-review.
- **Migrating download to streamed/typed `expeditionListArchive_Download`.** `getExpeditionListDownloadUrl` is consumed as an `href` for browser-initiated downloads, not a programmatic stream read. Converting it to a fetched blob would change UX (no native download progress, blob-URL lifecycle management) and is not required by the brief.
- **End-to-end Playwright coverage of the archive flow.** Not in this change; existing E2E coverage (if any) must continue to pass.

## Open Questions
None.

## Status: COMPLETE