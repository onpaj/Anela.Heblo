# Specification: Refactor Article Frontend Hooks to Use Generated NSwag Client Methods

## Summary
Three Article-module React Query hooks in `frontend/src/api/hooks/` bypass the typed NSwag-generated client by accessing private fields (`baseUrl`, `http`) via `as any` casts and constructing raw `fetch` calls. This spec defines the refactor to call the typed generated methods directly, restoring type safety and removing fragile reliance on private internals, while preserving every consumer-facing contract through explicit DTO-to-local-shape mapping.

## Background
The frontend uses an NSwag-generated TypeScript API client whose `baseUrl` and `http` fields are declared `private` (`frontend/src/api/generated/api-client.ts:12-13`). Three hooks circumvent this:

- `useArticleTraceQuery` — `frontend/src/api/hooks/useArticleTrace.ts:28-33`
- `useArticleFeedbackListQuery` — `frontend/src/api/hooks/useArticles.ts:276-278`
- `useGetArticleQuery` — `frontend/src/api/hooks/useArticles.ts:184-188`

For the first two, typed generated methods (`articles_GetTrace`, `articles_FeedbackList`) already exist and cover the exact endpoints being fetched. For the third, the `GetArticleResponse` type already declares the three fields being cast through `as any` (`precisionScore`, `styleScore`, `feedbackComment` — generated file lines 12863–12865), so the casts are stale.

Risks of the current state:
- **Silent runtime breakage** if NSwag renames or restructures `baseUrl`/`http` — TypeScript will not catch it.
- **Lost type safety** — `as any` suppresses response-shape checks.
- **Inconsistency** — sibling hooks (`useListArticlesQuery`, the rest of `useGetArticleQuery`) already use generated methods; these three are outliers.

The generated DTOs for the trace and feedback-list endpoints have shapes that genuinely differ from the local consumer-facing interfaces (different field names, optional vs required fields, `Date` vs `string` types). Therefore the refactor must not return generated DTOs directly and must not use blanket casts — it must project each generated DTO into the existing local shape, mirroring the explicit-mapping pattern already established in `useGetArticleQuery` for `sources`, `createdAt`, and `generatedAt` (`frontend/src/api/hooks/useArticles.ts:166-182`).

Sibling hook `useSubmitArticleFeedbackMutation` (lines 220–253) also uses raw fetch but has a documented justification (HTTP 409 handling as a non-exceptional branch). It is explicitly **out of scope** for this refactor; a single dated `// TODO` comment will be added at its raw-fetch site to flag the residual fragility for future review.

## Functional Requirements

### FR-1: Refactor `useArticleTraceQuery` to use `articles_GetTrace`
Replace the raw fetch implementation with a call to the generated `articles_GetTrace(id)` method on the authenticated API client, then explicitly project each generated `ArticleGenerationStepDto` into the existing local `ArticleGenerationStep` shape. The hook's external contract (input parameters, return shape, query key, `enabled` flag, error semantics) MUST remain unchanged.

**Acceptance criteria:**
- File `frontend/src/api/hooks/useArticleTrace.ts` contains no `as any` casts on `apiClient`.
- File contains no manual URL construction (no string template referencing `/api/articles/.../trace`).
- The query function obtains the client via the already-imported `getAuthenticatedApiClient()` accessor and calls `client.articles_GetTrace(id)`.
- The local `ArticleGenerationStep` interface is **not** modified.
- No blanket cast (`as ArticleGenerationStep[]`) is used; instead each step is mapped explicitly:
  ```typescript
  steps: (data.steps ?? []).map((step) => ({
    id: step.id ?? '',
    stepName: step.stepName ?? '',
    sequence: step.sequence ?? 0,
    status: step.status ?? '',
    startedAt: step.startedAt?.toISOString() ?? '',
    finishedAt: step.finishedAt?.toISOString() ?? null,
    durationMs: step.durationMs ?? null,
    model: step.model ?? null,
    inputJson: step.inputJson ?? null,
    outputJson: step.outputJson ?? null,
    errorMessage: step.errorMessage ?? null,
  })),
  ```
- Return value preserves `{ articleId, steps }` shape with the `articleId ?? id` fallback.
- All existing consumers of `useArticleTraceQuery` compile without modification.

### FR-2: Refactor `useArticleFeedbackListQuery` to use `articles_FeedbackList`
Replace the raw fetch implementation with a call to the generated `articles_FeedbackList(...)` method, passing parameters in the exact order the generated signature declares. Then project the generated `GetArticleFeedbackListResponse` into the existing local `ArticleFeedbackListResponse` shape that `useArticleFeedbackAdapter.ts:15-25` consumes.

**Acceptance criteria:**
- The `useArticleFeedbackListQuery` block in `frontend/src/api/hooks/useArticles.ts` contains no `as any` casts on `apiClient` and no manual URL construction for the feedback-list endpoint.
- Parameters are forwarded in the exact order the generated method declares (per the brief: `params.hasFeedback ?? null, params.requestedBy ?? null, params.sortBy, params.descending, params.page, params.pageSize` — adjust verbatim if the generated signature differs).
- The hook does **not** `return data` directly. It explicitly projects the generated DTO into the local `ArticleFeedbackListResponse` interface, applying at minimum these field translations (confirm exact set against the generated type at refactor time):
  - `articles ← items`
  - `generatedAt ← createdAt?.toISOString() ?? ''` (per item)
  - `hasFeedback ← hasComment`
  - `feedbackComment ← null` (field is absent on the generated DTO; preserve the consumer-facing field as `null`)
  - All other top-level pagination fields (`page`, `pageSize`, `totalCount`, etc.) are forwarded under their existing local names.
- Query-key derivation, `enabled`, and all other React Query options are unchanged.
- All existing consumers (notably `useArticleFeedbackAdapter.ts`) compile and behave identically without modification.

### FR-3: Remove stale `as any` casts in `useGetArticleQuery`
Remove the three `as any` casts on the response for `precisionScore`, `styleScore`, and `feedbackComment`. The typed `GetArticleResponse` already declares these fields (generated file lines 12863–12865).

**Acceptance criteria:**
- The three lines at `frontend/src/api/hooks/useArticles.ts:184-188` no longer contain `(response as any).` access.
- The fields are read directly from the typed response with the existing `?? null` fallback preserved:
  ```typescript
  precisionScore:  response.precisionScore  ?? null,
  styleScore:      response.styleScore      ?? null,
  feedbackComment: response.feedbackComment ?? null,
  ```
- TypeScript compiles with no new errors.

### FR-4: Preserve external behavior
None of the three hooks may change their:
- React Query key shape or stability.
- `enabled` / `staleTime` / `gcTime` / retry / refetch options.
- Returned data shape consumed by components or adapter hooks.
- Error-thrown semantics (errors from the generated client propagate the same way to React Query's `error` state).

**Acceptance criteria:**
- No changes are required in any component or adapter (`useArticleFeedbackAdapter.ts`, etc.) that consumes these hooks.
- Existing unit/integration tests for these hooks pass without modification, or are updated only to drop assertions about internal `fetch` calls.

### FR-5: Flag `useSubmitArticleFeedbackMutation` with a dated TODO
`useSubmitArticleFeedbackMutation` is **not** modified by this refactor. To meet the flagging requirement without opening a separate GitHub issue (this is a solo-dev workspace with AI-assisted PR review per `CLAUDE.md`), add exactly one single-line `// TODO` comment at the raw-fetch site.

**Acceptance criteria:**
- The mutation's behavior, fetch call, and 409-handling branch are untouched.
- Immediately above the `const fullUrl = ...` line at `frontend/src/api/hooks/useArticles.ts:222`, add:
  ```typescript
  // TODO(arch-review 2026-05-25): Uses private apiClient internals (baseUrl/http) via `as any` — same fragility as the hooks refactored in this PR. Keep raw fetch only for 409 branch; revisit when generated client exposes typed-mutation 409 handling.
  ```
- No new GitHub issue is created.

## Non-Functional Requirements

### NFR-1: Performance
No regression. The generated methods issue equivalent HTTP requests; response parsing may be marginally faster because the generated client skips ad-hoc JSON post-processing in the hooks.

### NFR-2: Type Safety
After the refactor, the three target hooks contain:
- Zero `(apiClient as any)` references.
- Zero `(response as any)` references.
- Zero blanket casts on collections (no `as ArticleGenerationStep[]`, no `as ArticleFeedbackListResponse`). Type bridging between generated DTOs and local interfaces is done by explicit per-field projection.

### NFR-3: Security
No change. Authentication continues to flow through the same `getAuthenticatedApiClient()` accessor (`frontend/src/api/client.ts:232`) that already injects bearer tokens for every Article hook today.

### NFR-4: Maintainability
Pattern consistency: after the refactor, every Article hook except `useSubmitArticleFeedbackMutation` uses the generated client uniformly, and every generated-DTO-to-local-shape conversion uses the same explicit-mapping pattern already established by `useGetArticleQuery` for `sources`, `createdAt`, and `generatedAt`. New developers reading the file find one pattern, not two.

## Data Model
No data-model changes. The refactor consumes the existing generated types and preserves the existing local interfaces:

**Generated (read-only inputs to the hooks):**
- `GetArticleTraceResponse` with `articleId: string | undefined`, `steps: ArticleGenerationStepDto[] | undefined`.
- `ArticleGenerationStepDto` (`frontend/src/api/generated/api-client.ts:13077-13151`) — every field optional; `startedAt`/`finishedAt` are `Date | undefined`.
- `GetArticleResponse` already declaring `precisionScore`, `styleScore`, `feedbackComment` (generated file lines 12863–12865).
- `GetArticleFeedbackListResponse` with `items` (not `articles`), each item carrying `createdAt`/`hasComment` (not `generatedAt`/`hasFeedback`), no `feedbackComment` field.

**Local (consumer-facing, unchanged):**
- `ArticleGenerationStep` (`frontend/src/api/hooks/useArticleTrace.ts:4-16`) — fields required, dates as `string`/`string | null`.
- `ArticleFeedbackListResponse` (existing local interface consumed by `useArticleFeedbackAdapter.ts:15-25`) — uses `articles`, `generatedAt`, `hasFeedback`, `feedbackComment`.

The refactor's mapping functions are the bridge between these two sets of types.

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

**`useArticleTraceQuery`:**
```typescript
const client = getAuthenticatedApiClient();
const data = await client.articles_GetTrace(id);
return {
  articleId: data.articleId ?? id,
  steps: (data.steps ?? []).map((step) => ({
    id: step.id ?? '',
    stepName: step.stepName ?? '',
    sequence: step.sequence ?? 0,
    status: step.status ?? '',
    startedAt: step.startedAt?.toISOString() ?? '',
    finishedAt: step.finishedAt?.toISOString() ?? null,
    durationMs: step.durationMs ?? null,
    model: step.model ?? null,
    inputJson: step.inputJson ?? null,
    outputJson: step.outputJson ?? null,
    errorMessage: step.errorMessage ?? null,
  })),
};
```

**`useArticleFeedbackListQuery`:**
```typescript
const client = getAuthenticatedApiClient();
const data = await client.articles_FeedbackList(
  params.hasFeedback ?? null,
  params.requestedBy ?? null,
  params.sortBy,
  params.descending,
  params.page,
  params.pageSize,
);
return {
  articles: (data.items ?? []).map((item) => ({
    // ...forward identical fields verbatim...
    generatedAt: item.createdAt?.toISOString() ?? '',
    hasFeedback: item.hasComment ?? false,
    feedbackComment: null,
  })),
  page: data.page ?? params.page,
  pageSize: data.pageSize ?? params.pageSize,
  totalCount: data.totalCount ?? 0,
  // ...any other pagination/meta fields the local interface declares...
};
```
(The exact item-level field set is determined by reading the current local `ArticleFeedbackListResponse` interface at refactor time; the contract is "every field the consumer reads must be present with its current name and type.")

**`useGetArticleQuery` — within the existing mapping block:**
```typescript
precisionScore:  response.precisionScore  ?? null,
styleScore:      response.styleScore      ?? null,
feedbackComment: response.feedbackComment ?? null,
```

**`useSubmitArticleFeedbackMutation` — TODO only, no code change:**
```typescript
// TODO(arch-review 2026-05-25): Uses private apiClient internals (baseUrl/http) via `as any` — same fragility as the hooks refactored in this PR. Keep raw fetch only for 409 branch; revisit when generated client exposes typed-mutation 409 handling.
const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
```

## Dependencies
- NSwag-generated client at `frontend/src/api/generated/api-client.ts` — must already expose `articles_GetTrace` (line 479) and `articles_FeedbackList` (line 601). Confirmed present per the brief.
- `getAuthenticatedApiClient()` exported from `frontend/src/api/client.ts:232` — already imported by `useArticleTrace.ts:2` and `useArticles.ts:2`. Do **not** substitute `getAuthenticatedApiClientWithProvider` (`client.ts:342`); it is for custom-token-provider callers and would expand scope beyond this refactor.
- React Query (`@tanstack/react-query`) — no version change.

No new packages, no backend changes, no API contract changes, no generated-client regeneration required.

## Out of Scope
- Behavioral change to `useSubmitArticleFeedbackMutation` (TODO comment only; documented in FR-5).
- Adding new functionality to any of the three refactored hooks.
- Changing React Query options (cache times, retry policy, etc.).
- Renaming hooks, files, or exported types.
- Modifying the local `ArticleGenerationStep` or `ArticleFeedbackListResponse` interfaces to match generated shapes — these are consumer-facing contracts and stay untouched.
- Refactoring components or adapter hooks (`useArticleFeedbackAdapter.ts`) that consume these hooks.
- Touching backend endpoints or generated-client emission settings.
- Adding new tests beyond updating existing ones where they assert on now-removed `fetch` internals.
- Opening a separate GitHub issue for the `useSubmitArticleFeedbackMutation` follow-up.

## Open Questions
None.

## Status: COMPLETE