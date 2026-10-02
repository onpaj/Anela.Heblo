---
process: job-leaflet-ingestion
kind: job
module: leaflet
summary: Turns example Anela leaflets (PDF/Word/text) dropped into the SharePoint inbox folder or uploaded on the Leaflet Generator page into embedded text chunks in LeafletDocuments/LeafletChunks, the style library the leaflet generator imitates.
owns:
  - backend/src/Anela.Heblo.Application/Features/Leaflet/Infrastructure/Jobs/**
  - backend/src/Anela.Heblo.Application/Features/Leaflet/Services/**
  - backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/IndexLeaflet/**
  - backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/**
  - backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/DeleteLeafletDocument/**
  - backend/src/Anela.Heblo.Domain/Features/Leaflet/LeafletDocument*.cs
  - backend/src/Anela.Heblo.Domain/Features/Leaflet/LeafletChunk.cs
  - backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletDocumentRepository.cs
  - backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocument*.cs
  - backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletChunkConfiguration.cs
verified_at: "5e993f9e2"
related:
  - flow-leaflet-generation
---

# Leaflet ingestion (style library)

## Purpose
The leaflet generator writes new Czech product leaflets "in Anela's voice". It learns that voice
from **past leaflets** that marketing collects. This process takes those example leaflets and
stores them as searchable text pieces (chunks) with an AI embedding each, so the generator can
find the passages most similar to a new topic and use them as tone/style references.

It does **not** feed product facts — facts come from the Knowledge Base (Poradenství), which has
its own ingestion in the knowledge-base module. A leaflet here only influences style.

Staff see the result on page **Generátor letáků** (`/leaflet-generator`), tab **Dokumenty**
(list, status, chunk preview, delete) and add files either via SharePoint or tab
**Nahrát soubor** (upload).

## Trigger
Two entry points, both ending in the same MediatR command `IndexLeafletRequest`:

| Entry | Who / when | Notes |
|---|---|---|
| Hangfire recurring job **`leaflet-ingestion`** ("Leaflet Ingestion", category Content) | Cron `Leaflet:IngestionCronExpression`, repo default `*/15 * * * *` (every 15 min, Europe/Prague), `DefaultIsEnabled = true` | Can be switched off in Recurring Jobs; disabled → logs "Job leaflet-ingestion is disabled. Skipping." |
| Upload `POST /api/leaflet/documents/upload` | User with `marketing.leaflet.write`, tab *Nahrát soubor* (accepts `.pdf`, `.docx`, `.txt`, `.md`) | Synchronous: the request waits for extraction + summaries + embeddings |

Delete (`DELETE /api/leaflet/documents/{id}`, write permission) removes a document and, by
cascade, its chunks — database only; the file in SharePoint is not touched.

## Data flow

### Scheduled job
1. Configuration `Leaflet:OneDriveFolderMappings` — every mapping with `DocumentType = Leaflet`.
   Repo default: one SharePoint drive (`DriveId b!jj_-5-Foh…`), inbox `/AI/Leaflets/Inbox`,
   archive `/AI/Leaflets/Archived`.
2. Microsoft Graph `GET /drives/{driveId}/root:/{InboxPath}:/children` — files directly in the inbox
   (sub-folders are skipped). App-only token, scope `https://graph.microsoft.com/.default`.
3. Per file: `GET /drives/{driveId}/items/{id}/content` → bytes → `IndexLeafletRequest`
   (filename, Graph `webUrl` as `SourcePath`, Graph MIME type, `DriveId`, `GraphItemId`).
4. Indexing (see *Logic*) → rows in `LeafletDocuments` + `LeafletChunks`.
5. Graph `PATCH /drives/{driveId}/items/{id}` moves the file to `ArchivedPath` (folder created on
   demand, `conflictBehavior=replace` — a same-named archived file is overwritten).
6. `LeafletDocuments.SourcePath` is updated to the archived file's `webUrl` (also for duplicates).
   If this update fails the file is already archived; a warning "Manual correction required" is logged.

### Upload
1. Browser multipart file → content type resolved (browser value, or from the extension when the
   browser sends `application/octet-stream`: `.pdf`, `.docx`, `.doc`, `.txt`, `.md`).
2. No extractor for the type → `UnsupportedFileType` error, nothing stored.
3. `IndexLeafletRequest` with `SourcePath = upload/{newGuid}/{filename}`, no Drive/Graph ids.
   The file itself is not stored anywhere — only its extracted text in chunks.

### Indexing (`IndexLeafletHandler` + `LeafletIndexingService`)
1. SHA-256 of the raw bytes (`ContentHash`, lowercase hex).
2. **Same hash already in `LeafletDocuments`** (any status) → duplicate: nothing re-indexed;
   for a legacy row without Graph ids they are back-filled. Response `WasDuplicate = true`.
3. **Same file identity** (job: same `DriveId` + `GraphItemId`; upload: same `SourcePath`, which
   is always new) but different content → old document and its chunks deleted, then re-indexed.
4. Text extraction by content type: PDF (`application/pdf`), Word (`…wordprocessingml.document`,
   `application/msword`), plain text (`text/*`, `application/markdown`). Other type →
   `NotSupportedException` (job counts it as *skipped*, file stays in the inbox).
5. `LeafletDocuments` row inserted and committed immediately with status `processing`.
6. Text split into word windows of `ChunkSize` = 800 words, overlap `ChunkOverlap` = 80
   (step 720 words). Zero chunks → document still marked `indexed` with 0 chunks.
7. Per chunk, if `SummarizationEnabled` (default true): one LLM call with `SummarizationPrompt`
   (Czech: extract Produkt / Kontext / Ingredience / Benefity / Cílová skupina) → `Summary`.
   Disabled → `Summary` = chunk text.
8. One batched embedding call over the chunk **content** (not the summary): OpenAI
   `EmbeddingModel` = `text-embedding-3-large`, `EmbeddingDimensions` = 1536.
9. Chunks bulk-inserted by raw SQL into `LeafletChunks` (≤ 1000 rows per statement,
   `ON CONFLICT (Id) DO NOTHING`).
10. Status → `indexed`, `IndexedAt` = UTC now. Any exception → status `failed` and rethrow.

## Logic & formulas
- Word count = whitespace-separated tokens (document and each chunk).
- Dedup key is the byte hash, not the filename: the same PDF renamed is a duplicate; a re-saved
  PDF with identical text but different bytes is a new document.
- Job counters logged at the end: `Indexed`, `Skipped` (duplicates + unsupported types), `Failed`.
  One failing file does not stop the others; a failed file stays in the inbox.
- Summary LLM call uses the shared default chat client (model = `KnowledgeBase:ChatModel`,
  `claude-sonnet-4-6`, max tokens `KnowledgeBase:ChatMaxTokens` = 1024), not `Leaflet:ChatModel`.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `Leaflet:IngestionCronExpression` | `*/15 * * * *` | Job schedule |
| `Leaflet:OneDriveFolderMappings[]` | 1 mapping, `/AI/Leaflets/Inbox` → `/AI/Leaflets/Archived` | SharePoint folders polled; must have ≥ 1 entry (options validated on start) |
| `Leaflet:ChunkSize` / `ChunkOverlap` | 800 / 80 | Word window size and overlap |
| `Leaflet:SummarizationEnabled` | `true` (code default, not in appsettings) | LLM summary per chunk |
| `Leaflet:SummarizationPrompt` | Czech extraction prompt (code) | Summary prompt |
| `Leaflet:EmbeddingModel` / `EmbeddingDimensions` | `text-embedding-3-large` / 1536 | Embedding call; column is `vector(1536)` |
| `KnowledgeBase:OneDriveFolderMappings` | — | Decides Graph vs. mock OneDrive for the whole app (see quirks) |
| `UseMockAuth`, `BypassJwtValidation` | false | Either true → mock OneDrive (empty inbox) |

`appsettings.Production.json` repeats the Leaflet section without folder mappings or cron; the base
values apply.

## Runtime facts
None.

## Known quirks
- **A failed file is never retried.** Indexing commits the `LeafletDocuments` row (with its hash)
  before chunking; on failure the row stays as `failed` and the file stays in the inbox. On the
  next run the hash matches that row → treated as a duplicate → archived without indexing. A
  transient LLM/embedding outage therefore leaves the leaflet permanently `failed` with no chunks.
  Fix today: delete the document in tab *Dokumenty* and put the file back into the inbox.
  Same for a row stuck in `processing` after a crash. (Found in code review, 2026-10-02.)
- **Summaries are paid for but unused.** Each chunk costs one Sonnet call to build `Summary`, but
  embeddings are computed from `Content` and generation uses `Content` too; `Summary` is only
  displayed in the chunk detail modal.
- **Graph-vs-mock decision ignores the Leaflet config.** `SharedRagModule` registers the real
  Graph service only if `KnowledgeBase:OneDriveFolderMappings` has a `DriveId`. With KB mappings
  empty, the leaflet job silently sees an always-empty inbox (mock). Acknowledged in code comment.
- **Inbox listing reads one Graph page only** (`@odata.nextLink` not followed); with a very large
  inbox the rest waits for later runs (each run archives what it processed, so it drains).
- **Archive overwrite.** Moving uses `conflictBehavior=replace`, so a newer file with the same name
  replaces the older archived copy; the older document's `SourcePath` still points to that URL.
- **`.doc` (old binary Word) is accepted but cannot be read** — the Word extractor uses OpenXML,
  so a `.doc` fails with an exception (job: *failed*, then the never-retried quirk above).
- **Delete is database-only.** Deleting a document does not remove the SharePoint archive copy;
  dropping that file into the inbox again re-indexes it (hash no longer present).
- **Upload blocks the HTTP request** for all LLM summary calls; large documents (many chunks)
  can take long.
- No `IsEnabled` gate besides the Hangfire job toggle; the generator works with an empty library
  (cold start) — see `flow-leaflet-generation`.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Infrastructure/Jobs/LeafletIngestionJob.cs` — job loop, archive, counters
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/IndexLeaflet/IndexLeafletHandler.cs` — dedup, replace, status lifecycle
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Services/LeafletIndexingService.cs` — chunk, summarize, embed
- `backend/src/Anela.Heblo.Application/Features/Leaflet/Services/LeafletChunkSummarizer.cs` — summary LLM call
- `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/UploadLeaflet/UploadLeafletHandler.cs` — manual upload
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs` — raw-SQL chunk insert
- `backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/GraphOneDriveService.cs` — Graph calls (shared RAG infrastructure)
- `backend/src/Anela.Heblo.Application/Shared/Rag/SharedRagModule.cs` — Graph vs. mock registration
