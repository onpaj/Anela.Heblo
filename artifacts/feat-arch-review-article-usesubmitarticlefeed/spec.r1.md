The brief is self-contained — the worktree only holds artifacts, not source. Producing the spec from the brief.

# Specification: Remove `as any` bypass in `useSubmitArticleFeedbackMutation`

## Summary
The `useSubmitArticleFeedbackMutation` hook in `frontend/src/api/hooks/useArticles.ts` reaches into the NSwag-generated API client's private `http` and `baseUrl` properties via `as any` casts to issue a raw `fetch` that can branch on HTTP 409. This spec replaces the private-field access with a typed, public mechanism while preserving the 409 (already-submitted) detection that motivated the bypass.

## Background
The arch-review routine flagged a TODO (dated 2026-05-25) on lines 218–220 of `useArticles.ts`. The hook submits article feedback and must distinguish HTTP 409 ("already submitted") from other errors as a typed result, but the NSwag-generated `AnelaHebloApiClient` raises 409s as exceptions and exposes no typed-result shape for that status. The author worked around this by:

1. Reading `(apiClient as any).baseUrl` to build the URL.
2. Calling `(apiClient as any).http.fetch(...)` to reuse the client's HTTP transport (and its authentication wiring).
3. Branching on `response.status === 409` to return a typed `{ alreadySubmitted: true }`-style result instead of throwing.

This violates:
- The project rule (`CLAUDE.md`) that hooks construct URLs using `${apiClient.baseUrl}${relativeUrl}` via the **public** accessor.
- TypeScript type safety: the `as any` masks drift between the hand-rolled body shape and the C# `SubmitArticleFeedbackRequest` DTO and silently breaks if NSwag regeneration renames or restructures `baseUrl`/`http`.

The bypass must go, but the 409-as-typed-result behaviour must remain.

## Functional Requirements

### FR-1: Eliminate `as any` casts in `useSubmitArticleFeedbackMutation`
Remove both `(apiClient as any).baseUrl` and `(apiClient as any).http.fetch` from `useArticles.ts`. After this change, `useArticles.ts` must contain zero occurrences of `as any` referencing `apiClient` internals.

**Acceptance criteria:**
- `grep -n "apiClient as any" frontend/src/api/hooks/useArticles.ts` returns no matches.
- The TODO comment block (lines 218–220 at the time of brief filing) is removed.
- TypeScript compiles cleanly (`tsc --noEmit`) with no new errors and no new `@ts-ignore`/`@ts-expect-error` pragmas.

### FR-2: Public, typed access to the API base URL
Introduce a public, typed accessor for the API base URL that hooks can use to construct absolute URLs when they need a raw `fetch`. The accessor must not require modifications to the auto-generated NSwag client file (which is regenerated and would lose hand edits), and must not rely on `as any`.

**Acceptance criteria:**
- A new helper `getApiBaseUrl(): string` is exported from the same module that exports `getAuthenticatedApiClient` (today: `frontend/src/api/client.ts` or equivalent — confirm exact location during implementation).
- `getApiBaseUrl()` returns the same string that `apiClient.baseUrl` resolves to today (i.e. the configured backend base URL, no trailing slash normalisation regressions).
- The helper has a non-`any` return type (`string`).
- If `baseUrl` is not exposed publicly on the generated client, the helper resolves the base URL from the same configuration source the client uses (e.g. the environment variable / runtime config consumed by `getAuthenticatedApiClient`) rather than reading a private field.

### FR-3: Preserve authenticated transport for the raw `fetch` call
Today the bypass uses `apiClient.http.fetch` which inherits the authentication interceptor/headers wired into the generated client. The replacement must continue to send the same auth headers (bearer token, tenant headers, anti-forgery, etc.) that the generated client attaches to other requests. A plain `fetch` without auth headers is not acceptable.

**Acceptance criteria:**
- The `POST /api/articles/{articleId}/feedback` request issued by the refactored hook carries the same `Authorization` / auth-related headers as a request issued via a generated client method (verifiable by network-tab inspection or a mocked-fetch unit test that asserts headers).
- The auth header source is the same singleton/provider used elsewhere in the app (no duplicate token plumbing introduced).
- If the existing auth wiring is exposed only through `apiClient.http`, expose a public helper (e.g. `getAuthenticatedFetch(): typeof fetch`) that returns an auth-augmenting fetch function, and use it in the hook.

### FR-4: Typed request body matching the server DTO
The request body sent to `POST /api/articles/{articleId}/feedback` must be typed against the NSwag-generated `SubmitArticleFeedbackRequest` (or equivalent) type, not a hand-rolled inline object literal typed as `any`.

**Acceptance criteria:**
- The body object is declared with an explicit type imported from the generated client module (e.g. `SubmitArticleFeedbackRequest`).
- Renaming a field on the C# DTO and regenerating the client produces a compile-time TypeScript error at the hook call site.

### FR-5: Preserve the 409 "already submitted" typed-result behaviour
The hook's existing contract — that an HTTP 409 response resolves to a typed success-shaped result (e.g. `{ alreadySubmitted: true }`) rather than throwing — must remain unchanged from the caller's perspective. Non-409 non-2xx responses must continue to throw or otherwise surface as errors in the same way they do today.

**Acceptance criteria:**
- A unit test (mocking `fetch`) asserts that a 409 response resolves the mutation with the existing typed "already submitted" result shape, and that the mutation's `onSuccess` callback fires (not `onError`).
- A unit test asserts that a 500 response rejects/throws and the mutation's `onError` callback fires.
- A unit test asserts that a 2xx response resolves with the success shape.
- No caller of `useSubmitArticleFeedbackMutation` requires changes.

### FR-6: File a follow-up tracking the long-term NSwag improvement
A GitHub issue is filed (or the existing arch-review issue updated) to track the long-term fix: configuring the NSwag template / endpoint annotations so that 409 on `POST /api/articles/{articleId}/feedback` is returned as a typed alternative result by the generated client, eliminating the need for a raw `fetch` entirely.

**Acceptance criteria:**
- A GitHub issue exists referencing this spec and the 2026-05-25 arch-review tag.
- The issue is labelled with the project's tech-debt / arch-review label convention.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change. The request continues to be a single `POST` to the same endpoint with the same body. Replacement of `apiClient.http.fetch` with a thin authenticated-fetch helper must not introduce extra round trips, retries, or serialization overhead.

### NFR-2: Security
- The replacement must transmit the same authentication credentials as the current implementation (FR-3). A regression that strips auth headers and turns the endpoint into an effectively-anonymous call is unacceptable.
- No secrets, tokens, or auth headers may be logged by the new helper.

### NFR-3: Maintainability / Regeneration safety
The hook must survive an NSwag regeneration that:
- Renames the private `baseUrl` field on the generated client.
- Replaces the `http` field with a different transport abstraction.
- Reshapes the generated client class hierarchy.

After regeneration with any of the above changes, the hook either continues to compile and work, or fails at **compile time** with a clear TypeScript error — never silently at runtime.

### NFR-4: Test coverage
The hook must have direct unit-test coverage for the three response branches (2xx, 409, other non-2xx) — see FR-5. If such tests existed prior to the refactor, they are preserved and continue to pass; if not, they are added.

## Data Model
No persisted-data changes. The in-flight request body is the existing `SubmitArticleFeedbackRequest` DTO:

```
SubmitArticleFeedbackRequest {
  // shape defined by the C# DTO, consumed via NSwag-generated TypeScript type
  // (exact fields out of scope here — implementation must import the generated type, not redeclare it)
}
```

The hook's resolved-value shape (success and "already submitted" branches) is unchanged from current behaviour.

## API / Interface Design

### Frontend module changes
- **`frontend/src/api/client.ts`** (or wherever `getAuthenticatedApiClient` lives):
  - Add `export function getApiBaseUrl(): string` (FR-2).
  - If needed for FR-3, add `export function getAuthenticatedFetch(): (input: RequestInfo, init?: RequestInit) => Promise<Response>` that returns a fetch wrapper attaching the standard auth headers used by the rest of the app.
- **`frontend/src/api/hooks/useArticles.ts`** — `useSubmitArticleFeedbackMutation`:
  - Replace `(apiClient as any).baseUrl` with `getApiBaseUrl()`.
  - Replace `(apiClient as any).http.fetch(...)` with `getAuthenticatedFetch()(...)` (or equivalent — see Open Questions).
  - Type the request body as `SubmitArticleFeedbackRequest` imported from the generated client module.
  - Keep the `if (response.status === 409)` branch verbatim in behaviour (typed result, not throw).
  - Remove the TODO comment block.

### Backend
No backend changes in scope. The endpoint contract (`POST /api/articles/{articleId}/feedback`, 2xx on success, 409 on duplicate) is preserved.

### NSwag configuration
No NSwag template changes in scope for this fix (tracked separately under FR-6).

## Dependencies
- The NSwag-generated `AnelaHebloApiClient` and its associated TypeScript types (`SubmitArticleFeedbackRequest`).
- The application's existing auth-header provider (the same one consumed by `getAuthenticatedApiClient`).
- React Query (the hook is a `useMutation`).
- Existing test infrastructure for hook unit tests (e.g. `@testing-library/react`, MSW or `vi.spyOn(global, 'fetch')`).

## Out of Scope
- Changing the NSwag template to return 409 as a typed alternative result on this endpoint — tracked separately (FR-6).
- Refactoring other hooks in `useArticles.ts` that may use the same pattern (none flagged by the brief, but the audit is out of scope).
- Backend changes to the feedback endpoint.
- Changes to the caller contract of `useSubmitArticleFeedbackMutation`.
- Migrating away from React Query or restructuring the hook surface.

## Open Questions
None.

## Status: COMPLETE