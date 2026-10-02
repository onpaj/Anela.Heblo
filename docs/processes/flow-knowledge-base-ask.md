---
process: flow-knowledge-base-ask
kind: workflow
module: knowledge-base
summary: Answers a staff question from the knowledge base — expands the query with Claude, finds the closest chunks by vector similarity, lets Claude write a Czech answer with product links resolved from the catalog, logs the interaction to RagInteractionLogs and collects 1–5 star feedback.
owns:
  - backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/**
  - backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SearchDocuments/**
  - backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SubmitFeedback/**
  - backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetFeedbackList/**
  - backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/**
  - backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseConstants.cs
  - backend/src/Anela.Heblo.Application/Shared/Rag/RagQueryExpander.cs
  - backend/src/Anela.Heblo.Application/Shared/Rag/RagInteractionRecorder.cs
  - backend/src/Anela.Heblo.Application/Shared/Rag/RagInteractionLogFactory.cs
  - backend/src/Anela.Heblo.Application/Shared/Rag/RagFeedback*.cs
  - backend/src/Anela.Heblo.Domain/Features/Rag/**
  - backend/src/Anela.Heblo.Persistence/Rag/**
  - backend/src/Anela.Heblo.API/MCP/Tools/KnowledgeBaseTools.cs
verified_at: "5e993f9e2"
related: [job-knowledge-base-ingestion]
---

# Knowledge base question & answer

## Purpose
Lets customer-care staff ask "what do we usually recommend for…?" and get an answer grounded in
Anela's own documents and past customer conversations, instead of searching them by hand. The
answer is in Czech, written as an Anela skin-care advisor, cites its sources, and turns every
mentioned Anela product into a link to the e-shop. Staff rate each answer (precision and style,
1–5) so the prompts can be tuned; a reviewer browses those ratings on `/knowledge-base/feedback`.

Where it is used:
- Page `/knowledge-base` ("Poradenství (KB)"), tab **Hledat** — it calls *Ask*, not plain search.
  Each answer shows its sources (click → chunk detail) and a feedback form.
- MCP tools **`AskKnowledgeBase`** (answer) and **`SearchKnowledgeBase`** (raw chunks), both
  gated by `Customer_KnowledgeBase` read.
- The *search* half (steps 1–4) is reused through module contracts by the Smartsupp draft reply
  and the AI article writer (`ISmartsuppKnowledgeSource`, `IArticleKnowledgeSource`).

## Trigger
On demand, no schedule:
- `POST /api/KnowledgeBase/ask` `{question, topK}` and `POST /api/KnowledgeBase/search`
  `{query, topK}` — `Customer_KnowledgeBase` read. `question`/`query` 1–2000 chars, `topK` 1–20
  (default 5; the page sends no `topK`).
- `POST /api/KnowledgeBase/feedback` `{logId, precisionScore, styleScore, comment}` — read.
- `GET /api/KnowledgeBase/feedback/list` — **write** permission.

## Data flow
1. **Query expansion** (`RagQueryExpander`): Claude `QueryExpansionModel`
   (`claude-haiku-4-5-20251001`) rewrites the question into the same Czech key/value shape the
   chunk summaries use (Problém / Kontext / Doporučení / Ingredience), prompt
   `KnowledgeBase:QueryExpansionPrompt`. Network/timeout errors or an empty reply → raw question.
2. **Embed** the expanded text with OpenAI `text-embedding-3-large`, 1536 dims. A transient
   error returns an empty result (→ "nothing found" answer), not an error.
3. **Vector search** (`KnowledgeBaseRepository.SearchSimilarAsync`, raw SQL, 120 s timeout):
   top `topK` rows of `public."KnowledgeBaseChunks"` joined to `KnowledgeBaseDocuments`, ordered
   by cosine distance `"Embedding" <=> @embedding`; `Score = 1 − distance`. All chunks are
   searched — no filter by document type or status.
4. **Threshold**: drop chunks with `Score < MinSimilarityScore` (0.55). The kept chunks, the
   expanded query and `topK` are stored in the scoped `RagInteractionRecorder`.
   `/search` and MCP `SearchKnowledgeBase` stop here and return the chunks plus
   `BelowThresholdCount`.
5. **No chunk left** → fixed answer "V dostupných dokumentech jsem nenašla relevantní informaci
   k vaší otázce.", no LLM call, no sources.
6. **Prompt**: chunk `Content`s joined with `---`, Markdown links stripped to their text
   (`ContextSanitizer`, so old `[name](url)` links in chats don't teach the model stale URLs).
   Product table = every catalog product of type Product or Goods as `CODE | Name`, sorted by
   code, from `ProductEnrichmentCache`. `AskQuestionSystemPrompt` gets `{context}`,
   `{products}`, `{query}` substituted; sent as system message + the question as user message.
7. **Answer** by the KB-keyed chat client (`knowledge-base-answer`) = default Anthropic client
   (`KnowledgeBase:ChatModel` `claude-sonnet-4-6`, max 1024 tokens) wrapped in
   `PostAnswerEnrichmentMiddleware`.
8. **Product links** (middleware): `[Name](CODE)` → `[Name](product URL)`, or just `Name` when the
   product has no URL or the code is unknown. A bare `(CODE)` of a known product →
   `[catalog name](URL)` or the catalog name; unknown bare tokens are left alone.
9. **Log** (`QuestionLoggingBehavior`, KB-only MediatR behavior): one `public."RagInteractionLogs"`
   row with `Feature = 0` (KnowledgeBase): user id, question, expanded query, `TopK`,
   `SourceCount`, `RetrievedChunksJson` (jsonb, full chunk text + scores), rendered system
   prompt, answer, `DurationMs`. Its id is returned as `id`; the page shows the feedback form
   only when an id came back. Log write failures are logged and swallowed.
10. **Response**: `answer` + `sources[]` (chunk id, document id, filename, first 200 characters,
    score).
11. **Feedback**: `SubmitFeedbackHandler` sets `PrecisionScore`, `StyleScore` (each 1–5) and
    `FeedbackComment` on the log row.

## Logic & formulas
- Similarity: cosine similarity on the HNSW index `idx_kb_chunks_embedding`
  (`vector_cosine_ops`); chunks were embedded from their **summary**, the answer context uses
  their **content** (see `job-knowledge-base-ingestion`).
- `MinSimilarityScore` 0.55 is applied after `LIMIT topK`, so the answer uses at most `topK`
  chunks and often fewer.
- Product cache: in-memory singleton, reloaded at most once per
  `ProductEnrichmentCacheTtlMinutes` (60) on first use after expiry, from
  `IProductCatalogQueryService.GetActiveProductsAsync` (catalog `ProductType.Product` or
  `Goods`; `Url` = the catalog's e-shop URL).
- Feedback rules: the log must exist and be `Feature = KnowledgeBase` (else 404
  `KnowledgeBaseFeedbackLogNotFound`), belong to the current user (else `Forbidden`), and have no
  score yet (else 409 `KnowledgeBaseFeedbackAlreadySubmitted`). One rating per answer, by its
  asker only.
- Feedback list: KnowledgeBase rows only, filters `hasFeedback`, `userId`; sort `CreatedAt`
  (default, desc), `PrecisionScore`, `StyleScore`; page size 10/20/50 (else 20); stats = total
  questions, total rated, average precision and style.
- Errors: Claude network/timeout/cancel/disposed → `Success = false`,
  `KnowledgeBaseAiUnavailable` (503); such answers are not logged.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `KnowledgeBase:QueryExpansionEnabled` | true | Rewrite the question before embedding |
| `KnowledgeBase:QueryExpansionModel` | `claude-haiku-4-5-20251001` | Model for the rewrite |
| `KnowledgeBase:QueryExpansionPrompt` | Czech prompt in `KnowledgeBaseOptions` | Rewrite instructions |
| `KnowledgeBase:MinSimilarityScore` | 0.55 | Chunk cut-off |
| `KnowledgeBase:AskQuestionSystemPrompt` | Czech advisor prompt in `KnowledgeBaseOptions` | Answer prompt with `{context}`, `{products}`, `{query}` |
| `KnowledgeBase:ChatModel` / `ChatMaxTokens` | `claude-sonnet-4-6` / 1024 | Answer model (app-wide default client) |
| `KnowledgeBase:EmbeddingModel` / `EmbeddingDimensions` | `text-embedding-3-large` / 1536 | Query embedding; must match ingestion |
| `KnowledgeBase:ProductEnrichmentCacheTtlMinutes` | 60 | Product link cache lifetime |
| `KnowledgeBase:MaxRetrievedChunks` | 5 | **Not read anywhere**; `topK` comes from the request |
| `Anthropic:HttpTimeoutSeconds` | 180 | HTTP timeout of the Claude client |

## Runtime facts
None.

## Known quirks
- **Answers can repeat the same conversation several times.** Every topic chunk of a
  conversation carries the whole transcript as content; when several topics of one conversation
  match, the full transcript lands in the prompt once per topic and is listed as several sources.
- **Failed and half-indexed documents are searched too** — the search has no `Status` filter
  (see the failed-file quirk in `job-knowledge-base-ingestion`).
- **Gift sets never get links**: the product table and link resolver use only catalog types
  Product and Goods, so a `BAL…`/`SET…` code the model emits is shown as plain name.
- **Product links can be up to 60 min stale** after a catalog URL change (singleton cache).
- **The "no relevant information" answer is logged and rateable** like a real answer; a failed
  Claude call is not logged at all, so the feedback stats only cover answers that came back.
- **MCP calls bypass the request validation**: `topK` and question length limits are
  DataAnnotations checked by MVC only, so MCP `AskKnowledgeBase`/`SearchKnowledgeBase` accept any
  `topK`.
- **`/search` and Smartsupp/Article searches are not logged** by this flow; Smartsupp writes its
  own `RagInteractionLogs` rows with `Feature = 1` (SmartsuppDraftReply) in the same table.
- **The legacy table `KnowledgeBaseQuestionLogs` no longer exists**: migration
  `20260708105245_AddRagInteractionLogs` copied its rows (with feedback) into
  `RagInteractionLogs` — without chunks, expanded query or prompt — and dropped it.
- **The feedback page route is not permission-guarded in the frontend**; the API behind it
  requires write, so read-only users see an empty/error page.
- **Two unused components** `KnowledgeBaseAskTab.tsx` and `KnowledgeBaseSearchTab.tsx` exist in
  `frontend/src/components/knowledge-base/` that nothing imports; the page uses
  `KnowledgeBaseSearchAskTab`.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionHandler.cs` — prompt assembly, answer, error handling
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SearchDocuments/SearchDocumentsHandler.cs` — expansion, embedding, threshold
- `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs` — `SearchSimilarAsync` SQL
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/PostAnswerEnrichmentMiddleware.cs` — product link rewriting
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/ProductEnrichmentCache.cs` — product table source + TTL
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Pipeline/QuestionLoggingBehavior.cs` — RagInteractionLogs write
- `backend/src/Anela.Heblo.Application/Shared/Rag/RagInteractionRecorder.cs` — scoped capture shared with Smartsupp
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SubmitFeedback/SubmitFeedbackHandler.cs` — rating rules
- `backend/src/Anela.Heblo.Persistence/Rag/RagInteractionLogRepository.cs` — feedback list + stats
- `backend/src/Anela.Heblo.API/MCP/Tools/KnowledgeBaseTools.cs` — MCP tools
- `frontend/src/components/knowledge-base/KnowledgeBaseSearchAskTab.tsx` — the page's ask UI
