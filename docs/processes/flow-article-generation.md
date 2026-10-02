---
process: flow-article-generation
kind: workflow
module: article
summary: Turns a marketer's brief from the Article generator (Generátor článků) into a Czech HTML article via a five-step Claude pipeline (plan queries → knowledge base + web search + optional style guide → facts → fact check → write), run as a one-shot Hangfire job, with every step traced in ArticleGenerationSteps and the cited sources stored in ArticleSources.
owns:
  - backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/**
  - backend/src/Anela.Heblo.Application/Features/Article/UseCases/GenerateArticle/**
  - backend/src/Anela.Heblo.Application/Features/Article/ArticleOptions.cs
  - backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleKnowledgeSource.cs
  - backend/src/Anela.Heblo.Application/Features/Article/Contracts/IArticleStyleGuideSource.cs
  - backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleKnowledgeSource.cs
  - backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSource.cs
  - backend/src/Anela.Heblo.Application/Shared/WebSearch/**
  - backend/src/Adapters/Anela.Heblo.Adapters.WebSearch/**
  - backend/src/Anela.Heblo.Domain/Features/Article/Article.cs
  - backend/src/Anela.Heblo.Domain/Features/Article/ArticleStatus.cs
  - backend/src/Anela.Heblo.Domain/Features/Article/ArticleSource.cs
  - backend/src/Anela.Heblo.Domain/Features/Article/SourceType.cs
  - backend/src/Anela.Heblo.Domain/Features/Article/ArticleGenerationStep*.cs
  - backend/src/Anela.Heblo.Persistence/Features/Article/ArticleConfiguration.cs
  - backend/src/Anela.Heblo.Persistence/Features/Article/ArticleSourceConfiguration.cs
  - backend/src/Anela.Heblo.Persistence/Features/Article/ArticleGenerationStepConfiguration.cs
verified_at: "5e993f9e2"
related: []
---

# Article generation

## Purpose
Marketing writes blog / newsletter articles about cosmetics topics. Instead of starting from a
blank page, a marketer fills in a short brief on page **`/articles` (Generátor článků)** and
Heblo produces a finished **Czech** article in email-ready HTML, researched from Anela's own
knowledge base (internal documents) and from Google search results, optionally following a
house style guide stored in SharePoint/OneDrive. The result is a **draft for a human editor**:
Heblo does not publish it anywhere (no CMS, Shoptet or email integration). The marketer copies
it out of the detail view.

Every step's input, raw model output, duration and model is kept, so the detail page's debug
panel (`GET /api/Articles/{id}/trace`) shows exactly which facts and sources the text was built
from. After generation the requester can rate it (see *Feedback*, below); ratings are reviewed
on `/marketing/feedback` (tab Articles).

## Trigger
On demand only — no recurring job.
1. User with `marketing.article.write` submits the form → `POST /api/Articles/generate`
   (`GenerateArticleHandler`).
2. The handler inserts the `Articles` row with status **Queued** and `RequestedBy` = the
   caller's Entra object id (`CurrentUser.Id ?? Email ?? "system"`), then
   `IBackgroundJobClient.Enqueue<GenerateArticleJob>(j => j.RunAsync(articleId, …))` on the default
   Hangfire queue. The response returns `articleId` and `hangfireJobId`.
3. `GenerateArticleJob` is marked `[AutomaticRetry(Attempts = 0)]`: it runs once and is never
   retried by Hangfire. To try again the user submits a new brief (new article id).

The UI polls `GET /api/Articles/{id}` every 3 s while status is Queued / Researching / Writing.

States (`ArticleStatus`, stored as int): **Queued (0) → Researching (1) → Writing (2) →
Generated (3)**, or **Failed (4)** from any step with `ErrorMessage` (truncated to 500 chars).
A cancelled job ends Failed with `"Job cancelled."`.

## Data flow
Brief fields (`GenerateArticleRequest`): `Topic` (3–500 chars, required), `Scope`
(`overview` | `deep-dive` | `how-to` | `comparison`, default overview), `Length`
(`brief (500w)` | `medium (1000w)` | `long (2000w)`, default medium), optional `Audience`,
`Angle`, `LanguageNote` (tone, ≤500), `UseKnowledgeBase` / `UseWebSearch` (both default true),
optional `StyleGuideDriveId` + `StyleGuideItemPath` (free-text OneDrive drive id and file path).

| # | Step (`StepName`, `Sequence`) | Model (config key) | Reads | Produces |
|---|---|---|---|---|
| 1 | `PlanQueries`, 1 | `Articles:QueryPlannerModel` (Haiku 4.5) | Topic only | 6–8 Czech search queries (capped at 8) |
| 2 | `GatherContext`, 2 | none here (KB search makes its own calls) | queries | KB snippets + web snippets + style-guide text |
| 3 | `AggregateFacts`, 3 | `Articles:AggregateFactsModel` (Sonnet 4.6) | Topic, Angle, Scope, first 50 snippets | list of facts `{claim, confidence, source_url, source_title}` |
| 4 | `ValidateFacts`, 4 | `Articles:ValidateFactsModel` (Haiku 4.5) | fact claims | a reliability note per fact |
| 5 | `WriteArticle`, 5 | `Articles:DefaultModel` (Sonnet 4.6) | brief, facts + notes, style guide | title, HTML, list of sources used |

Status moves to Researching before step 1 and to Writing before step 5.

1. **Plan queries** — system prompt `Articles:QueryPlannerSystemPrompt`, user message = topic,
   max 512 output tokens. If the reply is not valid JSON or has no queries, the fallback is
   `[topic, "{topic} statistiky", "{topic} recenze"]`.
2. **Gather context** — three branches run in parallel:
   - **Knowledge base** (if `UseKnowledgeBase`): for each query, the KnowledgeBase module's
     `SearchDocumentsRequest` (via `KnowledgeBaseArticleKnowledgeSource`) with
     `TopK = Articles:KnowledgeBaseTopK` (8). That search expands the query with
     `KnowledgeBase:QueryExpansionModel`, embeds it with OpenAI `KnowledgeBase:EmbeddingModel`,
     runs a pgvector similarity search over the KB chunks and keeps chunks with score ≥
     `KnowledgeBase:MinSimilarityScore` (0.55). Snippet = chunk text, title = source filename,
     score kept.
   - **Web** (if `UseWebSearch`): for each query, `IWebSearchClient.SearchAsync` with locale
     `cs`, geo `cz`, top `Articles:WebSearchTopK` (5). With `WebSearch:Provider = SerpApi`
     this is `GET https://serpapi.com/search.json?q=…&hl=cs&gl=cz&num=5&api_key=…`
     (Google organic results: title, link, snippet only — pages are not fetched). Results are
     de-duplicated by URL (case-insensitive).
   - **Style guide** (if both drive id and path are given): Microsoft Graph
     `GET /drives/{driveId}/root:/{path}:/content`, read as text.
   A failed query or style-guide download is logged as a warning and skipped; the step still
   succeeds. Snippet order: all KB snippets first, then web.
3. **Aggregate facts** — user message lists topic, angle, scope and up to **50** snippets as
   `n. [title] excerpt`; system prompt `Articles:AggregateFactsSystemPrompt`; max
   `Articles:AggregateMaxTokens` (1024) output tokens. The model returns facts plus a summary
   and gaps; only the facts are carried forward.
4. **Validate facts** — the claims are sent as a JSON array with
   `Articles:ValidateFactsSystemPrompt`; each returned `note` is attached to the fact at the
   **same position**. Skipped when there are no facts. Any error here is swallowed and the
   original facts are kept.
5. **Write article** — system prompt = (`STYLE GUIDE — follow this exactly:` + style guide,
   if any) + `Articles:WriteArticleSystemPrompt`; user message = `Articles:WriteArticleUserPromptTemplate`
   with `{length} {topic} {audience} {angle} {scope} {tone_note_line} {facts} {style_guide}`
   filled in (audience default "obecné publikum", angle default "(nevyspecifikováno)", facts
   as `n. claim [zdroj: …] (pozn.: note)` or "(žádná fakta)"). Max
   `Articles:WriteMaxTokens` output tokens. Expected reply:
   `{"article_title", "article_html": "<article>…</article>", "sources_used": [{title, url}]}`.
6. **Persist** — `Articles.Title`, `HtmlContent`, `Status = Generated`, `GeneratedAt`; one
   `ArticleSources` row per `sources_used` entry.

Each step is wrapped by `PipelineStepRecorder`: a row in `ArticleGenerationSteps` is inserted as
**Running** before the step (with `InputJson`), then updated to **Succeeded** (with
`OutputJson` incl. the raw model reply, `DurationMs`) or **Failed** (`ErrorMessage` ≤ 2000).
Status is stored as a string.

Every model call goes through `ChatRetry.RetryOnceAsync` (one retry after 1 s on
`HttpRequestException` / `IOException` / `TimeoutException`) and the shared Anthropic
`IChatClient` (HTTP timeout `Anthropic:HttpTimeoutSeconds`, 180 s in repo).

## Logic & formulas
- **Language**: all prompts are Czech and require Czech output; HTML must be email-safe
  (no `<html>/<body>`).
- **JSON tolerance**: replies are parsed after stripping ```` ``` ```` fences. Steps 1, 3, 4
  fall back silently on bad JSON (see quirks). Step 5 first tries strict JSON; if that fails it
  "rescues" `article_title` and `article_html` by regex (also from a truncated reply); if no
  HTML can be found the whole raw reply is HTML-encoded into one `<p>`. Title falls back to
  the topic.
- **Source rows** (`ArticleSources`) come only from the writer's `sources_used`, matched back by
  **title** (case-insensitive):
  - `Type` = Web (0) if the source has a URL, otherwise KnowledgeBase (1). `StyleGuide` (2) is
    never written.
  - `KnowledgeBaseChunkId` and `Confidence` = chunk id and **KB similarity score** of the first
    snippet with the same title (web snippets have no score, so web sources have no confidence).
  - `Excerpt` = first ≤200 chars of the claim of the first fact whose `source_title` matches —
    an extracted fact, not a quote from the source. `ValidationNote` = that fact's note.
  - When the rescue path was used, no sources are stored.
- **Who can do what** (`Feature.Marketing_Article`): read (`marketing.article.read`) to list and
  view articles and traces; write (`marketing.article.write`) to generate and to open the
  feedback list. Only the requester can rate.

### Feedback
`POST /api/Articles/{id}/feedback` with `PrecisionScore` and `StyleScore` (1–5) and optional
`Comment` (≤1000). Accepted only if the caller is the article's `RequestedBy` (ordinal match),
status is Generated and no score exists yet — **once per article**, not editable. The list
`GET /api/Articles/feedback/list` (filters has-feedback / requested-by, sort CreatedAt |
PrecisionScore | StyleScore) shows averages over all articles rounded to 1 decimal and resolves
requester names through the shared user-directory lookup (`IUserDisplayNameResolver`, cached). This is the only quality signal; nothing feeds it back into the
prompts.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Articles:DefaultModel` | `claude-sonnet-4-6` | Writer model |
| `Articles:WriteMaxTokens` | 4096 (appsettings; class default 8192) | Writer output cap |
| `Articles:QueryPlannerModel` | `claude-haiku-4-5-20251001` | Query planner |
| `Articles:AggregateFactsModel` | `claude-sonnet-4-6` | Fact aggregation |
| `Articles:AggregateMaxTokens` | 1024 | Fact aggregation output cap |
| `Articles:ValidateFactsModel` | `claude-haiku-4-5-20251001` | Fact check (no own token cap) |
| `Articles:WebSearchTopK` | 5 | Web hits per query |
| `Articles:KnowledgeBaseTopK` | 8 | KB chunks per query (before the 0.55 threshold) |
| `Articles:*SystemPrompt`, `Articles:WriteArticleUserPromptTemplate` | in `ArticleOptions.cs` | Prompts; overridable from config |
| `WebSearch:Provider` | `Mock` | `SerpApi` = real Google results; anything else = fake hits |
| `WebSearch:ApiKey` | empty (secret) | SerpApi key; missing key with provider SerpApi fails every web query |
| `WebSearch:Endpoint` | `https://serpapi.com/search.json` | SerpApi endpoint |
| `WebSearch:TimeoutSeconds` | 15 | Per-request HTTP timeout (`SerpApi` client) |
| `KnowledgeBase:ChatMaxTokens` | 1024 | Default output cap used by the fact check (it sets none) |
| `KnowledgeBase:MinSimilarityScore` | 0.55 | KB chunk relevance threshold |
| `Anthropic:HttpTimeoutSeconds` | 180 | Per Claude call |
| `Anthropic:ApiKey`, `OpenAI:ApiKey` | secrets | Claude / embeddings |

No feature flag. `ArticleOptions` is validated on start (model names required, numeric ranges).

## Runtime facts
None.

## Known quirks
- **Web search is fake unless `WebSearch:Provider` is set to `SerpApi`.** The repo default is
  `Mock`, and no `appsettings.{Env}.json` overrides it; `MockWebSearchClient` returns two
  `https://example.com/…` hits per query with placeholder text, which then flow into the facts
  and the source list. Whether production sets `WebSearch:Provider` / `WebSearch:ApiKey` via
  Key Vault cannot be determined from the repo — check an article's trace (GatherContext output)
  for example.com URLs.
- **KB can crowd out the web.** Snippets are ordered KB first and the aggregator takes only the
  first 50. With 8 queries × up to 8 KB chunks (64) above the 0.55 threshold, no web snippet
  reaches the model at all. KB chunks are also not de-duplicated, so the same chunk found by
  several queries is sent several times.
- **A failed fact aggregation silently produces an article without facts.** If the Sonnet reply
  is not valid JSON (e.g. cut off at 1024 tokens), the fallback has no facts — its summary is
  computed but never used — and the writer is told "(žádná fakta)". The article still ends
  Generated; only the trace shows it.
- **The fact check does not filter anything.** The `reliable` flag is ignored; unreliable facts
  are passed to the writer with a note. Notes are matched by position, so a reply that skips
  or reorders facts attaches notes to the wrong claims. Any failure is swallowed. It uses the
  shared `KnowledgeBase:ChatMaxTokens` (1024) cap, so a long fact list can be truncated → no
  notes.
- **Long articles may be cut off.** The writer must return the whole HTML inside JSON, capped
  at `WriteMaxTokens` = 4096 in appsettings (the class default is 8192). A `long (2000w)`
  Czech article plus markup can exceed that; the rescue regex then saves the truncated HTML and
  the article is still marked Generated, with no sources. (Read from code, not observed.)
- **Style guide must be a plain-text file.** It is downloaded as raw content and read as text,
  so a `.docx`/`.pdf` becomes binary noise in the prompt. A wrong drive id / path is only
  logged — the article is generated without the guide and the UI does not say so (trace shows
  `styleGuideLength: null`). The guide text is put in both the system prompt and the user
  prompt.
- **Source list is approximate.** Matching is by title text; KB "confidence" is a vector
  similarity score, not a fact confidence; the excerpt is the model's claim, not source text.
- **Stuck articles have no reaper.** If the worker dies mid-run (deploy/restart), the row can
  stay Queued/Researching/Writing indefinitely and the UI keeps polling. The job has no retry.
- **A failed final save can leave the article in Writing.** Source titles are limited to 500
  chars and URLs to 2000 (`ArticleSources`). If the writer returns a longer one, `SaveChanges`
  throws; the catch block marks the article Failed but saves through the same DbContext, which
  still holds the invalid source rows, so that save fails too and the row stays Writing while
  Hangfire records the job as Failed. (Read from code, not observed; see the agent-memory
  gotcha "shared DbContext poison".)
- **`Audience` and `Angle` have no request length limit** but the columns are 500 chars; a
  longer value fails the insert before anything is queued.
- **One article blocks the single Hangfire worker for minutes.** Production runs
  `Hangfire:WorkerCount = 1` (`HangfireOptions` default), so while an article is generating,
  other enqueued jobs (e.g. picking-list prints) wait. On 2026-09-29 slow jobs on that one
  worker delayed prints by 12–15 min (agent memory).
- **Every query costs several external calls**: per query one KB query-expansion Claude call +
  one OpenAI embedding + up to 4 SerpApi attempts (3 retries on 429/503/network errors, 2 s
  exponential back-off). The SerpApi key travels in the URL — do not add URL logging to the
  `SerpApi` HttpClient.
- **Unused bits**: the `{language_note}` placeholder is replaced but the default template uses
  `{tone_note_line}`; `SourceType.StyleGuide` is never stored; `ErrorMessage` column is 2000
  chars but the domain truncates to 500.
- **No retention.** Articles, raw prompts' inputs and model outputs in
  `ArticleGenerationSteps.InputJson/OutputJson` are kept forever; there is no delete endpoint.
- `docs/features/article-generation.md` is the original spec (2026-05-04, status Draft); it
  predates the current options and quirks above.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/GenerateArticle/GenerateArticleHandler.cs` — creates the row, enqueues the job
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/GenerateArticleJob.cs` — status transitions, source persistence, failure handling
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PlanQueriesStep.cs` — query planning + fallback
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/GatherContextStep.cs` — KB / web / style guide in parallel, dedup, error swallowing
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/AggregateFactsStep.cs` — 50-snippet cap, fact JSON
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/ValidateFactsStep.cs` — positional notes
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/WriteArticleStep.cs` — prompt assembly, rescue parsing, source mapping
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/Generate/Pipeline/PipelineStepRecorder.cs` — trace rows
- `backend/src/Anela.Heblo.Application/Features/Article/ArticleOptions.cs` — models, caps, prompts
- `backend/src/Adapters/Anela.Heblo.Adapters.WebSearch/SerpApiWebSearchClient.cs` — SerpApi call, retry, parsing
- `backend/src/Adapters/Anela.Heblo.Adapters.WebSearch/WebSearchAdapterServiceCollectionExtensions.cs` — Mock vs SerpApi switch
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleKnowledgeSource.cs` — KB search bridge
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseArticleStyleGuideSource.cs` — style guide via Graph
- `backend/src/Anela.Heblo.Persistence/Features/Article/ArticleConfiguration.cs` — tables, column limits, indexes
- `docs/features/article-generation.md` — original design spec
