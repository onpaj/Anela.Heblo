All tasks complete. Writing the output summary.

---

# Implementation: Surface HTTP 409 as a Typed Branch on `articles_SubmitFeedback`

## What was implemented

Replaced the raw-fetch fallback in `useSubmitArticleFeedbackMutation` with a direct typed call to `getAuthenticatedApiClient().articles_SubmitFeedback()`. The 409 "already submitted" case is handled via a hook-level `try/catch` (escape hatch activated — see Notes).

**Key changes:**
1. `ArticlesController.SubmitFeedback` now declares `[ProducesResponseType(200)]` and `[ProducesResponseType(409)]` so the OpenAPI contract is accurate.
2. A NSwag Liquid template override (`Client.ProcessResponse.HandleStatusCode.liquid`) was written and committed with a correct 409-specific predicate — it exists for future activation but is **not** wired today (`templateDirectory: null`).
3. `getAuthenticatedApiClient()`'s `authenticatedHttp.fetch` suppresses the global error toast on 409 responses whose body is a structured `BaseResponse` — preserving the toast-free behavior of the prior raw-fetch path.
4. `useSubmitArticleFeedbackMutation` uses `try/catch` to intercept `SwaggerException` with `status === 409` and return `{ alreadySubmitted: true }`.
5. JSDoc and `docs/development/api-client-generation.md` updated to drop the stale `useSubmitArticleFeedbackMutation` reference and reframe the raw-fetch helpers as a forward-looking escape hatch.
6. FR-6 audit completed: 33 files have `apiClient as any`, 0 in-scope after this PR.

## Files created/modified

- `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs` — added two `[ProducesResponseType]` attributes to `SubmitFeedback`
- `backend/src/Anela.Heblo.API/nswag-templates/README.md` — created; documents the override contract, predicate, and wiring status
- `backend/src/Anela.Heblo.API/nswag-templates/Client.ProcessResponse.HandleStatusCode.liquid` — created; NSwag template override with 409-specific predicate (unwired)
- `frontend/src/api/client.ts` — `suppressOn409` guard in toast logic; updated JSDoc on `getAuthenticatedFetch`
- `frontend/src/api/hooks/useArticles.ts` — removed `getApiBaseUrl`/`getAuthenticatedFetch` imports; replaced raw-fetch with typed client call + try/catch
- `frontend/src/api/hooks/__tests__/useArticles.test.ts` — rewrote third describe block to mock `articles_SubmitFeedback` directly
- `frontend/src/api/__tests__/client.test.ts` — new; 3 toast-suppression tests for 409 structured/unstructured/500
- `docs/development/api-client-generation.md` — rewritten status-branching guidance section

## Tests

- `frontend/src/api/__tests__/client.test.ts` — 3 tests: no toast on 409+structured, toast on 500+structured, toast on 409+unstructured
- `frontend/src/api/hooks/__tests__/useArticles.test.ts` — 3 rewritten tests: 200 success, 409 catch → alreadySubmitted, 500 propagates as error

## How to verify

```bash
# Backend
dotnet build backend/Anela.Heblo.sln
dotnet format backend/Anela.Heblo.sln --verify-no-changes

# Frontend
cd frontend && npm run lint && npm run build
cd frontend && npm test -- --watchAll=false

# Spot checks
grep -n "getApiBaseUrl\|getAuthenticatedFetch" frontend/src/api/hooks/useArticles.ts  # empty
grep -n "ProducesResponseType.*409" backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs  # present
```

## Notes

**Escape hatch active.** The original plan called for a NSwag Liquid template override that would make `articles_SubmitFeedback` resolve (not throw) on 409. During implementation, the schema-equality predicate proved too broad: `FeatureFlagsController` already declares 404 responses with the same DTO type as 200 for unrelated reasons, which would have silently converted those 404s into non-throwing branches. The plan's escape hatch (hook-level `try/catch SwaggerException`) was activated. The template file is committed with the narrower 409-only predicate and documented as "ready to wire when validated". FR-2 (typed branch in generated client) is abandoned in this PR. FR-1 (409 in OpenAPI contract), FR-3 (hook uses typed client), FR-4 (tests), FR-5 (helpers stay), FR-6 (audit) are all fulfilled.

**FR-6 audit (33 files, 0 in-scope):** All remaining `apiClient as any` bypasses are for raw HTTP access (binary downloads, FormData uploads, query-string composition, SSE polling) or NSwag type-cast coercion — none involve the same DTO-on-4xx pattern fixed here.

## PR Summary

Surface HTTP 409 as a typed, non-throwing business outcome on `POST /api/articles/{id}/feedback`.

The `useSubmitArticleFeedbackMutation` hook previously used a raw `fetch` bypass via `getAuthenticatedFetch()` to avoid the NSwag-generated client throwing on 409. This PR replaces the bypass with a direct typed call to `apiClient.articles_SubmitFeedback()`. The hook catches `SwaggerException` with `status === 409` and returns `{ alreadySubmitted: true }` without surfacing a React Query error or an "Upozornění" toast (the new `suppressOn409` guard in `authenticatedHttp.fetch` takes care of the latter).

The `ArticlesController.SubmitFeedback` action is annotated with `[ProducesResponseType(200)]` and `[ProducesResponseType(409)]` so the OpenAPI contract accurately advertises both outcomes. A NSwag Liquid template override is committed but not yet wired — it will enable the generated client to resolve (not throw) on 409 in a follow-up PR once the 409-only predicate is confirmed safe against all controllers.

### Changes
- `backend/.../Controllers/ArticlesController.cs` — `[ProducesResponseType]` annotations on `SubmitFeedback`
- `backend/.../nswag-templates/` — template override directory (README + `.liquid` file, not wired)
- `frontend/src/api/client.ts` — toast suppression on 409+structured; updated `getAuthenticatedFetch` JSDoc
- `frontend/src/api/hooks/useArticles.ts` — typed client call + try/catch for 409; removed raw-fetch imports
- `frontend/src/api/hooks/__tests__/useArticles.test.ts` — rewritten third describe block
- `frontend/src/api/__tests__/client.test.ts` — new toast-suppression tests
- `docs/development/api-client-generation.md` — rewritten status-branching guidance

## Status

DONE_WITH_CONCERNS — FR-2 (typed branch in generated client) is abandoned per the plan's escape hatch. The NSwag template override is ready but unwired. All other functional requirements (FR-1, FR-3, FR-4, FR-5, FR-6) are fulfilled. 245 test suites, 1986 tests passing.