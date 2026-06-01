# Implementation: Refactor Article Frontend Hooks to Use Generated NSwag Client Methods

## What was implemented

Three Article-module React Query hooks were refactored from raw `fetch` calls using private `(apiClient as any).baseUrl`/`.http` internals to typed NSwag-generated client methods, restoring full type safety with zero consumer-facing behavior change.

- **`useArticleTraceQuery`** — now calls `client.articles_GetTrace(id)` with explicit per-field projection of each `ArticleGenerationStepDto` into the local `ArticleGenerationStep` shape (dates converted `Date → string | null` via `.toISOString()`).
- **`useArticleFeedbackListQuery`** — now calls `client.articles_FeedbackList(...)` with six positional arguments, then explicitly projects the generated `GetArticleFeedbackListResponse` into `ArticleFeedbackListResponse` (field renames: `items → articles`, `createdAt → generatedAt`, `hasComment → hasFeedback`, plus `feedbackComment: null` with explanatory comment; `stats` coalesced to a safe default when the DTO omits it).
- **`useGetArticleQuery`** — three stale `(response as any)` casts for `precisionScore`, `styleScore`, and `feedbackComment` were removed along with their paired `eslint-disable-next-line` directives; the generated `GetArticleResponse` already declares these fields.
- **`useSubmitArticleFeedbackMutation`** — intentionally unchanged; a single dated `// TODO(arch-review 2026-05-25)` comment was added above the raw-fetch line to flag the residual fragility.

## Files created/modified

- `frontend/src/api/hooks/useArticleTrace.ts` — `queryFn` body rewritten to use `articles_GetTrace` with explicit step mapping
- `frontend/src/api/hooks/useArticles.ts` — three surgical edits: (1) `useArticleFeedbackListQuery` `queryFn` rewritten to use `articles_FeedbackList` with full projection, (2) three `as any` casts removed from `useGetArticleQuery`, (3) single TODO comment added above `useSubmitArticleFeedbackMutation`'s raw-fetch call

## Tests

No new test files created. Per spec (FR-4 / Out of Scope), no existing test exercises these three hooks directly. The `useArticleFeedbackAdapter.test.ts` mocks `useArticleFeedbackListQuery` at the hook level and was not modified.

Pre-existing Jest/Babel configuration issue (`SyntaxError: Unexpected token` on `import type`) prevents all 29 test suites from running in this environment — this is a pre-existing infrastructure issue unrelated to this refactoring. All modified files pass `npx tsc --noEmit` strictly.

## How to verify

```bash
# 1. Confirm no as any in the three refactored hooks
grep -n "apiClient as any" frontend/src/api/hooks/useArticleTrace.ts         # no output
grep -nE "\(response as any\)" frontend/src/api/hooks/useArticles.ts          # no output
grep -nE "as ArticleGenerationStep\[\]|as ArticleFeedbackListResponse" \
  frontend/src/api/hooks/useArticleTrace.ts \
  frontend/src/api/hooks/useArticles.ts                                        # no output

# 2. Full TypeScript type-check and build
cd frontend && npm run build

# 3. Lint
cd frontend && npm run lint

# 4. Consumer components unaffected
cd frontend && npx tsc --noEmit -p tsconfig.json 2>&1 | \
  grep -E "ArticleDebugPanel\.tsx|ArticleDetail\.tsx|useArticleFeedbackAdapter\.ts" || echo "clean"

# 5. Confirm commits
git log --oneline main..HEAD | head -4
```

## Notes

- **Arch-review Amendment #1 applied**: the actual hook signature is `(id: string, enabled: boolean)`, not `(id, options?)` as the spec's example section wrote. The actual signature was preserved.
- **Parameter name mismatch (Amendment #5)**: the generated client names the 4th parameter `sortDescending`; the local `ArticleFeedbackListParams` calls it `descending`. Positional invocation is used, which is correct; noted in PR description.
- **`feedbackComment: null` is behavior-preserving**: confirmed by arch review that the backend list handler never emits a per-item `feedbackComment` field (only `hasComment`). The projection comment documents this at the call site.
- **`stats` safe default**: `ArticleFeedbackListResponse.stats` is a required field; `GetArticleFeedbackListResponse.stats` is optional. The projection coalesces to `{ totalArticles: 0, totalWithFeedback: 0, avgPrecisionScore: null, avgStyleScore: null }` when the DTO returns `undefined`. In practice the backend always returns `stats`, so the default is defensive only.
- **`useSubmitArticleFeedbackMutation` out of scope**: the 409 branch is a non-exceptional business return value; migrating to `articles_FeedbackSubmit` would require catch-and-rethrow logic for `ApiException`. The dated TODO marks the residual fragility without expanding scope.

## PR Summary

Refactored three Article React Query hooks to use the typed NSwag-generated client instead of bypassing private `baseUrl`/`http` fields via `as any` casts. All three refactored hooks now call generated methods (`articles_GetTrace`, `articles_FeedbackList`, `articles_GetById`) and explicitly project generated DTOs into the existing consumer-facing local interfaces — matching the explicit-mapping pattern already established by `useListArticlesQuery` and the non-cast portion of `useGetArticleQuery`. No consumer files were modified; no hook signatures, query keys, or cache options were changed.

### Changes
- `frontend/src/api/hooks/useArticleTrace.ts` — `queryFn` rewritten to use `articles_GetTrace` with explicit per-field step mapping (11 fields, `Date → string | null`)
- `frontend/src/api/hooks/useArticles.ts` — `useArticleFeedbackListQuery` `queryFn` rewritten to use `articles_FeedbackList` with full DTO projection; three stale `(response as any)` casts removed from `useGetArticleQuery`; single dated TODO added above `useSubmitArticleFeedbackMutation`'s raw-fetch call

## Status
DONE_WITH_CONCERNS

**Concern:** Pre-existing Jest/Babel configuration issue (`SyntaxError: Unexpected token` on `import type` in test files) prevented the adapter regression test (`useArticleFeedbackAdapter.test.ts`) and the hooks test suite from running. This is not caused by this refactoring — the same failure occurs on `main`. All code correctness was validated via `npx tsc --noEmit` (strict TypeScript) and `npm run build`. The concern is logged here for visibility; no action required from this PR.
