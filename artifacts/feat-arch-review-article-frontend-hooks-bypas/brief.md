## Module
Article

## Finding

Three hooks in `frontend/src/api/hooks/` access private implementation details of the generated NSwag client via `as any` casts, when typed generated methods already exist for all three endpoints.

**1. `useArticleTraceQuery` — `frontend/src/api/hooks/useArticleTrace.ts:28-33`**
```typescript
const fullUrl = `${(apiClient as any).baseUrl}/api/articles/${id}/trace`;
const response = await (apiClient as any).http.fetch(fullUrl, { ... });
```
The generated client already has `articles_GetTrace(id: string): Promise<GetArticleTraceResponse>` (generated file line 479). This hook constructs a raw fetch for no apparent reason.

**2. `useArticleFeedbackListQuery` — `frontend/src/api/hooks/useArticles.ts:276-278`**
```typescript
const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
const response = await (apiClient as any).http.fetch(fullUrl, { ... });
```
The generated client has `articles_FeedbackList(...)` (generated file line 601). Again bypassed without justification.

**3. `useGetArticleQuery` — `frontend/src/api/hooks/useArticles.ts:184-188`**
```typescript
precisionScore: ((response as any).precisionScore as number | null) ?? null,
styleScore:     ((response as any).styleScore     as number | null) ?? null,
feedbackComment:((response as any).feedbackComment as string | null) ?? null,
```
The generated `GetArticleResponse` type already declares these three fields (generated file lines 12863–12865). The `as any` casts are stale workarounds — probably left over from a time before the backend added these fields and the client was regenerated.

**Why `baseUrl` and `http` are fragile:** Both are declared `private` in the generated client (generated file lines 12–13):
```typescript
private http: { fetch(url: RequestInfo, init?: RequestInit): Promise<Response> };
private baseUrl: string;
```
Accessing private fields via `as any` means any NSwag version bump that renames or restructures these internals silently breaks the hooks at runtime with no TypeScript error.

Note: `useSubmitArticleFeedbackMutation` (lines 220–253) also uses raw fetch but has a partial justification — it needs to handle HTTP 409 as a non-exceptional branch. Still, the URL construction via `(apiClient as any).baseUrl` carries the same fragility.

## Why it matters

- **Silent runtime breakage**: if NSwag changes internal field names, hooks fail with a runtime `TypeError` and no compile-time signal.
- **Type safety lost**: `as any` escapes suppress TypeScript checks on the response shape; typed generated methods provide full inference.
- **Inconsistency**: `useListArticlesQuery` and `useGetArticleQuery` call generated methods correctly; three sibling hooks do not — inconsistent patterns make the codebase harder to maintain.

## Suggested fix

**`useArticleTraceQuery`** — replace raw fetch with the generated method:
```typescript
const client = getAuthenticatedApiClient();
const data = await client.articles_GetTrace(id);
return { articleId: data.articleId ?? id, steps: (data.steps ?? []) as ArticleGenerationStep[] };
```

**`useArticleFeedbackListQuery`** — replace raw fetch with:
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
```

**`useGetArticleQuery`** — remove the three `as any` casts; the typed response already has these fields:
```typescript
precisionScore: response.precisionScore ?? null,
styleScore:     response.styleScore     ?? null,
feedbackComment:response.feedbackComment ?? null,
```

---
_Filed by daily arch-review routine on 2026-05-25._