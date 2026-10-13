---
process: flow-leaflet-generation
kind: workflow
module: leaflet
summary: Generates a Czech marketing leaflet in Markdown from a topic, audience and length by retrieving product facts from the Knowledge Base and style examples from the leaflet library, running two Claude calls, and logging each result in LeafletGenerations for 1–5 feedback.
owns:
  - backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/**
  - backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/**
  - backend/src/Anela.Heblo.Application/Features/Leaflet/Contracts/**
  - backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/SubmitLeafletFeedback/**
  - backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletGeneration/**
  - backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletFeedbackList/**
  - backend/src/Anela.Heblo.Domain/Features/Leaflet/LeafletGeneration.cs
  - backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletGenerationRepository.cs
  - backend/src/Anela.Heblo.Domain/Features/Leaflet/LeafletFeedbackStats.cs
  - backend/src/Anela.Heblo.Domain/Features/Leaflet/UpdateFeedbackResult.cs
  - backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletGeneration*.cs
  - backend/src/Anela.Heblo.API/MCP/Tools/LeafletTools.cs
verified_at: "5e993f9e2"
related:
  - job-leaflet-ingestion
---

# Leaflet generation

## Purpose
Marketing types a topic (e.g. "Bisabolol pro citlivou pleť"), picks the audience and length, and
gets a ready-to-edit **Czech product leaflet (leták)** in Markdown. The facts come from Anela's
Knowledge Base (Poradenství); the tone imitates past Anela leaflets from the leaflet library
(`job-leaflet-ingestion`). Nothing is published anywhere — the text is shown on screen with a
copy button; the only side effects are AI calls and one log row per generation.

Where:
- Page **Generátor letáků** `/leaflet-generator`, tab **Generovat** (form + result + rating form).
- MCP tool **`GenerateLeaflet`** (topic, audience `EndConsumer`|`B2B`, length `Short`|`Medium`|`Long`).
- Ratings are reviewed on **`/marketing/feedback`**, tab **Letáky**.

## Trigger
On demand:
- `POST /api/leaflet/generate` — requires `marketing.leaflet.write`.
- MCP `GenerateLeaflet` — requires only `marketing.leaflet.read` (see quirks).

States of a generation: *generated* (row in `LeafletGenerations`, no scores) → *rated*
(`PrecisionScore` + `StyleScore` set, once, by the author). No other state changes.

## Data flow
1. **Query expansion** (`IRagQueryExpander`): if `QueryExpansionEnabled`, one Claude call
   (`QueryExpansionModel` = `claude-haiku-4-5-20251001`) rewrites the topic into a structured Czech
   search description (Produkt / Kontext / Klíčové ingredience / Benefity / Cílová skupina).
   On HTTP error/timeout or empty answer the raw topic is used.
2. **Embedding** of the expanded query: OpenAI `text-embedding-3-large`, 1536 dimensions
   (retried once on transient error).
3. **Fact retrieval**: `ILeafletKnowledgeSource` → `KnowledgeBaseLeafletSourceAdapter` (knowledge-base
   module) → cosine search over `KnowledgeBaseChunks` ⨝ `KnowledgeBaseDocuments`, top `KbTopK` = 8.
4. **Style retrieval**: cosine search over `LeafletChunks` ⨝ `LeafletDocuments`, top
   `LeafletTopK` = 5 (HNSW index `IX_LeafletChunks_Embedding_HNSW`).
5. Both hit lists are filtered to score ≥ `MinSimilarityScore` = 0.55 (score = 1 − cosine distance).
   **Both empty** → error `LeafletEmptyRetrieval` (2504, HTTP 422); UI: "Knowledge Base zatím toto
   téma nepokrývá. Zkuste obecnější formulaci." Nothing is logged.
6. **Stage 1 — facts outline**: Claude `ChatModel` = `claude-sonnet-4-6`, max `ChatMaxTokens` = 2048.
   System prompt `Stage1SystemPrompt` with topic, audience, word target and the KB chunks joined by
   `---` ("(empty)" if none); user message = topic. Output: structured outline (ingredients,
   benefits, use cases, regulatory cautions), told not to invent facts.
7. **Stage 2 — copywriting**: same model/limit. System prompt `Stage2SystemPrompt` with audience,
   word target, `coldStart` flag and the leaflet chunks as style references ("(none)" if none);
   user message = the Stage 1 outline. Output: Czech Markdown leaflet → response `content`.
8. **Logging** (`LeafletGenerationPersistenceBehavior`, only on success): insert into
   `LeafletGenerations` (topic, audience, length, final Markdown, KB/leaflet source counts,
   end-to-end `DurationMs`, `CreatedAt`, `UserId`); its id is returned as `id` so the UI can rate it.
9. **Feedback**: `POST /api/leaflet/feedback` {generationId, precisionScore 1–5, styleScore 1–5,
   comment ≤ 1000 chars} → updates the same row.

## Logic & formulas
- Word target from length: Short = `ShortWordTarget` 200, Medium = `MediumWordTarget` 400,
  Long = `LongWordTarget` 700. It is only a prompt instruction; output length is not checked.
- Audience label in prompts: `EndConsumer` → "Koncový zákazník", `B2B` → "B2B".
- **Cold start**: no leaflet hit above 0.55 → `coldStart = true`, prompt asks for a neutral
  professional register; warning logged "Leaflet cold-start: zero leaflet style references…".
- If KB hits are empty but leaflet hits exist, Stage 1 is told to build a minimal outline from
  "common cosmetic-industry knowledge" — i.e. facts are then not from Anela's KB.
- Every chat/embedding call (except query expansion, which falls back) is retried once after 1 s on
  `HttpRequestException` / `IOException` / `TimeoutException`; then the error propagates
  (UI: "Generování selhalo. Zkuste to prosím znovu.").
- Feedback rules: only the user who generated it (`UserId` match) may rate; anonymous generations
  cannot be rated (`Forbidden`); a second rating → `LeafletFeedbackAlreadySubmitted` (2503, 409);
  unknown id → `LeafletFeedbackNotFound` (2502, 404).
- Feedback overview (`GET /api/leaflet/feedback/list`, write permission): filter by has-feedback /
  user, sort by `CreatedAt`, `PrecisionScore` or `StyleScore`; stats = total generations, total
  rated, average precision, average style.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Leaflet:KbTopK` | 8 | Max KB chunks for facts |
| `Leaflet:LeafletTopK` | 5 | Max leaflet chunks for style |
| `Leaflet:MinSimilarityScore` | 0.55 | Cut-off for both lists |
| `Leaflet:ChatModel` / `ChatMaxTokens` | `claude-sonnet-4-6` / 2048 | Stage 1 + 2 |
| `Leaflet:QueryExpansionEnabled` / `QueryExpansionModel` | `true` / `claude-haiku-4-5-20251001` | Topic rewrite before search |
| `Leaflet:QueryExpansionPrompt`, `Stage1SystemPrompt`, `Stage2SystemPrompt` | in `LeafletOptions.cs` | Prompts (overridable by config) |
| `Leaflet:ShortWordTarget` / `MediumWordTarget` / `LongWordTarget` | 200 / 400 / 700 | Word targets |
| `Leaflet:EmbeddingModel` / `EmbeddingDimensions` | `text-embedding-3-large` / 1536 | Query embedding |

## Runtime facts
None.

## Known quirks
- **Readers see the Generovat tab but cannot use it.** The page needs only
  `marketing.leaflet.read`, the generate endpoint needs `marketing.leaflet.write`; a reader gets
  the generic "Generování selhalo" banner (403), not a permission message.
- **MCP bypasses that rule**: `GenerateLeaflet` checks only `marketing.leaflet.read`.
- **MCP skips the 200-character topic limit.** `[MaxLength(200)]` is enforced only by MVC model
  validation; via MCP a longer topic is generated, then the log insert fails on
  `LeafletGenerations.Topic varchar(200)` — the error is swallowed (logged "Failed to log leaflet
  generation"), the leaflet is returned with `id = null`, so it is neither logged nor rateable.
- **Facts search covers the whole Knowledge Base** with no `DocumentType` filter — every indexed
  KB document type can become "facts" (e.g. conversation documents), not only curated product info.
- **Fallback to generic knowledge**: with an empty KB result Stage 1 is allowed to use general
  cosmetic-industry knowledge, so a leaflet can contain claims not backed by Anela's KB; the UI
  does not flag this (`kbSourceCount = 0` is only in the response/log).
- **Leaflet search does not filter by document status**; chunks of a document stuck in
  `processing` (status update failed after insert) are still used.
- A logging failure never fails the request — the generation then has no id and no rating form.
- `DurationMs` includes query expansion, embeddings, both searches and both LLM calls.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs` — retrieval + two-stage generation
- `backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletOptions.cs` — prompts and defaults
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationPersistenceBehavior.cs` — generation log
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/SubmitLeafletFeedback/SubmitLeafletFeedbackHandler.cs` — rating rules
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletGenerationRepository.cs` — feedback list + stats
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/KnowledgeBaseLeafletSourceAdapter.cs` — KB search adapter (owned by the knowledge-base module)
- `backend/src/Anela.Heblo.Application/Shared/Rag/RagQueryExpander.cs` — query expansion
- `backend/src/Anela.Heblo.API/MCP/Tools/LeafletTools.cs` — MCP tool
- `frontend/src/features/leaflet-generator/LeafletGenerateTab.tsx` — UI and error banners
