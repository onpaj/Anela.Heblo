# Leaflet Generator — RAG System

## Overview

The Leaflet Generator is a two-stage RAG-powered tool that produces Czech-language marketing leaflets for Anela cosmetics products. It combines factual ingredient/benefit data from the **Knowledge Base** with writing style examples from a dedicated **Leaflet Store**, then sends both through a two-pass LLM pipeline (Claude) to produce a polished, audience-appropriate marketing text.

The feature is accessible via:
- **REST API** — `POST /api/leaflet/generate` for direct integration
- **Background ingestion** — Hangfire recurring job polls OneDrive and indexes new leaflet documents automatically

---

## Use Cases

1. **Leaflet Generation** — A user picks a topic, audience, and desired length. The system retrieves relevant KB context and leaflet style examples, then generates a Czech marketing leaflet in Markdown.
2. **Style Example Ingestion** — A user drops an existing leaflet (PDF, DOCX, etc.) into the OneDrive Inbox. The system picks it up, chunks and embeds it, and stores it as a style reference for future generation.
3. **Cold Start** — When no style examples exist yet for a topic, the generator falls back to a neutral professional marketing register without failing.
4. **Deduplication** — Re-uploading an unchanged document does not cause re-embedding; the system detects duplicates via SHA-256 content hash.

---

## Architecture

### Layer Overview

```
OneDrive (/Leaflets/Inbox)
        │
        ▼
LeafletIngestionJob (Hangfire, every 15 min)
        │  IOneDriveService.ListInboxFilesAsync()
        │  IOneDriveService.DownloadFileAsync()
        │  SHA-256 dedup check
        │
        ▼
IndexLeafletHandler (MediatR)
        │  IDocumentTextExtractor.ExtractTextAsync()
        │  LeafletChunker.Chunk()         → sliding-window (800 words, 80-word overlap)
        │  IEmbeddingGenerator             → text-embedding-3-small (1536 dims)
        │  ILeafletRepository.AddDocumentAsync()
        │  ILeafletRepository.AddChunksAsync()
        │
        ▼
PostgreSQL  LeafletDocuments + LeafletChunks
        │  vector(1536) column, cosine distance (<=>)
        │
        ▼ (on query)
GenerateLeafletHandler (MediatR)
        │  Embed topic → text-embedding-3-small
        │  IKnowledgeBaseRepository.SearchSimilarAsync()  → up to 8 KB chunks
        │  ILeafletRepository.SearchSimilarAsync()        → up to 5 leaflet chunks
        │  Stage 1: Claude → factual outline
        │  Stage 2: Claude → polished Czech leaflet (Markdown)
        │
        ▼
POST /api/leaflet/generate → { content: "# Leaflet…" }
```

---

## Two-Stage Generation Pipeline

### Stage 1 — Factual Outline

- **Input**: user topic + KB context chunks (top-8, min similarity 0.55)
- **LLM task**: extract ingredient facts, claimed benefits, use cases, and regulatory cautions into a structured outline (headings + bullets)
- **Constraint**: LLM must not invent facts; uses only the provided KB context
- **Fallback**: if KB context is empty, the LLM builds a minimal outline from general cosmetics industry knowledge

### Stage 2 — Marketing Copy

- **Input**: Stage 1 outline + leaflet style-reference chunks (top-5, min similarity 0.55)
- **LLM task**: rewrite the outline into polished Czech-language marketing copy in Markdown, matching the voice and rhythm of the style references
- **Cold start**: when no leaflet style examples match the topic, the LLM uses a neutral professional register instead

Both stages use `claude-sonnet-4-6` with a 2048-token output limit. Transient network errors trigger one automatic retry (1 s delay).

---

## What Data to Embed

There are **two separate vector stores** that feed the generator. They serve different purposes and should contain different types of content.

### 1. Knowledge Base (shared with the KB feature)

**Purpose**: Factual ingredient and product information.

**What to upload:**
- Technical data sheets (TDS) for raw materials
- Safety data sheets (SDS/MSDS)
- INCI ingredient descriptions
- Supplier product information documents
- Cosmetics formulation guides
- Regulatory summaries (EU cosmetics regulation)
- Internal product specification sheets

**What NOT to upload:**
- Existing marketing copy (that belongs in the Leaflet Store below)
- Internal financial or HR documents
- Supplier invoices or order forms

**Where to drop files**: `/KnowledgeBase/Inbox` folder on OneDrive (handled by the KB ingestion job, not the leaflet job).

### 2. Leaflet Store (leaflet-specific)

**Purpose**: Writing style and tone reference for the LLM.

**What to upload:**
- Finished Czech marketing leaflets for any Anela products
- Product brochure texts (any format with continuous prose)
- Marketing copy documents with the desired brand voice

**What NOT to upload:**
- Raw ingredient data sheets (put those in the KB)
- Price lists or order forms
- Technical specifications without marketing prose

**Where to drop files**: `/Leaflets/Inbox` folder on OneDrive (picked up by the leaflet ingestion job every 15 minutes).

**Rule of thumb**: If it answers *"what does this ingredient do?"* → Knowledge Base. If it answers *"how does Anela talk about products?"* → Leaflet Store.

---

## Supported File Formats

File format support depends on the `IDocumentTextExtractor` implementations registered in the application. The same extractors are used by both the Knowledge Base and Leaflet ingestion jobs. Unsupported file types are skipped with a warning log entry.

Common supported formats (verify with extractor registrations in `LeafletModule.cs`):
- **PDF** — extracted via PdfPig
- **DOCX** — extracted via Open XML

---

## API Reference

### Generate Leaflet

```
POST /api/leaflet/generate
Content-Type: application/json
Authorization: Bearer <Entra ID token>
Requires: marketing_writer role
```

**Request body:**

| Field | Type | Required | Description |
|-------|------|----------|-------------|
| `topic` | string (1–200 chars) | ✅ | The product or ingredient to write about, e.g. `"bisabolol"` |
| `audience` | `0` or `1` | ✅ | `0` = EndConsumer (Koncový zákazník), `1` = B2B |
| `length` | `0`, `1`, or `2` | ✅ | `0` = Short (~200 words), `1` = Medium (~400 words), `2` = Long (~700 words) |

**Example request:**

```json
{
  "topic": "bisabolol",
  "audience": 0,
  "length": 1
}
```

**Responses:**

| Status | Meaning |
|--------|---------|
| `200 OK` | `{ "content": "# Bisabolol…" }` — Markdown leaflet |
| `422 Unprocessable Entity` | Neither KB nor leaflet store returned hits above the similarity threshold; the topic is not covered |
| `502 Bad Gateway` | LLM call failed after one retry |

---

## Configuration

All settings live under the `Leaflet` section in `appsettings.json`:

| Key | Default | Description |
|-----|---------|-------------|
| `OneDriveFolderMappings` | *(required)* | List of OneDrive inboxes to poll. Each entry has DriveId, InboxPath, ArchivedPath, and DocumentType = Leaflet. |
| `ChunkSize` | `800` | Target word count per chunk |
| `ChunkOverlap` | `80` | Word overlap between consecutive chunks |
| `KbTopK` | `8` | Max Knowledge Base chunks retrieved per query |
| `LeafletTopK` | `5` | Max Leaflet Store chunks retrieved per query |
| `MinSimilarityScore` | `0.55` | Minimum cosine similarity (0–1) to include a chunk |
| `ChatModel` | `claude-sonnet-4-6` | Claude model for both generation stages |
| `ChatMaxTokens` | `2048` | Max tokens for each LLM response |
| `EmbeddingModel` | `text-embedding-3-large` | OpenAI embedding model (1536 dimensions) |
| `IngestionCronExpression` | `*/15 * * * *` | How often the ingestion job polls OneDrive |
| `ShortWordTarget` | `200` | Target word count for Short leaflets |
| `MediumWordTarget` | `400` | Target word count for Medium leaflets |
| `LongWordTarget` | `700` | Target word count for Long leaflets |

System prompts (`Stage1SystemPrompt`, `Stage2SystemPrompt`) are also configurable in options and support template variables: `{topic}`, `{audience}`, `{length}`, `{kbContext}`, `{leafletContext}`, `{coldStart}`.

---

## Database Schema

### `LeafletDocuments`

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `uuid` | Primary key |
| `Filename` | `text` | Original filename |
| `SourcePath` | `text` | OneDrive path at time of ingestion |
| `ContentType` | `text` | MIME type |
| `ContentHash` | `text` | SHA-256 of raw file bytes (dedup key) |
| `IngestedAt` | `timestamptz` | When the document was indexed |
| `WordCount` | `int` | Total word count of extracted text |

### `LeafletChunks`

| Column | Type | Description |
|--------|------|-------------|
| `Id` | `uuid` | Primary key |
| `DocumentId` | `uuid` | FK → `LeafletDocuments` |
| `ChunkIndex` | `int` | Zero-based position within the document |
| `Content` | `text` | Raw chunk text |
| `WordCount` | `int` | Word count of this chunk |
| `Embedding` | `vector(1536)` | Dense embedding from text-embedding-3-small |

Similarity search uses cosine distance (`<=>`). An HNSW index on `Embedding` is recommended for production performance.

---

## Key File Locations

| Layer | Path |
|-------|------|
| Domain entities | `backend/src/Anela.Heblo.Domain/Features/Leaflet/` |
| Application handlers | `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/` |
| Indexing services | `backend/src/Anela.Heblo.Application/Features/Leaflet/Services/` |
| Ingestion job | `backend/src/Anela.Heblo.Application/Features/Leaflet/Infrastructure/Jobs/LeafletIngestionJob.cs` |
| Configuration | `backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletOptions.cs` |
| Module registration | `backend/src/Anela.Heblo.Application/Features/Leaflet/LeafletModule.cs` |
| Persistence | `backend/src/Anela.Heblo.Persistence/Features/Leaflet/` |
| Controller | `backend/src/Anela.Heblo.API/Controllers/LeafletController.cs` |
| Frontend page | `frontend/src/features/leaflet-generator/LeafletGeneratorPage.tsx` |
| E2E tests | `frontend/test/e2e/marketing/leaflet-generator.spec.ts` |
| Backend tests | `backend/test/Anela.Heblo.Tests/Features/Leaflet/` |

---

## Monitoring & Operations

- **Ingestion job** is visible in the Hangfire dashboard under the name `leaflet-ingestion`. It can be disabled/enabled without a redeploy via the recurring jobs management UI.
- **Cold starts** are logged at `Warning` level: `Leaflet cold-start: zero leaflet style references for topic '{Topic}'`
- **Duplicates** are logged at `Information` level and the file is still archived to prevent infinite reprocessing.
- **Failed files** increment a `failed` counter logged at `Error` level; the job continues processing remaining files.
