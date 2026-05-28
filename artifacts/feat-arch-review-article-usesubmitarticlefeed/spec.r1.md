# Specification: Remove Private API Client Access in `useSubmitArticleFeedbackMutation`

## Summary
The `useSubmitArticleFeedbackMutation` hook in `frontend/src/api/hooks/useArticles.ts` currently bypasses the NSwag-generated API client by reaching into its private `http` and `baseUrl` properties via two `as any` casts. This spec defines the work to eliminate the private-internals access while preserving the HTTP 409 ("already submitted") typed-result behavior that motivated the bypass.

## Background
The NSwag-generated `AnelaHebloApiClient` throws on non-2xx responses, so the original implementation could not distinguish a 409 "feedback already submitted" outcome from a transport/server error in a typed way. To work around this, the hook constructs the request URL from `(apiClient as any).baseUrl` and dispatches it via `(apiClient as any).http.fetch`, hand-rolling the request body shape.

This violates the project rule (`CLAUDE.md`) that hooks must use `${apiClient.baseUrl}${relativeUrl}` — the rule's intent is to consume the client's *managed* base URL via a public surface, not to read private state. A TODO tagged `arch-review 2026-05-25` acknowledges the fragility:

- **NSwag-regeneration fragility**: a template change to the HTTP abstraction or field rename silently breaks the cast at runtime, not at compile time.
- **No body-shape type safety**: the request body is hand-rolled and will drift from the C# `SubmitArticleFeedbackRequest` DTO without any tooling warning.
- **Untracked debt**: no GitHub issue exists for the TODO.

## Functional Requirements

### FR-1: Remove `as any` casts from feedback submission hook
The hook MUST construct the feedback submission request without `as any` casts against `apiClient`. Both private-property accesses (`apiClient.http`, `apiClient.baseUrl`) must be eliminated.

**Acceptance criteria:**
- No occurrence of `(apiClient as any)` in `useSubmitArticleFeedbackMutation`.
- No occurrence of `apiClient.http` (any access form) in the hook.
- TypeScript compilation succeeds with `--noImplicitAny` / strict settings already enforced by the project.
- The TODO comment block (`// TODO(arch-review 2026-05-25): …`) is removed once the fix lands.

### FR-2: Preserve typed HTTP 409 handling
The hook MUST continue to expose HTTP 409 ("feedback already submitted") as a typed mutation result, not as a thrown exception. Consumers that branch on the already-submitted case must not change.

**Acceptance criteria:**
- When the server returns 409 for `POST /api/articles/{articleId}/feedback`, the mutation resolves (does not reject) with a discriminated result indicating "already submitted".
- When the server returns 2xx, the mutation resolves with a "submitted" result.
- When the server returns any other non-2xx (4xx other than 409, 5xx, network failure), the mutation rejects with an error consistent with the rest of the hooks layer.
- Existing call sites of `useSubmitArticleFeedbackMutation` continue to compile and behave identically.

### FR-3: Use a public, typed accessor for the API base URL
The fix MUST source the base URL from a public, typed accessor — either (a) NSwag-exposed `baseUrl` on the generated client, or (b) a thin project-owned helper such as `getApiBaseUrl()` defined alongside `getAuthenticatedApiClient()`.

**Acceptance criteria:**
- The accessor returns `string` (not `any`).
- The accessor lives next to `getAuthenticatedApiClient()` (e.g. in the same module/file) so it shares lifecycle and auth-configuration assumptions.
- Other hooks in `frontend/src/api/hooks/**` that currently follow the `${apiClient.baseUrl}${relativeUrl}` pattern (per `CLAUDE.md`) are not regressed by the change. (No mass refactor is required; the new accessor is additive.)

### FR-4: Type the request body against the generated DTO
The request body sent to `POST /api/articles/{articleId}/feedback` MUST be typed against the NSwag-generated `SubmitArticleFeedbackRequest` (or the equivalent generated type), not a hand-rolled literal.

**Acceptance criteria:**
- The object passed to `JSON.stringify` (or the equivalent) has a TypeScript type derived from the generated client, such that renaming a field in the C# DTO produces a TypeScript compile error in this hook after NSwag regeneration.
- No field names are duplicated as string literals where the generated type would suffice.

### FR-5: Use the application's standard authentication header path
The replacement request MUST send the same auth headers the generated client would send for an authenticated call, sourced through the project's existing auth-header mechanism — not duplicated inline.

**Acceptance criteria:**
- The replacement uses the same mechanism that produces the bearer/MSAL token for other authenticated hook calls (e.g. via the shared MSAL/auth helper already used by `getAuthenticatedApiClient`).
- A reviewer reading the diff can identify the shared helper; the hook does not directly call `msalInstance.acquireTokenSilent` (or equivalent) if a shared helper exists.

### FR-6: File a tracking issue for the long-term NSwag fix
A GitHub issue MUST be filed describing the longer-term fix: configuring the NSwag template (or the endpoint) so that 409 is returned as a typed result and the raw `fetch` becomes unnecessary entirely.

**Acceptance criteria:**
- Issue exists in the project's GitHub repo, linked from the PR that closes this spec's work.
- Issue references this endpoint (`POST /api/articles/{articleId}/feedback`) and the `arch-review 2026-05-25` provenance.

## Non-Functional Requirements

### NFR-1: Performance
No measurable change. The request remains a single HTTP POST; only its construction path changes. No new round-trips or token acquisitions are introduced.

### NFR-2: Security
Auth header handling MUST be equivalent to the generated client's path. The fix MUST NOT log tokens, MUST NOT widen the set of endpoints called without auth, and MUST NOT introduce a code path that sends the bearer token to an origin other than the configured API base URL.

### NFR-3: Maintainability
After the change, an NSwag regeneration that renames `http` or `baseUrl` on the generated client MUST NOT break the feedback submission flow at runtime. Any breakage MUST surface at TypeScript compile time.

### NFR-4: Test coverage
The hook MUST retain at least the existing level of test coverage. Tests MUST cover: (a) 2xx → submitted result, (b) 409 → already-submitted result, (c) other errors → rejected mutation.

## Data Model

No persistence changes. The only typed entity touched is the existing generated `SubmitArticleFeedbackRequest` DTO (path: `frontend/src/api/generated/**`, exact path inherited from NSwag output). The hook continues to send `{ articleId, …feedback fields per the generated DTO }` to the same endpoint.

## API / Interface Design

### Endpoint (unchanged)
- `POST /api/articles/{articleId}/feedback`
- Request body: `SubmitArticleFeedbackRequest` (generated)
- Responses:
  - `2xx` — feedback recorded
  - `409` — feedback already submitted for this user/article
  - other — error

### Hook interface (unchanged externally)
`useSubmitArticleFeedbackMutation` continues to return a mutation whose `mutateAsync`/`mutate` accepts `{ articleId, …feedback }` and resolves to a discriminated result of the form:

```ts
type SubmitArticleFeedbackResult =
  | { kind: 'submitted' }
  | { kind: 'already-submitted' };
```

(or whatever shape the current implementation already exposes — the goal is *no change* to consumer-visible behavior).

### New internal accessor (recommended Option B from brief)
A new helper `getApiBaseUrl(): string` is added in the module that already owns `getAuthenticatedApiClient()`. Implementation reads the same configured base URL the client uses, with a `string` return type, no `any`.

### Replacement request shape (informative)
```ts
const baseUrl = getApiBaseUrl();
const url = `${baseUrl}/api/articles/${articleId}/feedback`;
const body: SubmitArticleFeedbackRequest = { /* typed fields */ };

const response = await fetch(url, {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    ...(await getAuthHeaders()), // shared helper, same source as the generated client
  },
  body: JSON.stringify(body),
});

if (response.status === 409) return { kind: 'already-submitted' };
if (!response.ok) throw await buildApiError(response);
return { kind: 'submitted' };
```

## Dependencies
- Existing NSwag-generated client and its `baseUrl` configuration mechanism.
- Existing authentication helper used by `getAuthenticatedApiClient()` (MSAL or equivalent).
- React Query (or the project's mutation library) — unchanged.
- No new npm dependencies.

## Out of Scope
- Reworking the NSwag template to make 409 a typed result for this or any endpoint (tracked in FR-6 issue).
- Refactoring other hooks that already use `${apiClient.baseUrl}${relativeUrl}` correctly.
- Changing the server-side `POST /api/articles/{articleId}/feedback` contract.
- Migrating the project off NSwag or to a different code generator.
- Adding new feedback-submission features (edit, delete, list).

## Open Questions
None.

## Status: COMPLETE