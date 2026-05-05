# Phase 3 — Article Generation Feedback + Source Enrichment

## Goal
Articles already persist outputs. This phase adds:
1. Feedback layer (1–5 precision/style + comment) on the `Article` entity.
2. Proper population of `ArticleSource.KnowledgeBaseChunkId`, `Confidence`, `Excerpt`, `ValidationNote` from the pipeline.
3. KB source citations in `ArticleDetail` become clickable `ChunkDetailModal` triggers instead of plain text.

## Dependency
Phase 1 must be complete (sidebar entry + shared `ChunkDetailModal` accessible). Phase 2 is independent.

---

## Step 1 — Domain: add feedback fields to `Article`

**File**: `backend/src/Anela.Heblo.Domain/Features/Article/Article.cs`

Add three nullable properties:
```csharp
public int? PrecisionScore { get; set; }
public int? StyleScore { get; set; }
public string? FeedbackComment { get; set; }
```

These mirror the flat-column pattern on `KnowledgeBaseQuestionLog` — no separate feedback table needed.

---

## Step 2 — Domain: update repository interface

**File**: `backend/src/Anela.Heblo.Domain/Features/Article/IArticleRepository.cs`

Add:
```csharp
Task<(IReadOnlyList<Article> Items, int TotalCount)> GetArticlesPagedAsync(
    bool? hasFeedback, string? requestedBy, string sortBy, bool descending,
    int page, int pageSize, CancellationToken cancellationToken);
Task<ArticleFeedbackStats> GetFeedbackStatsAsync(CancellationToken cancellationToken);
```

**New file**: `backend/src/Anela.Heblo.Domain/Features/Article/ArticleFeedbackStats.cs`
```csharp
namespace Anela.Heblo.Domain.Features.Article;

public sealed record ArticleFeedbackStats(
    int TotalArticles,
    int TotalWithFeedback,
    double? AvgPrecisionScore,
    double? AvgStyleScore);
```

---

## Step 3 — Persistence: EF configuration update

**File**: `backend/src/Anela.Heblo.Persistence/Features/Article/ArticleConfiguration.cs`

Add configuration for the three new columns:
```csharp
builder.Property(a => a.FeedbackComment).HasColumnType("text");
builder.HasIndex(a => a.PrecisionScore).HasFilter("\"PrecisionScore\" IS NOT NULL");
```

(EF infers `int?` as nullable int column automatically — no explicit configuration needed for score columns.)

---

## Step 4 — Persistence: repository implementation

**File**: `backend/src/Anela.Heblo.Persistence/Features/Article/ArticleRepository.cs`

Implement `GetArticlesPagedAsync` (filtered/sorted/paged version of what `ListArticles` already has, extended with feedback filter):

```csharp
public async Task<(IReadOnlyList<Article> Items, int TotalCount)> GetArticlesPagedAsync(
    bool? hasFeedback, string? requestedBy, string sortBy, bool descending,
    int page, int pageSize, CancellationToken cancellationToken)
{
    var query = _context.Articles.AsQueryable();

    if (hasFeedback == true)
        query = query.Where(a => a.PrecisionScore != null || a.StyleScore != null);
    else if (hasFeedback == false)
        query = query.Where(a => a.PrecisionScore == null && a.StyleScore == null);

    if (!string.IsNullOrWhiteSpace(requestedBy))
        query = query.Where(a => a.RequestedBy == requestedBy);

    query = (sortBy, descending) switch
    {
        ("PrecisionScore", true)  => query.OrderByDescending(a => a.PrecisionScore),
        ("PrecisionScore", false) => query.OrderBy(a => a.PrecisionScore),
        ("StyleScore", true)      => query.OrderByDescending(a => a.StyleScore),
        ("StyleScore", false)     => query.OrderBy(a => a.StyleScore),
        (_, true)                 => query.OrderByDescending(a => a.CreatedAt),
        _                         => query.OrderBy(a => a.CreatedAt),
    };

    var total = await query.CountAsync(cancellationToken);
    var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
    return (items, total);
}

public async Task<ArticleFeedbackStats> GetFeedbackStatsAsync(CancellationToken cancellationToken)
{
    var total = await _context.Articles.CountAsync(cancellationToken);
    var withFeedback = await _context.Articles
        .CountAsync(a => a.PrecisionScore != null || a.StyleScore != null, cancellationToken);
    var avgPrecision = await _context.Articles
        .Where(a => a.PrecisionScore != null)
        .AverageAsync(a => (double?)a.PrecisionScore, cancellationToken);
    var avgStyle = await _context.Articles
        .Where(a => a.StyleScore != null)
        .AverageAsync(a => (double?)a.StyleScore, cancellationToken);
    return new ArticleFeedbackStats(total, withFeedback, avgPrecision, avgStyle);
}
```

---

## Step 5 — Migration

Name: `AddArticleFeedbackColumns` (next sequential after `20260504195511_AddArticles`).

```
dotnet ef migrations add AddArticleFeedbackColumns \
  --project backend/src/Anela.Heblo.Persistence \
  --startup-project backend/src/Anela.Heblo.API
```

Expected migration:
- `AlterTable("Articles")` adding three nullable columns: `PrecisionScore int`, `StyleScore int`, `FeedbackComment text`.
- `CreateIndex("IX_Articles_PrecisionScore", ..., filter: "\"PrecisionScore\" IS NOT NULL")`.

---

## Step 6 — Fix `WriteArticleStep.MapSources` for KB chunk enrichment

**File**: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs`

### Problem
`MapSources` (line 116–125) only classifies `SourceType.Web` vs `SourceType.KnowledgeBase` by checking for a URL. It never populates `KnowledgeBaseChunkId`, `Confidence`, `Excerpt`, or `ValidationNote`.

### Fix strategy
The LLM in `WriteArticleStep` references sources by title/url from its JSON output. The actual KB chunks (with their `Id`) live in `ArticlePipelineContext` — set in `GatherContextStep`.

### 6a. Check `ArticlePipelineContext`

**File**: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/ArticlePipelineContext.cs`

Verify whether KB snippet objects (`ContextSnippet`) carry the chunk ID. If they look like:
```csharp
public sealed class ContextSnippet
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    // potentially: Guid? ChunkId, double? Score, string? SourcePath
}
```
If `ChunkId` is absent, add it.

### 6b. Update `GatherContextStep`

**File**: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs`

Where KB search results are mapped into `ContextSnippet` objects, ensure `ChunkId` and `Score` (for `Confidence`) are captured. The step calls `IMediator.Send(new SearchDocumentsRequest{...})` and gets back `ChunkResult[]`. Map:
```csharp
new ContextSnippet
{
    Title = chunk.SourceFilename,
    Content = chunk.Content,
    ChunkId = chunk.ChunkId,
    Score = chunk.Score,   // this becomes Confidence
}
```

### 6c. Update `AggregateFactsStep` and `ValidateFactsStep`

These steps may further lose chunk provenance. Ensure `AggregatedFact.SourceChunkId` (add if missing) is threaded through from `ContextSnippet.ChunkId`.

### 6d. Update `WriteArticleStep.MapSources`

Change the method signature and logic to accept context snippets:

```csharp
private static List<(string Title, string? Url, SourceType Type, Guid? ChunkId, double? Confidence, string? Excerpt, string? ValidationNote)>
    MapSources(List<SourceUsedDto>? sourcesUsed, List<AggregatedFact> facts)
{
    if (sourcesUsed == null) return [];

    return sourcesUsed.Select(s =>
    {
        var matchingFact = facts.FirstOrDefault(f =>
            f.SourceTitle == s.Title || f.SourceUrl == s.Url);

        var type = s.Url != null ? SourceType.Web : SourceType.KnowledgeBase;
        var chunkId = type == SourceType.KnowledgeBase ? matchingFact?.SourceChunkId : null;
        var confidence = matchingFact?.Score;
        var excerpt = matchingFact?.Claim is { Length: > 0 } claim
            ? (claim.Length > 200 ? claim[..200] : claim)
            : null;
        var note = matchingFact?.ValidationNote;

        return (s.Title, s.Url, type, chunkId, confidence, excerpt, note);
    }).ToList();
}
```

### 6e. Update `GenerateArticleJob` — `Article.Sources` write

**File**: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/GenerateArticleJob.cs`

Where `context.SourceRefs` are mapped into `ArticleSource` objects, populate the new fields:
```csharp
new ArticleSource
{
    Id = Guid.NewGuid(),
    ArticleId = article.Id,
    Title = src.Title,
    Url = src.Url,
    Type = src.Type,
    KnowledgeBaseChunkId = src.ChunkId,
    Confidence = src.Confidence,
    Excerpt = src.Excerpt,
    ValidationNote = src.ValidationNote,
}
```

---

## Step 7 — Application: `SubmitArticleFeedback`

**New files** under `backend/src/Anela.Heblo.Application/Features/Article/UseCases/SubmitArticleFeedback/`:

`SubmitArticleFeedbackRequest.cs`:
```csharp
public class SubmitArticleFeedbackRequest : IRequest<SubmitArticleFeedbackResponse>
{
    public Guid ArticleId { get; set; }
    [Range(1, 5)] public int PrecisionScore { get; set; }
    [Range(1, 5)] public int StyleScore { get; set; }
    [MaxLength(1000)] public string? Comment { get; set; }
}
```

`SubmitArticleFeedbackHandler.cs`:
```csharp
public async Task<SubmitArticleFeedbackResponse> Handle(
    SubmitArticleFeedbackRequest request, CancellationToken cancellationToken)
{
    var article = await _repository.GetByIdAsync(request.ArticleId, cancellationToken);
    if (article is null)
        return new SubmitArticleFeedbackResponse(ErrorCodes.ArticleNotFound, ...);

    var currentUser = _currentUserService.GetCurrentUser();
    if (article.RequestedBy != currentUser.Id)
        return new SubmitArticleFeedbackResponse(ErrorCodes.Forbidden, ...);

    if (article.PrecisionScore is not null || article.StyleScore is not null)
        return new SubmitArticleFeedbackResponse(ErrorCodes.ArticleFeedbackAlreadySubmitted, ...);

    if (article.Status != ArticleStatus.Generated)
        return new SubmitArticleFeedbackResponse(ErrorCodes.ArticleNotGenerated, ...);

    article.PrecisionScore = request.PrecisionScore;
    article.StyleScore = request.StyleScore;
    article.FeedbackComment = request.Comment;

    await _repository.SaveChangesAsync(cancellationToken);
    return new SubmitArticleFeedbackResponse();
}
```

Add error codes: `ArticleNotFound`, `ArticleFeedbackAlreadySubmitted`, `ArticleNotGenerated`.

---

## Step 8 — Application: `GetArticleFeedbackList`

**New files** under `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetArticleFeedbackList/`:

Pattern identical to KB's `GetFeedbackListHandler` and Leaflet's `GetLeafletFeedbackListHandler`. Sort columns: `["CreatedAt", "PrecisionScore", "StyleScore"]`.

Response shape (`GetArticleFeedbackListResponse`):
```csharp
public class GetArticleFeedbackListResponse : BaseResponse
{
    public List<ArticleFeedbackSummary> Articles { get; set; } = [];
    public int TotalCount { get; set; }
    public int PageNumber { get; set; }
    public int PageSize { get; set; }
    public ArticleFeedbackStatsDto Stats { get; set; } = new();
}

public class ArticleFeedbackSummary
{
    public Guid Id { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string? Title { get; set; }
    public ArticleStatus Status { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
    public string? RequestedBy { get; set; }
    public int? PrecisionScore { get; set; }
    public int? StyleScore { get; set; }
    public string? FeedbackComment { get; set; }
}

public class ArticleFeedbackStatsDto
{
    public int TotalArticles { get; set; }
    public int TotalWithFeedback { get; set; }
    public double? AvgPrecisionScore { get; set; }
    public double? AvgStyleScore { get; set; }
}
```

---

## Step 9 — API controller additions

**File**: `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs`

Add two actions:

```csharp
[HttpPost("{id:guid}/feedback")]
[ProducesResponseType(typeof(SubmitArticleFeedbackResponse), 200)]
[ProducesResponseType(typeof(ProblemDetails), 409)]
public async Task<ActionResult<SubmitArticleFeedbackResponse>> SubmitFeedback(
    Guid id, [FromBody] SubmitArticleFeedbackRequest request, CancellationToken ct)
{
    request.ArticleId = id;
    var result = await _mediator.Send(request, ct);
    return HandleResponse(result);
}

[HttpGet("feedback/list")]
[Authorize(Policy = AuthorizationConstants.Policies.ArticleGenerator)]
public async Task<ActionResult<GetArticleFeedbackListResponse>> GetFeedbackList(
    [FromQuery] bool? hasFeedback = null,
    [FromQuery] string? requestedBy = null,
    [FromQuery] string sortBy = "CreatedAt",
    [FromQuery] bool sortDescending = true,
    [FromQuery] int pageNumber = 1,
    [FromQuery] int pageSize = 20,
    CancellationToken ct = default)
{
    var result = await _mediator.Send(new GetArticleFeedbackListRequest
    {
        HasFeedback = hasFeedback,
        RequestedBy = requestedBy,
        SortBy = sortBy,
        SortDescending = sortDescending,
        PageNumber = pageNumber,
        PageSize = pageSize,
    }, ct);
    return HandleResponse(result);
}
```

---

## Step 10 — Update `GetArticleResponse` — expose feedback fields

**File**: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetArticle/GetArticleResponse.cs`

Add to the response DTO:
```csharp
public int? PrecisionScore { get; set; }
public int? StyleScore { get; set; }
public string? FeedbackComment { get; set; }
```

**File**: `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GetArticle/GetArticleHandler.cs`

Map these fields when building the response.

Also add `KnowledgeBaseChunkId` to `ArticleSourceDto` (if not already present) so the frontend can trigger the chunk modal:
```csharp
public class ArticleSourceDto
{
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    public string Type { get; set; } = "";
    public Guid? KnowledgeBaseChunkId { get; set; }  // new
    public string? Excerpt { get; set; }             // new
}
```

---

## Step 11 — Frontend: feedback form on `ArticleDetail`

**File**: `frontend/src/features/articles/ArticleDetail.tsx`

Import `RagFeedbackForm` from `../../components/feedback/RagFeedbackForm` (extracted in Phase 2).

In `ArticleView`:
```tsx
function ArticleView({ article }: { article: ArticleDetailType }) {
  // ... existing content render ...
  return (
    <div>
      {/* existing: title, iframe, sources */}
      {!article.precisionScore && !article.styleScore && (
        <div className="mt-6 border-t pt-4">
          <h3 className="text-sm font-semibold text-gray-700 mb-3">Hodnotit</h3>
          <RagFeedbackForm
            onSubmit={(data) => submitFeedback.mutate({ articleId: article.id, ...data })}
            isSubmitting={submitFeedback.isPending}
            alreadySubmitted={submitFeedback.isError && submitFeedback.error?.status === 409}
            isSuccess={submitFeedback.isSuccess}
          />
        </div>
      )}
      {(article.precisionScore || article.styleScore) && (
        <div className="mt-4 text-xs text-gray-500">
          Hodnocení: Přesnost {article.precisionScore}/5, Styl {article.styleScore}/5
        </div>
      )}
    </div>
  );
}
```

---

## Step 12 — Frontend: KB sources in `ArticleDetail` → `ChunkDetailModal`

**File**: `frontend/src/features/articles/ArticleDetail.tsx`

In `SourceList`, when `source.type === 'KnowledgeBase'` and `source.knowledgeBaseChunkId` is set, render a button that opens `ChunkDetailModal`:

```tsx
import ChunkDetailModal from '../../components/knowledge-base/ChunkDetailModal';

function SourceList({ sources }: { sources: ArticleSource[] }) {
  const [selectedChunkId, setSelectedChunkId] = useState<string | null>(null);

  return (
    <div className="mt-6 border-t pt-4">
      <h3 className="text-sm font-semibold text-gray-700 mb-2">Zdroje</h3>
      <ul className="space-y-1">
        {sources.map((source) => (
          <li key={...} className="flex items-start gap-2 text-sm">
            <SourceIcon type={source.type} />
            {source.url ? (
              <a href={source.url} ...>{source.title}</a>
            ) : source.knowledgeBaseChunkId ? (
              <button
                className="text-green-700 hover:underline text-left"
                onClick={() => setSelectedChunkId(source.knowledgeBaseChunkId!)}
              >
                {source.title}
              </button>
            ) : (
              <span className="text-gray-700">{source.title}</span>
            )}
          </li>
        ))}
      </ul>
      {selectedChunkId && (
        <ChunkDetailModal
          chunkId={selectedChunkId}
          onClose={() => setSelectedChunkId(null)}
        />
      )}
    </div>
  );
}
```

---

## Step 13 — Frontend: `useArticles.ts` — add feedback hooks

**File**: `frontend/src/api/hooks/useArticles.ts`

```ts
export function useSubmitArticleFeedbackMutation() {
  return useMutation({
    mutationFn: ({ articleId, ...data }: { articleId: string; precisionScore: number; styleScore: number; comment?: string }) =>
      client.articles_SubmitFeedback(articleId, data),
    // handle 409 as alreadySubmitted
  });
}

export function useArticleFeedbackListQuery(params: ArticleFeedbackListParams) {
  return useQuery({
    queryKey: articleKeys.feedbackList(params),
    queryFn: () => client.articles_GetFeedbackList(params),
    staleTime: 30_000,
  });
}
```

---

## Step 14 — i18n

**File**: `frontend/src/i18n.ts`

Add:
```ts
ArticleFeedbackAlreadySubmitted: {
  cs: 'Zpětná vazba k tomuto článku již byla odeslána.',
  en: 'Feedback for this article has already been submitted.',
},
ArticleNotGenerated: {
  cs: 'Článek ještě nebyl vygenerován.',
  en: 'Article has not been generated yet.',
},
```

---

## Tests to write

### Backend
- `SubmitArticleFeedbackHandlerTests` — not found / forbidden / already submitted / not-yet-generated / success
- `GetArticleFeedbackListHandlerTests` — paging, filters, stats
- `WriteArticleStepTests` — update existing test: assert `SourceRefs` carries `ChunkId` when LLM cites a KB title matching a gathered snippet
- `GatherContextStepTests` — assert `ContextSnippet.ChunkId` is set from `ChunkResult.ChunkId`
- Integration: `ArticleRepositoryTests` — `GetArticlesPagedAsync` with `hasFeedback` filter

### Frontend
- `ArticleDetail.test.tsx` — renders feedback form when status=Generated and no scores; hides after submission; renders KB source as button that opens modal
- `useArticles.test.ts` — `useSubmitArticleFeedbackMutation` converts 409 → alreadySubmitted

---

## Verification

1. `dotnet build` + `dotnet format`.
2. `npm run build` + `npm run lint`.
3. Migration applies cleanly.
4. Generate an article → wait for `Generated` status → article detail shows feedback form.
5. Submit feedback → DB row has `PrecisionScore`/`StyleScore`.
6. Submit again → HTTP 409 → UI shows already-submitted message.
7. KB source in article detail: click → `ChunkDetailModal` opens with correct chunk content.
8. `GET /api/articles/feedback/list` (role-gated) returns paged data + stats.
9. New backend tests all pass.
