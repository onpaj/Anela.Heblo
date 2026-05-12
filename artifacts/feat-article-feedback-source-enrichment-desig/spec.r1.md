# Specification: Article Feedback + Source Enrichment

## Summary
Add a feedback layer (precision/style 1–5 scores + optional comment) to generated articles, enrich `ArticleSource` records with chunk references and validation context from the generation pipeline, and convert KB source citations in the article detail view into clickable triggers that open the existing `ChunkDetailModal`. Single migration, no breaking API changes.

## Background
Phase 3 of the article generation feature (issue #933) extends the existing pipeline introduced in `feature/genai_consistency`. Three gaps need closing:

1. **No feedback channel.** Generated articles have no mechanism for the requester to record quality signals. The KB and Leaflet features already use a precision/style score + comment pattern; articles should follow it for consistency and to feed future quality dashboards.
2. **`ArticleSource` is half-populated.** The pipeline persists `Title`, `Url`, and `Type` but leaves `KnowledgeBaseChunkId`, `Confidence`, `Excerpt`, and `ValidationNote` null. The data exists in the pipeline (`ContextSnippet`, `AggregatedFact`) but is not threaded into the saved entity.
3. **KB sources are dead text.** Article detail renders KB sources as plain strings even though `ChunkDetailModal` is already wired up for KB search. Users have no way to inspect the underlying chunk to verify a citation.

## Functional Requirements

### FR-1: Article feedback domain extension
Extend the `Article` aggregate with three nullable feedback properties using the flat-column pattern established by `KnowledgeBaseQuestionLog`. No separate feedback entity.

**Acceptance criteria:**
- `Article` exposes `PrecisionScore : int?`, `StyleScore : int?`, `FeedbackComment : string?`.
- Migration `AddArticleFeedbackColumns` adds the three columns plus a filtered index on `PrecisionScore` (`WHERE "PrecisionScore" IS NOT NULL`) and applies cleanly to a populated database.
- `FeedbackComment` is mapped as `text` in `ArticleConfiguration`.
- Existing articles remain valid; all three columns default to NULL.

### FR-2: Submit feedback use case
Add a `SubmitArticleFeedback` MediatR request that validates and persists feedback.

**Acceptance criteria:**
- Request shape: `ArticleId : Guid`, `PrecisionScore : int [Range 1..5]`, `StyleScore : int [Range 1..5]`, `Comment : string? [MaxLength 1000]`.
- Validation chain (fail-fast, in order):
  1. Article missing → error code `ArticleNotFound` (HTTP 404).
  2. Current user identity ≠ `article.RequestedBy` → `Forbidden` (HTTP 403).
  3. `article.Status != Generated` → `ArticleNotGenerated` (HTTP 422).
  4. Either `PrecisionScore` or `StyleScore` already set → `ArticleFeedbackAlreadySubmitted` (HTTP 409).
- On success, sets all three fields, calls `SaveChangesAsync`, returns 200 with the updated values.
- Both scores are required to submit (frontend enforces; backend `[Range]` validation rejects 0/null).

### FR-3: Feedback list use case
Add `GetArticleFeedbackList` returning paged articles + aggregate stats, gated by the `ArticleGenerator` policy (same as the KB `GetFeedbackList` endpoint).

**Acceptance criteria:**
- Query parameters: `hasFeedback : bool?`, `requestedBy : string?`, `sortBy : string` (default `CreatedAt`), `descending : bool` (default true), `page : int` (default 1), `pageSize : int` (default 20).
- `hasFeedback=true` filters to articles where either score is non-null; `=false` to articles where both are null; `null` returns all.
- Allowed `sortBy` values: `CreatedAt`, `PrecisionScore`, `StyleScore`. Unknown values fall back to `CreatedAt`.
- Response contains: `ArticleFeedbackSummary[]`, `ArticleFeedbackStatsDto`, `TotalCount`, `Page`, `PageSize`.
- Repository call and stats query execute in parallel (`Task.WhenAll`).
- Non-`ArticleGenerator` callers receive HTTP 403.

### FR-4: Aggregate stats projection
Add `ArticleFeedbackStats` sealed record exposing `TotalArticles`, `TotalWithFeedback`, `AvgPrecisionScore : double?`, `AvgStyleScore : double?`. Averages are null when no feedback exists.

**Acceptance criteria:**
- Computed in a single SQL aggregation via `IArticleRepository.GetFeedbackStatsAsync`.
- `TotalWithFeedback` counts articles where either score is non-null.
- Averages are computed only over rows where the corresponding score is non-null.

### FR-5: Pipeline source enrichment
Fix `WriteArticleStep.MapSources` and `GenerateArticleJob` so each persisted `ArticleSource` carries `KnowledgeBaseChunkId`, `Confidence`, `Excerpt`, and `ValidationNote` when available.

**Acceptance criteria:**
- `MapSources` signature returns tuples including `ChunkId`, `Confidence`, `Excerpt`, `ValidationNote`.
- For each `SourceUsedDto`:
  - Title-match against `context.ContextSnippets` → assigns `ChunkId` and `Score → Confidence`.
  - Title-match against `AggregatedFact` list → assigns `Claim → Excerpt` (truncated to 200 chars) and `ValidationNote`.
  - `Type = Url != null ? Web : KnowledgeBase`.
- Web sources have `KnowledgeBaseChunkId == null`.
- KB sources whose title matches no snippet still persist with null chunk fields (no failure).
- `GenerateArticleJob` constructs `ArticleSource` with all six new fields (`KnowledgeBaseChunkId`, `Confidence`, `Excerpt`, `ValidationNote` plus existing `Title`, `Url`, `Type`).
- Existing articles are unaffected (no backfill).

### FR-6: API endpoints
Extend `ArticlesController` with two endpoints.

**Acceptance criteria:**
- `POST /api/articles/{id}/feedback` → `SubmitArticleFeedback`. Authenticated user only. Returns 200 on success, 404/403/422/409 per FR-2.
- `GET /api/articles/feedback/list` → `GetArticleFeedbackList`. Gated by `ArticleGenerator` policy. Returns 200 with the paged list + stats payload.
- Both endpoints surface OpenAPI documentation; the TypeScript client is regenerated on build.
- No existing endpoints change.

### FR-7: DTO surface updates
Extend response DTOs to expose feedback fields and chunk references.

**Acceptance criteria:**
- `GetArticleResponse` adds `PrecisionScore : int?`, `StyleScore : int?`, `FeedbackComment : string?`.
- `ArticleSourceDto` adds `KnowledgeBaseChunkId : Guid?`, `Excerpt : string?` (the existing `Confidence` and `ValidationNote` fields, if absent in the DTO, are added so the frontend can display them; verify during implementation).
- DTOs remain classes (per project rule — never C# records).
- New properties are nullable; existing consumers ignoring them continue to work.

### FR-8: Reusable feedback form component
Extract a shared `RagFeedbackForm` component (path: `components/feedback/RagFeedbackForm.tsx`) from the inline KB pattern in `KnowledgeBaseSearchAskTab.tsx`.

**Acceptance criteria:**
- Props: `onSubmit({precisionScore, styleScore, comment?})`, `isSubmitting`, `isSuccess`, `alreadySubmitted`.
- Renders two `ScoreRow` instances (1–5 dot selector) + optional comment textarea (max 1000 chars) + submit button.
- Submit button disabled until both scores are selected and `!isSubmitting`.
- Shows success confirmation when `isSuccess === true`.
- Shows "already submitted" notice (no form) when `alreadySubmitted === true`.
- No API calls inside the component — fully controlled.
- The KB tab (`KnowledgeBaseSearchAskTab.tsx`) is updated to consume the shared component to prove reuse and avoid duplicate code.

### FR-9: Article detail feedback section
Render the feedback form inside `ArticleDetail.tsx` under the article iframe, with conditional logic.

**Acceptance criteria:**
- When `article.status === 'Generated'` and both `precisionScore` and `styleScore` are null → render `RagFeedbackForm`.
- When either score is set → render read-only summary in Czech: `"Hodnocení: Přesnost X/5, Styl Y/5"` followed by the comment if non-empty.
- When `article.status !== 'Generated'` → no feedback UI shown.
- Submission uses `useSubmitArticleFeedbackMutation`; HTTP 409 sets `alreadySubmitted=true` on the form.
- Successful submit invalidates the article query so the read-only summary appears without a manual refresh.

### FR-10: Clickable KB source citations
Update `SourceList` inside `ArticleDetail.tsx` so KB sources with a chunk reference open `ChunkDetailModal`.

**Acceptance criteria:**
- Source rendering rules:
  - `source.url` non-null → `<a href={source.url} target="_blank" rel="noopener noreferrer">` (existing web link behaviour).
  - `source.url == null && source.knowledgeBaseChunkId != null` → `<button type="button">` styled as green text with underline on hover; click sets local `selectedChunkId`.
  - Neither URL nor chunk id → plain `<span>` (degraded KB source).
- `ChunkDetailModal` (existing component at `components/knowledge-base/ChunkDetailModal.tsx`) renders when `selectedChunkId` is non-null and closes via its `onClose` callback (clears state) and via ESC key (handled by the modal itself).
- Modal receives the chunk id and fetches its own data — no prop drilling of chunk content from the article detail.

### FR-11: Frontend hooks and i18n
Add React Query hooks and error-code translations.

**Acceptance criteria:**
- `useSubmitArticleFeedbackMutation` calls `client.articles_SubmitFeedback(articleId, data)`; on 409 surfaces an `alreadySubmitted` flag via the mutation's error path.
- `useArticleFeedbackListQuery(params)` with `staleTime: 30_000` and key derived from `articleKeys.feedbackList(params)`.
- Successful mutation invalidates `articleKeys.detail(articleId)`.
- `i18n.ts` adds:
  - `ArticleFeedbackAlreadySubmitted` — cs: "Zpětná vazba k tomuto článku již byla odeslána." / en: "Feedback for this article has already been submitted."
  - `ArticleNotGenerated` — cs: "Článek ještě nebyl vygenerován." / en: "Article has not been generated yet."

## Non-Functional Requirements

### NFR-1: Performance
- `GetArticleFeedbackList` runs paged repository query and stats aggregation in parallel; combined latency target ≤ 300 ms p95 at 10k articles.
- Filtered index on `PrecisionScore` keeps `hasFeedback=true` queries index-seek-only.
- Stats query is a single round-trip using `COUNT/AVG` with `FILTER` clauses (PostgreSQL).
- `ChunkDetailModal` continues to fetch its own chunk on open; no eager chunk loading on article detail render.

### NFR-2: Security & authorization
- `POST /api/articles/{id}/feedback` requires authentication; only `article.RequestedBy` can submit (others get 403).
- `GET /api/articles/feedback/list` requires the `ArticleGenerator` policy.
- Comment input is bounded server-side (`MaxLength(1000)`) and rendered as plain text (no HTML) to prevent XSS.
- No new secrets, tokens, or sensitive data introduced.

### NFR-3: Data integrity
- Feedback submission is a single `SaveChangesAsync` write; no partial state.
- The "already submitted" check uses the loaded entity inside the same DbContext transaction; concurrent double-submit is mitigated by application-level check (acceptable given low contention — solo user per article).
- Migration is forward-only; no destructive change to existing rows.

### NFR-4: Testing & coverage
- 80%+ line coverage on new code.
- Unit tests for handlers, mappers, and components (see "Testing" section below); integration test for repository paging/filter.
- All new tests run inside the existing xUnit / Vitest suites; no new test runners.

### NFR-5: Backwards compatibility
- Zero breaking changes to existing endpoints, DTOs, or persisted entities (additive only).
- Existing TypeScript clients continue to compile; new optional fields are appended.

## Data Model

### `Article` (modified)
| Field | Type | Notes |
|---|---|---|
| `PrecisionScore` | `int?` | Range 1..5 enforced at use case |
| `StyleScore` | `int?` | Range 1..5 enforced at use case |
| `FeedbackComment` | `string?` | `text` column, ≤ 1000 chars |

Indexed: filtered index on `PrecisionScore WHERE PrecisionScore IS NOT NULL`.

### `ArticleSource` (existing entity, fully populated)
Already-defined fields `KnowledgeBaseChunkId`, `Confidence`, `Excerpt`, `ValidationNote` start receiving non-null values from FR-5. No schema change.

### `ArticleFeedbackStats` (new value object)
```csharp
public sealed record ArticleFeedbackStats(
    int TotalArticles,
    int TotalWithFeedback,
    double? AvgPrecisionScore,
    double? AvgStyleScore);
```

### Repository contract additions
```csharp
Task<(IReadOnlyList<Article> Items, int TotalCount)> GetArticlesPagedAsync(
    bool? hasFeedback, string? requestedBy, string sortBy, bool descending,
    int page, int pageSize, CancellationToken ct);

Task<ArticleFeedbackStats> GetFeedbackStatsAsync(CancellationToken ct);
```

## API / Interface Design

### Endpoints

| Method | Path | Auth | Purpose |
|---|---|---|---|
| `POST` | `/api/articles/{id}/feedback` | Authenticated, owner only | Submit feedback (FR-2) |
| `GET` | `/api/articles/feedback/list` | `ArticleGenerator` policy | Paged feedback + stats (FR-3) |

### Request — `SubmitArticleFeedbackRequest`
```csharp
public class SubmitArticleFeedbackRequest : IRequest<SubmitArticleFeedbackResponse>
{
    public Guid ArticleId { get; set; }
    [Range(1, 5)] public int PrecisionScore { get; set; }
    [Range(1, 5)] public int StyleScore { get; set; }
    [MaxLength(1000)] public string? Comment { get; set; }
}
```

### Response — `GetArticleFeedbackListResponse`
- `Items: ArticleFeedbackSummary[]` (id, title, requestedBy, createdAt, precisionScore, styleScore, hasComment)
- `Stats: ArticleFeedbackStatsDto` (mirrors `ArticleFeedbackStats`)
- `TotalCount`, `Page`, `PageSize`

### Error codes
| Code | HTTP | When |
|---|---|---|
| `ArticleNotFound` | 404 | Article id missing |
| `Forbidden` | 403 | Caller not owner / lacks policy |
| `ArticleNotGenerated` | 422 | Status not `Generated` |
| `ArticleFeedbackAlreadySubmitted` | 409 | Either score already set |

### Frontend flows

**Submit feedback (article detail):**
1. User views generated article → form rendered.
2. Selects scores + optional comment → submit.
3. Mutation success → invalidate article detail query → read-only summary appears.
4. Mutation 409 → form shows "already submitted" notice.

**Open KB chunk from article:**
1. User clicks KB source button in `SourceList`.
2. `selectedChunkId` set → `ChunkDetailModal` opens, fetches chunk by id.
3. User closes (X / ESC / backdrop) → state cleared.

## Dependencies

**Internal**
- Existing `Article` aggregate, `ArticleSource` entity, `ArticleConfiguration`.
- `IArticleRepository` and EF Core `ApplicationDbContext`.
- Article generation pipeline: `WriteArticleStep`, `GenerateArticleJob`, `ContextSnippet`, `AggregatedFact`, `SourceUsedDto`.
- KB feedback pattern: `ScoreRow` component (or its current location), `KnowledgeBaseSearchAskTab` (refactored to consume `RagFeedbackForm`).
- `ChunkDetailModal` at `components/knowledge-base/ChunkDetailModal.tsx`.
- `ArticleGenerator` authorization policy (already defined for the KB feedback endpoint).
- React Query keys factory (`articleKeys`).
- OpenAPI TypeScript client generation pipeline.

**External**
- None new. Existing PostgreSQL, EF Core 8, MediatR, React, React Query, Vitest stack.

## Out of Scope

- Editing or deleting submitted feedback.
- Multiple feedback rounds per article (only first submission accepted).
- Aggregate dashboards / charts for feedback trends.
- Backfilling `KnowledgeBaseChunkId`, `Confidence`, `Excerpt`, `ValidationNote` for historical articles.
- Feedback for non-`Generated` article statuses (drafts, failed, etc.).
- Inline chunk preview without modal.
- Notifications when feedback is received.
- Exporting feedback data.
- Per-source-citation feedback (feedback is article-level only).
- Schema split into a dedicated `ArticleFeedback` table.

## Open Questions

None.

## Status: COMPLETE