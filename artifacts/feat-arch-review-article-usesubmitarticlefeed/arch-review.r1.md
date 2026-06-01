# Architecture Review: Replace `apiClient as any` Bypass in `useSubmitArticleFeedbackMutation`

## Skip Design: true

No UI/UX changes. This is a pure frontend refactor of an existing hook behind a stable public API; the user-facing feedback flow (modal, form, success/already-submitted states) is unchanged.

## Architectural Fit Assessment

The proposal aligns cleanly with how `frontend/src/api/client.ts` is already structured. The module already centralizes (1) base-URL resolution from `runtimeConfig`, (2) token acquisition via `getAuthHeader`, (3) E2E test-token routing, and (4) header building via the private `buildAuthHeaders(init)` helper. The two new exports are thin wrappers over machinery that already exists — `getApiBaseUrl()` over `getConfig().apiUrl` and `getAuthenticatedFetch()` over `buildAuthHeaders` + `fetch`. No new abstraction layer, no new dependency, no new runtime cost.

The integration points are exactly two: `frontend/src/api/client.ts` (add helpers) and `frontend/src/api/hooks/useArticles.ts` (consume them in `useSubmitArticleFeedbackMutation`). The hook keeps its existing React Query mutation contract — only the internal URL/fetch wiring changes — so call sites in the article-feedback UI are untouched.

Note: as of the current branch (`f331b30f`, `e4669139`, `6ff136cd`) both helpers and the hook refactor are already on disk, plus initial tests. This review therefore focuses on architectural correctness of what landed and the gaps still required to fully satisfy FR-2, FR-4, and the docs prerequisite.

## Proposed Architecture

### Component Overview

```
              runtimeConfig.getConfig()         globalTokenProvider / mockAuth / e2eAuth
                       │                                       │
                       ▼                                       ▼
  ┌──────────────────────────────────  client.ts  ───────────────────────────────────┐
  │                                                                                  │
  │   getApiBaseUrl(): string ◀── pure getter, no I/O                                │
  │                                                                                  │
  │   buildAuthHeaders(init)  ──┬── attaches Authorization / X-E2E-Test-Token        │
  │       (private)             └── sets Content-Type unless FormData                │
  │                                                                                  │
  │   getAuthenticatedFetch():                                                       │
  │       (input, init?) => fetch(input, { headers, credentials, ...init })          │
  │           ▲                                                                      │
  │           │ shares header logic with                                             │
  │   getAuthenticatedApiClient() ── ApiClient(baseUrl, { fetch: authedFetch+toast}) │
  │                                                                                  │
  └──────────────────────────────────────────────────────────────────────────────────┘
                                       ▲                       ▲
                  used by every typed   │                       │  used only when an endpoint
                  hook (default path)   │                       │  needs status-code branching
                                        │                       │  (e.g. 409 → typed result)
                          useListArticlesQuery,         useSubmitArticleFeedbackMutation
                          useGetArticleQuery,           (and future similar hooks)
                          useGenerateArticleMutation,
                          useArticleFeedbackListQuery
```

### Key Design Decisions

#### Decision 1: Two helpers, not a single `submitFeedback` function

**Options considered:**
- (A) Export a single `submitArticleFeedback(articleId, payload)` from `client.ts` that owns the 409-branching logic.
- (B) Export two general-purpose primitives (`getApiBaseUrl`, `getAuthenticatedFetch`) and let the hook compose them.
- (C) Patch the NSwag template to surface 409 as a typed result on this endpoint.

**Chosen approach:** B.

**Rationale:** (A) leaks endpoint-specific knowledge into `client.ts`, which currently knows nothing about articles or any other module — wrong altitude. (C) is correct long-term but out of scope and blocked on template work. (B) gives any future hook that needs status-code branching a typed, reusable seam without growing `client.ts` per endpoint. The spec already records (C) as the longer-term direction; the helpers do not foreclose it — when the typed mutation lands, the hook switches one line and the helpers either stay (for the next case) or are removed cleanly.

#### Decision 2: `getAuthenticatedFetch` returns a function, not performs the call

**Options considered:**
- (A) `getAuthenticatedFetch(url, init): Promise<Response>` — direct call.
- (B) `getAuthenticatedFetch(): (url, init) => Promise<Response>` — factory.

**Chosen approach:** B (already implemented).

**Rationale:** Matches the existing `getAuthenticatedApiClient()` factory pattern, mirrors the global `fetch` signature so the returned value is drop-in for code expecting `typeof fetch`, and makes mocking in tests trivial (`mockGetAuthenticatedFetch.mockReturnValue(mockFetch)` — exactly what `useArticles.test.ts:280` does). The one-extra-call cost is irrelevant; the symmetry with the existing module shape is the real win.

#### Decision 3: Header merge order — auth headers win

**Options considered:**
- (A) `{ ...buildAuthHeaders(init), ...init.headers }` — caller wins.
- (B) `{ ...init.headers, ...buildAuthHeaders(init) }` — auth wins (caller can only add, never override).

**Chosen approach:** B.

**Rationale:** FR-2 acceptance criterion explicitly states "caller headers do not silently override auth headers." A caller that accidentally passes `Authorization: ...` (e.g. while debugging) must not silently break auth in production. **The current implementation in `client.ts:407` does `{ ...headers, ...(init.headers ?? {}) }`, which is order (A) and contradicts the spec.** See Risks below — this needs to flip before merge, and a test must lock the invariant.

## Implementation Guidance

### Directory / Module Structure

No new files. Changes are confined to:

- `frontend/src/api/client.ts` — `getApiBaseUrl` + `getAuthenticatedFetch` already added (lines 174–180, 399–411). Do not move them into a new `client/helpers.ts` — the rest of the module's auth state (token cache, E2E mode, runtime config) lives in this file, and splitting now creates a circular-import hazard for marginal benefit.
- `frontend/src/api/hooks/useArticles.ts` — `useSubmitArticleFeedbackMutation` already consumes the helpers (lines 218–255). The `TODO(arch-review 2026-05-25)` comment has been removed.
- `frontend/src/api/__tests__/client.test.ts` — already covers `getApiBaseUrl` + the happy path of `getAuthenticatedFetch`.
- `frontend/src/api/hooks/__tests__/useArticles.test.ts` — already covers 2xx / 409 / 5xx paths of the mutation (lines 274–368).
- `frontend/src/api/__tests__/authenticated-api-usage.test.ts` — needs tightening (see FR-4 gap below).
- `docs/development/api-client-generation.md` — needs updating (see Prerequisites).

### Interfaces and Contracts

```ts
// frontend/src/api/client.ts — keep exactly these signatures

export const getApiBaseUrl: () => string;

export function getAuthenticatedFetch(): (
  input: RequestInfo | URL,
  init?: RequestInit,
) => Promise<Response>;
```

Contract guarantees the hook layer (and future callers) must be able to assume:

1. `getApiBaseUrl()` returns the **same** URL passed into `new ApiClient(...)` in `getAuthenticatedApiClient()`. Both must read from `getConfig().apiUrl` — never one from config and the other from a private field of the NSwag class.
2. `getAuthenticatedFetch()` never throws on non-2xx. The caller owns status-code branching.
3. `getAuthenticatedFetch()` attaches **at most one** `Authorization` header, sourced from the same `getAuthHeader()` pipeline as the NSwag client, with the same token cache. The caller cannot override it (see Decision 3).
4. The helper never logs or persists the bearer token (it inherits this property by reusing `buildAuthHeaders`, which does not log the token value — keep it that way).
5. NSwag regeneration must not require any code change in the hook layer. The helpers only depend on `getConfig()` and `getAuthHeader`; neither references generated symbols.

### Data Flow (feedback submission)

```
  user clicks "Submit feedback" in <ArticleFeedbackForm/>
              │
              ▼
  useSubmitArticleFeedbackMutation(articleId).mutate(payload)
              │
              ▼ payload typed as SubmitArticleFeedbackPayload
  body: SubmitArticleFeedbackRequest = { articleId, ...payload }       ◀── DTO from generated/api-client
  url  = `${getApiBaseUrl()}/api/articles/${articleId}/feedback`
              │
              ▼
  getAuthenticatedFetch()(url, { method:'POST', body: JSON.stringify(body), headers: {...} })
              │
              ▼ inside the helper
  buildAuthHeaders(init) ── attaches Authorization (real auth) OR X-E2E-Test-Token (E2E)
              │
              ▼
  fetch(url, { ...init, headers, credentials: isE2E ? 'include' : 'same-origin' })
              │
       ┌──────┼──────────────────────────────────────────────┐
       ▼                                                      ▼
   status 200                                            status 409
   → response.json() → SubmitArticleFeedbackResult        → return { alreadySubmitted: true }   (typed success)
       │
       ▼ onSuccess
   queryClient.invalidateQueries({ queryKey: articleKeys.detail(articleId) })
              │
              ▼
  useGetArticleQuery refetches → UI shows precisionScore/styleScore/feedbackComment
```

5xx / non-2xx (non-409): `throw new Error(...)` → React Query exposes `isError + error.message`. The error path does **not** go through `getAuthenticatedApiClient`'s toast pipeline (because the hook uses `getAuthenticatedFetch` directly), so the hook's `useEffect`/onError in the consuming component is responsible for surfacing the failure — confirm this matches the existing feedback-form UX before merge.

## Risks and Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| `getAuthenticatedFetch` merges caller `init.headers` **after** auth headers (`client.ts:407`), letting a caller silently override `Authorization`. Contradicts FR-2 acceptance criterion. | High | Flip the spread order to `{ ...(init.headers ?? {}), ...headers }`. Add a unit test in `client.test.ts` asserting that a caller-supplied `Authorization: 'Bearer caller-bogus'` does **not** appear in the final headers — the helper's auth header wins. |
| `authenticated-api-usage.test.ts` still treats `(apiClient as any).http.fetch` and `apiClient.http.fetch` as **valid** authenticated patterns (lines 77–79, 131–133). FR-4 requires the opposite — these strings must now be banned. | High | Tighten the guard: hooks under `frontend/src/api/hooks/` must use `getAuthenticatedApiClient()` OR `getAuthenticatedFetch()`. Any occurrence of `apiClient as any`, `as any).http`, or `as any).baseUrl` is a hard failure. Update the error message to point at the new helpers. |
| `docs/development/api-client-generation.md` (lines 184–249, 423–434) still documents the `(apiClient as any).baseUrl` / `(apiClient as any).http.fetch` pattern as the recommended approach. Future contributors will copy the doc, not the new helpers. | Medium | Replace the "✅ CORRECT" code blocks with `getApiBaseUrl()` + `getAuthenticatedFetch()` snippets. Add an "Avoid" callout for `as any` reaches. Mention that the helpers exist for status-code-branching cases like 409. |
| NSwag regeneration of `ApiClient` could in principle change the constructor signature `new ApiClient(baseUrl, http?)`. Both `getAuthenticatedApiClient` and `getApiBaseUrl` would still compile but could disagree on the actual URL used. | Low | The unit test added by FR-1 already asserts equality against the mocked `runtimeConfig.apiUrl`. Add one more test asserting `getApiBaseUrl() === new ApiClient(getApiBaseUrl()).baseUrl` (using the public, declared shape only — no `as any`). If NSwag ever stops accepting the URL as the first constructor arg, that test fails loudly at build. |
| Loss of central toast/401-redirect behavior. `getAuthenticatedApiClient()` performs `extractErrorMessage` + global toast + 401-redirect on every non-2xx; `getAuthenticatedFetch()` does none of that. A consumer that forgets to handle errors in its mutation will silently swallow 5xx. | Medium | Acceptable for `useSubmitArticleFeedbackMutation` — it explicitly throws on non-2xx, so React Query surfaces the error, and the consuming form already handles `isError`. **Document this divergence** in the JSDoc above `getAuthenticatedFetch` ("does NOT trigger global error toasts or 401 redirect — caller owns error UX") so the next consumer doesn't reach for it as a default. |
| Spec FR-2 requires "response is returned unmodified (no error wrapping for non-2xx)" — verified — but the existing `getAuthenticatedFetch` JSDoc says "Use this for resources that must be fetched via JS (e.g. images behind auth)", which understates its intended role. Future readers may not realize it's the sanctioned escape hatch for status-code branching. | Low | Expand the JSDoc to call out the 409-branching use case explicitly and link to `useSubmitArticleFeedbackMutation` as the canonical example. |

## Specification Amendments

1. **FR-2 acceptance criterion (b)** must add an explicit test: a caller-supplied `Authorization` header is **dropped or overwritten** by the helper's auth header (not merged after it). Current spec text "caller headers do not silently override auth headers" already implies this; make it testable.
2. **FR-4 scope must include updating the existing guard test, not adding a new one.** `frontend/src/api/__tests__/authenticated-api-usage.test.ts` already exists and is the right place; the spec's "or equivalent" should be tightened to "extend this file". Remove the now-stale allowances for `(apiClient as any).http.fetch` and `apiClient.http.fetch` as part of FR-4 — those patterns are exactly what FR-4 is meant to ban.
3. **Add a documentation deliverable to the spec.** `docs/development/api-client-generation.md` is the authoritative URL-construction reference cited in `CLAUDE.md`. It must be updated in the same PR — otherwise the project rule and the doc diverge from the code. Treat this as an FR (e.g. FR-6), not just a nice-to-have.
4. **Clarify in FR-3 that `headers: { 'Content-Type': 'application/json', Accept: 'application/json' }` is intentional.** `buildAuthHeaders` already sets Content-Type, so the hook-level Content-Type is redundant but harmless under Decision 3's "auth wins" merge order. State this so a future reader doesn't "clean it up" and accidentally change behavior.
5. **Out-of-scope list should explicitly include adding a global error/toast behavior to `getAuthenticatedFetch`.** Mirroring `getAuthenticatedApiClient`'s toast pipeline inside the raw helper is tempting but would change the error UX of the feedback form and conflict with the explicit "no error wrapping for non-2xx" criterion in FR-2.

## Prerequisites

1. **Doc update merged in the same PR** — `docs/development/api-client-generation.md` rewrites the "✅ CORRECT" sections to show `getApiBaseUrl()` + `getAuthenticatedFetch()`. Without this the project's own canonical guide still tells contributors to use `as any` reaches.
2. **No infrastructure / migration / config work.** No backend changes, no Key Vault secrets, no environment-variable changes, no DB migration. Base URL still comes from `runtimeConfig` which is already wired across dev / staging / production.
3. **NSwag template is **not** modified.** Typed 409 support is explicitly deferred (Out of Scope item 1). The helpers must work against the current generated client without any template change.
4. **Existing tests must pass before merge:** `frontend/src/api/__tests__/client.test.ts`, `frontend/src/api/hooks/__tests__/useArticles.test.ts`, and the tightened `authenticated-api-usage.test.ts`. Run `npm run build` and `npm run lint` per the project's "Validation before completion" checklist. E2E is not required for this change (the feedback flow's E2E coverage is unchanged).