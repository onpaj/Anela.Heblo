---
process: module-knowledge-base
kind: module
module: knowledge-base
summary: Anela's AI knowledge base (RAG) — indexes internal documents and customer chat transcripts from SharePoint into a vector store and answers customer-care questions from them; also the fact source for Smartsupp draft replies, AI articles and leaflets.
owns: []
verified_at: "5e993f9e2"
related: [job-knowledge-base-ingestion, flow-knowledge-base-ask, flow-article-generation]
---

# Knowledge base (Poradenství)

## Purpose
Gives customer-care staff a searchable memory of what Anela knows and has already told
customers. Internal documents (product information, care guides) and exported customer chats are
indexed into a vector store; a question in plain Czech returns an AI-written answer in the voice
of an Anela skin-care advisor, with the sources it used and links to the products it recommends.
Other Heblo features use the same store as their fact source, so what is in the knowledge base
also shapes Smartsupp reply drafts, AI-written articles and leaflets.

## Users & screens
- **`/knowledge-base`** — sidebar "Poradenství (KB)", permission `customer.knowledge_base.read`.
  Tabs:
  - **Hledat** — ask a question; answer with sources (chunk detail modal: filename, summary,
    full text, SharePoint link) and a 1–5 rating form (precision, style, comment).
  - **Dokumenty** — paged list of indexed documents (filter by name, status
    `processing`/`indexed`/`failed`, content type; sort by name, status, created, indexed);
    delete needs write.
  - **Nahrát soubor** (write only) — upload `.pdf .docx .txt .md`, choose "Znalostní báze" or
    "Konverzace" per file.
- **`/knowledge-base/feedback`** — browser of all rated/unrated answers with averages
  (API requires write).
- **MCP tools**: `AskKnowledgeBase` (answer + sources), `SearchKnowledgeBase` (raw chunks).
- Feeding the store: staff put files into the SharePoint folders
  `/KnowledgeBase/Documents/Inbox` and `/KnowledgeBase/Conversations/Inbox`.

## Processes
- `job-knowledge-base-ingestion` — Hangfire `knowledge-base-ingestion`, `*/15 * * * *`: SharePoint
  inbox → text → LLM summary → OpenAI embedding → chunks; file moved to `Archived`. Also covers
  manual upload (same pipeline).
- `flow-knowledge-base-ask` — on demand (page, MCP): query expansion → vector search → Claude
  answer with product links → `RagInteractionLogs` row → user feedback.

Plain CRUD (no process doc): document list, content-type filter values
(`GET documents/content-types`), chunk detail (`GET chunks/{id}`), document delete
(`DELETE documents/{id}`, cascades to chunks; the SharePoint file is not touched).

## Data owned
- `public."KnowledgeBaseDocuments"` — one indexed file: filename, `SourcePath` (SharePoint
  `webUrl` of the archived file, or `upload/{guid}/{name}`), content type, SHA-256
  `ContentHash` (unique), status, `DocumentType` (0 KnowledgeBase, 1 Conversation), created /
  indexed time (UTC, `timestamp without time zone`), `DriveId` + `GraphItemId`.
- `public."KnowledgeBaseChunks"` — one searchable piece of a document: `ChunkIndex`, `Content`
  (original text; for conversations the whole transcript), `Summary` (LLM keyword block that was
  embedded), `DocumentType`, `Embedding` `vector(1536)` (HNSW cosine index
  `idx_kb_chunks_embedding`, managed by raw-SQL migrations, ignored by EF).
- `public."RagInteractionLogs"` — one AI answer for the eval dataset, shared with Smartsupp
  (`Feature` 0 = KnowledgeBase, 1 = SmartsuppDraftReply): question, expanded query, retrieved
  chunks (jsonb), rendered prompt, answer, duration, user, scores and comment.
- In-memory: product lookup cache (`ProductEnrichmentCache`, 60 min), Graph folder ids
  (`graph:folder-id:*`, 60 min).

## External systems
- **Microsoft Graph / SharePoint** (read + move): list inbox children, download content, `PATCH`
  move to archive, create missing archive folders. App-only token. Replaced by
  `MockOneDriveService` (empty inbox) when no mapping has a `DriveId` or mock auth is on.
  The same `IOneDriveService` also serves the Article module's style-guide download
  (`IArticleStyleGuideSource`).
- **OpenAI** embeddings `text-embedding-3-large` at 1536 dimensions (ingestion and every query).
- **Anthropic Claude**: `claude-sonnet-4-6` for chunk summaries, topic split and answers;
  `claude-haiku-4-5-20251001` for query expansion.

## Dependencies
- Reads **Catalog** (`IProductCatalogQueryService.GetActiveProductsAsync`) for product codes,
  names and e-shop URLs used in answers.
- Read by (contracts implemented here, registered in `KnowledgeBaseModule`):
  - **Smartsupp** draft reply — `ISmartsuppKnowledgeSource` → this module's search (expansion +
    0.55 threshold).
  - **Article** generator — `IArticleKnowledgeSource` (search, `Articles:KnowledgeBaseTopK` 8)
    and `IArticleStyleGuideSource` (SharePoint text file).
  - **Leaflet** generator — `ILeafletKnowledgeSource` → raw vector search with Leaflet's own
    embedding and threshold, no KB query expansion.
- Shared code in `Application/Shared/Rag` (chunker, extractors, OneDrive, query expander,
  interaction recorder) is also used by the Leaflet and Smartsupp modules.

## Known quirks
- A failed or unsupported file is silently archived on the next run and its `failed` row is
  never retried (details in `job-knowledge-base-ingestion`).
- Conversation chunks all repeat the full transcript, so one chat can fill several answer
  sources (details in `flow-knowledge-base-ask`).
- `KnowledgeBase:ChatModel` / `ChatMaxTokens` set the default Claude model for the whole app, not
  only this module.
- Dead config keys: `KnowledgeBase:MaxRetrievedChunks`, `KnowledgeBase:IngestionCronExpression`.
- `docs/features/knowledge-base-rag.md` describes an older design (single inbox,
  `text-embedding-3-small`, `dbo` schema); trust the code and these docs.
- `KnowledgeBaseQuestionLogs` was migrated into `RagInteractionLogs` and dropped (migration
  `20260708105245_AddRagInteractionLogs`).

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs` — DI, cross-module contracts, keyed chat client
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs` — all prompts and defaults
- `backend/src/Anela.Heblo.Application/Shared/Rag/SharedRagModule.cs` — shared RAG services, Graph vs mock
- `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs` — REST endpoints and permissions
- `backend/src/Anela.Heblo.API/MCP/Tools/KnowledgeBaseTools.cs` — MCP tools
- `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs` — pgvector SQL
- `frontend/src/pages/KnowledgeBasePage.tsx` — page tabs
