# Specification: Surface HTTP 409 as a Typed Branch on the Generated `articles_SubmitFeedback` Client

## Summary
Eliminate the `getAuthenticatedFetch()` raw-fetch fallback inside `useSubmitArticleFeedbackMutation` by making the NSwag-generated TypeScript client method `articles_SubmitFeedback(...)` return HTTP 409 ("feedback already submitted") as a typed, non-throwing branch alongside the existing 2xx success branch. Once surfaced, the hook switches back to a direct typed call into the generated client; the `getApiBaseUrl()` / `getAuthenticatedFetch()` helpers remain available for future endpoints with similar branching needs (e.g. 412 precondition-failed).

## Background

### Current state
The `POST /api/articles/{id}/feedback` endpoint (`backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs:72-81`) handles three failure paths through MediatR + `BaseApiController.HandleResponse<T>`:

- `ErrorCodes.ArticleNotFound` → HTTP 404
- `ErrorCodes.Forbidden` → HTTP 403
- `ErrorCodes.ArticleNotGenerated` → HTTP 422
- `ErrorCodes.ArticleFeedbackAlreadySubmitted` → **HTTP 409** (via `[HttpStatusCode(HttpStatusCode.Conflict)]` on the enum at `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:262`)

For the 409 path the response body is the same `SubmitArticleFeedbackResponse` DTO that the 200 path returns — a `BaseResponse` carrying `success: false`, `errorCode`, and `params`. The 409 path is **not exceptional** in the business sense: it means the user has already given feedback on this article, and the UI must render the previously-stored scores rather than show an error toast.

The action currently declares **no `[ProducesResponseType]` attributes**, so the NSwag-generated OpenAPI doc only sees the inferred 200 response. Consequently the generated `articles_SubmitFeedback(...)` method in `frontend/src/api/generated/api-client.ts:560-599` treats 200 as success and routes every other status (including 409) through `throwException(...)`.

The previous arch-review (2026-05-25, `feat-arch-review-article-frontend-hooks-bypas`) refactored three sibling hooks onto the typed generated methods, but explicitly excluded `useSubmitArticleFeedbackMutation` from that refactor because the 409 branch could not be expressed through the throwing generated method. In place of the prior `apiClient as any` bypass it introduced two helpers in `frontend/src/api/client.ts`:

- `getApiBaseUrl()` — returns the runtime API base URL.
- `getAuthenticatedFetch()` — returns a fetch wrapper that attaches the same auth headers as `getAuthenticatedApiClient()`, **does not** trigger global error toasts, **does not** trigger the 401 login redirect, and **does not** throw on non-2xx responses, leaving status-code branching to the caller.

The current hook (`frontend/src/api/hooks/useArticles.ts:218-255`) uses these helpers to perform a raw POST, mapping `response.status === 409` to `{ alreadySubmitted: true }`. The mutation returns a `SubmitArticleFeedbackResult` union (`{ alreadySubmitted: true } | { precisionScore, styleScore, feedbackComment }`).

### Why change it
The raw-fetch implementation works but reintroduces three fragilities the arch-review tried to remove:

1. **Hand-rolled URL composition.** The hook hardcodes `${getApiBaseUrl()}/api/articles/${articleId}/feedback`, duplicating the route already declared on the controller. A backend route rename would silently break the hook with no TypeScript signal.
2. **Hand-rolled request shape.** The hook serializes a request body constructed from `ISubmitArticleFeedbackRequest` and Content-Type/Accept headers manually. The generated client method already does both, correctly, and would track future backend changes automatically.
3. **No type safety on the 200 body.** The 2xx branch does `await response.json()` then reads `data.precisionScore`, `data.styleScore`, `data.feedbackComment` from an `any` payload. The typed `SubmitArticleFeedbackResponse` is bypassed.

The proper long-term fix is to surface 409 in the OpenAPI contract AND change the generated client method so that 409 returns a typed value instead of throwing. The hook can then issue a single typed call into `apiClient.articles_SubmitFeedback(...)` and branch on the discriminator carried by the typed response.

### Scope decision: the helpers stay
The brief is explicit: even after this hook is migrated, `getApiBaseUrl()` and `getAuthenticatedFetch()` remain in `frontend/src/api/client.ts` for future endpoints that need similar status-code branching (e.g. a future `If-Match`-based update returning HTTP 412). They are not deprecated, not deleted, not marked unused. Their JSDoc must be updated to reflect that the reference call site is no longer this hook.

### Audit follow-up
The brief asks for a grep audit of `apiClient as any` across `frontend/src/api/hooks/` to identify any other hooks still using the old bypass pattern. This audit is in scope; the spec defines a triage outcome per hook (refactor here, refactor elsewhere, or document as accepted technical debt). Refactor of audited hooks beyond `useSubmitArticleFeedbackMutation` itself is out of scope — they become their own follow-up issues.

## Functional Requirements

### FR-1: Declare HTTP 409 (and 200) in the OpenAPI contract for `POST /api/articles/{id}/feedback`
Annotate the `SubmitFeedback` action on `ArticlesController` so the OpenAPI document accurately advertises both the 200 success response and the 409 "already submitted" response, both carrying the `SubmitArticleFeedbackResponse` body.

**Acceptance criteria:**
- `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs:72-81` declares (in order, immediately above the existing `[HttpPost("{id:guid}/feedback")]` attribute):
  ```csharp
  [ProducesResponseType(typeof(SubmitArticleFeedbackResponse), StatusCodes.Status200OK)]
  [ProducesResponseType(typeof(SubmitArticleFeedbackResponse), StatusCodes.Status409Conflict)]
  ```
- No other `[ProducesResponseType]` attributes are added on this action in this change (404/403/422 paths are not in scope; adding them is a separate concern and may distract from the discriminated-union refactor).
- The action signature, MediatR dispatch, and `HandleResponse(...)` call are not modified.
- `dotnet build` succeeds; running NSwag regeneration produces a generated OpenAPI document that lists both `"200"` and `"409"` responses for `POST /api/Articles/{id}/feedback`, each referencing `SubmitArticleFeedbackResponse`.

### FR-2: Generated TypeScript client returns 409 as a typed, non-throwing branch
The generated `articles_SubmitFeedback(id, request)` method MUST return a typed value on HTTP 409 (parsing the body as `SubmitArticleFeedbackResponse`) rather than calling `throwException(...)`. The 200 branch MUST continue to return the parsed `SubmitArticleFeedbackResponse`. All other non-2xx statuses MUST continue to throw a `SwaggerException`.

The return type of the method MUST express the two-branch outcome in a way that lets callers discriminate **at the type level**, not by inspecting properties of an untyped object.

**Recommended approach:** Introduce a minimal NSwag template customization (`templateDirectory` in `backend/src/Anela.Heblo.API/nswag.frontend.json`) that, for any operation declaring a 4xx `ProducesResponseType` whose body type matches the 2xx body type, generates the matching `else if (status === <4xx>)` branch as a non-throwing branch returning the parsed body, and updates the operation's return type accordingly. The template change MUST be additive — operations that do not declare any matching 4xx (the vast majority of the codebase) MUST continue to generate exactly the same code as today.

**Acceptance criteria:**
- After running the NSwag code generator, `frontend/src/api/generated/api-client.ts` contains an `articles_SubmitFeedback(...)` method whose `processArticles_SubmitFeedback(response)` body includes a `status === 409` branch that:
  - parses the body via `SubmitArticleFeedbackResponse.fromJS(...)`,
  - returns the parsed value (does **not** invoke `throwException`).
- The method's declared return type allows the caller to discriminate the 409 outcome from the 200 outcome by reading a field of the returned `SubmitArticleFeedbackResponse` instance (specifically: `success === false` together with `errorCode === ErrorCodes.ArticleFeedbackAlreadySubmitted` on the 409 branch; `success === true` on the 200 branch). Both branches return the same TypeScript type — the runtime discrimination is via the `BaseResponse` shape already present in the codebase.
- All other generated client methods in `api-client.ts` are byte-for-byte identical to their previous output (verified by `git diff` after regeneration on a clean tree).
- Regenerating the client is idempotent — running the generator twice in a row produces no diff on the second run.
- The TypeScript build (`npm run build`) succeeds with no new errors or warnings.

### FR-3: `useSubmitArticleFeedbackMutation` calls the generated client directly
The hook MUST call `apiClient.articles_SubmitFeedback(articleId, request)` directly (via the existing `getAuthenticatedApiClient()` accessor), branch on the typed response's `BaseResponse` discriminator, and return the existing `SubmitArticleFeedbackResult` union shape unchanged.

**Acceptance criteria:**
- `frontend/src/api/hooks/useArticles.ts` no longer imports `getApiBaseUrl` or `getAuthenticatedFetch` (the imports are removed from this file; they remain exported from `client.ts`).
- The mutation function in `useSubmitArticleFeedbackMutation` (currently lines 218-255):
  - obtains the client via `getAuthenticatedApiClient()`,
  - constructs a `SubmitArticleFeedbackRequest` instance (or `ISubmitArticleFeedbackRequest` literal — match whichever signature the generated method expects),
  - calls `await client.articles_SubmitFeedback(articleId, request)`,
  - branches on the typed response: `response.success === false && response.errorCode === ErrorCodes.ArticleFeedbackAlreadySubmitted` ⇒ return `{ alreadySubmitted: true }`; otherwise return `{ precisionScore: response.precisionScore ?? null, styleScore: response.styleScore ?? null, feedbackComment: response.feedbackComment ?? null }`.
- The function contains no manual URL composition, no manual `fetch(...)` call, no manual `JSON.stringify(body)`, no manual `Content-Type` / `Accept` headers, no `response.status === 409` check on a raw `Response` object, and no `as any` casts.
- `ErrorCodes` is imported from the generated client (`frontend/src/api/generated/api-client.ts`) — not redeclared.
- Any other non-2xx outcome surfaces as a `SwaggerException` thrown by the generated client; the hook does **not** catch it, so React Query's `error` state receives the exception (preserving the prior behavior where 500-level responses fired `onError`).
- The public `SubmitArticleFeedbackResult` interface, the hook's parameter list (`articleId: string`), the `onSuccess` callback (which invalidates `articleKeys.detail(articleId)`), and the React Query key shape are unchanged.

### FR-4: Tests are updated to reflect the new call surface
The existing tests for `useSubmitArticleFeedbackMutation` in `frontend/src/api/hooks/__tests__/useArticles.test.ts` (lines 279-374) mock `getAuthenticatedFetch` and assert on raw `fetch` arguments. They MUST be rewritten to mock the generated client method, exercising the same three branches.

**Acceptance criteria:**
- The `describe('useSubmitArticleFeedbackMutation')` block:
  - no longer mocks `getAuthenticatedFetch` or `getApiBaseUrl` for this mutation,
  - mocks `getAuthenticatedApiClient` to return an object exposing `articles_SubmitFeedback: jest.Mock`,
  - covers exactly three cases:
    1. **2xx success:** `articles_SubmitFeedback` resolves to a `SubmitArticleFeedbackResponse`-shaped object with `success: true`, `precisionScore: 4`, `styleScore: 5`, `feedbackComment: 'great'`. Assertion: mutation `isSuccess` is true and `data` deep-equals `{ precisionScore: 4, styleScore: 5, feedbackComment: 'great' }`.
    2. **409 already-submitted:** `articles_SubmitFeedback` resolves to a `SubmitArticleFeedbackResponse`-shaped object with `success: false`, `errorCode: ErrorCodes.ArticleFeedbackAlreadySubmitted`. Assertion: mutation `isSuccess` is true (NOT `isError`), and `data` deep-equals `{ alreadySubmitted: true }`.
    3. **other non-2xx:** `articles_SubmitFeedback` rejects with a `SwaggerException`-like error containing `status: 500`. Assertion: mutation `isError` is true and `error.message` contains `'500'`.
- The two sibling test blocks (`useArticleFeedbackListQuery mapping` and `useArticleFeedbackListQuery parameter passing`, lines 49-277) are untouched.
- The top-level `jest.mock('../../client', ...)` mock factory keeps `getApiBaseUrl` and `getAuthenticatedFetch` in its return value (they remain exported and other tests may still need them), but the `useSubmitArticleFeedbackMutation` block no longer references the spies for those helpers.
- `npm test -- useArticles.test` passes locally.

### FR-5: `getApiBaseUrl` and `getAuthenticatedFetch` remain exported and documented
The two helpers remain in `frontend/src/api/client.ts` exactly as currently defined, with one JSDoc update.

**Acceptance criteria:**
- The implementations of `getApiBaseUrl()` (lines 177-180) and `getAuthenticatedFetch()` (lines 407-419) are unchanged.
- The JSDoc block on `getAuthenticatedFetch()` (currently referring to `useSubmitArticleFeedbackMutation` as the reference call site) is updated: replace the sentence
  > "Canonical use case: endpoints where status-code branching is required (e.g. 409 = already submitted). See `useSubmitArticleFeedbackMutation` in `hooks/useArticles.ts` for the reference implementation."
  with a sentence that drops the stale reference and instead frames the helper as available for future endpoints that need status-code branching not yet expressed through the generated client (e.g. HTTP 412 precondition-failed responses).
- The helpers remain exported (no `@deprecated` JSDoc tag, no removal).
- No other call site in the codebase currently imports either helper (verifiable by `grep -r "getAuthenticatedFetch\|getApiBaseUrl" frontend/src` after the refactor — the only remaining hits should be the definitions/exports in `client.ts`, the JSDoc, and the test file's mock factory).

### FR-6: Audit and triage other `apiClient as any` hook bypasses
Produce a single audit table inside the change's PR description (not as a code artifact) listing every occurrence of `apiClient as any` under `frontend/src/api/hooks/` along with a one-line classification per occurrence.

**Acceptance criteria:**
- The audit covers every match returned by `grep -rn "apiClient as any" frontend/src/api/hooks/`.
- Each match is classified as exactly one of:
  - **In scope (this PR):** Same status-code-branching pattern that this spec resolves. (Expected count: 0 — the brief's expectation is that `useSubmitArticleFeedbackMutation` was the only such hook; verify.)
  - **Out of scope — different bypass pattern:** The hook uses `apiClient as any` for a reason unrelated to status-code branching (e.g. binary download via `.http.fetch`, FormData upload, query-string composition the generated client does not support). Document the reason in one sentence. Open a separate follow-up GitHub issue per match using the existing arch-review labels (`feat-arch-review-…`).
  - **Out of scope — accepted technical debt:** Documented and intentional. No action required.
- The PR description includes the table and links to any follow-up issues opened.
- Known candidates at the time of writing this spec (confirm during implementation; the actual triage applies to whatever `grep` returns at refactor time): `frontend/src/api/hooks/useInvoiceImportStatistics.ts:42,44`, `frontend/src/api/hooks/useSemiproductRecipePdf.ts:15`. Both appear to be "different bypass pattern" (binary download / query-string composition) and should be classified accordingly.

## Non-Functional Requirements

### NFR-1: Backwards compatibility — no observable behavior change
The mutation's externally observable contract MUST be unchanged after this refactor:

- Same HTTP method, route, request body shape, and Authorization headers reach the backend.
- Same React Query mutation `data` shape on success (`{ precisionScore, styleScore, feedbackComment }`).
- Same React Query mutation `data` shape on 409 (`{ alreadySubmitted: true }`).
- Same React Query `error` semantics for other non-2xx (throws into `error` state).
- Same `onSuccess` invalidation of `articleKeys.detail(articleId)`.
- The consumer in `frontend/src/features/articles/ArticleFeedbackSection.tsx` continues to work with no changes to that file.

### NFR-2: Type safety
After this change, the hook contains zero `as any`, zero `as unknown as ...`, and zero `// eslint-disable` for the `@typescript-eslint/no-explicit-any` or `@typescript-eslint/no-unsafe-*` rules. The 200 and 409 branches both read fields from a typed `SubmitArticleFeedbackResponse` instance.

### NFR-3: NSwag template change must be minimally invasive
If FR-2 is implemented via a custom NSwag template (the recommended approach), the template directory MUST:
- Live inside the backend project tree (e.g. `backend/src/Anela.Heblo.API/nswag-templates/`) so it is versioned with the OpenAPI/NSwag config and the code generator finds it via a relative path.
- Override the minimum set of Liquid templates required for the discriminated-union behavior (no wholesale fork of the NSwag template tree).
- Be documented with a short README in the template directory explaining what was overridden, why, and how to verify the override produces byte-for-byte-identical output for operations that do not declare a matching 4xx response.

### NFR-4: Performance and security
Unchanged. The new code path issues the same single HTTP request the raw-fetch path did. The auth header source (token cache / E2E test token / mock auth) is unchanged because both paths route through `buildAuthHeaders(...)` inside the generated client's `http.fetch` wrapper (`getAuthenticatedApiClient()` configures the same `authenticatedHttp` object that the generated client uses).

### NFR-5: Test coverage threshold
The test file for `useArticles.ts` retains ≥ 80% line coverage of the file's exported hooks after the refactor. The three rewritten test cases in FR-4 specifically cover the success branch, the 409 branch, and the throw branch of the refactored mutation.

## Data Model

No new entities, columns, or migrations. The data model is unchanged.

### Existing types referenced
- `SubmitArticleFeedbackRequest` (`ArticleId: Guid`, `PrecisionScore: int (1–5)`, `StyleScore: int (1–5)`, `Comment?: string ≤ 1000`) — declared at `backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackRequest.cs:7`.
- `SubmitArticleFeedbackResponse : BaseResponse` carrying `PrecisionScore?: int`, `StyleScore?: int`, `FeedbackComment?: string` on success and `Success: false` + `ErrorCode: ErrorCodes.ArticleFeedbackAlreadySubmitted` + `Params: { id }` on 409 — declared at `backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitFeedback/SubmitArticleFeedbackRequest.cs:21`.
- `ErrorCodes.ArticleFeedbackAlreadySubmitted = 2407` decorated with `[HttpStatusCode(HttpStatusCode.Conflict)]` at `backend/src/Anela.Heblo.Application/Shared/ErrorCodes.cs:262`.

The generated TypeScript counterparts (`SubmitArticleFeedbackRequest`, `SubmitArticleFeedbackResponse`, `ErrorCodes`) are regenerated as part of the build and require no manual edits.

## API / Interface Design

### Backend: HTTP contract (unchanged behavior, newly declared shape)
`POST /api/articles/{id}/feedback`

| Status | Body type                       | Meaning                                                                         |
|--------|---------------------------------|---------------------------------------------------------------------------------|
| 200    | `SubmitArticleFeedbackResponse` | Feedback recorded; body contains the saved scores and comment.                  |
| 409    | `SubmitArticleFeedbackResponse` | Feedback was already submitted; body has `success: false`, `errorCode: 2407`.   |
| 403    | (existing)                      | Caller is not the article's `requestedBy` owner. Out of scope for this change.  |
| 404    | (existing)                      | Article not found. Out of scope for this change.                                |
| 422    | (existing)                      | Article is not in `Generated` status. Out of scope for this change.             |

The 200 and 409 rows are the only ones newly advertised via `[ProducesResponseType]` in this change.

### Frontend: hook contract (unchanged)
```ts
export interface SubmitArticleFeedbackResult {
  alreadySubmitted?: true;
  precisionScore?: number | null;
  styleScore?: number | null;
  feedbackComment?: string | null;
}

export const useSubmitArticleFeedbackMutation: (articleId: string) =>
  UseMutationResult<SubmitArticleFeedbackResult, Error, SubmitArticleFeedbackPayload>;
```

The internal implementation changes; the exported type and call shape do not.

### Generated client: method contract (new branch)
After this change, the generated `articles_SubmitFeedback(id, request)` method:

- resolves with a parsed `SubmitArticleFeedbackResponse` on HTTP 200 (`success: true`, scores populated),
- resolves with a parsed `SubmitArticleFeedbackResponse` on HTTP 409 (`success: false`, `errorCode: 2407`),
- rejects with a `SwaggerException` on every other non-2xx status.

The runtime discriminator is the existing `BaseResponse.success` boolean; no new wrapper type is introduced.

## Dependencies

- **NSwag toolchain** — already used by the project; this change configures (not introduces) it via `backend/src/Anela.Heblo.API/nswag.frontend.json`. The template override (if used) targets the Liquid template set NSwag already ships.
- **`Microsoft.AspNetCore.Mvc.ApiExplorer` / `[ProducesResponseType]`** — already referenced throughout `Anela.Heblo.API`. No package changes.
- **No new npm packages**, no new NuGet packages.
- The change must be coordinated with the regular build pipeline that regenerates `frontend/src/api/generated/api-client.ts` — the diff in that file is part of this PR.

## Out of Scope

- Refactoring the other `apiClient as any` hooks identified by the FR-6 audit (e.g. `useInvoiceImportStatistics`, `useSemiproductRecipePdf`). Those are their own follow-up issues per FR-6's triage.
- Adding `[ProducesResponseType]` for the 403/404/422 paths on `SubmitFeedback`. They are not required to surface 409 as a typed branch; documenting them separately is a broader API-documentation effort that should not be conflated with this refactor.
- Applying the same discriminated-union pattern to the parallel `KnowledgeBase` and `Leaflet` "feedback already submitted" endpoints (`KnowledgeBaseController.SubmitFeedback`, `LeafletController.SubmitFeedback`). They have the same shape and would benefit from the same treatment, but their consumer hooks do not currently use the raw-fetch bypass; mass-migrating them is a separate, lower-priority cleanup.
- Removing the `getApiBaseUrl()` / `getAuthenticatedFetch()` helpers. The brief is explicit that they stay for future endpoints.
- Any change to the `BaseResponse` envelope, the `HandleResponse<T>` controller helper, the `HttpStatusCodeAttribute`, or the `ErrorCodes` enum.
- E2E (Playwright) coverage for the 409 path. The mutation's behavior is fully exercised by the unit tests in FR-4; adding a Playwright scenario for a "you already submitted feedback" toast is a UX-test concern out of scope here.

## Open Questions

None.

## Status: COMPLETE