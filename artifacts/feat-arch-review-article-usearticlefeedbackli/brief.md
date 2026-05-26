## Module
Article

## Finding
`useArticleFeedbackListQuery` in `useArticles.ts` uses raw `fetch` (bypassing the NSwag-generated client) and does `return response.json()` directly, casting the result to `ArticleFeedbackListResponse`. The TypeScript type and the actual JSON shape do not match on multiple fields:

### Top-level collection key mismatch

| Backend (`GetArticleFeedbackListResponse`) | Frontend TypeScript `ArticleFeedbackListResponse` |
|---|---|
| `items: ArticleFeedbackSummary[]` | `articles: ArticleFeedbackSummary[]` |

`useArticles.ts:98` declares `articles: ArticleFeedbackSummary[]`, but the serialised JSON from `GetArticleFeedbackListHandler` has the key `items`. Any component that accesses `.articles` gets `undefined` at runtime.

### Per-item field mismatches

| Backend `ArticleFeedbackSummary` (`GetArticleFeedbackListRequest.cs:33`) | Frontend `ArticleFeedbackSummary` (`useArticles.ts:78`) |
|---|---|
| `CreatedAt: DateTimeOffset` | `generatedAt: string \| null` (key mismatch) |
| `HasComment: bool` | `hasFeedback: boolean` (key mismatch — semantically different too) |
| *(not present)* | `feedbackComment: string \| null` (extra field that will always be `undefined`) |

The handler maps `HasComment = !string.IsNullOrWhiteSpace(a.FeedbackComment)` (`GetArticleFeedbackListHandler.cs:54`), so the backend sends `hasComment` (a boolean indicating comment presence), not `feedbackComment` (the actual text). The frontend TypeScript type expects `feedbackComment` (the text) and will never receive it from this endpoint.

All mismatches are invisible at compile time because the query function casts directly without any field-level mapping (`return response.json()`).

## Why it matters
- At runtime, any component consuming the feedback list will see `undefined` for `articles`, `generatedAt`, `feedbackComment`, and `hasFeedback`.
- TypeScript provides no protection because the cast bypasses the type system.
- Contrast with `useGetArticleQuery` which does an explicit field-by-field mapping (lines 155-188 in `useArticles.ts`), making the intent of that query clear. The feedback list query does the opposite, making it appear safe while hiding the divergence.

## Suggested fix
Add an explicit mapping in `useArticleFeedbackListQuery` that mirrors the pattern used in `useGetArticleQuery`:

```typescript
const raw = await response.json();
return {
  articles: (raw.items ?? []).map((a: any) => ({
    id: a.id,
    topic: a.topic ?? '',
    title: a.title ?? null,
    requestedBy: a.requestedBy ?? '',
    createdAt: a.createdAt ?? null,          // align TS field name with backend
    precisionScore: a.precisionScore ?? null,
    styleScore: a.styleScore ?? null,
    hasComment: a.hasComment ?? false,       // rename to match backend
  })),
  totalCount: raw.totalCount ?? 0,
  page: raw.page ?? 1,
  pageSize: raw.pageSize ?? 20,
  totalPages: raw.totalPages ?? 0,
  stats: raw.stats ?? { totalArticles: 0, totalWithFeedback: 0, avgPrecisionScore: null, avgStyleScore: null },
};
```

Also update the `ArticleFeedbackSummary` TypeScript interface and any consuming components to use `articles`, `createdAt`, and `hasComment`.

---
_Filed by daily arch-review routine on 2026-05-25._