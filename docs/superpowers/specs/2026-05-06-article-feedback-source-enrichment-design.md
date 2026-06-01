# Article Feedback + Source Enrichment — Design

**Issue:** #933 — Phase 3 Article Generation Feedback + Source Enrichment
**Branch:** `feature/933-article-feedback-source-enrichment` (base: `feature/genai_consistency`)
**Date:** 2026-05-06

## Goal

Three additions to the article generation feature:

1. Feedback layer (1–5 precision/style scores + optional comment) on the `Article` entity
2. Proper population of `ArticleSource.KnowledgeBaseChunkId`, `Confidence`, `Excerpt`, `ValidationNote` from the pipeline
3. KB source citations in `ArticleDetail` become clickable `ChunkDetailModal` triggers instead of plain text

## Architecture & Implementation Order

Three tracks executed in sequence to minimise integration risk:

**Track 1 — Domain + Persistence (foundation):**
1. Add `PrecisionScore`, `StyleScore`, `FeedbackComment` to `Article`
2. Add `GetArticlesPagedAsync` + `GetFeedbackStatsAsync` to `IArticleRepository` + implementation
3. Add `ArticleFeedbackStats` sealed record
4. EF config update + migration `AddArticleFeedbackColumns`

**Track 2 — Pipeline fix (independent, no migration):**
5. Fix `WriteArticleStep.MapSources` — direct match against `context.ContextSnippets`
6. Update `GenerateArticleJob` to map all new `ArticleSource` fields

**Track 3 — Application + API + Frontend (depends on Track 1):**
7. `SubmitArticleFeedback` use case + error codes
8. `GetArticleFeedbackList` use case
9. API controller: 2 new endpoints
10. Update `GetArticleResponse` + `ArticleSourceDto`
11. Frontend: `RagFeedbackForm` shared component
12. Frontend: `ArticleDetail` — feedback form + KB source → ChunkDetailModal
13. Frontend: `useArticles` hooks + i18n

One migration. No breaking changes to existing endpoints.

## Backend Design

### Domain — `Article.cs`

Add three nullable properties following the flat-column pattern on `KnowledgeBaseQuestionLog`:

```csharp
public int? PrecisionScore { get; set; }
public int? StyleScore { get; set; }
public string? FeedbackComment { get; set; }
```

No separate feedback table.

### Persistence — `ArticleConfiguration.cs`

```csharp
builder.Property(a => a.FeedbackComment).HasColumnType("text");
builder.HasIndex(a => a.PrecisionScore).HasFilter("\"PrecisionScore\" IS NOT NULL");
```

EF infers nullable int columns automatically.

### Domain — `ArticleFeedbackStats.cs`

```csharp
public sealed record ArticleFeedbackStats(
    int TotalArticles,
    int TotalWithFeedback,
    double? AvgPrecisionScore,
    double? AvgStyleScore);
```

### Repository — `IArticleRepository` additions

```csharp
Task<(IReadOnlyList<Article> Items, int TotalCount)> GetArticlesPagedAsync(
    bool? hasFeedback, string? requestedBy, string sortBy, bool descending,
    int page, int pageSize, CancellationToken cancellationToken);
Task<ArticleFeedbackStats> GetFeedbackStatsAsync(CancellationToken cancellationToken);
```

`GetArticlesPagedAsync` applies:
- `hasFeedback=true` → `PrecisionScore != null || StyleScore != null`
- `hasFeedback=false` → both null
- `hasFeedback=null` → no filter
- Sort columns: `CreatedAt` (default), `PrecisionScore`, `StyleScore`

### Pipeline Fix — `WriteArticleStep.MapSources`

**Approach: direct match against `context.ContextSnippets`** (chosen over threading through `AggregatedFact` — same title-match reliability, fewer changes).

Updated signature:
```csharp
private static List<(string Title, string? Url, SourceType Type,
    Guid? ChunkId, double? Confidence, string? Excerpt, string? ValidationNote)>
    MapSources(List<SourceUsedDto>? sourcesUsed,
               List<ContextSnippet> snippets,
               List<AggregatedFact> facts)
```

For each `SourceUsedDto`:
- Match by title against `snippets` to get `ChunkId` and `Score` (→ `Confidence`)
- Match by title against `facts` to get `ValidationNote` and `Claim` (→ `Excerpt`, truncated to 200 chars)
- `Type = Url != null ? Web : KnowledgeBase`

### Pipeline Fix — `GenerateArticleJob`

Populate all `ArticleSource` fields:

```csharp
new ArticleSource
{
    Id = Guid.NewGuid(),
    ArticleId = articleId,
    Title = src.Title,
    Url = src.Url,
    Type = src.Type,
    KnowledgeBaseChunkId = src.ChunkId,
    Confidence = src.Confidence,
    Excerpt = src.Excerpt,
    ValidationNote = src.ValidationNote,
}
```

### Use Case — `SubmitArticleFeedback`

**Validation chain (fail fast):**
1. Article not found → `ArticleNotFound`
2. Current user != `article.RequestedBy` → `Forbidden`
3. `article.Status != Generated` → `ArticleNotGenerated`
4. Either score already set → `ArticleFeedbackAlreadySubmitted` (→ HTTP 409)

On success: set `PrecisionScore`, `StyleScore`, `FeedbackComment`, call `SaveChangesAsync`.

**Request:**
```csharp
public class SubmitArticleFeedbackRequest : IRequest<SubmitArticleFeedbackResponse>
{
    public Guid ArticleId { get; set; }
    [Range(1, 5)] public int PrecisionScore { get; set; }
    [Range(1, 5)] public int StyleScore { get; set; }
    [MaxLength(1000)] public string? Comment { get; set; }
}
```

### Use Case — `GetArticleFeedbackList`

Calls `GetArticlesPagedAsync` + `GetFeedbackStatsAsync` in parallel. Access gated by `ArticleGenerator` policy (same as existing `GetFeedbackList` KB endpoint).

Response shape mirrors KB/Leaflet pattern: `ArticleFeedbackSummary` list + `ArticleFeedbackStatsDto` + pagination metadata.

Sort columns: `CreatedAt`, `PrecisionScore`, `StyleScore`.

### API Controller — `ArticlesController` additions

```csharp
POST   /api/articles/{id}/feedback          → SubmitArticleFeedback (200 / 409)
GET    /api/articles/feedback/list          → GetArticleFeedbackList [ArticleGenerator role]
```

### DTO Updates

**`GetArticleResponse`:**
```csharp
public int? PrecisionScore { get; set; }
public int? StyleScore { get; set; }
public string? FeedbackComment { get; set; }
```

**`ArticleSourceDto`:**
```csharp
public Guid? KnowledgeBaseChunkId { get; set; }
public string? Excerpt { get; set; }
```

## Frontend Design

### `RagFeedbackForm` (`components/feedback/RagFeedbackForm.tsx`)

Shared, reusable component extracted from the inline KB pattern in `KnowledgeBaseSearchAskTab.tsx`.

**Props:**
```ts
interface RagFeedbackFormProps {
  onSubmit: (data: { precisionScore: number; styleScore: number; comment?: string }) => void
  isSubmitting: boolean
  isSuccess: boolean
  alreadySubmitted: boolean
}
```

Renders two `ScoreRow` components (1–5 dot selector), optional comment textarea, submit button (disabled until both scores selected). Shows success message on `isSuccess`. Shows "already submitted" notice when `alreadySubmitted`. No API calls — pure controlled form.

### `ArticleDetail.tsx` — Feedback Section

After the article iframe, conditional rendering:
- `status === 'Generated' && !precisionScore && !styleScore` → renders `RagFeedbackForm`
- Scores exist → read-only summary: "Hodnocení: Přesnost X/5, Styl Y/5"
- Uses `useSubmitArticleFeedbackMutation`; 409 response → `alreadySubmitted=true`

### `ArticleDetail.tsx` — Source List

`SourceList` adds `selectedChunkId: string | null` local state.

Source rendering logic:
- `source.url` → `<a href>` link (web source, unchanged)
- `source.knowledgeBaseChunkId` → `<button>` (green text, hover underline) → sets `selectedChunkId`
- neither → `<span>` (plain KB source without chunk)

When `selectedChunkId` is set: renders `ChunkDetailModal` (from `components/knowledge-base/ChunkDetailModal.tsx`) with `onClose` clearing state.

### `useArticles.ts` additions

```ts
useSubmitArticleFeedbackMutation()
  // mutationFn: client.articles_SubmitFeedback(articleId, data)
  // 409 → sets alreadySubmitted flag via onError

useArticleFeedbackListQuery(params: ArticleFeedbackListParams)
  // staleTime: 30_000
  // queryKey: articleKeys.feedbackList(params)
```

### i18n

Two new error codes in `i18n.ts`:
- `ArticleFeedbackAlreadySubmitted` (cs: "Zpětná vazba k tomuto článku již byla odeslána.", en: "Feedback for this article has already been submitted.")
- `ArticleNotGenerated` (cs: "Článek ještě nebyl vygenerován.", en: "Article has not been generated yet.")

## Testing

### Backend (xUnit)

| Test class | Cases |
|---|---|
| `SubmitArticleFeedbackHandlerTests` | not found, forbidden, not Generated, already submitted (409), success |
| `GetArticleFeedbackListHandlerTests` | paging, hasFeedback filter, stats aggregation |
| `WriteArticleStepTests` | ChunkId populated from matching snippet, null ChunkId for web sources |
| `ArticleRepositoryIntegrationTests` | GetArticlesPagedAsync with hasFeedback=true/false/null, sort by PrecisionScore |

### Frontend (Vitest + RTL)

| Test file | Cases |
|---|---|
| `RagFeedbackForm.test.tsx` | renders scores, disables submit until both selected, calls onSubmit, success state, alreadySubmitted notice |
| `ArticleDetail.test.tsx` | feedback form shown when Generated+no scores, hidden when scores present, KB source as button, click opens modal, ESC closes modal |
| `useArticles.test.ts` | 409 maps to alreadySubmitted |

**Coverage target: 80%+ on new code.**

## Verification Checklist

- [ ] `dotnet build` passes, `dotnet format` clean
- [ ] `npm run build` passes, `npm run lint` clean
- [ ] Migration applies cleanly
- [ ] Generate article → Generated status → feedback form appears
- [ ] Submit feedback → DB has PrecisionScore/StyleScore
- [ ] Submit again → HTTP 409 → UI shows already-submitted message
- [ ] KB source in article detail → click → ChunkDetailModal opens with correct chunk
- [ ] `GET /api/articles/feedback/list` returns paged data + stats
- [ ] All new tests pass
