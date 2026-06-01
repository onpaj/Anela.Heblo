# Implementation: Remove `as any` bypass in `useSubmitArticleFeedbackMutation`

## What was implemented

Refactored `useSubmitArticleFeedbackMutation` in `frontend/src/api/hooks/useArticles.ts` to replace both `(apiClient as any).baseUrl` and `(apiClient as any).http.fetch` private-field accesses with the typed public helpers `getApiBaseUrl()` and `getAuthenticatedFetch()` that were already committed to `frontend/src/api/client.ts` in the prior round.

The hook's external contract (taking `articleId: string` as a hook parameter, accepting `SubmitArticleFeedbackPayload` as the mutation payload, and returning a typed `SubmitArticleFeedbackResult`) is unchanged. The 409 "already submitted" typed-result branch is preserved verbatim.

The request body is now explicitly typed as `SubmitArticleFeedbackRequest` imported from the generated NSwag client, satisfying the FR-4 compile-time field-rename detection requirement.

The `authenticated-api-usage.test.ts` guardrail was updated to recognise `getAuthenticatedFetch` as a valid authenticated-API pattern alongside the old `(apiClient as any).http.fetch` pattern.

Three unit tests were added covering the 2xx, 409, and 5xx response branches of `useSubmitArticleFeedbackMutation`.

The FR-6 follow-up issue was filed at https://github.com/onpaj/Anela.Heblo/issues/2178.

## Files created/modified

- `frontend/src/api/hooks/useArticles.ts` — imports extended; `useSubmitArticleFeedbackMutation` refactored; TODO comment removed; `(apiClient as any)` eliminated
- `frontend/src/api/hooks/__tests__/useArticles.test.ts` — mock factory extended with `getApiBaseUrl`/`getAuthenticatedFetch`; new `describe('useSubmitArticleFeedbackMutation', ...)` block with 3 test cases
- `frontend/src/api/__tests__/authenticated-api-usage.test.ts` — test 1 and test 2 checks updated to accept `getAuthenticatedFetch` as a valid auth pattern; stale error message example updated from `(apiClient as any).http.fetch` to `getAuthenticatedFetch()` pattern

## Tests

`frontend/src/api/hooks/__tests__/useArticles.test.ts` — new describe block:
- **2xx success**: resolves with parsed body (`precisionScore`, `styleScore`, `feedbackComment`), mutation is in success state
- **409 already submitted**: resolves with `{ alreadySubmitted: true }`, mutation is in success state (not error)
- **500 error**: rejects with `Error` containing the status code, mutation is in error state

## How to verify

```bash
cd frontend

# 1. Confirm zero remaining (apiClient as any) in the hook
grep -n "apiClient as any" src/api/hooks/useArticles.ts
# Expected: no output

# 2. TypeScript check
npx tsc --noEmit

# 3. Run the hook tests
npx jest --testPathPattern="useArticles"

# 4. Run the guardrail test
npx jest --testPathPattern="authenticated-api-usage"
```

## Notes

- No CLAUDE.md existed in this project to update (Task 11 was moot).
- `getApiBaseUrl()` and `getAuthenticatedFetch()` were already added to `client.ts` in the previous round (`f331b30f`). This round only consumed them in the hook.
- The `SubmitArticleFeedbackRequest` class in the generated client has all-optional fields (`articleId?: string`, `precisionScore?: number`, etc.), which matches the assignment from the typed payload. TypeScript accepts this without any cast.
- The `buildAuthHeaders` function in `client.ts` is async and includes `Content-Type: application/json` automatically for non-FormData bodies. The explicit `Content-Type` header in the hook's `fetch` init is redundant but harmless (caller-provided headers merge over defaults in the spread order used by `getAuthenticatedFetch`).

## PR Summary

Removed the `(apiClient as any).baseUrl` and `(apiClient as any).http.fetch` bypass in `useSubmitArticleFeedbackMutation` by consuming the `getApiBaseUrl()` and `getAuthenticatedFetch()` typed helpers already present in `client.ts`. The hook body now uses only public APIs, so an NSwag regeneration that renames or removes the generated client's private `baseUrl`/`http` fields cannot silently break this code path.

The request body is typed as the generated `SubmitArticleFeedbackRequest`, giving compile-time protection if the C# DTO fields are renamed. The 409-as-success behaviour is unchanged.

Three unit tests pin the 2xx, 409, and error branches. The repo's `authenticated-api-usage.test.ts` guardrail is updated to accept `getAuthenticatedFetch()` as a valid authenticated-API pattern.

FR-6 follow-up filed: https://github.com/onpaj/Anela.Heblo/issues/2178 — tracks the long-term NSwag fix to return 409 as a typed alternative result from the generated client, which would eliminate the raw-fetch workaround entirely.

### Changes
- `frontend/src/api/hooks/useArticles.ts` — refactored `useSubmitArticleFeedbackMutation`: removed `(apiClient as any)` accesses, used `getApiBaseUrl()`/`getAuthenticatedFetch()`, typed body as `SubmitArticleFeedbackRequest`, removed TODO comment
- `frontend/src/api/hooks/__tests__/useArticles.test.ts` — extended mock factory; added 3 unit tests for the mutation (2xx, 409, 500)
- `frontend/src/api/__tests__/authenticated-api-usage.test.ts` — updated auth-pattern guardrail to recognise `getAuthenticatedFetch` as valid; fixed stale error example that was still recommending the deprecated `(apiClient as any).http.fetch` pattern

## Status
DONE
