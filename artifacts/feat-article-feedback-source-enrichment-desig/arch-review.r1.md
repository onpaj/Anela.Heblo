I have sufficient context. Writing the architecture review now.

# Architecture Review: Article Feedback + Source Enrichment

## Architectural Fit Assessment

The feature aligns cleanly with existing patterns. The Article feature is already organized as a vertical slice with MediatR handlers, a `BaseResponse`/`ErrorCodes` envelope, and `BaseApiController.HandleResponse` mapping codes to HTTP via `[HttpStatusCode]` attributes. The KB feedback feature (`SubmitFeedbackHandler`, `GetFeedbackListHandler`) provides a near-identical template — the article version essentially reuses the same shape with the addition of an `article.Status == Generated` precondition and `RequestedBy`-based ownership.

Three friction points emerge from active code reading that the spec does not fully address:

1. **`ContextSnippet` has no `Score` field.** The spec (FR-5) and brief both claim `snippet.Score → ArticleSource.Confidence`, but `ContextSnippet` carries only `Source / Title / Excerpt / Url / ChunkId`. The KB similarity score is fetched in `KnowledgeBaseRepository.SearchSimilarAsync` but discarded by `GatherContextStep` when projecting `SearchDocumentsResponse.Chunks` into snippets.
2. **`IArticleRepository.GetByIdAsync` uses `AsNoTracking()`.** The KB equivalent (`GetQuestionLogByIdAsync`) does not — the existing read path is unsuitable for a write/update use case.
3. **`ArticleSourceDto` is missing `Confidence` and `ValidationNote` entirely** (not just `KnowledgeBaseChunkId` and `Excerpt`). The spec's "verify during implementation" hedge becomes a confirmed gap.

Otherwise, all integration points (`ErrorCodes`, `AuthorizationConstants.Policies.ArticleGenerator`, `ICurrentUserService`, `articleKeys`, `ChunkDetailModal`) exist and are usable as designed.

## Proposed Architecture

### Component Overview

```
┌────────────────────────────────────────────────────────────────────────┐
│ API Layer                                                              │
│  ArticlesController                                                    │
│    POST /api/articles/{id}/feedback     → SubmitArticleFeedback        │
│    GET  /api/articles/feedback/list     → GetArticleFeedbackList       │
└──────────────────────────┬─────────────────────────────────────────────┘
                           │ MediatR
┌──────────────────────────▼─────────────────────────────────────────────┐
│ Application Layer (vertical slices)                                    │
│  Features/Article/UseCases/                                            │
│    SubmitFeedback/{Request,Handler,Response}.cs                        │
│    GetFeedbackList/{Request,Handler,Response,Summary,StatsDto}.cs      │
│  Features/Article/UseCases/Generate/Pipeline/                          │
│    WriteArticleStep.cs        (modified — richer SourceRef tuple)      │
│    GatherContextStep.cs       (modified — propagate Score)             │
│    ContextSnippet.cs          (modified — add Score)                   │
│  Features/Article/UseCases/GenerateArticle/                            │
│    GenerateArticleJob.cs      (modified — full ArticleSource mapping)  │
│  Features/Article/UseCases/GetArticle/                                 │
│    GetArticleResponse.cs      (modified — add feedback fields)         │
│    ArticleSourceDto.cs        (modified — full chunk surface)          │
│    GetArticleHandler.cs       (modified — project new fields)          │
└──────────────────────────┬─────────────────────────────────────────────┘
                           │ IArticleRepository
┌──────────────────────────▼─────────────────────────────────────────────┐
│ Persistence Layer                                                      │
│  IArticleRepository           (+ GetForUpdateAsync, GetFeedbackPaged,  │
│                                  GetFeedbackStatsAsync)                │
│  ArticleRepository            (EF Core, tracked load for SubmitFB)    │
│  ArticleConfiguration         (+ FeedbackComment text, filtered idx)   │
│  Migrations/AddArticleFeedbackColumns.cs                               │
└──────────────────────────┬─────────────────────────────────────────────┘
                           │ EF Core / PostgreSQL
                           ▼
                       Articles table

┌────────────────────────────────────────────────────────────────────────┐
│ Frontend                                                               │
│  components/feedback/RagFeedbackForm.tsx           (new, reusable)     │
│  components/feedback/ScoreRow.tsx                  (extracted)         │
│  features/articles/ArticleDetail.tsx               (modified)          │
│  features/articles/ArticleSourceList.tsx           (extracted)         │
│  api/hooks/useArticles.ts                          (+ 2 hooks)         │
│  components/knowledge-base/KnowledgeBaseSearchAskTab.tsx (refactored)  │
│  i18n.ts                                            (+ 2 codes)        │
└────────────────────────────────────────────────────────────────────────┘
```

### Key Design Decisions

#### Decision 1: Tracked-load method on IArticleRepository
**Options considered:**
- (a) Drop `AsNoTracking()` from existing `GetByIdAsync` (single overload).
- (b) Add a separate `GetForUpdateAsync` method that returns a tracked entity.
- (c) Use `_context.Articles.FindAsync` directly inside the handler.

**Chosen approach:** (b) — add `Task<Article?> GetForUpdateAsync(Guid id, CancellationToken ct)` to `IArticleRepository`.

**Rationale:** `GetByIdAsync` has hot read callers (`GetArticleHandler`) and changing its tracking semantics would silently regress query performance for the read path. (c) leaks the DbContext into the application layer, breaking the existing repository abstraction. (b) preserves existing behavior, makes the mutation intent explicit at the call site, and matches how `KnowledgeBaseRepository` already differentiates `GetChunkByIdAsync` (tracked, mutates) from `GetDocumentByHashAsync` (`AsNoTracking`, read-only).

#### Decision 2: Source-enrichment data flow — propagate Score through the pipeline
**Options considered:**
- (a) Match `SourceUsedDto.Title` → `AggregatedFact.Confidence` (LLM-assigned confidence, already present).
- (b) Add `double Score` to `ContextSnippet` and have `GatherContextStep` thread it from `ChunkSearchResult.Score`.
- (c) Store both: snippet score for KB sources, fact confidence for web/aggregated.

**Chosen approach:** (b) — add `double? Score` to `ContextSnippet`, populate only for KB snippets in `GatherContextStep`, then `WriteArticleStep.MapSources` reads it.

**Rationale:** The user-facing meaning of `Confidence` for a KB source is "how relevant the chunk is to the article topic" — that is the cosine similarity score, not the LLM's fact-confidence rating. The brief explicitly says "Score → Confidence." Spec FR-5 has the right intent but the source field doesn't exist; adding it is one line on the record plus one assignment in `GatherContextStep.GatherKnowledgeBaseSnippetsAsync`. For the LLM-rated confidence on aggregated facts, `AggregatedFact.Confidence` is the canonical home — those are different signals and should not be conflated.

For `Excerpt` and `ValidationNote`, the source is `AggregatedFact`: matched by title against `Facts`. KB snippets that did not become facts persist with `null` for these two fields (no failure).

#### Decision 3: Feedback list policy — `ArticleGenerator`, not "owner-only"
**Options considered:**
- (a) Restrict the list to articles where `RequestedBy == currentUser.Id`.
- (b) Gate by `[Authorize(Policy = ArticleGenerator)]` and return all articles.

**Chosen approach:** (b), aligned with the brief and KB precedent. The spec's wording "same as the KB GetFeedbackList endpoint" was misleading — KB actually uses `KnowledgeBaseUpload`. The correct article policy is `AuthorizationConstants.Policies.ArticleGenerator` (already defined). 

**Rationale:** The list is an analytics view; it's a pre-existing role boundary in this codebase. Per-article submit retains the owner-only check (`article.RequestedBy == currentUser.Id`) at handler level — this is the KB pattern verbatim.

#### Decision 4: 409 detection on the frontend — raw fetch, not generated client
**Options considered:**
- (a) Call `client.articles_SubmitFeedback` and catch the thrown error; inspect the status.
- (b) Bypass the generated client with `apiClient.http.fetch(absoluteUrl, ...)` and read `response.status === 409` directly (mirroring KB).

**Chosen approach:** (b).

**Rationale:** The OpenAPI generator throws on non-2xx for void-success endpoints, losing the 409 distinction. The KB hook already established the raw-fetch + absolute-URL pattern (`useSubmitFeedbackMutation` in `useKnowledgeBase.ts:387-410`), and CLAUDE.md explicitly mandates absolute URLs (`${apiClient.baseUrl}${relativeUrl}`) for hooks. Reusing the pattern keeps frontend hook conventions consistent and avoids fragile error-message parsing.

#### Decision 5: Stats aggregation — single SQL round-trip with `FILTER` clauses
**Options considered:**
- (a) Mirror KB pattern (`CountAsync` + `ToListAsync` + in-memory averages).
- (b) Single SQL `SELECT COUNT(*), COUNT(*) FILTER (WHERE ...), AVG(...) FILTER (WHERE ...)` via raw SQL or LINQ.

**Chosen approach:** (b), as the spec's NFR-1 demands and as the brief's "single round-trip" goal requires.

**Rationale:** The KB implementation hydrates every feedback row into memory and computes averages client-side — this is fine at small scale but degrades linearly. The spec explicitly calls out 10k articles at p95 ≤ 300 ms; deviating from the KB pattern here is intentional and correct. EF Core 8 supports `EF.Functions.Average(... .Where(...))` projections, but the cleanest expression is a single grouped query:

```csharp
var stats = await _context.Articles
    .GroupBy(_ => 1)
    .Select(g => new ArticleFeedbackStats(
        g.Count(),
        g.Count(a => a.PrecisionScore != null || a.StyleScore != null),
        g.Where(a => a.PrecisionScore != null).Average(a => (double?)a.PrecisionScore),
        g.Where(a => a.StyleScore != null).Average(a => (double?)a.StyleScore)))
    .FirstOrDefaultAsync(ct) ?? new ArticleFeedbackStats(0, 0, null, null);
```

Combined with `Task.WhenAll(pageQuery, statsQuery)` in the handler, this satisfies the parallelism requirement.

#### Decision 6: `ArticleFeedbackStats` shape
**Options considered:**
- (a) Sealed record (per spec).
- (b) Class (matches existing `FeedbackAggregateStats`).

**Chosen approach:** (a) `sealed record` for the **domain value object** (internal), but the **DTO** that crosses the API boundary (`ArticleFeedbackStatsDto`) MUST be a class. 

**Rationale:** CLAUDE.md is explicit: "DTOs are classes, never C# records" because of OpenAPI client generator handling. Internal domain types may be records. The KB version was made a class without that distinction — we can do better by separating the internal aggregate (`ArticleFeedbackStats` record) from the wire DTO (`ArticleFeedbackStatsDto` class), matching the spec's `Data Model` section.

#### Decision 7: `SourceList` extraction
**Options considered:**
- (a) Inline modal state inside the existing `SourceList` function in `ArticleDetail.tsx`.
- (b) Extract `ArticleSourceList` to its own file, owning `selectedChunkId` state + `<ChunkDetailModal>` render.

**Chosen approach:** (b).

**Rationale:** `ArticleDetail.tsx` is already 156 lines mixing status orchestration, iframe rendering, and source listing. Adding feedback form state + modal state in the same file pushes it past the project's "many small files" preference (CLAUDE rule). Extracting the source list keeps the modal trigger logic colocated with the rendering and leaves `ArticleView` as a thin composition.

## Implementation Guidance

### Directory / Module Structure

```
backend/src/Anela.Heblo.Domain/Features/Article/
  Article.cs                                     [+3 props]
  ArticleFeedbackStats.cs                        [NEW]
  IArticleRepository.cs                          [+3 methods]

backend/src/Anela.Heblo.Application/Features/Article/UseCases/
  SubmitFeedback/
    SubmitArticleFeedbackRequest.cs              [NEW]
    SubmitArticleFeedbackResponse.cs             [NEW]
    SubmitArticleFeedbackHandler.cs              [NEW]
  GetFeedbackList/
    GetArticleFeedbackListRequest.cs             [NEW]
    GetArticleFeedbackListResponse.cs            [NEW]
    GetArticleFeedbackListHandler.cs             [NEW]
    ArticleFeedbackSummary.cs                    [NEW]
    ArticleFeedbackStatsDto.cs                   [NEW]
  GetArticle/
    GetArticleResponse.cs                        [+3 props]
    ArticleSourceDto.cs                          [+4 props]
    GetArticleHandler.cs                         [project new fields]
  Generate/Pipeline/
    ContextSnippet.cs                            [+ Score]
    GatherContextStep.cs                         [propagate Score]
    WriteArticleStep.cs                          [richer MapSources tuple]
  GenerateArticle/
    GenerateArticleJob.cs                        [richer ArticleSource mapping]

backend/src/Anela.Heblo.Application/Shared/
  ErrorCodes.cs                                  [+ 2 codes — see below]

backend/src/Anela.Heblo.Persistence/Features/Article/
  ArticleRepository.cs                           [+3 method impls]
  ArticleConfiguration.cs                        [+ filtered index, text]

backend/src/Anela.Heblo.Persistence/Migrations/
  YYYYMMDD_AddArticleFeedbackColumns.cs          [NEW]

backend/src/Anela.Heblo.API/Controllers/
  ArticlesController.cs                          [+2 endpoints]

backend/test/Anela.Heblo.Tests/Article/UseCases/
  SubmitArticleFeedbackHandlerTests.cs           [NEW]
  GetArticleFeedbackListHandlerTests.cs          [NEW]

backend/test/Anela.Heblo.Tests/Article/Pipeline/
  WriteArticleStepTests.cs                       [extend]
  GatherContextStepTests.cs                      [extend — Score propagation]

frontend/src/components/feedback/
  RagFeedbackForm.tsx                            [NEW]
  ScoreRow.tsx                                   [extracted]

frontend/src/features/articles/
  ArticleDetail.tsx                              [modified]
  ArticleSourceList.tsx                          [NEW — extracted]
  ArticleFeedbackSection.tsx                     [NEW — extracted]

frontend/src/api/hooks/
  useArticles.ts                                 [+ 2 hooks, types]

frontend/src/components/knowledge-base/
  KnowledgeBaseSearchAskTab.tsx                  [refactor: consume RagFeedbackForm]

frontend/src/i18n.ts                             [+ 2 entries × 2 langs]
```

### Interfaces and Contracts

**Domain — `ArticleFeedbackStats` (record, internal):**

```csharp
public sealed record ArticleFeedbackStats(
    int TotalArticles,
    int TotalWithFeedback,
    double? AvgPrecisionScore,
    double? AvgStyleScore);
```

**Domain — `IArticleRepository` (additions):**

```csharp
Task<Article?> GetForUpdateAsync(Guid id, CancellationToken ct = default);

Task<(IReadOnlyList<Article> Items, int TotalCount)> GetFeedbackPagedAsync(
    bool? hasFeedback,
    string? requestedBy,
    string sortBy,
    bool descending,
    int page,
    int pageSize,
    CancellationToken ct = default);

Task<ArticleFeedbackStats> GetFeedbackStatsAsync(CancellationToken ct = default);
```

**Application — Request/Response (CLASSES, not records — DTO rule):**

```csharp
public class SubmitArticleFeedbackRequest : IRequest<SubmitArticleFeedbackResponse>
{
    public Guid ArticleId { get; set; }
    [Range(1, 5)] public int PrecisionScore { get; set; }
    [Range(1, 5)] public int StyleScore { get; set; }
    [MaxLength(1000)] public string? Comment { get; set; }
}

public class SubmitArticleFeedbackResponse : BaseResponse
{
    public int? PrecisionScore { get; set; }
    public int? StyleScore { get; set; }
    public string? FeedbackComment { get; set; }
    public SubmitArticleFeedbackResponse() { }
    public SubmitArticleFeedbackResponse(ErrorCodes errorCode, Dictionary<string, string>? details = null)
        : base(errorCode, details) { }
}

public class GetArticleFeedbackListRequest : IRequest<GetArticleFeedbackListResponse>
{
    public bool? HasFeedback { get; set; }
    public string? RequestedBy { get; set; }
    public string SortBy { get; set; } = "CreatedAt";
    public bool SortDescending { get; set; } = true;
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}

public class GetArticleFeedbackListResponse : BaseResponse
{
    public List<ArticleFeedbackSummary> Items { get; set; } = [];
    public ArticleFeedbackStatsDto Stats { get; set; } = new();
    public int TotalCount { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalPages => PageSize > 0 ? (int)Math.Ceiling(TotalCount / (double)PageSize) : 0;
}

public class ArticleFeedbackSummary
{
    public Guid Id { get; set; }
    public string? Title { get; set; }
    public string Topic { get; set; } = "";
    public string? RequestedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int? PrecisionScore { get; set; }
    public int? StyleScore { get; set; }
    public bool HasComment { get; set; }
}

public class ArticleFeedbackStatsDto
{
    public int TotalArticles { get; set; }
    public int TotalWithFeedback { get; set; }
    public double? AvgPrecisionScore { get; set; }
    public double? AvgStyleScore { get; set; }
}
```

**ErrorCodes additions (in `Anela.Heblo.Application/Shared/ErrorCodes.cs`, Article block 24XX):**

```csharp
[HttpStatusCode(HttpStatusCode.UnprocessableEntity)]
ArticleNotGenerated = 2406,
[HttpStatusCode(HttpStatusCode.Conflict)]
ArticleFeedbackAlreadySubmitted = 2407,
```

(`ArticleNotFound = 2401` already exists; reuse it.)

**Pipeline contract changes:**

```csharp
// ContextSnippet.cs  (+ one field)
public sealed record ContextSnippet
{
    public SourceType Source { get; init; }
    public string Title { get; init; } = "";
    public string Excerpt { get; init; } = "";
    public string? Url { get; init; }
    public Guid? ChunkId { get; init; }
    public double? Score { get; init; }   // NEW — KB similarity score (null for web)
}

// ArticlePipelineContext.SourceRefs becomes:
public List<ArticleSourceRef> SourceRefs { get; set; } = [];

// New named record (replaces tuple — tuples were tolerable for 3 fields, not 7):
public sealed record ArticleSourceRef(
    string Title,
    string? Url,
    SourceType Type,
    Guid? ChunkId,
    double? Confidence,
    string? Excerpt,
    string? ValidationNote);
```

**Frontend types (`useArticles.ts` additions):**

```ts
export interface ArticleFeedback {
  precisionScore: number;
  styleScore: number;
  comment?: string;
}

export interface SubmitArticleFeedbackResult {
  alreadySubmitted?: true;
}

export interface ArticleSource {
  title: string;
  url: string | null;
  type: string;
  knowledgeBaseChunkId: string | null;  // NEW
  confidence: number | null;             // NEW
  excerpt: string | null;                // NEW
  validationNote: string | null;         // NEW
}

export interface ArticleDetail {
  // ...existing fields
  precisionScore: number | null;         // NEW
  styleScore: number | null;             // NEW
  feedbackComment: string | null;        // NEW
}

articleKeys.feedbackList = (params: ArticleFeedbackListParams) =>
  [...QUERY_KEYS.articles, 'feedbackList', params] as const;
```

**`RagFeedbackForm` props (matches spec FR-8 verbatim):**

```ts
interface RagFeedbackFormProps {
  onSubmit: (data: { precisionScore: number; styleScore: number; comment?: string }) => void;
  isSubmitting: boolean;
  isSuccess: boolean;
  alreadySubmitted: boolean;
}
```

### Data Flow

**Submit feedback (POST /api/articles/{id}/feedback):**

```
ArticlesController.SubmitFeedback
  ├─ Bind ArticleId from route, body to request
  ├─ MediatR → SubmitArticleFeedbackHandler
  │    ├─ repo.GetForUpdateAsync(id)         → tracked Article OR ArticleNotFound
  │    ├─ currentUserService.GetCurrentUser
  │    ├─ Verify article.RequestedBy == user.Id  → Forbidden
  │    ├─ Verify article.Status == Generated      → ArticleNotGenerated (422)
  │    ├─ Verify both scores null                 → ArticleFeedbackAlreadySubmitted (409)
  │    ├─ Mutate: PrecisionScore, StyleScore, FeedbackComment
  │    └─ repo.SaveChangesAsync
  └─ HandleResponse → 200 with new values OR mapped error
```

**Feedback list (GET /api/articles/feedback/list):**

```
[Authorize(Policy = ArticleGenerator)]
ArticlesController.FeedbackList
  └─ MediatR → GetArticleFeedbackListHandler
       ├─ Validate sortBy (allowlist), page≥1, pageSize ∈ {10,20,50}
       ├─ await Task.WhenAll(
       │     repo.GetFeedbackPagedAsync(...),
       │     repo.GetFeedbackStatsAsync(...))
       └─ Project Articles → ArticleFeedbackSummary, ArticleFeedbackStats → DTO
```

**Pipeline source enrichment (during article generation):**

```
GatherContextStep.GatherKnowledgeBaseSnippets
  └─ for each chunk in SearchDocumentsResponse.Chunks:
        new ContextSnippet { ..., ChunkId = chunk.ChunkId, Score = chunk.Score }
                                                                ^^^^^ ← NEW

WriteArticleStep.MapSources(sourcesUsed, snippets, facts)
  └─ for each source:
        snippetMatch = snippets.FirstOrDefault(s => Title-match)
        factMatch    = facts.FirstOrDefault(f => Title-match)
        yield ArticleSourceRef(
            source.Title, source.Url,
            Type = source.Url != null ? Web : KnowledgeBase,
            ChunkId        = snippetMatch?.ChunkId,
            Confidence     = snippetMatch?.Score,           // KB similarity
            Excerpt        = Truncate(factMatch?.Claim, 200),
            ValidationNote = factMatch?.ValidationNote)

GenerateArticleJob (after MarkAsGenerated)
  └─ for each ref in context.SourceRefs:
        article.Sources.Add(new ArticleSource {
            Id = NewGuid, ArticleId,
            Title, Url, Type,
            KnowledgeBaseChunkId = ref.ChunkId,
            Confidence           = ref.Confidence,
            Excerpt              = ref.Excerpt,
            ValidationNote       = ref.ValidationNote });
```

**Frontend feedback flow (article detail):**

```
ArticleDetail
  └─ ArticleView
       ├─ HtmlContent (iframe)
       ├─ ArticleSourceList   (owns selectedChunkId, renders ChunkDetailModal)
       └─ ArticleFeedbackSection
            ├─ if status==Generated && !precisionScore && !styleScore
            │    └─ <RagFeedbackForm onSubmit isSubmitting isSuccess alreadySubmitted />
            ├─ if scores present → "Hodnocení: Přesnost X/5, Styl Y/5" + comment
            └─ on submit → useSubmitArticleFeedbackMutation
                          └─ on success → queryClient.invalidateQueries(articleKeys.detail(id))
                          └─ on 409    → setAlreadySubmitted(true)
```

## Risks and Mitigations

| Risk | Severity | Mitigation |
|---|---|---|
| Tuple → named record refactor in `ArticlePipelineContext.SourceRefs` breaks existing pipeline tests | Medium | Update `WriteArticleStepTests`, `GenerateArticleJobTests` in the same PR; the rename is mechanical. Don't keep both shapes. |
| `GetForUpdateAsync` accidentally used in read paths (defeats `AsNoTracking` perf) | Low | Code-review checklist + naming: only `Submit*` handlers may call it. |
| Title-match in `MapSources` is fragile (LLM may rewrite source titles) | Medium | Spec accepts null chunk fields when no match — degrade gracefully. Add a unit test that asserts no exceptions on a no-match case. Already in spec FR-5. |
| Filtered index `WHERE "PrecisionScore" IS NOT NULL` syntax is PostgreSQL-specific | Low | The codebase is PostgreSQL-only (Npgsql usage confirmed in `KnowledgeBaseRepository`). Document in migration. |
| Frontend mutation 409 detection requires bypassing OpenAPI client | Low | Reuse the established KB pattern (`apiClient.http.fetch` + absolute URL). Confirmed by CLAUDE.md rule. |
| `ArticleFeedbackStatsDto` averages displayed in UI without rounding cause noisy fractions | Low | Mirror KB: `Math.Round(value, 1)` in the handler before populating the DTO. |
| Concurrent double-submit slips past in-handler check (no DB-level guarantee) | Low | Spec NFR-3 accepts this for the solo-user-per-article use case. Optionally add a future DB constraint when feedback editing arrives. |
| `AsNoTracking()` removal regression in `GetByIdAsync` | High | DO NOT modify `GetByIdAsync`. Add the new `GetForUpdateAsync` instead — keeps the existing read path performant. |
| `ContextSnippet.Score` is now nullable, but downstream code assumes non-null | Low | Only one consumer (`WriteArticleStep.MapSources`) uses it via `?.` propagation. Web snippets keep null Score (correct). |

## Specification Amendments

The spec is largely accurate but needs these corrections before implementation:

1. **FR-5 `Score` source.** Replace "`Score → Confidence`" against `ContextSnippets` with: "Add a nullable `Score` property to `ContextSnippet` and populate it in `GatherContextStep.GatherKnowledgeBaseSnippetsAsync` from `SearchDocumentsResponse.Chunks[].Score`. `MapSources` then reads `snippet.Score → ArticleSource.Confidence`. Web snippets continue to have `Score == null`."

2. **FR-7 DTO surface.** `ArticleSourceDto` is missing **all four** new fields, not two. The full additive set is `KnowledgeBaseChunkId`, `Confidence`, `Excerpt`, `ValidationNote`. Update `GetArticleHandler.cs` projection accordingly.

3. **FR-3 list policy.** Use `AuthorizationConstants.Policies.ArticleGenerator` (constant `"ArticleGenerator"`). The brief's "same as KB" comparison is misleading — KB uses `KnowledgeBaseUpload`. Confirm via `AuthorizationConstants.Policies` enum.

4. **FR-2 repository contract.** `IArticleRepository.GetByIdAsync` uses `AsNoTracking()` and CANNOT be used for the SubmitFeedback mutation. Add `GetForUpdateAsync(Guid id, CancellationToken)` (tracked) to the repository contract; the SubmitFeedback handler uses this method, not `GetByIdAsync`.

5. **NFR-1 stats query implementation.** Provide concrete LINQ template (single `GroupBy(_ => 1)` projection — see Decision 5) so implementation does not regress to KB's in-memory pattern. The `Task.WhenAll` requirement is unchanged.

6. **FR-11 hook implementation.** `useSubmitArticleFeedbackMutation` MUST use raw `apiClient.http.fetch(absoluteUrl, ...)` + status-code branch on 409, not `client.articles_SubmitFeedback`. Match the KB hook pattern at `useKnowledgeBase.ts:387-410`. The generated client throws on 409 and loses the distinction.

7. **`ArticleFeedbackStats` shape clarification.** Domain object: `sealed record` (per spec). Wire DTO `ArticleFeedbackStatsDto`: **class** (per CLAUDE.md DTO rule). Two types, mapped at the handler boundary.

8. **`SubmitArticleFeedbackResponse`.** Spec implies it returns "the updated values"; clarify that the response carries `PrecisionScore`, `StyleScore`, `FeedbackComment` after success so the frontend can update without a separate refetch (mutation also invalidates the article detail query for the read summary).

9. **`pageSize` allowlist.** Spec FR-3 lists default 20 but does not bound it. Match KB pattern: `AllowedPageSizes = [10, 20, 50]`; otherwise fall back to 20.

10. **Source ordering in `ArticleSourceList`.** Spec doesn't specify but cycling KB sources first then web sources improves citation UX. Mirror existing rendering order (insertion order in `Sources`); no sort is required.

## Prerequisites

Before implementation begins:

- **No infra changes required.** PostgreSQL, EF Core 8, MediatR, React Query stack already in place.
- **Migration prerequisites:** Database must be at the latest migration before applying `AddArticleFeedbackColumns`. The migration is forward-only and additive. Per CLAUDE.md, migrations are applied manually — coordinate with deploy.
- **OpenAPI client regeneration:** Two new endpoints will appear in the generated TypeScript client on `npm run build`. The frontend hook for SubmitFeedback uses raw fetch (not the generated method), so the regeneration is for type completeness and the GET endpoint (which the list page can call via the generated method).
- **Test-data fixtures:** No new E2E fixtures required; existing article fixtures should already have a `RequestedBy` user that the test-auth session matches.
- **Confirm `AuthorizationConstants.Policies.ArticleGenerator` is wired in `Program.cs`.** It already exists in code (`AuthorizationConstants.cs:71`); verify the policy is registered with the role binding (`Roles.ArticleGenerator = "article_generator"`) before relying on it for the new GET endpoint.
- **`ICurrentUserService` and `CurrentUser.Id`.** Already used by KB; reuse without change. The `Article.RequestedBy` value is set at generation time from the same source — verify symmetry by checking `GenerateArticleHandler` populates `RequestedBy = currentUser.Id` (same identity used in feedback ownership check).