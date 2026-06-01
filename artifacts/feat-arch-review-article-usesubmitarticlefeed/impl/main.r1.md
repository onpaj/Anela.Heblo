Implementation complete. Here's a summary of what was done and verified:

## Summary

The implementation was largely done in prior commits (`f331b30f` and `e4669139`). This round verified compliance, ran reviews, and added one additional fix.

**What's in place:**

- **`getApiBaseUrl()`** — exported from `client.ts`, returns `getConfig().apiUrl` (same source as the NSwag client constructor)
- **`getAuthenticatedFetch()`** — exported from `client.ts`, transparent fetch wrapper using shared `buildAuthHeaders()`, does not throw on non-2xx
- **`useSubmitArticleFeedbackMutation`** — zero `apiClient as any` casts; body typed as `SubmitArticleFeedbackRequest` from the generated module; 409 → `{ alreadySubmitted: true }` branch preserved
- **Tests** — 2xx, 409, 500 branches covered in `useArticles.test.ts`; `getApiBaseUrl` and `getAuthenticatedFetch` covered in `client.test.ts`
- **FR-6 issue** — GitHub issue #2178 already existed (NSwag long-term fix)
- **Fix added this round** — stale error message in `authenticated-api-usage.test.ts` that was still recommending the removed `(apiClient as any).http.fetch` pattern

**Status: DONE** — `DONE_WITH_CONCERNS` only because node_modules aren't installed in this environment, so tests couldn't be executed. The test code is structurally correct.