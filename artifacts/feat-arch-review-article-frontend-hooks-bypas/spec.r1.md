# Specification: Refactor Article Frontend Hooks to Use Generated NSwag Client Methods

## Summary
Three Article-module React Query hooks in `frontend/src/api/hooks/` bypass the typed NSwag-generated client by accessing private fields (`baseUrl`, `http`) via `as any` casts and constructing raw `fetch` calls. This spec defines the refactor to use the typed generated methods directly, restoring type safety and removing fragile reliance on private internals.

## Background
The frontend uses an NSwag-generated TypeScript API client whose `baseUrl` and `http` fields are declared `private` (generated file lines 12–13). Three hooks circumvent this:

- `useArticleTraceQuery` — `frontend/src/api/hooks/useArticleTrace.ts:28-33`
- `useArticleFeedbackListQuery` — `frontend/src/api/hooks/useArticles.ts:276-278`
- `useGetArticleQuery` — `frontend/src/api/hooks/useArticles.ts:184-188`

For the first two, typed generated methods (`articles_GetTrace`, `articles_FeedbackList`) already exist and cover the exact endpoints being fetched. For the third, the `GetArticleResponse` type already declares the three fields being cast through `as any` (`precisionScore`, `styleScore`, `feedbackComment` — generated file lines 12863–12865), so the casts are stale.

Risks of the current state:
- **Silent runtime breakage** if NSwag renames or restructures `baseUrl`/`http` — TypeScript will not catch it.
- **Lost type safety** — `as any` suppresses response-shape checks.
- **Inconsistency** — sibling hooks (`useListArticlesQuery`, `useGetArticleQuery` for unrelated fields) already use generated methods; these three are outliers.

Sibling hook `useSubmitArticleFeedbackMutation` (lines 220–253) also uses raw fetch but has a documented justification (HTTP 409 handling as a non-exceptional branch). It is explicitly **out of scope** for this refactor, with one minor caveat noted under Open Questions.

## Functional Requirements

### FR-1: Refactor `useArticleTraceQuery` to use `articles_GetTrace`
Replace the raw fetch implementation with a call to the generated `articles_GetTrace(id)` method on the authenticated API client. The hook's external contract (input parameters, return shape, query key, enabled flag, error semantics) MUST remain unchanged.

**Acceptance criteria:**
- File `frontend/src/api/hooks/useArticleTrace.ts` contains no `as any` casts on `apiClient`.
- File contains no manual URL construction (no string template referencing `/api/articles/.../trace`).
- The query function calls `client.articles_GetTrace(id)` where `client` is obtained via the standard authenticated-client accessor used elsewhere in the codebase (e.g. `getAuthenticatedApiClient()` or equivalent already-imported helper).
- Return value preserves `{ articleId, steps }` shape with the same fallbacks: `articleId ?? id`, `steps ?? []` cast to `ArticleGenerationStep[]`.
- All existing consumers of `useArticleTraceQuery` compile without modification.
- `npm run build` and `npm run lint` pass.

### FR-2: Refactor `useArticleFeedbackListQuery` to use `articles_FeedbackList`
Replace the raw fetch implementation with a call to the generated `articles_FeedbackList(...)` method, passing the existing parameters in the order the generator emits.

**Acceptance criteria:**
- File `frontend/src/api/hooks/useArticles.ts` `useArticleFeedbackListQuery` block contains no `as any` casts on `apiClient` and no manual URL construction for the feedback-list endpoint.
- Parameters are forwarded as: `(params.hasFeedback ?? null, params.requestedBy ?? null, params.sortBy, params.descending, params.page, params.pageSize)` — or whatever order the current generated signature declares; the implementation MUST match the generated method's parameter order verbatim.
- Return shape and query-key derivation are unchanged.
- All existing consumers compile without modification.

### FR-3: Remove stale `as any` casts in `useGetArticleQuery`
Remove the three `as any` casts on the response for `precisionScore`, `styleScore`, and `feedbackComment`. The typed `GetArticleResponse` already declares these fields.

**Acceptance criteria:**
- The three lines at `frontend/src/api/hooks/useArticles.ts:184-188` no longer contain `(response as any).` access.
- The fields are read directly from the typed response with the existing `?? null` fallback preserved.
- TypeScript compiles with no new errors.

### FR-4: Preserve external behavior
None of the three hooks may change their:
- React Query key shape or stability.
- `enabled` / `staleTime` / `gcTime` / retry / refetch options.
- Returned data shape consumed by components.
- Error-thrown semantics (errors from the generated client propagate the same way to React Query's `error` state).

**Acceptance criteria:**
- No changes are required in any component that consumes these hooks.
- Existing unit/integration tests for these hooks (if any) pass without modification, or are updated only to drop assertions about internal `fetch` calls.

### FR-5: Verification of unaffected sibling hooks
`useSubmitArticleFeedbackMutation` is **not** modified by this refactor. The reviewer flagged that its `(apiClient as any).baseUrl` URL construction carries the same fragility, but its 409-handling justification places it out of scope here.

**Acceptance criteria:**
- `useSubmitArticleFeedbackMutation` is untouched.
- A note is added (see Open Questions) flagging it for a follow-up review.

## Non-Functional Requirements

### NFR-1: Performance
No regression. The generated methods issue equivalent HTTP requests; if anything, response parsing may be marginally faster because the generated client skips ad-hoc JSON post-processing in the hooks.

### NFR-2: Type Safety
After the refactor, no `as any` cast may remain in the three target hooks (other than narrowly-scoped casts on enum-like `steps` array elements where the generated type is `unknown[]` or similar — see Open Questions). Specifically:
- Zero `(apiClient as any)` references in the three target hooks.
- Zero `(response as any)` references in `useGetArticleQuery`.

### NFR-3: Security
No change. Authentication continues to flow through the same `getAuthenticatedApiClient()` (or equivalent) accessor that already injects bearer tokens for the rest of the codebase.

### NFR-4: Maintainability
Pattern consistency: after the refactor, every Article hook except `useSubmitArticleFeedbackMutation` uses the generated client uniformly. New developers reading the file should find one pattern, not two.

## Data Model
No data-model changes. The refactor consumes the existing generated types:
- `GetArticleTraceResponse` with `articleId: string | undefined` and `steps: ArticleGenerationStep[] | undefined`.
- `GetArticleResponse` already declaring `precisionScore: number | null | undefined`, `styleScore: number | null | undefined`, `feedbackComment: string | null | undefined` (generated file lines 12863–12865).
- The feedback-list response type as currently emitted by NSwag for `articles_FeedbackList`.

## API / Interface Design

### Internal hook signatures — unchanged

```typescript
// useArticleTrace.ts
export function useArticleTraceQuery(id: string, options?: { enabled?: boolean }):
  UseQueryResult<{ articleId: string; steps: ArticleGenerationStep[] }, Error>;

// useArticles.ts
export function useArticleFeedbackListQuery(params: ArticleFeedbackListParams):
  UseQueryResult<ArticleFeedbackListResponse, Error>;

export function useGetArticleQuery(id: string):
  UseQueryResult<GetArticleResponse, Error>;
```

### Implementation pattern (target)

```typescript
// useArticleTraceQuery
const client = getAuthenticatedApiClient();
const data = await client.articles_GetTrace(id);
return {
  articleId: data.articleId ?? id,
  steps: (data.steps ?? []) as ArticleGenerationStep[],
};
```

```typescript
// useArticleFeedbackListQuery
const client = getAuthenticatedApiClient();
const data = await client.articles_FeedbackList(
  params.hasFeedback ?? null,
  params.requestedBy ?? null,
  params.sortBy,
  params.descending,
  params.page,
  params.pageSize,
);
return data;
```

```typescript
// useGetArticleQuery — within existing mapping block
precisionScore:  response.precisionScore  ?? null,
styleScore:      response.styleScore      ?? null,
feedbackComment: response.feedbackComment ?? null,
```

## Dependencies
- NSwag-generated client at `frontend/src/api/generated/` (or equivalent path) — must already expose `articles_GetTrace` (line 479) and `articles_FeedbackList` (line 601). Already present per the brief.
- `getAuthenticatedApiClient()` (or the project's standard accessor used by `useListArticlesQuery`) — already exists and is used elsewhere.
- React Query (`@tanstack/react-query`) — no version change.

No new packages, no backend changes, no API contract changes, no generated-client regeneration required.

## Out of Scope
- `useSubmitArticleFeedbackMutation` raw-fetch usage (has documented 409-handling justification).
- Adding new functionality to any of the three hooks.
- Changing React Query options (cache times, retry policy, etc.).
- Renaming hooks, files, or exported types.
- Refactoring components that consume these hooks.
- Touching backend endpoints or generated-client emission settings.
- Adding new tests beyond updating existing ones where they assert on now-removed `fetch` internals.

## Open Questions
1. **`steps` element typing.** If the generated `GetArticleTraceResponse.steps` element type does not match the local `ArticleGenerationStep` interface exactly, is a single narrow cast `as ArticleGenerationStep[]` acceptable (as the brief's suggested fix uses), or should the local interface be aligned with the generated type instead? **Assumption:** keep the narrow cast as in the brief's suggested code; alignment of `ArticleGenerationStep` with the generated type is a separate concern.
2. **Follow-up for `useSubmitArticleFeedbackMutation`.** Should this refactor also file a tracking issue / TODO comment flagging that mutation for a future review (it still uses `(apiClient as any).baseUrl`)? **Assumption:** add a one-line `// TODO` comment at the existing raw-fetch site referencing the same fragility, without changing behavior.
3. **Authenticated-client accessor name.** The brief uses `getAuthenticatedApiClient()`; confirm this matches the actual exported helper used by `useListArticlesQuery` in this codebase. **Assumption:** use whichever helper `useListArticlesQuery` already imports — if it differs, adopt the same one for consistency.

## Status: HAS_QUESTIONS