# Specification: Replace `apiClient as any` Bypass in `useSubmitArticleFeedbackMutation` with Typed Helpers

## Summary
Eliminate the two `(apiClient as any)` casts in `useSubmitArticleFeedbackMutation` by introducing public, typed helpers (`getApiBaseUrl()` and `getAuthenticatedFetch()`) on the API client module, and switching the hook to use them. The 409 ("already submitted") branch remains expressed as a typed result, but without reaching into private fields of the NSwag-generated client.

## Background
`useSubmitArticleFeedbackMutation` in `frontend/src/api/hooks/useArticles.ts` cannot use the generated NSwag client's typed mutation for feedback submission because the client treats HTTP 409 as a thrown exception, whereas the UI needs to surface it as a typed result (`{ alreadySubmitted: true }`). The existing workaround uses raw `fetch` driven by the client's private `baseUrl` and `http` fields via two `as any` casts. This is brittle against NSwag regeneration (a template rename silently breaks at runtime), bypasses TypeScript, and violates the project rule that hooks must use the client's managed base URL through public API.

The arch-review routine flagged this on 2026-05-27 (TODO tag `2026-05-25`) without an accompanying GitHub issue. This spec captures the agreed remediation: keep the raw-fetch + 409-branch shape, but route it through typed, public helpers that survive client regeneration.

## Functional Requirements

### FR-1: Public `getApiBaseUrl` helper
Expose a typed function `getApiBaseUrl(): string` from `frontend/src/api/client.ts` that returns the same base URL the authenticated NSwag client is configured with, without reading private fields of the generated class.

**Acceptance criteria:**
- `getApiBaseUrl` is exported from `frontend/src/api/client.ts` with return type `string`.
- The returned URL matches the base URL used by `getAuthenticatedApiClient()` for every supported environment (local dev, staging, production).
- The implementation does not use `as any`, `// @ts-ignore`, or any other type-system escape.
- The function works without requiring the caller to first instantiate or authenticate the NSwag client.
- A unit test in `frontend/src/api/__tests__/client.test.ts` asserts the returned URL is a non-empty string and matches the expected configured value under test setup.

### FR-2: Public `getAuthenticatedFetch` helper
Expose a typed function `getAuthenticatedFetch(): (input: RequestInfo | URL, init?: RequestInit) => Promise<Response>` from `frontend/src/api/client.ts` that returns a fetch-compatible function which automatically attaches the same authentication headers (bearer token, etc.) the NSwag client would attach.

**Acceptance criteria:**
- `getAuthenticatedFetch` is exported from `frontend/src/api/client.ts` with a signature compatible with the global `fetch` function.
- The returned function attaches the same `Authorization` header (and any other auth-related headers) used by `getAuthenticatedApiClient()`.
- The caller-supplied `headers` in `init` are preserved and merged with auth headers; caller headers do not silently override auth headers.
- The function does not use `as any` or reach into private fields of the generated client.
- Unit tests assert: (a) the auth header is attached, (b) caller headers are preserved, (c) the response is returned unmodified (no error wrapping for non-2xx).

### FR-3: Refactor `useSubmitArticleFeedbackMutation` to use the helpers
The hook in `frontend/src/api/hooks/useArticles.ts` must construct its request using `getApiBaseUrl()` and `getAuthenticatedFetch()` and remove both `(apiClient as any)` casts and the `TODO(arch-review 2026-05-25)` comment.

**Acceptance criteria:**
- No `as any` cast remains in `useSubmitArticleFeedbackMutation`.
- The request body is typed as `SubmitArticleFeedbackRequest` (no hand-rolled object that drifts from the C# DTO).
- The URL is constructed as `` `${getApiBaseUrl()}/api/articles/${articleId}/feedback` ``.
- The `if (response.status === 409) return { alreadySubmitted: true }` branch is preserved.
- The non-2xx, non-409 path still throws an `Error` so React Query surfaces it as a mutation error.
- On 2xx, the response is parsed and mapped to `SubmitArticleFeedbackResult` with the same fields (`precisionScore`, `styleScore`, `feedbackComment`) as before.
- The `TODO(arch-review 2026-05-25)` comment is removed (the underlying issue is resolved).
- `onSuccess` continues to invalidate `articleKeys.detail(articleId)`.

### FR-4: Lint/test guard against future regressions
A repo-level test (or lint rule) prevents hooks under `frontend/src/api/hooks/` from using plain `fetch()` or `(apiClient as any)` patterns.

**Acceptance criteria:**
- A test under `frontend/src/api/__tests__/authenticated-api-usage.test.ts` (or equivalent) fails if any file in `frontend/src/api/hooks/` uses plain `fetch(` without going through `getAuthenticatedFetch()`.
- The same test fails if any hook file contains `(apiClient as any)` or `as any).http` or `as any).baseUrl`.
- The failure message points the developer to import `getApiBaseUrl` / `getAuthenticatedFetch` from `../client`.

### FR-5: Preserve existing behavior of the feedback flow
The user-observable behavior of submitting article feedback is unchanged.

**Acceptance criteria:**
- Submitting a valid feedback for the first time returns `{ precisionScore, styleScore, feedbackComment }` and the React Query cache for `articleKeys.detail(articleId)` is invalidated.
- Submitting feedback for an article that already has feedback returns `{ alreadySubmitted: true }` without throwing.
- Network errors and HTTP 5xx surface as React Query `error` state with a message including the status code.
- Auth headers are attached identically to before (no change in 401 behavior).

## Non-Functional Requirements

### NFR-1: Performance
Refactor must not introduce additional network round-trips or perceivable latency. `getApiBaseUrl()` and `getAuthenticatedFetch()` are synchronous, in-memory accessors; calling them per request is acceptable. No new caching layer is required.

### NFR-2: Security
Auth header handling must be identical to the current authenticated client — same token source, same header name and format, same refresh behavior. The helper must not log, expose, or persist the bearer token. The base URL is not a secret but must never be hard-coded to a production host.

### NFR-3: Type Safety
All call sites of the helpers must compile under `strict` TypeScript without `any`, `unknown` widening, or `@ts-ignore`. Body and response types in the feedback hook must reference generated DTOs (`SubmitArticleFeedbackRequest`, etc.) so NSwag regeneration triggers a compile error if shapes drift.

### NFR-4: Resilience to NSwag regeneration
The helpers must not depend on the internal field names (`baseUrl`, `http`) of the generated `AnelaHebloApiClient` class. If NSwag templates change and rename or remove those internal fields, the helpers must continue to function or fail to compile — never fail silently at runtime.

### NFR-5: Test Coverage
Net change in test coverage for `frontend/src/api/client.ts` and `frontend/src/api/hooks/useArticles.ts` must be non-negative. The 409, 2xx, and 5xx paths of `useSubmitArticleFeedbackMutation` must each have at least one explicit unit test using mocked `getAuthenticatedFetch`.

## Data Model
No persistent data model changes. The relevant in-memory types remain:

- `SubmitArticleFeedbackRequest` (generated DTO; class, not record) — `{ articleId, precisionScore, styleScore, comment }`
- `SubmitArticleFeedbackPayload` (hook-local) — caller's input minus `articleId`
- `SubmitArticleFeedbackResult` (hook-local) — discriminated by presence of `alreadySubmitted: true` vs. populated score fields

## API / Interface Design

### New exports from `frontend/src/api/client.ts`
```ts
export const getApiBaseUrl = (): string => /* ... */;

export function getAuthenticatedFetch(): (
  input: RequestInfo | URL,
  init?: RequestInit,
) => Promise<Response>;
```

### Updated hook contract (unchanged signature)
```ts
export const useSubmitArticleFeedbackMutation = (articleId: string) =>
  useMutation<SubmitArticleFeedbackResult, Error, SubmitArticleFeedbackPayload>(/* ... */);
```

### HTTP endpoint (unchanged)
`POST /api/articles/{articleId}/feedback`
- Request body: `SubmitArticleFeedbackRequest`
- 2xx: returns feedback DTO
- 409: feedback already submitted (typed as `{ alreadySubmitted: true }` by the hook)
- 4xx (other) / 5xx: thrown as `Error`

## Dependencies
- NSwag-generated client in `frontend/src/api/` (`AnelaHebloApiClient`, generated DTOs).
- `@tanstack/react-query` mutation/query infrastructure.
- Authentication module that supplies the bearer token consumed by the generated client and by `getAuthenticatedFetch`.
- Project rule documented in `CLAUDE.md` and `docs/development/api-client-generation.md` ("API hooks use absolute URLs").

## Out of Scope
- Configuring the NSwag template to return typed 409 results for any endpoint. (Captured as a longer-term option; not part of this change.)
- Refactoring other hooks that already use `getAuthenticatedApiClient()` correctly.
- Backend changes to the `POST /api/articles/{articleId}/feedback` endpoint.
- Changing the on-disk shape of `frontend/src/api/client.ts` beyond adding the two helpers.
- Migrating the feedback hook to a fully typed `articles_SubmitFeedback` mutation — explicitly deferred until NSwag templates support typed 409 handling.
- Filing or closing a GitHub tracking issue for the original arch-review finding (handled by the arch-review routine, not by this work).

## Open Questions
None.

## Status: COMPLETE