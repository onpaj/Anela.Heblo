# Architecture Review: Remove Private API Client Access in `useSubmitArticleFeedbackMutation`

## Skip Design: true

Backend-adjacent refactor of a single hook. No UI components or visual changes.

## Architectural Fit Assessment

The spec correctly identifies a real defect (private-internals access via `as any`), but it under-explores the existing generated client and reaches for the wrong fix.

Three findings from active code exploration that should change the proposal:

1. **A typed, public path through the generated client already exists.** `apiClient.articles_SubmitFeedback(id, request)` is generated and public; it returns `Promise<SubmitArticleFeedbackResponse>` with `articleId`, `precisionScore`, `styleScore`, `comment` already typed (`frontend/src/api/generated/api-client.ts:560`, `:13480`). Both request and response DTOs are exported from the same module.
2. **409 is already a typed exception**, contrary to the spec's premise. The generated `processArticles_SubmitFeedback` throws via `throwException` for any non-2xx (`frontend/src/api/generated/api-client.ts:583-598`). `SwaggerException` is **publicly exported** with a `status` field and a static `isSwaggerException` guard (`:37959-37983`). So 409 already arrives as `err.status === 409` on a typed `SwaggerException` — no raw `fetch` is required to detect it.
3. **The current `(apiClient as any).baseUrl / .http` pattern is project-sanctioned**, not an isolated lapse. It is documented in `docs/development/api-client-generation.md` (the "CRITICAL: URL Construction Rules" section) and re-asserted in `memory/gotchas/api-client-must-use-absolute-urls.md`. It is used by **34 hook files** with hundreds of occurrences. The exact 409-with-private-fetch pattern is repeated verbatim in `useKnowledgeBase.useSubmitFeedbackMutation` (`useKnowledgeBase.ts:387-410`) and `useLeaflet` (`useLeaflet.ts:286-315`). The TODO in `useArticles.ts` is not an isolated debt — it is the visible tip of a project-wide convention that the docs explicitly endorse.

Consequence: the spec's "additive `getApiBaseUrl()` + native `fetch` + new shared `getAuthHeaders()`" approach is more code than needed, ships a worse outcome on cross-cutting behavior (see Decision 2 below), and leaves the broader convention untouched while introducing a parallel one.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────────────────────────────────────┐
│  ArticleFeedbackSection (consumer — unchanged)                     │
└──────────────────────────────┬─────────────────────────────────────┘
                               │ mutate(payload)
                               ▼
┌────────────────────────────────────────────────────────────────────┐
│  useSubmitArticleFeedbackMutation (the only file changed)          │
│    ├── new SubmitArticleFeedbackRequest({...})   ← typed body      │
│    ├── apiClient.articles_SubmitFeedback(id, req) ← typed call     │
│    └── catch (SwaggerException w/ status===409)  ← typed 409       │
└──────────────────────────────┬─────────────────────────────────────┘
                               │
                               ▼
┌────────────────────────────────────────────────────────────────────┐
│  getAuthenticatedApiClient(false)  ← suppress global toast only    │
│    └── authenticatedHttp.fetch  ← unchanged; keeps 401 redirect,   │
│                                   E2E header, credentials, MSAL    │
└──────────────────────────────┬─────────────────────────────────────┘
                               ▼
                       POST /api/Articles/{id}/feedback
```

### Key Design Decisions

#### Decision 1: Use the generated typed method instead of a raw `fetch`

**Options considered:**
- (A) Spec's Option B — add `getApiBaseUrl()`, call native `fetch`, type the body against the generated DTO, share an auth-header helper.
- (B) Call the generated `articles_SubmitFeedback(id, request)` and catch `SwaggerException` with `status === 409`.
- (C) Change NSwag template to return a discriminated 409 result for this endpoint.

**Chosen approach:** B.

**Rationale:** All four spec FRs that touch implementation (FR-1, FR-2, FR-4, FR-5) are satisfied by B with strictly less code than A:
- FR-1 (no `as any`): zero `as any` — generated method is public.
- FR-2 (typed 409): `SwaggerException.isSwaggerException(err) && err.status === 409`, publicly typed.
- FR-4 (typed body): pass `new SubmitArticleFeedbackRequest({...})` — already generated.
- FR-5 (shared auth path): unchanged — calls go through `authenticatedHttp.fetch` exactly like every other hook.

FR-3 (the `getApiBaseUrl()` accessor) becomes unnecessary and should be dropped — no base URL is hand-assembled. C is the right long-term move but is out of scope per FR-6.

The spec author's TODO note ("revisit when generated client exposes typed-mutation 409 handling") appears to have missed that this capability is already present.

#### Decision 2: Suppress the global error toast for the expected 409 by passing `showErrorToasts: false`

**Options considered:**
- (A) Leave global toasts on — user sees "Chyba API (409)" toast plus the component's local "already submitted" state.
- (B) Pass `getAuthenticatedApiClient(false)` for this mutation only.
- (C) Add an `expectedStatuses?: number[]` option to `getAuthenticatedApiClient` / `authenticatedHttp.fetch`.

**Chosen approach:** B.

**Rationale:** The current implementation's use of `(apiClient as any).http.fetch` already routes through `authenticatedHttp.fetch`, which fires a global "Chyba API (409)" toast before the hook can branch on `response.status` (`client.ts`, `shouldHandleHttpErrors` block). The current code therefore has a latent toast-on-409 bug regardless of the refactor. The consumer (`ArticleFeedbackSection.tsx`) already renders its own error text ("Odeslání selhalo. Zkuste to znovu.") on `isError`, so global toasts add nothing for this mutation. B is one argument change and matches what consumers expect. C is a cleaner long-term API but is over-scope; defer to FR-6's follow-up.

#### Decision 3: Scope strictly to `useArticles.ts`; do not refactor sibling hooks

**Options considered:**
- (A) Fix only `useSubmitArticleFeedbackMutation`.
- (B) Apply the same pattern to `useKnowledgeBase.useSubmitFeedbackMutation` and `useLeaflet`'s feedback submit.

**Chosen approach:** A, with a follow-up issue (see Specification Amendments).

**Rationale:** The spec explicitly scopes out a mass refactor. Doing A keeps the diff surgical, proves the pattern in one place, and gives a reviewable template for the follow-up that converts the two siblings.

#### Decision 4: Leave `docs/development/api-client-generation.md` and `memory/gotchas/api-client-must-use-absolute-urls.md` for the follow-up

**Rationale:** Those docs currently teach `(apiClient as any).baseUrl` / `.http.fetch` as the *recommended* pattern. Updating them is the right thing to do, but it belongs with the broader rollout (Decision 3's follow-up), not with this single-hook fix — otherwise we'd ship docs that contradict 34 still-extant hooks.

## Implementation Guidance

### Directory / Module Structure

- **Modify only:** `frontend/src/api/hooks/useArticles.ts` — `useSubmitArticleFeedbackMutation` body and the imports at the top.
- **Modify only:** `frontend/src/api/hooks/__tests__/useArticles.test.ts` — add the three submission cases (NFR-4).
- **Do not add:** `getApiBaseUrl()`, `getAuthHeaders()`, or any new helper in `client.ts`. They are not needed under Decision 1.
- **Do not modify:** the generated client, sibling hooks, docs, or memory files.

### Interfaces and Contracts

Public surfaces touched (all already exported from `frontend/src/api/generated/api-client.ts`):

- `ApiClient.articles_SubmitFeedback(id: string, request: SubmitArticleFeedbackRequest): Promise<SubmitArticleFeedbackResponse>`
- `class SubmitArticleFeedbackRequest { articleId?, precisionScore?, styleScore?, comment? }`
- `class SubmitArticleFeedbackResponse { precisionScore?, styleScore?, feedbackComment? }`
- `class SwaggerException { status: number; static isSwaggerException(obj): obj is SwaggerException }`

External hook signature stays identical:
```ts
useSubmitArticleFeedbackMutation(articleId: string)
  → UseMutationResult<SubmitArticleFeedbackResult, unknown, SubmitArticleFeedbackPayload>
```
`SubmitArticleFeedbackResult` (`{ alreadySubmitted?, precisionScore?, styleScore?, feedbackComment? }`) is unchanged. Consumers in `ArticleFeedbackSection.tsx` require no edits.

### Data Flow

1. Component calls `submitFeedback.mutate(payload)`.
2. Hook obtains `getAuthenticatedApiClient(false)` (toasts suppressed for this mutation only).
3. Hook constructs `new SubmitArticleFeedbackRequest({ articleId, precisionScore, styleScore, comment })`.
4. Hook awaits `apiClient.articles_SubmitFeedback(articleId, request)`.
   - 2xx → resolves to `SubmitArticleFeedbackResponse`; hook maps to `{ precisionScore, styleScore, feedbackComment }`.
   - 409 → `authenticatedHttp.fetch` returns the `Response`; the generated `processArticles_SubmitFeedback` throws `SwaggerException` with `status === 409`. Hook catches, returns `{ alreadySubmitted: true }`.
   - Other non-2xx → re-thrown `SwaggerException`; mutation rejects; React Query surfaces `isError`.
   - 401 → still triggers `globalAuthRedirectHandler` inside `authenticatedHttp.fetch` before the throw (behavior preserved).
5. `onSuccess` invalidates `articleKeys.detail(articleId)` (unchanged).

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `SwaggerException` shape changes in a future NSwag template upgrade. | Low | Use the exported `SwaggerException.isSwaggerException` guard, not `instanceof`. Any rename surfaces at TypeScript compile time (satisfies NFR-3). |
| Suppressing the global toast hides a real server error to the user. | Low | Consumer already renders local error text on `isError`. Global toast was only ever firing because the original implementation routed 409 through the generic error path — that was the latent bug, not a feature. |
| Reviewers may flag deviation from the documented `(apiClient as any).baseUrl` pattern. | Medium | Call this out explicitly in the PR description: this fix replaces a documented-but-fragile pattern with a typed equivalent for one hook; doc + sibling-hook rollout is tracked separately (see follow-up issue). |
| Test mocks of `getAuthenticatedApiClient` need to mock `articles_SubmitFeedback` and also be able to simulate a thrown `SwaggerException` with a given status. | Low | Existing test file (`__tests__/useArticles.test.ts`) already mocks `getAuthenticatedApiClient` to return `{ articles_FeedbackList: jest.fn() }`. Same pattern; import `SwaggerException` from the generated module and throw an instance to simulate non-2xx. |
| `SubmitArticleFeedbackRequest.articleId` is also passed positionally — duplicated as both URL param and body field. | Low (existing behavior) | Preserve current behavior: pass `articleId` both as the path arg to `articles_SubmitFeedback(articleId, …)` and on the request DTO. If the backend ignores the body field, removing it is a server-contract change and out of scope. |

## Specification Amendments

1. **Drop FR-3.** No `getApiBaseUrl()` accessor is needed. The fix uses `apiClient.articles_SubmitFeedback(...)` directly, which internally uses the (private) baseUrl via the generated code's own path. The spec's "public, typed accessor for the base URL" requirement is satisfied by *not assembling the URL at all*.
2. **Reframe FR-5.** "Use the standard auth-header path" is automatically satisfied because the call goes through `authenticatedHttp.fetch` exactly like all other hooks. No new `getAuthHeaders()` helper. Update the FR-5 acceptance criteria accordingly: a reviewer can verify the auth path is unchanged because there is no new auth-related code at all.
3. **Replace the informative "Replacement request shape" snippet in the spec** with:
   ```ts
   const apiClient = getAuthenticatedApiClient(false); // suppress global toast for expected 409
   const request = new SubmitArticleFeedbackRequest({
     articleId, precisionScore: payload.precisionScore,
     styleScore: payload.styleScore, comment: payload.comment,
   });
   try {
     const r = await apiClient.articles_SubmitFeedback(articleId, request);
     return {
       precisionScore: r.precisionScore ?? null,
       styleScore: r.styleScore ?? null,
       feedbackComment: r.feedbackComment ?? null,
     };
   } catch (err) {
     if (SwaggerException.isSwaggerException(err) && err.status === 409) {
       return { alreadySubmitted: true };
     }
     throw err;
   }
   ```
4. **Tighten NFR-4.** Required tests: (a) 2xx → mapped result with score/comment passthrough; (b) `SwaggerException` with `status === 409` → `{ alreadySubmitted: true }`; (c) `SwaggerException` with `status === 500` (or network error) → rejected mutation, `isError === true`.
5. **Expand FR-6's tracking issue.** It should cover three concrete items, not just the NSwag template change:
   - (i) Convert `useKnowledgeBase.useSubmitFeedbackMutation` and the `useLeaflet` feedback submitter to the same `try/catch SwaggerException.status === 409` pattern.
   - (ii) Update `docs/development/api-client-generation.md` and `memory/gotchas/api-client-must-use-absolute-urls.md` to recommend using generated methods + `SwaggerException` for status-based branching, and to discourage `(apiClient as any).baseUrl` for new code.
   - (iii) Add a typed escape hatch to `getAuthenticatedApiClient` (e.g. `expectedStatuses?: number[]`) so individual mutations can opt out of the global toast for known business-conflict codes without disabling toasts wholesale.

## Prerequisites

None. Required types (`SubmitArticleFeedbackRequest`, `SubmitArticleFeedbackResponse`, `SwaggerException`) and the typed method (`articles_SubmitFeedback`) are already present in the current generated client. No migration, no config change, no infra change. No new npm dependency.