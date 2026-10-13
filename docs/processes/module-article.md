---
process: module-article
kind: module
module: article
summary: AI article writer for marketing (Generátor článků) — turns a short brief into a researched Czech HTML article draft using the internal knowledge base, Google search and an optional style guide, and collects the requester's quality rating.
owns: []
verified_at: "5e993f9e2"
related:
  - flow-article-generation
---

# Article (AI article generator)

## Purpose
Marketing at Anela writes blog and newsletter articles about skin care and cosmetics. This
module gives them a first draft in a few minutes: the marketer describes the topic, audience,
angle, scope and length; Heblo researches it in Anela's own knowledge base (internal product
and ingredient documents) and on the web, extracts and checks facts with Claude, and writes a
Czech article in email-ready HTML with a list of the sources it used. The article is a
**draft** — Heblo does not publish it anywhere; the marketer copies it out and edits it.

The requester then rates the draft (factual precision and style, 1–5) so the team can see
whether the generator is getting better or worse.

## Users & screens
Permission `Feature.Marketing_Article` ("Články"): read = `marketing.article.read`, write =
`marketing.article.write`.

| Route | Who | What |
|---|---|---|
| `/articles` (sidebar Marketing → "Generátor článků") | read | Form for a new brief (needs write to submit), list of articles with status, detail with the HTML, source list, debug trace of every pipeline step, and the feedback form for the requester |
| `/marketing/feedback` (sidebar "Feedback", tab Articles) | write | All articles' ratings with averages, filter by has-feedback / requester, sort by date or score; the same page also shows KB, leaflet and Smartsupp feedback |

API: `ArticlesController` (`/api/Articles`). No MCP tool and no dashboard tile.

## Processes
- `flow-article-generation` — brief → Hangfire job `GenerateArticleJob` (one-shot, no retry)
  running PlanQueries → GatherContext → AggregateFacts → ValidateFacts → WriteArticle;
  trigger: user submits the form (`POST /api/Articles/generate`).

Plain reads / writes without a process doc:
- List articles (`GET /api/Articles`, filter by status, page size 1–100, newest first), detail
  (`GET /{id}`), step trace (`GET /{id}/trace`).
- Rate an article (`POST /{id}/feedback`) — requester only, once, Generated articles only;
  feedback list (`GET /feedback/list`). Rules are in `flow-article-generation` → *Feedback*.
- Admin one-off: `POST /api/Articles/admin/backfill-requested-by` (`Admin_Administration`
  write) — older articles stored the requester's **display name** in `RequestedBy`; this maps
  each name to an Entra object id using the members of a given Entra group (Microsoft Graph,
  read-only). Values that are already a GUID or contain `@` are skipped; names with no match or
  with several group members of the same name are reported, not changed. `DryRun` defaults to
  true. Without the backfill those requesters cannot rate their old articles.

No recurring jobs.

## Data owned
All in the main Heblo database (`public` schema), EF configurations in
`Anela.Heblo.Persistence/Features/Article`:
- `Articles` — one row per brief: the brief fields, `Status` (int: Queued 0, Researching 1,
  Writing 2, Generated 3, Failed 4), `Title`, `HtmlContent`, `ErrorMessage`, `RequestedBy`
  (Entra object id), `PrecisionScore`, `StyleScore`, `FeedbackComment`, `CreatedAt`,
  `GeneratedAt`. Indexes on (Status, CreatedAt) and on PrecisionScore (filtered, not null).
- `ArticleGenerationSteps` — one row per pipeline step run: name, sequence 1–5, status
  (string Running / Succeeded / Failed), timings, model, `InputJson`, `OutputJson` (raw model
  reply included), error. Cascade-deleted with the article.
- `ArticleSources` — the sources the writer says it used: title, URL, type (Web 0 /
  KnowledgeBase 1), KB chunk id, KB similarity score, excerpt (an extracted claim), validation
  note. Cascade-deleted with the article.

No caches, no blob storage. Nothing is ever deleted (no delete endpoint, no retention job).

## External systems
| System | Direction | What |
|---|---|---|
| Anthropic Claude (`Anthropic:ApiKey`) | out | 4 model calls per article (planner, aggregator, fact check, writer) + the KB query expansion per query |
| OpenAI embeddings (via KnowledgeBase) | out | One embedding per search query |
| SerpApi (Google search) | out, read-only | `GET serpapi.com/search.json`, only when `WebSearch:Provider = SerpApi` (repo default is `Mock`) |
| Microsoft Graph / OneDrive–SharePoint | read | Style guide file by drive id + path; Entra group members for the admin backfill |

## Dependencies
- Reads from **KnowledgeBase** (vector search over its document chunks, through
  `IArticleKnowledgeSource`; style-guide download through `IArticleStyleGuideSource`, both
  implemented in the KnowledgeBase module) and **UserManagement** (`IArticleUserResolver` for
  the backfill). Requester display names on the feedback list come from the shared `IUserDisplayNameResolver`.
- Uses the shared `IWebSearchClient` (`Anela.Heblo.Adapters.WebSearch`) — the Article module is
  its only consumer.
- Nothing else reads the article tables; the KnowledgeBase feedback page only shares the UI.
  The article text is not re-ingested into the knowledge base.

## Known quirks
- Web search is a **mock** (two example.com hits per query) unless `WebSearch:Provider` is set
  to `SerpApi` outside the repo; the repo cannot tell whether production does.
- Generation runs on the single Hangfire worker (`WorkerCount = 1`) and can take minutes, so it
  delays other background jobs; an interrupted run leaves the article stuck in progress (no
  retry, no reaper).
- Several pipeline failures are silent — no facts, no style guide, truncated long article —
  and still end as Generated. Detail in `flow-article-generation` → *Known quirks*.
- Feedback can be given only by the requester and only once; articles whose `RequestedBy` is
  null or an un-migrated display name cannot be rated.
- `docs/features/article-generation.md` is the original design spec (Draft, 2026-05-04), not
  updated since.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Article/ArticleModule.cs` — DI registration, validators
- `backend/src/Anela.Heblo.API/Controllers/ArticlesController.cs` — endpoints and permissions
- `backend/src/Anela.Heblo.Application/Features/Article/UseCases/` — one folder per use case
- `backend/src/Anela.Heblo.Application/Features/Article/Admin/BackfillArticleRequestedByHandler.cs` — RequestedBy backfill
- `backend/src/Anela.Heblo.Domain/Features/Article/Article.cs` — entity and status transitions
- `backend/src/Anela.Heblo.Persistence/Features/Article/ArticleRepository.cs` — queries, feedback stats
- `frontend/src/pages/ArticlesPage.tsx`, `frontend/src/features/articles/` — UI
- `frontend/src/pages/MarketingFeedbackPage.tsx` — shared feedback page
