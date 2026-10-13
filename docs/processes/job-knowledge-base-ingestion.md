---
process: job-knowledge-base-ingestion
kind: job
module: knowledge-base
summary: Every 15 minutes picks up new files from the SharePoint knowledge-base inbox folders (and any file uploaded by hand), turns them into LLM-summarised, OpenAI-embedded chunks in KnowledgeBaseDocuments/KnowledgeBaseChunks, and moves the processed files to the archive folder.
owns:
  - backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/Jobs/KnowledgeBaseIngestionJob.cs
  - backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/**
  - backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/**
  - backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/**
  - backend/src/Anela.Heblo.Application/Features/KnowledgeBase/ContentTypeResolver.cs
  - backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs
  - backend/src/Anela.Heblo.Application/Shared/Rag/WordWindowChunker.cs
  - backend/src/Anela.Heblo.Application/Shared/Rag/RagFeatureOptions.cs
  - backend/src/Anela.Heblo.Application/Shared/Rag/OneDriveFolderMapping.cs
  - backend/src/Anela.Heblo.Application/Shared/Rag/IOneDriveService.cs
  - backend/src/Anela.Heblo.Application/Shared/Rag/SharedRagModule.cs
  - backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/**
  - backend/src/Anela.Heblo.Application/Shared/Rag/DocumentExtractors/**
  - backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/**
  - backend/src/Anela.Heblo.Persistence/KnowledgeBase/**
verified_at: "5e993f9e2"
related: [flow-knowledge-base-ask]
---

# Knowledge base ingestion

## Purpose
Fills the AI knowledge base ("Poradenství (KB)") that answers customer-care questions. Anela
staff drop files into two SharePoint folders — internal documents (product sheets, care guides)
and exported customer chat transcripts (Smartsupp conversations). This job reads each new file,
splits it into searchable pieces ("chunks"), lets Claude write a keyword summary of each piece,
turns the summary into an OpenAI embedding vector and stores everything in Postgres. The file is
then moved to an `Archived` folder so it is not processed again.

The resulting chunks are what `flow-knowledge-base-ask` searches. They are also the fact source
for the Smartsupp draft reply, the AI article writer and the leaflet generator (other modules,
see `module-knowledge-base`).

The same indexing pipeline runs when a user uploads a file on `/knowledge-base` → tab
"Nahrát soubor" (needs Knowledge Base write permission). That path skips SharePoint entirely.

## Trigger
- Hangfire recurring job **`knowledge-base-ingestion`** (category Content, display name
  "Knowledge Base Ingestion"), cron **`*/15 * * * *`**, time zone Europe/Prague,
  `DefaultIsEnabled = true`. The cron comes from the job's `RecurringJobMetadata`; it can be
  changed, disabled or triggered by hand on the Recurring Jobs admin page. When disabled the run
  logs "is disabled. Skipping." and does nothing.
- On demand: `POST /api/KnowledgeBase/documents/upload` (multipart `file` + form field
  `documentType`, default `KnowledgeBase`), permission `Customer_KnowledgeBase` Write.

## Data flow
Source: SharePoint document library (Microsoft Graph, app-only token, scope
`https://graph.microsoft.com/.default`) → target: `public."KnowledgeBaseDocuments"` and
`public."KnowledgeBaseChunks"` in the Heblo database.

1. For each entry of `KnowledgeBase:OneDriveFolderMappings`, in config order (repo defaults, one
   drive `b!tB0jjQ…`):

   | Inbox | Archive | DocumentType |
   |---|---|---|
   | `/KnowledgeBase/Documents/Inbox` | `/KnowledgeBase/Documents/Archived` | `KnowledgeBase` |
   | `/KnowledgeBase/Conversations/Inbox` | `/KnowledgeBase/Conversations/Archived` | `Conversation` |

2. **List** `GET /drives/{driveId}/root:/{inbox}:/children`; folders (no `file` facet) are
   skipped. Only the first response page is read (no `@odata.nextLink` paging).
3. **Download** each file: `GET /drives/{driveId}/items/{id}/content`.
4. **Index** (`IndexDocumentHandler`, MediatR `IndexDocumentRequest`):
   1. Resolve the content type: when Graph says empty or `application/octet-stream`, map by
      extension (`.pdf`, `.docx`, `.doc`, `.txt`, `.md`).
   2. `ContentHash` = SHA-256 of the raw bytes (hex, 64 chars).
   3. **Same hash already in the table** → no re-indexing. If the stored `SourcePath` differs it
      is updated; a legacy row without `DriveId`/`GraphItemId` gets them back-filled. Result
      `WasDuplicate = true`.
   4. Otherwise look for the **same file identity** — `DriveId` + `GraphItemId` for SharePoint
      files, `SourcePath` for uploads. A match means the file was edited: the old document and
      its chunks are deleted (cascade) before the new version is indexed.
   5. Insert a `KnowledgeBaseDocuments` row with `Status = processing` and save it.
   6. Extract text (PdfPig for PDF, OpenXml for Word, UTF-8 for `text/*` and
      `application/markdown`), then strip the chat boilerplate regexes
      (`KnowledgeBase:PreprocessorPatterns`) and collapse 3+ blank lines.
   7. Build chunks with the strategy for the document type (see Logic).
   8. Embed with OpenAI (`EmbeddingModel` `text-embedding-3-large`, `EmbeddingDimensions` 1536),
      insert each chunk with raw SQL into `KnowledgeBaseChunks` (`Embedding` is a pgvector
      `vector(1536)` column with HNSW cosine index `idx_kb_chunks_embedding`).
   9. Set `Status = indexed`, `IndexedAt` = UTC now, save. Any exception: `Status = failed`,
      saved, exception re-thrown.
5. **Archive**: `PATCH /drives/{driveId}/items/{id}` with the archive folder's item id as
   `parentReference` and `@microsoft.graph.conflictBehavior = replace`. The archive folder (and
   missing parents) is created if absent; folder ids are cached 60 min
   (`graph:folder-id:{driveId}:{path}`). This runs for new **and** duplicate files.
6. Set the document's `SourcePath` to the archived file's SharePoint `webUrl` (the "open source"
   link users see). A failure here is only logged as a warning ("Manual correction required").
7. Log the totals `Indexed / Skipped / Failed`.

Manual upload: the handler first checks that some extractor can read the content type
(otherwise `UnsupportedFileType`, 400), then sends the same `IndexDocumentRequest` with
`SourcePath = upload/{new guid}/{filename}` and no Graph identity. Nothing is written to
SharePoint.

## Logic & formulas
- **Document chunks (`DocumentType = KnowledgeBase`, `KnowledgeBaseDocIndexingStrategy`)**:
  sliding window over whitespace-separated **words**, `ChunkSize` 512 words with
  `ChunkOverlap` 50 (repo `appsettings.json`; class defaults 800/80), i.e. step 462 words.
  Each chunk is summarised by Claude with `SummarizationPrompt` (one LLM call per chunk,
  sequential); the **summary** is embedded, the original text is stored as `Content`.
- **Conversation chunks (`DocumentType = Conversation`, `ConversationIndexingStrategy`)**:
  one Claude call over the whole cleaned transcript with `TopicSummarizationPrompt`; the answer
  is split on `[TOPIC]` into one summary per topic. One chunk per topic: `Summary` = topic block
  (embedded), `Content` = **the whole transcript** (same text in every chunk of that document).
  If the model returns no `[TOPIC]` block, the whole answer becomes one topic.
- Summaries are Czech key/value blocks (Problém, Kontext, Doporučení, Produkty, Ingredience,
  Výsledek). `SummarizationEnabled = false` skips the LLM: the chunk text (or the whole
  transcript as one topic) is embedded as-is.
- Chat model: the default `IChatClient` (Anthropic). Its model and max tokens come from
  `KnowledgeBase:ChatModel` (`claude-sonnet-4-6`) and `KnowledgeBase:ChatMaxTokens` (1024).
- Embedding retries: the OpenAI adapter retries `HttpRequestException` 3× with exponential
  back-off from 2 s.
- Statuses stored lower-case: `processing`, `indexed`, `failed`.
- Unique keys: `ContentHash` (unique), `(DriveId, GraphItemId)` unique where `GraphItemId` is not
  null. Chunks cascade-delete with their document.
- Supported files: PDF, DOCX, plain text / Markdown. The upload UI accepts `.pdf .docx .txt .md`
  and pre-selects "Konverzace" for `.txt`/`.md`, "Znalostní báze" otherwise.

## Configuration
| Key | Repo default | Meaning |
|---|---|---|
| `KnowledgeBase:OneDriveFolderMappings[]` | 2 mappings above | `DriveId`, `InboxPath`, `ArchivedPath`, `DocumentType` per inbox; `[MinLength(1)]`, validated on start |
| `KnowledgeBase:ChunkSize` / `ChunkOverlap` | 512 / 50 words (class 800 / 80) | Document sliding window |
| `KnowledgeBase:SummarizationEnabled` | true | LLM summary per chunk / topic split |
| `KnowledgeBase:SummarizationPrompt`, `TopicSummarizationPrompt`, `TopicDelimiter` | class defaults (Czech), `[TOPIC]` | Indexing prompts |
| `KnowledgeBase:PreprocessorPatterns` | 4 Smartsupp boilerplate regexes | Removed from every document before chunking |
| `KnowledgeBase:EmbeddingModel` / `EmbeddingDimensions` | `text-embedding-3-large` / 1536 | Must match the `vector(1536)` column |
| `KnowledgeBase:ChatModel` / `ChatMaxTokens` | `claude-sonnet-4-6` / 1024 | Default Anthropic client for the whole app (see quirks) |
| `KnowledgeBase:IngestionCronExpression` | `*/15 * * * *` | **Not read** — the job's cron is hard-coded in its metadata |
| `OpenAI:ApiKey`, `Anthropic:ApiKey` | secrets (empty in repo) | Embedding / chat credentials |
| `UseMockAuth`, `BypassJwtValidation` | false | Either true, or no mapping with a `DriveId`, → `MockOneDriveService` (inbox always empty) |

## Runtime facts
- `KnowledgeBaseChunks` is the second-largest table in prod `Heblo_V3` (72 MB) — memory note
  `ops_heblosql_b1ms_burstable` — 2026-09-22.

## Known quirks
- **A failed file is archived on the next run and never retried.** The failed attempt leaves a
  `failed` document row carrying the file's hash; the next poll finds that hash, treats the file
  as a duplicate and moves it to `Archived`. The document stays `failed` (possibly with some
  chunks already inserted, which search still returns). Fix: delete the failed document on
  `/knowledge-base` → Dokumenty, then put the file back in the inbox. (Read from code.)
- **Unsupported files behave the same way**: the type check happens after the document row is
  created, so the first poll counts the file as "skipped" and leaves it in the inbox, the second
  poll archives it as a duplicate of the `failed` row.
- **Legacy `.doc` files are accepted but cannot be read**: the Word extractor claims
  `application/msword` but OpenXml opens only `.docx`, so they end `failed`.
- **Editing a file replaces it destructively**: the old document is deleted *before* the new
  version is indexed; if re-indexing fails the old knowledge is gone.
- **Hash dedup is global**: identical bytes dropped into the Conversations inbox after the
  Documents inbox keep the first `DocumentType`; manual re-upload of the same file only rewrites the stored path.
  A changed manual upload creates a second document (upload paths are unique), the old one stays.
- **Uploading `documentType=Leaflet` or `Article`** passes validation but there is no indexing
  strategy for them: a `failed` row is created and the request errors.
- **One failing inbox stops the run**: listing errors (e.g. 404 for a missing inbox folder) are
  not caught per mapping, so the Conversations inbox is skipped when the Documents inbox fails.
- **Only the first Graph page** (default 200 items) of an inbox is read per run; the rest is
  picked up by later runs as files move out.
- **Every conversation chunk stores the whole transcript** as `Content`, so a long conversation
  with many topics multiplies its text in the table and in the answer context.
- **The document summary prompt talks about customer chats** ("úryvek zákaznického chatu") even
  for internal documents; the summaries of product sheets are forced into the chat format.
- **`KnowledgeBase:ChatModel` / `ChatMaxTokens` configure the app-wide default Claude client**
  (`AnthropicAdapterServiceCollectionExtensions`), not only the knowledge base.
- **`MockOneDriveService` is chosen from the `KnowledgeBase` section only**; Leaflet's own
  mappings are never consulted (noted in `SharedRagModule`).
- **Runs on the single Hangfire worker**: prod has `WorkerCount = 1`, so a run with many LLM
  calls delays every other job (memory note `gotcha_hangfire_single_worker_starves_print`,
  2026-09-29).
- **`docs/features/knowledge-base-rag.md` is outdated**: it mentions `text-embedding-3-small`,
  the `dbo` schema, `DocumentChunker`, single-inbox config keys and a per-chunk N+1 query that
  no longer exist.

## Code entry points
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Infrastructure/Jobs/KnowledgeBaseIngestionJob.cs` — job id, cron, inbox loop, archive + SourcePath update
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/IndexDocument/IndexDocumentHandler.cs` — hash/identity dedup, status handling
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/DocumentIndexingService.cs` — extract → clean → strategy
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/KnowledgeBaseDocIndexingStrategy.cs` — word window + per-chunk summary
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/ConversationIndexingStrategy.cs` — topic split for transcripts
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs` — prompts, preprocessor patterns, defaults
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/UploadDocument/UploadDocumentHandler.cs` — manual upload
- `backend/src/Anela.Heblo.Application/Shared/Rag/OneDrive/GraphOneDriveService.cs` — Graph list/download/move
- `backend/src/Anela.Heblo.Application/Shared/Rag/SharedRagModule.cs` — Graph vs mock selection, extractors
- `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs` — raw-SQL chunk insert
- `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseDocumentConfiguration.cs` — unique indexes, status conversion
- `backend/src/Anela.Heblo.Persistence/Migrations/20260331070417_UpgradeEmbeddingTo3Large.cs` — `vector(1536)` + HNSW index
