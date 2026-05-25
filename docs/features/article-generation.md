# Article Generation — Heblo Feature Spec

**Date:** 2026-05-04
**Supersedes:** `n8n/docs/superpowers/specs/2026-04-17-article-branch-design.md` (was never implemented in n8n)
**Status:** Draft
**Scope change (v1):** n8n/email integration removed. Users trigger generation via the Heblo UI. The pipeline is 5 stages (no EnrichBrief).

---

## 1. Context & Motivation

Article generation is now a Heblo feature, exposing REST endpoints and a React UI. (The earlier n8n design was never implemented.)

Heblo already has all the infrastructure needed:
- **pgvector + OpenAI `text-embedding-3-large` (1536-d)** with HNSW cosine retrieval (`KnowledgeBaseRepository`)
- **Anthropic `IChatClient`** wrapping `claude-sonnet-4-6` with Polly retry + `PostAnswerEnrichmentMiddleware`
- **MediatR vertical-slice** feature pattern (`Application/Features/{Feature}/UseCases/{Action}/`)
- **Hangfire** for background jobs with `IRecurringJob` abstraction
- **Microsoft Graph** for SharePoint/OneDrive access (`GraphOneDriveService`)
- **React + NSwag-generated TypeScript client** with TanStack Query and i18n (Czech default)
- **Azure AD auth** via `Microsoft.Identity.Web` (cookie + JWT, role-based)

Building the feature in Heblo lets us reuse all of the above and persist generated articles with full version control and testing.

---

## 2. Goals

1. Generate Czech-language articles on demand from a structured brief in the Heblo UI.
2. Blend three context sources during research: **internal KB**, **external web search**, **optional SharePoint style guide**.
3. Expose REST endpoints for the React UI and future integrations.
4. Persist every article + its sources for review, feedback, and re-ingestion later.
5. Match existing Heblo conventions exactly so a new contributor can read three files and understand the slice.

## 3. Non-Goals

- **Streaming output.** `AnthropicChatClient.GetStreamingResponseAsync` already throws `NotSupportedException`; UI uses polling instead.
- **Auto-publishing to any CMS.** Out of scope for v1.
- **Multi-language output.** Czech only in v1; the prompt enforces it.

---

## 4. High-Level Architecture

```
┌────────────────────────────────────┐
│  Heblo UI / ArticleController      │
│  POST /api/Articles/generate       │
└─────────────┬──────────────────────┘
              │ enqueue
              ▼
    ┌─────────────────────────┐
    │ Hangfire job            │
    │ GenerateArticleJob      │
    └─────────────┬───────────┘
       ┌──────────┼──────────────────────────────────┐
       ▼          ▼                                  ▼
┌──────────────────────┐    ┌──────────────────┐    ┌────────────────────┐    ┌──────────────────┐
│ PlanQueries         │ -> │ GatherContext    │ -> │ Aggregate+Validate │ -> │ WriteArticle     │
│ (Haiku)             │    │ (parallel: KB +  │    │ (Sonnet + Haiku)   │    │ (Sonnet 4-6)     │
│                     │    │  Web + Style)    │    │                    │    │                  │
└─────────────────────┘    └──────────────────┘    └────────────────────┘    └────────┬─────────┘
                                                                                       │ persist
                                                                                       ▼
                                                                              ┌──────────────────┐
                                                                              │ Article + Sources │
                                                                              │ (Postgres)        │
                                                                              └──────────────────┘
```

---

## 5. New Components Summary

| Component | Type | Path |
|---|---|---|
| `Article`, `ArticleSource`, `ArticleStatus`, `SourceType` | Domain entities/enums | `Domain/Features/Articles/` |
| `IArticleRepository` | Repository interface | `Domain/Features/Articles/` |
| `ArticleRepository`, `ArticleConfiguration`, `ArticleSourceConfiguration` | Persistence | `Persistence/Articles/` |
| Migration `AddArticles` | EF Core | `Persistence/Migrations/` |
| `ArticlesModule.cs`, `ArticleOptions.cs` | DI + bound config | `Application/Features/Articles/` |
| `GenerateArticleHandler`, `GetArticleHandler`, `ListArticlesHandler`, `SubmitArticleFeedbackHandler` | MediatR handlers | `Application/Features/Articles/UseCases/` |
| `PlanQueriesHandler`, `GatherContextHandler`, `AggregateFactsHandler`, `ValidateFactsHandler`, `WriteArticleHandler` | Internal pipeline handlers | same folder, sub-namespaces |
| `GenerateArticleJob` | Hangfire job | `Application/Features/Articles/Pipeline/` |
| `ArticlesController` | HTTP | `API/Controllers/` |
| `Anela.Heblo.Adapters.WebSearch` | New adapter project | `Adapters/Anela.Heblo.Adapters.WebSearch/` |
| `IWebSearchClient`, `SerpApiWebSearchClient`, `MockWebSearchClient` | Web-search abstraction | inside the adapter |
| `IOneDriveService.DownloadFileTextByPathAsync` | Extension to existing service | `Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs` |
| `ArticlesPage`, `ArticleGenerationForm`, `ArticleList`, `ArticleDetail` | React | `frontend/src/pages/`, `frontend/src/features/articles/` |
| `useArticles.ts` | TanStack Query hooks | `frontend/src/api/hooks/` |
| Tests | xUnit + Moq | `backend/test/Anela.Heblo.Tests/Articles/` |

---

## 6. Domain Model

```csharp
// Domain/Features/Articles/Article.cs
public class Article
{
    public Guid Id { get; set; }
    public string Topic { get; set; } = "";
    public string Scope { get; set; } = "";          // overview | deep-dive | technical | beginner
    public string Audience { get; set; } = "";
    public string Angle { get; set; } = "";
    public string Length { get; set; } = "";          // short (500w) | medium (1000w) | long (2000w+)
    public string? LanguageNote { get; set; }
    public bool UsedKnowledgeBase { get; set; }
    public bool UsedWebSearch { get; set; }
    public string? StyleGuideDriveId { get; set; }
    public string? StyleGuideItemPath { get; set; }
    public string? Title { get; set; }
    public string? HtmlContent { get; set; }
    public ArticleStatus Status { get; set; }
    public string? ErrorMessage { get; set; }
    public string? RequestedBy { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? GeneratedAt { get; set; }
    public List<ArticleSource> Sources { get; set; } = new();
}

public class ArticleSource
{
    public Guid Id { get; set; }
    public Guid ArticleId { get; set; }
    public string Title { get; set; } = "";
    public string? Url { get; set; }
    public SourceType Type { get; set; }
    public double? Confidence { get; set; }
    public Guid? KnowledgeBaseChunkId { get; set; }
    public string? Excerpt { get; set; }
    public string? ValidationNote { get; set; }
}

public enum ArticleStatus { Queued = 0, Researching = 1, Writing = 2, Generated = 3, Failed = 4 }
public enum SourceType   { Web = 0, KnowledgeBase = 1, StyleGuide = 2 }
```

---

## 7. REST API

All endpoints `[Authorize]`. POST `/generate` requires role `marketing_writer`; reads require `heblo_user`.

### `POST /api/Articles/generate`
Request body (class, not record — OpenAPI codegen requirement):
```csharp
public class GenerateArticleRequest : IRequest<GenerateArticleResponse>
{
    [Required, MaxLength(2000)] public string Topic { get; set; } = "";
    [Required] public string Scope { get; set; } = "overview";       // enum-validated
    [MaxLength(500)] public string? Audience { get; set; }
    [MaxLength(500)] public string? Angle { get; set; }
    [Required] public string Length { get; set; } = "medium (1000w)";
    [MaxLength(500)] public string? LanguageNote { get; set; }
    public bool UseKnowledgeBase { get; set; } = true;
    public bool UseWebSearch { get; set; } = true;
    public StyleGuideRef? StyleGuide { get; set; }
    public string? RequestedBy { get; set; }
}

public class StyleGuideRef { public string DriveId { get; set; } = ""; public string ItemPath { get; set; } = ""; }
```

Response (`BaseResponse`):
```csharp
public class GenerateArticleResponse : BaseResponse
{
    public Guid? ArticleId { get; set; }
    public string? HangfireJobId { get; set; }
    public ArticleStatus Status { get; set; }
}
```

Behavior: validates request → inserts `Article` row with `Status=Queued` → enqueues `GenerateArticleJob.Run(articleId)` via `IBackgroundJobClient` → returns immediately.

### `GET /api/Articles/{id}`
Returns full article + sources.

### `GET /api/Articles?status=&page=&pageSize=`
Paged list, default pageSize=20, max 100.

### `POST /api/Articles/{id}/feedback`
`{rating: 1..5, comment?: string}`. Persisted in a `ArticleFeedback` side-table (or reuse `KnowledgeBaseFeedback` shape).

---

## 8. Pipeline Handlers (in execution order inside `GenerateArticleJob`)

### 8.1 `PlanQueriesHandler`
- **Model:** `claude-haiku-4-5-20251001` via `IChatClient`
- **System prompt** (stored in `ArticleOptions.QueryPlannerSystemPrompt`):
  > Generate 6-8 search queries to thoroughly research an article. Cover: (1) definition/what it is, (2) recent news/developments (current year and prior), (3) statistics/data/studies, (4) expert opinions/authoritative sources, (5) Czech-specific context if relevant, (6) counterarguments/nuance. Respond ONLY with valid JSON (no markdown): `{"queries": ["..."]}`
- **Fallback:** seed-based fallback (`<seed>`, `<seed> statistiky`, `<seed> recenze`)

### 8.2 `GatherContextHandler` (parallel — `Task.WhenAll`)

- **KB branch** (`UseKnowledgeBase=true`): for each query, call `IMediator.Send(new SearchDocumentsRequest { Query = q, TopK = options.KnowledgeBaseTopK })`. Map results to `ContextSnippet { Source=KB, Title=filename, Excerpt, Url=null, ChunkId }`.
- **Web branch** (`UseWebSearch=true`): for each query, `IWebSearchClient.SearchAsync(q, new WebSearchOptions { Locale="cs", Geo="cz", Top=5 })`. Map to `ContextSnippet { Source=Web, Title, Excerpt=snippet, Url=link }`.
- **Style guide branch** (only if `StyleGuide` provided): `IOneDriveService.DownloadFileTextByPathAsync(StyleGuide.DriveId, StyleGuide.ItemPath)` → string stored in pipeline state.
- All branch failures are caught per-query/per-source; partial results pass through. Total failure of a branch only logs a warning.

### 8.3 `AggregateFactsHandler`
- **Model:** `claude-sonnet-4-6` via `IChatClient`, `MaxTokens = 1024`
- **Input:** `topic, angle, scope` + flat list of all `ContextSnippet`s
- **System prompt** (Czech): synthesize into `{facts:[{claim, confidence, source_url, source_title}], summary, gaps}` — JSON-only output
- **Fallback:** parse failure → empty facts, raw concatenated snippets in `summary`

### 8.4 `ValidateFactsHandler`
- **Model:** `claude-haiku-4-5-20251001`
- **Output:** annotates each fact with `validation_note` (or null). **No gating** — annotations only; user-visible later.

### 8.5 `WriteArticleHandler`
- **Model:** `claude-sonnet-4-6`, `MaxTokens = 4096`
- **System prompt structure:**
  1. Optional style guide block: `STYLE GUIDE — follow this exactly:\n<style_guide>\n\n`
  2. Persona + JSON instruction:
     > You are an expert Czech article writer. Write articles in Czech only. Output ONLY valid JSON (no markdown, no code fences):
     > `{"article_title":"...","article_html":"<h2>... — no <html>/<body> wrapper","sources_used":[{"title":"...","url":"..."}]}`
- **User prompt template:**
  ```
  Write a {length} Czech article.
  Topic: {topic}
  Audience: {audience}
  Angle: {angle}
  Scope: {scope}
  [Tone note: {language_note}]

  Research facts:
  1. {claim} [zdroj: {source}] [poznámka: {validation_note}]
  ...

  Requirements:
  - Write entirely in Czech
  - Cite sources naturally inline
  - Output valid HTML for email (no html/body wrapper)
  - Only include sources that support specific claims
  ```
- **Fallback:** parse failure → wrap raw text in `<p>...</p>`, derive title from topic, empty sources

#### 8.5.1 Custom prompt templates

`ArticleOptions.WriteArticleSystemPromptTemplate` is rendered via `string.Replace`. Available placeholders:

| Placeholder | Value | Notes |
|---|---|---|
| `{topic}` | `Article.Topic` | Always present. |
| `{audience}` | `Article.Audience` or `"obecné publikum"` if null. | |
| `{length}` | `Article.Length` | E.g. `"medium (1000w)"`. |
| `{angle}` | `Article.Angle` or `"(nevyspecifikováno)"` if null. | |
| `{scope}` | `Article.Scope` raw value | One of `overview`, `deep-dive`, `how-to`, `comparison`. |
| `{language_note}` | `Article.LanguageNote` raw value, or `""` if null. | Use this for full-line custom templates. |
| `{tone_note_line}` | Composed line: `Tonalita: <note>` when present, `""` when absent. | Use this to add a single self-contained line that disappears when no note is supplied. |
| `{facts}` | Numbered list of aggregated facts. | |
| `{style_guide}` | Style guide body, or `""` if none. | |

**Back-compat:** appsettings overrides that omit `{scope}` or `{tone_note_line}` continue to work — those values are simply not surfaced to the LLM. To surface them, add the placeholders to the override.

### 8.6 Persistence
- Update `Article` row with `Title`, `HtmlContent`, `Status=Generated`, `GeneratedAt = now`.
- Insert `ArticleSource` rows (one per `sources_used` entry; type=Web for URLs, type=KnowledgeBase for KB-tagged ones, type=StyleGuide if used).
- Any unhandled exception → `Status=Failed`, `ErrorMessage=<truncated message>`. No retry in v1 — user can re-trigger from UI.

---

## 9. New Adapter — `Anela.Heblo.Adapters.WebSearch`

```csharp
public interface IWebSearchClient
{
    Task<WebSearchResult> SearchAsync(string query, WebSearchOptions options, CancellationToken ct);
}

public class WebSearchOptions { public string Locale { get; set; } = "cs"; public string Geo { get; set; } = "cz"; public int Top { get; set; } = 5; }
public class WebSearchResult { public string Query { get; set; } = ""; public List<WebSearchHit> Hits { get; set; } = new(); }
public class WebSearchHit { public string Title { get; set; } = ""; public string Url { get; set; } = ""; public string Snippet { get; set; } = ""; }
```

Default impl: `SerpApiWebSearchClient` (HttpClient + Polly retry, mirrors `OpenAiEmbeddingGenerator` pattern). Mock: `MockWebSearchClient` returns canned hits — registered automatically in Development when `WebSearch:Provider == "Mock"`.

Registration: `services.AddWebSearchAdapter(configuration)` reads `WebSearch:Provider` and `WebSearch:ApiKey`.

> **Note:** SerpAPI is the recommended provider (cs/cz locale is well-supported; pricing transparent). Bing/Brave/Google CSE can replace it later behind the same `IWebSearchClient` interface.

---

## 10. OneDrive Service Extension

Add to `IOneDriveService`:
```csharp
Task<string> DownloadFileTextByPathAsync(string driveId, string path, CancellationToken ct);
```
Implementation in `GraphOneDriveService` calls `GET /drives/{driveId}/root:/{path}:/content`, decodes UTF-8, returns text. `MockOneDriveService` returns a canned string.

---

## 11. Persistence

Migration `AddArticles`:
- Table `dbo.Articles` — columns matching domain entity, `Status` as `int`, `Topic` as `varchar(2000)`, index on `(Status, CreatedAt)`.
- Table `dbo.ArticleSources` — FK to `Articles` with cascade delete, `Url varchar(2000)`, `Type` as `int`.
- Optional `dbo.ArticleFeedback` for the feedback endpoint.

Configurations follow `KnowledgeBaseDocumentConfiguration` style: explicit `HasMaxLength`, `HasIndex`, `HasConversion<int>()` for enums, column names PascalCase.

---

## 12. Configuration

Add to `appsettings.json`:
```json
"Articles": {
  "DefaultModel": "claude-sonnet-4-6",
  "WriteMaxTokens": 4096,
  "AggregateMaxTokens": 1024,
  "WebSearchTopK": 5,
  "KnowledgeBaseTopK": 8,
  "DefaultLength": "medium (1000w)",
  "QueryPlannerModel": "claude-haiku-4-5-20251001",
  "AggregateFactsModel": "claude-sonnet-4-6",
  "ValidateFactsModel": "claude-haiku-4-5-20251001",
  "QueryPlannerSystemPrompt": "...",
  "AggregateFactsSystemPrompt": "...",
  "ValidateFactsSystemPrompt": "...",
  "WriteArticleSystemPromptTemplate": "..."
},
"WebSearch": {
  "Provider": "SerpApi",
  "ApiKey": "",
  "Endpoint": "https://serpapi.com/search.json",
  "DefaultLocale": "cs",
  "DefaultGeo": "cz",
  "TimeoutSeconds": 15
}
```

User Secrets in dev for keys; env vars / App Service config in prod (mirrors `Anthropic:ApiKey` and `OpenAI:ApiKey`).

`ArticleOptions` binds via `services.AddOptions<ArticleOptions>().Bind(...).ValidateDataAnnotations().ValidateOnStart()` — same pattern as `KnowledgeBaseOptions`.

---

## 13. DI / Module Registration

`Application/Features/Articles/ArticlesModule.cs`:
```csharp
public static IServiceCollection AddArticlesModule(this IServiceCollection services, IConfiguration cfg)
{
    services.AddOptions<ArticleOptions>()
        .Bind(cfg.GetSection(ArticleOptions.SectionName))
        .ValidateDataAnnotations().ValidateOnStart();
    return services;
}
```

Wired in `ApplicationModule.AddApplicationServices` alongside the existing `AddKnowledgeBaseModule`. Web-search adapter wired in `Program.cs`: `builder.Services.AddWebSearchAdapter(builder.Configuration);`.

MediatR handlers are auto-discovered. Hangfire job is a regular class with `[AutomaticRetry(Attempts = 0)]` — retries are UI-triggered.

---

## 14. Auth & Roles

- Uses the unified `marketing_writer` role — assigned to Heblo human users with content-creation responsibilities (admin assigns)
- `[Authorize(Roles = "marketing_writer")]` on `POST /generate`
- `[Authorize]` (default `heblo_user`) on `GET` and feedback endpoints
- `KnowledgeBaseUpload` policy is **not** reused — articles are a separate concern
- **Note:** The earlier `article_generator` role was removed and replaced by the unified `marketing_writer` role.

---

## 15. Frontend

- Page: `frontend/src/pages/ArticlesPage.tsx` — wired into `App.tsx` and i18n nav
- Feature folder: `frontend/src/features/articles/`
  - `ArticleGenerationForm.tsx` — topic / scope select / audience / angle / length select / source toggles / optional style guide path
  - `ArticleList.tsx` — recent articles, status badges, click → detail
  - `ArticleDetail.tsx` — renders `htmlContent` in a sandboxed div; sources list with type icons; feedback widget
- Hook: `frontend/src/api/hooks/useArticles.ts` — TanStack Query mutations/queries calling NSwag client; absolute URLs
- i18n keys under `cs.translation.articles.*`
- Polling: when `Status ∈ {Queued, Researching, Writing}`, refetch every 3s
- New error codes added to `LocalizationCoverageTests` whitelist

---

## 16. Testing

- **Unit tests** (xUnit + Moq):
  - `PlanQueriesHandlerTests` — verify 6–8 query bound, fallback path
  - `GatherContextHandlerTests` — mock `IMediator`, `IWebSearchClient`, `IOneDriveService`; verify parallel execution and partial-failure handling
  - `AggregateFactsHandlerTests`, `ValidateFactsHandlerTests`, `WriteArticleHandlerTests`
  - `GenerateArticleJobTests` — full pipeline with mocks; assert status transitions
  - `ArticlesControllerTests` — happy + 401/403 paths
- **Adapter tests:** `Anela.Heblo.Adapters.WebSearch.Tests` — `SerpApiWebSearchClient` against `HttpMessageHandler` mock
- **Smoke / startup:** new module appears in `ApplicationStartupTests`
- **Localization coverage:** new error codes added to `LocalizationCoverageTests`

---

## 17. Error Codes

`ArticleNotFound`, `ArticleGenerationFailed`, `WebSearchUnavailable`, `StyleGuideFetchFailed`, `ArticleBriefInvalid`, `ArticleAlreadyGenerated`. All added to `ErrorCodes` enum + Czech and English translations.

---

## 18. Migration / Rollout

1. **Backend:** implement adapter → domain → persistence → handlers → controller → tests.
2. **Frontend:** ship UI tab; verify end-to-end manually.

---

## 19. Open Questions

1. **Re-ingestion:** should generated articles be auto-ingested back into the KB (as `DocumentType=Article`) so future KB questions can cite them? Recommendation: yes, but only after human approval — defer to v1.1.
2. **Web search provider:** SerpAPI vs Bing vs Brave. Recommendation: SerpAPI for v1; pluggable behind `IWebSearchClient`.
3. **Approval workflow:** "draft → human approves → publish" flow? Recommendation: defer to v1.1; add `AwaitingApproval` status then.
4. **Quota / rate limiting:** log per-article token + search count in v1; add per-day cap in v1.1.
5. **Streaming UI updates:** 3s polling is fine for v1; SignalR can be added later.

---

## 20. Acceptance Criteria

- [ ] `POST /api/Articles/generate` returns `{articleId, jobId, Status: Queued}` within 500ms
- [ ] Hangfire job completes within 90s for a typical request (medium length, KB+web)
- [ ] `GET /api/Articles/{id}` returns `Generated` with valid HTML and ≥1 source for a non-trivial topic
- [ ] React UI form submits and shows live status updates via polling
- [ ] Style guide fetch works for a real SharePoint file; absence falls back gracefully
- [ ] All new endpoints covered by unit tests; coverage ≥ 80% on the slice

---

## 21. Critical Files to Reference During Implementation

- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionHandler.cs` — model for `IChatClient` usage + JSON parse fallback
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SearchDocuments/SearchDocumentsHandler.cs` — KB retrieval; reuse via `IMediator.Send`
- `backend/src/Anela.Heblo.Adapters.OpenAI/OpenAiEmbeddingGenerator.cs` — HttpClient + Polly retry adapter pattern (mirror in `SerpApiWebSearchClient`)
- `backend/src/Anela.Heblo.Adapters.Anthropic/AnthropicChatClient.cs` — `IChatClient` registration pattern
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/Jobs/KnowledgeBaseIngestionJob.cs` — Hangfire job pattern
- `backend/src/Anela.Heblo.Persistence/Migrations/20260302163014_AddKnowledgeBase.cs` — migration style for adjacent tables
- `frontend/src/pages/KnowledgeBasePage.tsx` + `frontend/src/api/hooks/useKnowledgeBase.ts` — page + hook pattern to mirror

---

## 22. Verification Plan

1. **Local manual:** spin up Heblo (`dotnet run` + `npm start`), open Articles page, submit topic "fermentované potraviny pro střevní zdraví", confirm pipeline runs end-to-end with both KB and web hits.
2. **API:** `curl POST /api/Articles/generate` with a JWT, poll `GET /api/Articles/{id}`, inspect `htmlContent` and `sources`.
3. **Failure paths:** disable web-search key → article still generated from KB only; broken style-guide path → graceful fallback note.
4. **Tests:** `dotnet test` passes all new test projects with ≥80% line coverage on the `Articles` slice.

---

## 23. Follow-ups

### Resolve RequestedBy identifier → display name (UX)

After 2026-05-25, `Article.RequestedBy` stores a stable Entra identifier
(OID or email) instead of a display name. The feedback list and detail
views currently render the raw value, which now appears as an opaque OID.

**Affected frontend components:**
- `frontend/src/components/feedback/GenericFeedbackDetailModal.tsx` — renders
  `detail.userId` (maps from `Article.RequestedBy` via adapter) at line 51
- `frontend/src/api/hooks/useArticles.ts` — passes through `requestedBy` OID
  from backend to adapter

**Fix scope:**
- `backend/src/Anela.Heblo.Application/Features/Articles/UseCases/GetFeedback/GetArticleFeedbackListHandler.cs` —
  resolve OID → display name via `IGraphService.GetGroupMembersAsync` (or similar identity resolution, cached) when projecting
  `ArticleFeedbackSummary.RequestedBy`.
- Frontend renderers consume the resolved name; no UI logic change required
  if the backend swap is transparent.

**Tracked separately** from the ownership/security fix that introduced the
regression.
