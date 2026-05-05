# Specification: Article Generation Feedback + Source Enrichment (Phase 3)

## Summary
Phase 3 of the GenAI consistency initiative adds a 1–5 precision/style feedback layer with optional comment to generated `Article` entities, fully populates `ArticleSource` chunk provenance fields (`KnowledgeBaseChunkId`, `Confidence`, `Excerpt`, `ValidationNote`) from the generation pipeline, and turns KB citations in `ArticleDetail` into clickable triggers for the shared `ChunkDetailModal`. The work mirrors existing flat-column feedback patterns used by KB question logs and Leaflet feedback, and reuses the `RagFeedbackForm` component extracted in Phase 2.

## Background
Article generation already persists outputs (status, content, sources). However, two gaps remain after Phases 1–2:

1. There is no way for the requester to rate the quality of a generated article. Operationally, the team needs precision/style metrics to assess pipeline quality and decide whether to tune prompts or reweight retrieval.
2. `WriteArticleStep.MapSources` only distinguishes Web vs KnowledgeBase by URL presence and never populates the chunk provenance fields. As a result, KB citations in the UI are dead text — users cannot inspect the underlying chunk, and downstream analytics cannot correlate articles to KB chunks.

This phase closes both gaps and integrates the existing `ChunkDetailModal` (delivered in Phase 1) and `RagFeedbackForm` (delivered in Phase 2) into the Article surface.

**Integration branch**: All work for this phase lands on `feature/genai_consistency` (not `main`). The phase branch must be cut from `feature/genai_consistency`, and the PR must target `feature/genai_consistency`.

## Functional Requirements

### FR-1: Article entity carries flat feedback columns
Add three nullable properties to the `Article` domain entity: `PrecisionScore` (int?), `StyleScore` (int?), `FeedbackComment` (string?). No separate feedback table — flat columns mirror the `KnowledgeBaseQuestionLog` pattern.

**Acceptance criteria:**
- `Article.cs` exposes `PrecisionScore`, `StyleScore`, `FeedbackComment` as public nullable properties.
- EF configuration in `ArticleConfiguration` declares `FeedbackComment` as `text` and creates a filtered index `IX_Articles_PrecisionScore` (`"PrecisionScore" IS NOT NULL`).
- Migration `AddArticleFeedbackColumns` (next sequential after `20260504195511_AddArticles`) adds the three columns and the filtered index, applies cleanly forward and reverses cleanly.

### FR-2: Submit feedback for a generated article
Authenticated users can submit one feedback record per article they requested, scoring precision and style 1–5 with an optional ≤1000-char comment.

**Acceptance criteria:**
- `POST /api/articles/{id}/feedback` accepts `{ precisionScore: 1..5, styleScore: 1..5, comment?: string ≤1000 }`.
- Validation rejects out-of-range scores (`[Range(1,5)]`) and over-length comments (`[MaxLength(1000)]`) with 400.
- Returns `ErrorCodes.ArticleNotFound` (404) when the article does not exist.
- Returns `ErrorCodes.Forbidden` (403) when `article.RequestedBy != currentUser.Id`.
- Returns `ErrorCodes.ArticleNotGenerated` (409) when `article.Status != ArticleStatus.Generated`.
- Returns `ErrorCodes.ArticleFeedbackAlreadySubmitted` (409) when either score is already set.
- On success, persists the three values atomically in a single `SaveChangesAsync` and returns success.

### FR-3: List feedback with stats (admin-style view)
A paged, filtered, sorted list of articles with their feedback, plus aggregate stats. Mirrors KB's `GetFeedbackListHandler` and Leaflet's `GetLeafletFeedbackListHandler`.

**Acceptance criteria:**
- `GET /api/articles/feedback/list` is gated by policy `AuthorizationConstants.Policies.ArticleGenerator`.
- Query parameters: `hasFeedback?: bool`, `requestedBy?: string`, `sortBy?: "CreatedAt"|"PrecisionScore"|"StyleScore"` (default `CreatedAt`), `sortDescending?: bool` (default `true`), `pageNumber?: int` (default 1), `pageSize?: int` (default 20).
- Response contains `articles[]` (`ArticleFeedbackSummary`), `totalCount`, `pageNumber`, `pageSize`, and `stats` (`{ totalArticles, totalWithFeedback, avgPrecisionScore?, avgStyleScore? }`).
- `hasFeedback=true` filters to rows where `PrecisionScore != null OR StyleScore != null`; `hasFeedback=false` is the negation.
- Unknown `sortBy` falls back to `CreatedAt` order.
- Stats are computed from the unfiltered article set (so they describe corpus-level coverage, not the current filter view).

### FR-4: Pipeline propagates KB chunk provenance through to `ArticleSource`
The generation pipeline must thread chunk identity, retrieval score, claim excerpt, and validation note from KB search results all the way to persisted `ArticleSource` rows.

**Acceptance criteria:**
- `ContextSnippet` (in `ArticlePipelineContext`) carries `ChunkId: Guid?` and `Score: double?`. Add fields if missing.
- `GatherContextStep` populates `ContextSnippet.ChunkId` from `ChunkResult.ChunkId` and `ContextSnippet.Score` from `ChunkResult.Score` for every KB result mapped from `SearchDocumentsRequest`.
- `AggregatedFact` carries `SourceChunkId: Guid?` (and `Score: double?` if not already present); `AggregateFactsStep` and `ValidateFactsStep` thread these from the originating `ContextSnippet`.
- `WriteArticleStep.MapSources` matches each `SourceUsedDto` (LLM-cited title/url) against `facts` by `SourceTitle`/`SourceUrl` and emits a tuple including `ChunkId`, `Confidence` (= `Score`), `Excerpt` (≤200 chars truncation of `Claim`), `ValidationNote`.
- Source classification rule: `SourceType.Web` when `s.Url != null`, otherwise `SourceType.KnowledgeBase`. `ChunkId` is only set for `KnowledgeBase`.
- `GenerateArticleJob` writes `KnowledgeBaseChunkId`, `Confidence`, `Excerpt`, `ValidationNote` onto each persisted `ArticleSource`.

### FR-5: `GetArticle` response surfaces feedback and chunk references
The article detail endpoint exposes the new feedback fields and the `KnowledgeBaseChunkId` for each source so the UI can render the feedback form and clickable KB citations.

**Acceptance criteria:**
- `GetArticleResponse` includes `precisionScore?: int`, `styleScore?: int`, `feedbackComment?: string`.
- `ArticleSourceDto` includes `knowledgeBaseChunkId?: Guid` and `excerpt?: string`.
- `GetArticleHandler` maps these from the entity and from `ArticleSource`.

### FR-6: Article detail UI shows feedback form
On a generated article without prior feedback, the detail view renders the shared `RagFeedbackForm`. Once feedback exists, the form is hidden and a compact summary line is shown.

**Acceptance criteria:**
- When `article.precisionScore == null && article.styleScore == null`, render `RagFeedbackForm` under the existing content + sources block, titled "Hodnotit".
- Submission calls `useSubmitArticleFeedbackMutation` with `{ articleId, precisionScore, styleScore, comment }`.
- A 409 response surfaces as `alreadySubmitted=true` on the form.
- On success, the form switches to its success state; on next render (after refetch), the form disappears and a summary "Hodnocení: Přesnost {x}/5, Styl {y}/5" is displayed.
- Form is not rendered when `article.status != Generated`.

### FR-7: KB sources in article detail open `ChunkDetailModal`
Plain-text KB citations become buttons that open the shared `ChunkDetailModal` with the corresponding `chunkId`.

**Acceptance criteria:**
- For `source.type === 'KnowledgeBase' && source.knowledgeBaseChunkId`, render a button styled with `text-green-700 hover:underline` that opens `ChunkDetailModal` for that chunk.
- For `source.url` (Web), continue to render an `<a href>` link (existing behaviour preserved).
- For KB sources without a `knowledgeBaseChunkId` (legacy rows), render plain `<span>` (no button).
- Modal closes via its own `onClose` callback and clears `selectedChunkId`.

### FR-8: i18n strings for new error codes
Czech and English error messages for the two new codes are wired into the i18n catalog.

**Acceptance criteria:**
- `frontend/src/i18n.ts` exports `ArticleFeedbackAlreadySubmitted` and `ArticleNotGenerated` with the strings:
  - `ArticleFeedbackAlreadySubmitted` — cs: "Zpětná vazba k tomuto článku již byla odeslána.", en: "Feedback for this article has already been submitted."
  - `ArticleNotGenerated` — cs: "Článek ještě nebyl vygenerován.", en: "Article has not been generated yet."

## Non-Functional Requirements

### NFR-1: Performance
- Feedback submission is a single-row update: target p95 < 100 ms server-side.
- `GET /api/articles/feedback/list` paged at 20 rows must satisfy in < 300 ms p95 with the filtered `IX_Articles_PrecisionScore` index supporting `hasFeedback=true` queries.
- Stats query (`GetFeedbackStatsAsync`) runs a single round-trip per request; acceptable for the corpus size (low thousands). No caching required at this stage.

### NFR-2: Security & Authorization
- `POST /api/articles/{id}/feedback` requires authenticated user. Ownership check enforces `article.RequestedBy == currentUser.Id`; cross-user submission returns 403 with `ErrorCodes.Forbidden`.
- `GET /api/articles/feedback/list` is gated by `AuthorizationConstants.Policies.ArticleGenerator` (existing role policy used by article admin endpoints).
- Comment input is bounded at 1000 chars and stored as `text`. Rendered as plain text (no HTML); existing React text rendering escapes by default.
- No PII beyond `RequestedBy` (already present on the entity) is introduced.

### NFR-3: Data integrity
- Feedback is one-shot per article: handler enforces "already submitted" check before write. There is no concurrent-update race in practice (single user, single article), but the check + save are issued in the same unit-of-work.
- Migration is forward-and-back reversible. Filtered index uses Postgres-quoted column name (`"PrecisionScore"`).

### NFR-4: Compatibility
- Adding nullable columns + new endpoints is backward-compatible; existing article rows return `null` feedback fields.
- Legacy `ArticleSource` rows persisted before this phase will have `KnowledgeBaseChunkId == null` and render as plain text in the UI (graceful fallback). No backfill is in scope.

### NFR-5: Validation before completion
- BE: `dotnet build` + `dotnet format` clean.
- FE: `npm run build` + `npm run lint` clean.
- All touched tests pass; new tests listed below pass.
- Migration applies and rolls back cleanly against a Phase-1/2 baseline.

## Data Model

### `Article` (modified)
```
Article
  Id                Guid (PK, existing)
  Topic             string (existing)
  Title             string? (existing)
  Status            ArticleStatus (existing)
  CreatedAt         DateTimeOffset (existing)
  GeneratedAt       DateTimeOffset? (existing)
  RequestedBy       string? (existing)
  Sources           ICollection<ArticleSource> (existing)
  ─── new ───
  PrecisionScore    int?           (1..5, app-level validation)
  StyleScore        int?           (1..5, app-level validation)
  FeedbackComment   string?        (≤1000 chars, stored as text)
```

Indexes: `IX_Articles_PrecisionScore` filtered on `"PrecisionScore" IS NOT NULL` to accelerate `hasFeedback=true` filters and `OrderBy(PrecisionScore)`.

### `ArticleSource` (provenance fields populated; schema unchanged)
Existing columns `KnowledgeBaseChunkId`, `Confidence`, `Excerpt`, `ValidationNote` are now actually written by the pipeline (previously always null).

### `ArticleFeedbackStats` (new domain record)
```
ArticleFeedbackStats(
  TotalArticles: int,
  TotalWithFeedback: int,
  AvgPrecisionScore: double?,   // null when no feedback yet
  AvgStyleScore: double?
)
```

### Pipeline value objects (modified)
- `ContextSnippet`: add `ChunkId: Guid?`, `Score: double?` (if absent).
- `AggregatedFact`: add `SourceChunkId: Guid?`, `Score: double?` (if absent).

## API / Interface Design

### Backend endpoints

| Method | Path | Auth | Body / Query | Response |
|---|---|---|---|---|
| POST | `/api/articles/{id:guid}/feedback` | Authenticated; ownership-checked | `{ precisionScore: 1..5, styleScore: 1..5, comment?: string }` | `200 SubmitArticleFeedbackResponse` / `400` validation / `403 Forbidden` / `404 ArticleNotFound` / `409 ArticleNotGenerated|ArticleFeedbackAlreadySubmitted` |
| GET | `/api/articles/feedback/list` | Policy: `ArticleGenerator` | `hasFeedback?, requestedBy?, sortBy?, sortDescending?, pageNumber?, pageSize?` | `200 GetArticleFeedbackListResponse` |

`GET /api/articles/{id}` (existing) is extended in payload only — adds `precisionScore`, `styleScore`, `feedbackComment` on the article and `knowledgeBaseChunkId`, `excerpt` on each source.

### MediatR handlers (new)
- `SubmitArticleFeedbackRequest` → `SubmitArticleFeedbackHandler` → `SubmitArticleFeedbackResponse`
- `GetArticleFeedbackListRequest` → `GetArticleFeedbackListHandler` → `GetArticleFeedbackListResponse`

### Repository surface (new methods on `IArticleRepository`)
```csharp
Task<(IReadOnlyList<Article> Items, int TotalCount)> GetArticlesPagedAsync(
    bool? hasFeedback, string? requestedBy, string sortBy, bool descending,
    int page, int pageSize, CancellationToken cancellationToken);

Task<ArticleFeedbackStats> GetFeedbackStatsAsync(CancellationToken cancellationToken);
```

### Frontend surfaces
- `ArticleDetail.tsx`: feedback form section + sources list converting KB items to `<button>` triggers for `ChunkDetailModal`.
- `useArticles.ts`: new hooks `useSubmitArticleFeedbackMutation` (treats 409 as `alreadySubmitted`) and `useArticleFeedbackListQuery` (staleTime 30 s, keyed by query params).
- Generated OpenAPI client method bindings: `articles_SubmitFeedback(id, body)` and `articles_GetFeedbackList(params)` are produced via the build-time generator after the controller actions ship.

### UI flows
1. **Submit feedback flow**: User opens generated article → form below sources → selects 1–5 for precision and style, optional comment → submit → success state → on refetch, form replaced by static "Hodnocení" line.
2. **Resubmit attempt**: API returns 409 → form switches to `alreadySubmitted` message via existing `RagFeedbackForm` prop.
3. **KB citation drill-down**: Article detail → click KB source button → `ChunkDetailModal` opens with `chunkId` → user inspects chunk content → close.

## Dependencies

**Phase 1** (must be complete before this phase ships):
- Sidebar entry for KB / chunk browser.
- Shared `ChunkDetailModal` exported from `frontend/src/components/knowledge-base/ChunkDetailModal`.

**Phase 2** (must be complete before this phase ships):
- Shared `RagFeedbackForm` exported from `frontend/src/components/feedback/RagFeedbackForm`.
- `ErrorCodes.Forbidden` already exists across the codebase (yes — used by other features).

**External / library dependencies**: none new. Uses existing MediatR, EF Core, FluentValidation patterns in the repository, plus the existing OpenAPI client generator.

**Internal pipeline dependencies**:
- `IMediator.Send(SearchDocumentsRequest)` returning `ChunkResult[]` with `ChunkId` and `Score` (existing KB search use case).
- `ICurrentUserService` for ownership check (existing).

**Integration branch**: `feature/genai_consistency` — all work cut from and merged back to this branch, not `main`.

## Out of Scope
- Backfilling chunk provenance (`KnowledgeBaseChunkId`, `Confidence`, `Excerpt`, `ValidationNote`) on existing `ArticleSource` rows persisted before this phase.
- Editing or deleting submitted feedback. Feedback is one-shot.
- Aggregating feedback across users or per-prompt-version analytics dashboards.
- Per-section / per-source feedback granularity (only article-level scoring).
- Notifications, email digests, or webhooks on new feedback.
- Admin UI page to render `GET /api/articles/feedback/list`. The endpoint ships in this phase; the consuming page is not in scope here.
- Localization beyond cs/en for the two new error strings.
- Schema-level CHECK constraints on `PrecisionScore`/`StyleScore` ranges (validation is enforced at the application layer via `[Range(1,5)]`).
- HTML rendering or markdown sanitization for `FeedbackComment` (stored and displayed as plain text).

## Test Plan

### Backend
- `SubmitArticleFeedbackHandlerTests`:
  - article not found → `ArticleNotFound`
  - foreign user → `Forbidden`
  - already submitted (either score set) → `ArticleFeedbackAlreadySubmitted`
  - status != Generated → `ArticleNotGenerated`
  - happy path → fields persisted, success response
- `GetArticleFeedbackListHandlerTests`: paging, `hasFeedback` filter (true/false/null), `requestedBy` filter, sort fallback for unknown column, stats math with empty/partial coverage.
- `WriteArticleStepTests` (extended): when LLM cites a title matching a `ContextSnippet`, the produced source tuple carries `ChunkId`, `Confidence`, `Excerpt` (truncated to 200), and `ValidationNote`.
- `GatherContextStepTests`: `ContextSnippet.ChunkId` and `Score` populated from `ChunkResult`.
- `ArticleRepositoryTests` (integration): `GetArticlesPagedAsync` filters/sorts correctly using a real Postgres test DB.

### Frontend
- `ArticleDetail.test.tsx`:
  - renders `RagFeedbackForm` when status=Generated and no scores
  - hides form and shows summary line after scores present
  - KB source with `knowledgeBaseChunkId` renders as button and opens `ChunkDetailModal`
  - KB source without `knowledgeBaseChunkId` renders as plain span
  - Web source still renders as `<a>`
- `useArticles.test.ts`: `useSubmitArticleFeedbackMutation` maps 409 response to `alreadySubmitted` semantics.

### Manual verification (per brief)
1. Generate article → wait for `Generated` → detail shows form.
2. Submit feedback → DB row carries `PrecisionScore`/`StyleScore`/`FeedbackComment`.
3. Re-submit → 409 → UI shows already-submitted message.
4. Click KB source in detail → `ChunkDetailModal` opens with correct content.
5. `GET /api/articles/feedback/list` (role-gated) returns paged data + stats.

## Open Questions
None.

## Status: COMPLETE