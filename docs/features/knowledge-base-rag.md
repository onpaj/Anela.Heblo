# Knowledge Base — RAG System

## Overview

The Knowledge Base is a Retrieval-Augmented Generation (RAG) system that enables AI-assisted Q&A over company documents. Documents are ingested from OneDrive, chunked, embedded into a vector store (PostgreSQL + pgvector), and made available for semantic search and natural language Q&A via Claude.

The feature is accessible via:
- **REST API** — `GET/POST /api/knowledgebase/*` for direct integration
- **MCP tools** — `SearchKnowledgeBase`, `AskKnowledgeBase` for AI assistant access
- **Background ingestion** — Hangfire recurring job polls OneDrive and indexes new documents automatically

---

## Use Cases

1. **Document Ingestion** — A user uploads a PDF to the OneDrive Inbox folder. The system automatically picks it up, extracts text, chunks it, generates embeddings, and stores everything in the vector database.
2. **Semantic Search** — An AI assistant or API consumer sends a natural-language query and receives relevant document chunks with similarity scores and source references.
3. **Q&A** — An AI assistant asks a question; the system retrieves relevant context chunks and feeds them to Claude to generate a grounded prose answer with cited sources.
4. **Deduplication** — Re-uploading an unchanged document (or moving/renaming it) does not cause re-embedding; the system detects the duplicate via SHA-256 content hash.

---

## Architecture

### Layer Overview

```
OneDrive (Inbox folder)
        │
        ▼
KnowledgeBaseIngestionJob (Hangfire, every 15 min)
        │  IOneDriveService.ListInboxFilesAsync()
        │  IOneDriveService.DownloadFileAsync()
        │  SHA-256 dedup check
        │
        ▼
IndexDocumentHandler (MediatR)
        │  IDocumentTextExtractor.ExtractTextAsync()   → PdfTextExtractor (PdfPig)
        │  DocumentChunker.Chunk()                     → sliding-window word chunker
        │  IEmbeddingService.GenerateEmbeddingAsync()  → OpenAiEmbeddingService
        │  IKnowledgeBaseRepository.AddDocumentAsync()
        │  IKnowledgeBaseRepository.AddChunksAsync()   → raw Npgsql INSERT (vector)
        │
        ▼
PostgreSQL dbo."KnowledgeBaseDocuments" + dbo."KnowledgeBaseChunks"
        │  vector(1536) column, HNSW cosine index
        │
        ▼ (on query)
SearchDocumentsHandler (MediatR)
        │  IEmbeddingService.GenerateEmbeddingAsync()
        │  IKnowledgeBaseRepository.SearchSimilarAsync()  → cosine distance SQL
        │
        ▼ (on ask)
AskQuestionHandler (MediatR)
        │  → SearchDocumentsHandler (via IMediator)
        │  IClaudeService.GenerateAnswerAsync()            → AnthropicClaudeService
        │
        ▼
REST API / MCP tools
```

### Projects and File Locations

```
backend/src/
├── Anela.Heblo.Domain/
│   └── Features/KnowledgeBase/
│       ├── KnowledgeBaseDocument.cs          # Domain entity
│       ├── KnowledgeBaseChunk.cs             # Domain entity
│       ├── IKnowledgeBaseRepository.cs       # Repository interface
│       └── DocumentStatus.cs                 # Status constants (nested in Document)
│
├── Anela.Heblo.Application/
│   └── Features/KnowledgeBase/
│       ├── KnowledgeBaseModule.cs            # DI registration
│       ├── KnowledgeBaseOptions.cs           # Configuration options
│       ├── Services/
│       │   ├── IEmbeddingService.cs
│       │   ├── IClaudeService.cs
│       │   ├── IOneDriveService.cs
│       │   ├── IDocumentTextExtractor.cs
│       │   ├── OpenAiEmbeddingService.cs     # OpenAI text-embedding-3-small
│       │   ├── AnthropicClaudeService.cs     # Claude claude-sonnet-4-6
│       │   ├── GraphOneDriveService.cs       # Microsoft Graph implementation
│       │   ├── MockOneDriveService.cs        # Local dev / mock auth mode
│       │   ├── DocumentChunker.cs            # Sliding-window word chunker
│       │   └── PdfTextExtractor.cs           # PdfPig-based PDF extractor
│       ├── Jobs/
│       │   └── KnowledgeBaseIngestionJob.cs  # Hangfire recurring job
│       └── UseCases/
│           ├── AskQuestion/
│           │   ├── AskQuestionRequest.cs
│           │   └── AskQuestionHandler.cs
│           ├── IndexDocument/
│           │   ├── IndexDocumentRequest.cs
│           │   └── IndexDocumentHandler.cs
│           └── SearchDocuments/
│               ├── SearchDocumentsRequest.cs
│               └── SearchDocumentsHandler.cs
│
├── Anela.Heblo.Persistence/
│   └── KnowledgeBase/
│       ├── KnowledgeBaseRepository.cs
│       ├── KnowledgeBaseDocumentConfiguration.cs
│       └── KnowledgeBaseChunkConfiguration.cs
│
└── Anela.Heblo.API/
    ├── Controllers/
    │   └── KnowledgeBaseController.cs
    └── MCP/Tools/
        └── KnowledgeBaseTools.cs

backend/test/Anela.Heblo.Tests/
└── KnowledgeBase/
    ├── Services/DocumentChunkerTests.cs
    ├── UseCases/AskQuestionHandlerTests.cs
    ├── UseCases/IndexDocumentHandlerTests.cs
    └── UseCases/SearchDocumentsHandlerTests.cs
    └── (MCP/Tools/KnowledgeBaseToolsTests.cs — in MCP/Tools/ folder)
```

---

## Data Model

### `KnowledgeBaseDocument`

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `Id` | `uuid` | PK | |
| `Filename` | `varchar(500)` | NOT NULL | Original filename from OneDrive |
| `SourcePath` | `varchar(1000)` | NOT NULL, UNIQUE | OneDrive webUrl; unique per document |
| `ContentType` | `varchar(100)` | NOT NULL | MIME type, e.g. `application/pdf` |
| `ContentHash` | `varchar(64)` | NOT NULL, UNIQUE | SHA-256 hex digest of raw bytes |
| `Status` | `varchar(50)` | NOT NULL, indexed | `processing` → `indexed` \| `failed` |
| `CreatedAt` | `timestamp` | NOT NULL | UTC, set at creation |
| `IndexedAt` | `timestamp` | nullable | UTC, set after successful indexing |

**Indexes:** `ContentHash` (UNIQUE), `SourcePath` (UNIQUE), `Status`

### `KnowledgeBaseChunk`

| Column | Type | Constraints | Notes |
|---|---|---|---|
| `Id` | `uuid` | PK | |
| `DocumentId` | `uuid` | FK → Document (CASCADE DELETE) | |
| `ChunkIndex` | `int` | NOT NULL | 0-based position within document |
| `Content` | `text` | NOT NULL | Raw text of the chunk |
| `Embedding` | `vector(1536)` | managed via raw SQL | OpenAI embedding; ignored by EF Core |

**Indexes:** `DocumentId`, HNSW cosine index on `Embedding` (`m=16, ef_construction=64`)

> **Note:** The `Embedding` column is intentionally excluded from the EF Core model (`builder.Ignore(e => e.Embedding)`) because EF Core / Npgsql 8.x cannot map `vector(1536)` natively. Writes and reads use raw `NpgsqlCommand`.

### Document Status Lifecycle

```
OneDrive file detected
        │
        ▼
DocumentStatus.Processing  ← document record inserted
        │
        ├─ extraction / chunking / embedding fails
        │       ▼
        │   DocumentStatus.Failed  (job logs error, continues to next file)
        │
        └─ all chunks stored successfully
                ▼
            DocumentStatus.Indexed  ← IndexedAt set
```

---

## Ingestion Pipeline

### Trigger

Hangfire recurring job `knowledge-base-ingestion`, cron `*/15 * * * *` (every 15 minutes). Registered via `IRecurringJob` interface and auto-discovered by `RecurringJobDiscoveryService`. Can be disabled/triggered manually via the Hangfire Jobs Management UI.

### Steps

1. **Check job enabled** via `IRecurringJobStatusChecker`
2. **List files** in OneDrive Inbox folder (`KnowledgeBaseOptions.OneDriveInboxPath`)
3. **For each file:**
   a. Download raw bytes
   b. Compute SHA-256 content hash
   c. Look up hash in `KnowledgeBaseDocuments`
   d. **If already indexed:** update `SourcePath` if file was moved/renamed; skip re-embedding
   e. **If new:** dispatch `IndexDocumentRequest` via MediatR
   f. On success: move file to Archived folder (`KnowledgeBaseOptions.OneDriveArchivedPath`)
4. Log summary: indexed / skipped / failed counts

### `IndexDocumentHandler` Steps

1. Validate content type via `IDocumentTextExtractor.CanHandle()` — throws `NotSupportedException` for unsupported types
2. Create `KnowledgeBaseDocument` with status `Processing`, persist via `AddDocumentAsync`
3. Extract text via `IDocumentTextExtractor.ExtractTextAsync()`
4. Chunk text via `DocumentChunker.Chunk()`
5. For each chunk: generate embedding via `IEmbeddingService.GenerateEmbeddingAsync()`
6. Bulk-insert chunks (with embeddings) via `AddChunksAsync()` using raw Npgsql
7. Update document status to `Indexed`, set `IndexedAt`, call `SaveChangesAsync()`

### Chunking Strategy

`DocumentChunker` uses **sliding-window word chunking**:
- Splits text by whitespace into words
- Window size: `ChunkSize` words (default: 512)
- Overlap: `ChunkOverlapTokens` words (default: 50)
- Step = `ChunkSize - ChunkOverlapTokens` = 462 words

> **Important:** The chunker treats words (whitespace-delimited tokens) as a proxy for LLM tokens. Actual token count is ~1.3× word count for English / Czech text. A 512-word chunk ≈ 650–700 tokens. This is well within OpenAI embedding and Claude context limits but does not map precisely to the `text-embedding-3-small` 8192-token context window.

### Deduplication

Content-hash deduplication (SHA-256 of raw bytes) ensures:
- Identical files are never re-embedded, regardless of filename or path
- Renamed / moved files update `SourcePath` without re-indexing
- Changed files (new hash) are indexed as new documents

---

## Search Pipeline

`POST /api/knowledgebase/search` → `SearchDocumentsHandler`

1. Generate query embedding via `IEmbeddingService.GenerateEmbeddingAsync(request.Query)`
2. Run cosine similarity search in PostgreSQL:
   ```sql
   SELECT c."Id", c."DocumentId", c."ChunkIndex", c."Content",
          1 - (c."Embedding" <=> @embedding) AS "Score"
   FROM dbo."KnowledgeBaseChunks" c
   ORDER BY c."Embedding" <=> @embedding
   LIMIT @topK
   ```
   Score = `1 - cosine_distance` ∈ [0, 1]; higher = more similar
3. Load parent `KnowledgeBaseDocument` for each chunk (currently N+1 queries — see Known Issues)
4. Return `SearchDocumentsResponse` with `ChunkResult[]`

---

## Q&A Pipeline

`POST /api/knowledgebase/ask` → `AskQuestionHandler`

1. Dispatch `SearchDocumentsRequest` via `IMediator` to retrieve top-K chunks
2. Concatenate chunk contents as context (separated by `---`)
3. Build prompt:
   ```
   You are an expert assistant for a cosmetics manufacturing company.
   Answer the following question based strictly on the provided context.
   If the answer cannot be found in the context, say so explicitly.
   Always be precise and cite specific details from the context.

   CONTEXT:
   {chunks joined by \n\n---\n\n}

   QUESTION:
   {question}

   ANSWER:
   ```
4. Call Claude via `IClaudeService.GenerateAnswerAsync()`
5. Return `AskQuestionResponse` with `Answer` (prose) and `Sources[]` (document references with score and 200-char excerpt)

---

## REST API

Base path: `GET|POST /api/knowledgebase`
Authentication: `[Authorize]` — requires Microsoft Entra ID JWT

### `GET /api/knowledgebase/documents`

Returns list of all indexed documents ordered by `CreatedAt` descending.

**Response:**
```json
[
  {
    "id": "uuid",
    "filename": "EU_cosmetics_regulation.pdf",
    "status": "indexed",
    "contentType": "application/pdf",
    "createdAt": "2026-03-02T16:30:00Z",
    "indexedAt": "2026-03-02T16:30:15Z"
  }
]
```

### `POST /api/knowledgebase/search`

**Request:**
```json
{
  "query": "maximum phenoxyethanol concentration",
  "topK": 5
}
```

**Response:** `SearchDocumentsResponse`
```json
{
  "chunks": [
    {
      "chunkId": "uuid",
      "documentId": "uuid",
      "content": "Phenoxyethanol max 1.0% in Annex V...",
      "score": 0.94,
      "sourceFilename": "EU_reg.pdf",
      "sourcePath": "https://..."
    }
  ]
}
```

### `POST /api/knowledgebase/ask`

**Request:**
```json
{
  "question": "What is the maximum allowed concentration of phenoxyethanol?",
  "topK": 5
}
```

**Response:** `AskQuestionResponse`
```json
{
  "answer": "According to EU Cosmetics Regulation Annex V, phenoxyethanol is permitted up to a maximum concentration of 1.0%.",
  "sources": [
    {
      "documentId": "uuid",
      "filename": "EU_reg.pdf",
      "excerpt": "Phenoxyethanol max 1.0% in Annex V...",
      "score": 0.94
    }
  ]
}
```

---

## MCP Tools

Registered in `McpModule` via `WithTools<KnowledgeBaseTools>()`.

### `SearchKnowledgeBase`

```
Description: Search the knowledge base for relevant document chunks using semantic similarity.
             Returns raw chunks with source references.
Parameters:
  query  (string)  — Natural language search query
  topK   (int=5)   — Number of chunks to return
Returns: JSON-serialized SearchDocumentsResponse
```

### `AskKnowledgeBase`

```
Description: Ask a question and get an AI-generated answer grounded in company documents.
             Returns a prose answer with cited sources.
Parameters:
  question  (string)  — Question to answer using the knowledge base
  topK      (int=5)   — Number of context chunks to retrieve
Returns: JSON-serialized AskQuestionResponse
```

---

## Configuration

### `appsettings.json` — `KnowledgeBase` section

```json
{
  "KnowledgeBase": {
    "OneDriveInboxPath": "/KnowledgeBase/Inbox",
    "OneDriveArchivedPath": "/KnowledgeBase/Archived",
    "EmbeddingModel": "text-embedding-3-small",
    "EmbeddingDimensions": 1536,
    "ChunkSize": 512,
    "ChunkOverlapTokens": 50,
    "MaxRetrievedChunks": 5,
    "ClaudeModel": "claude-sonnet-4-6",
    "ClaudeMaxTokens": 1024
  },
  "OpenAI": {
    "ApiKey": ""
  },
  "Anthropic": {
    "ApiKey": ""
  }
}
```

API keys are set via environment variables or Azure Key Vault — never committed to source control.

### `KnowledgeBaseOptions` properties

| Property | Default | Description |
|---|---|---|
| `OneDriveInboxPath` | `/KnowledgeBase/Inbox` | OneDrive folder path to poll for new documents |
| `OneDriveArchivedPath` | `/KnowledgeBase/Archived` | OneDrive folder where processed documents are moved |
| `EmbeddingModel` | `text-embedding-3-small` | OpenAI embedding model name |
| `EmbeddingDimensions` | `1536` | Embedding vector dimension — must match DB `vector(N)` |
| `ChunkSize` | `512` | Words per chunk |
| `ChunkOverlapTokens` | `50` | Overlap words between consecutive chunks |
| `MaxRetrievedChunks` | `5` | Default `topK` for search and Q&A |
| `ClaudeModel` | `claude-sonnet-4-6` | Claude model for Q&A generation |
| `ClaudeMaxTokens` | `1024` | Max tokens in Claude response |

### Local Development — OneDrive Mock

When `UseMockAuth=true` or `BypassJwtValidation=true`, `MockOneDriveService` is registered instead of `GraphOneDriveService`. The mock returns an empty file list, so the ingestion job runs cleanly without OneDrive access.

---

## Dependencies (NuGet)

| Package | Version | Layer | Purpose |
|---|---|---|---|
| `OpenAI` | 2.2.0 | Application | OpenAI embedding client (`EmbeddingClient`) |
| `Anthropic` | 1.0.0 | Application | Claude API client (`AnthropicApi`) |
| `PdfPig` | 0.1.9 | Application | PDF text extraction |
| `Pgvector` | 0.3.2 | Persistence | `Vector` type for raw Npgsql parameters |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 8.0.4 | Persistence | EF Core PostgreSQL provider |

---

## Database Migrations

| Migration | Date | Changes |
|---|---|---|
| `20260302163014_AddKnowledgeBase` | 2026-03-02 | Creates `KnowledgeBaseDocuments` and `KnowledgeBaseChunks` tables, enables `vector` extension, adds `Embedding vector(1536)` column via raw SQL, creates HNSW index |
| `20260306192831_AddContentHashToKnowledgeBaseDocument` | 2026-03-06 | Adds `ContentHash varchar(64)` column with UNIQUE index to `KnowledgeBaseDocuments` |

> **Note:** Migrations are applied manually (not automated in deployment), consistent with the rest of the project.

---

## Testing

### Unit Tests (5 files, ~20 tests)

| File | Coverage |
|---|---|
| `DocumentChunkerTests` | Short text, empty text, whitespace, multi-chunk with overlap |
| `IndexDocumentHandlerTests` | Stores document + chunks with embeddings, throws for unsupported MIME type |
| `SearchDocumentsHandlerTests` | Generates embedding, calls repository, maps chunk results |
| `AskQuestionHandlerTests` | Calls search, calls Claude, returns answer with sources |
| `KnowledgeBaseToolsTests` | Parameter mapping, JSON serialization, `McpException` on error |

### Missing Tests

- Integration tests against real PostgreSQL (pgvector)
- Integration tests for `GraphOneDriveService` against Graph API
- Integration tests for `AnthropicClaudeService` / `OpenAiEmbeddingService` against real APIs
- E2E Playwright tests (no frontend UI yet)

---

## Known Issues and Required Fixes

### Critical

#### 1. `GraphOneDriveService` — wrong Graph endpoint for app-only token

`GetAccessTokenForAppAsync` returns an **app-only token** (client credentials flow). App-only tokens cannot call `/me/drive` — that endpoint requires a delegated user token. All three methods (`ListInboxFilesAsync`, `DownloadFileAsync`, `MoveToArchivedAsync`) use `/me/drive/...` and will fail with HTTP 403 in production.

**Fix options:**
- Add `OneDriveUserId` (or UPN) to `KnowledgeBaseOptions` and use `/users/{userId}/drive/root:/{path}:/children`
- Alternatively, use a SharePoint site drive if documents are stored in SharePoint

```csharp
// ❌ Current (fails with app-only token)
$"{GraphBaseUrl}/me/drive/root:/{encodedPath}:/children"

// ✅ Fix
$"{GraphBaseUrl}/users/{_options.OneDriveUserId}/drive/root:/{encodedPath}:/children"
```

#### 2. `KnowledgeBaseRepository` — double-open of DB connection

`AddChunksAsync` and `SearchSimilarAsync` call `connection.OpenAsync(ct)` unconditionally. If EF Core has already opened the connection in the same request scope (e.g. after `AddDocumentAsync`), this throws `InvalidOperationException: Connection already open`.

**Fix:** Check connection state before opening, or use `_context.Database.OpenConnectionAsync()` which is idempotent:

```csharp
if (connection.State != System.Data.ConnectionState.Open)
    await connection.OpenAsync(ct);
```

### High Priority

#### 3. N+1 queries in `SearchSimilarAsync`

For each of the `topK` chunk results, a separate `FindAsync` call loads the parent document. Fix by joining in the raw SQL query:

```sql
SELECT c."Id", c."DocumentId", c."ChunkIndex", c."Content",
       1 - (c."Embedding" <=> @embedding) AS "Score",
       d."Filename", d."SourcePath"
FROM dbo."KnowledgeBaseChunks" c
JOIN dbo."KnowledgeBaseDocuments" d ON c."DocumentId" = d."Id"
ORDER BY c."Embedding" <=> @embedding
LIMIT @topK
```

#### 4. `AnthropicClaudeService` — verify Anthropic SDK compatibility

The code uses `new AnthropicApi()`, `api.AuthorizeUsingApiKey()`, `api.CreateMessageAsync(model, messages: [prompt], ...)`, and `response.Content.Value2` (OneOf pattern). These do not correspond to the documented API of any official Anthropic SDK. Verify the `Anthropic` v1.0.0 package source and API contract. If incompatible, replace with `Anthropic.SDK` (community) or direct `HttpClient` call to the Messages API.

#### 5. `KnowledgeBaseController.GetDocuments` bypasses MediatR

The `GET /documents` endpoint injects `IKnowledgeBaseRepository` directly, violating the project's MediatR pattern. Add `GetDocumentsRequest` + `GetDocumentsHandler`.

### Medium Priority

#### 6. `AnthropicClaudeService` and `OpenAiEmbeddingService` — direct `IConfiguration` reads

Both services read config via `_configuration["KnowledgeBase:ClaudeModel"]` instead of `IOptions<KnowledgeBaseOptions>`. Use `IOptions<KnowledgeBaseOptions>` for consistency and testability.

#### 7. `OpenAiEmbeddingService` — new `EmbeddingClient` per request

Creates a new `EmbeddingClient` (and underlying `HttpClient`) on every `GenerateEmbeddingAsync` call. Register as singleton or inject via `IHttpClientFactory`.

#### 8. `GraphOneDriveService` — new `HttpClient` per method call

`CreateHttpClient()` instantiates a new `HttpClient` on each method call, violating `HttpClient` lifecycle best practices (socket exhaustion risk). Use `IHttpClientFactory`.

#### 9. Missing input validation

`SearchDocumentsRequest.Query` and `AskQuestionRequest.Question` accept empty strings. `TopK` accepts 0 or negative values. Add validation attributes:

```csharp
[Required, MinLength(1), MaxLength(2000)]
public string Question { get; set; } = string.Empty;

[Range(1, 20)]
public int TopK { get; set; } = 5;
```

#### 10. `SourcePath` unique constraint — update scenario gap

If a document at a given `SourcePath` was previously indexed and then replaced with a different file (new content hash), the ingestion job will attempt to insert a new document with the same path — failing on the UNIQUE constraint. The fix: when no hash match is found but a path match exists, delete/replace the old document, or update it in-place.

#### 11. `appsettings.json` — OpenAI placeholder values

```json
"OpenAI": { "ApiKey": "xxxxxx" }
```
Should be `""` (empty string) to match the Anthropic section pattern.

#### 12. Namespace inconsistency in `KnowledgeBaseIngestionJob`

File path: `.../Application/Features/KnowledgeBase/Jobs/KnowledgeBaseIngestionJob.cs`
Namespace: `Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure.Jobs` (extra `Infrastructure` segment)

---

## Not Yet Implemented

The following components are required for a production-complete feature but are not yet present in the codebase:

### Frontend UI (High Priority)

No React components exist for the Knowledge Base. Required pages/components:
- **Knowledge Base page** — document list with status badges
- **Upload UI** — trigger for OneDrive workflow (or direct upload endpoint)
- **Search UI** — search input with chunk results and source links
- **Ask UI** — Q&A interface with answer and cited sources

### Document Management

- `DELETE /api/knowledgebase/documents/{id}` endpoint and `DeleteDocumentHandler` — removes document and all its chunks (cascade delete is already configured in DB)
- Re-index endpoint for manually re-processing a failed document

### Additional Document Formats

Currently only `application/pdf` is supported via `PdfTextExtractor`. Required for practical use:
- `.docx` / `.doc` (Word documents)
- `.txt` / `.md` (plain text)
- Potentially `.xlsx` for product specification sheets

New extractors implement `IDocumentTextExtractor` and are registered as a list (strategy pattern).

### Resilience

No retry logic for external API calls. Network errors or rate-limit responses from OpenAI or Anthropic will fail the entire document. Add Polly retry policy (already available via `Polly.Extensions` in the project) in `OpenAiEmbeddingService` and `AnthropicClaudeService`.

### Token-Aware Chunking

The current word-count chunker is a simplification. For production accuracy — especially to respect embedding model context limits — replace with token-aware chunking using a tokenizer library (e.g. `SharpToken` for `cl100k_base` used by `text-embedding-3-small`).

### Integration Tests

Unit tests mock all external dependencies. Required integration tests:
- Vector search against real PostgreSQL + pgvector
- `PdfTextExtractor` against known PDF fixtures
- `DocumentChunker` edge cases (very long documents, non-ASCII text)

### Observability

No metrics or structured logging beyond basic `ILogger` calls. Recommended additions:
- Log embedding generation latency and token count
- Track Q&A answer quality feedback (thumbs up/down)
- Alert on ingestion job failure rate

---

## OneDrive Folder Setup

Required manual setup in OneDrive before the ingestion job can run:

```
/KnowledgeBase/
├── Inbox/      ← drop new documents here; job polls every 15 min
└── Archived/   ← job moves processed documents here automatically
```

The paths are configurable via `KnowledgeBase:OneDriveInboxPath` and `KnowledgeBase:OneDriveArchivedPath`.

---

## Security

- All REST API endpoints require `[Authorize]` (Microsoft Entra ID JWT)
- MCP tools inherit MCP server authentication (Entra ID bearer token)
- OpenAI and Anthropic API keys are stored in environment variables / Azure Key Vault — never in source control
- Ingestion job uses app-only Microsoft Graph permissions (requires `Files.Read.All` or equivalent application permission — see fix for `/me/drive` issue above)
