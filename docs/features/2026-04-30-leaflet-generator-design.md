# Leaflet Generator — Design Spec

**Date:** 2026-04-30
**Status:** Approved

---

## Overview

A marketing leaflet assistant that accepts a topic, target audience, and desired length and returns a polished markdown leaflet. Generation draws from two sources: the existing customer chat Knowledge Base (facts, product details) and a new Leaflet store (style, tone, structure learned from previous leaflets).

Accessible via:
- **UI page** — `/leaflet-generator` in the Heblo app
- **REST API** — `POST /api/leaflet/generate`
- **MCP tool** — `GenerateLeaflet`

---

## Storage & Ingestion

### New DB Tables

Two new tables mirroring the Knowledge Base pattern:

**`LeafletDocuments`**

| Column | Type | Constraints |
|---|---|---|
| `Id` | `uuid` | PK |
| `Filename` | `varchar(500)` | NOT NULL |
| `SourcePath` | `varchar(1000)` | NOT NULL, UNIQUE |
| `ContentType` | `varchar(100)` | NOT NULL |
| `ContentHash` | `varchar(64)` | NOT NULL, UNIQUE |
| `Status` | `varchar(50)` | NOT NULL, indexed |
| `CreatedAt` | `timestamp` | NOT NULL |
| `IndexedAt` | `timestamp` | nullable |

**`LeafletChunks`**

| Column | Type | Constraints |
|---|---|---|
| `Id` | `uuid` | PK |
| `DocumentId` | `uuid` | FK → LeafletDocuments (CASCADE DELETE) |
| `ChunkIndex` | `int` | NOT NULL |
| `Content` | `text` | NOT NULL |
| `Embedding` | `vector(1536)` | managed via raw Npgsql (excluded from EF model) |

**Indexes:** HNSW cosine index on `LeafletChunks.Embedding` (`m=16, ef_construction=64`).

### Ingestion

Hangfire recurring job polling OneDrive `/Leaflets/Inbox` every 15 minutes. Same pattern as `KnowledgeBaseIngestionJob`. On success, moves files to `/Leaflets/Archived`.

**Supported formats:**
- `.pdf` — existing `PdfTextExtractor`
- `.txt` / `.md` — new `PlainTextExtractor` (implements `IDocumentTextExtractor`, reads raw bytes as UTF-8 string)

Both extractors are registered as a list in `LeafletModule` (strategy pattern — `CanHandle()` determines which is used). This is independent of the KB extractor registration.

**Chunking:** larger chunks than KB — 800 words with 80-word overlap — suited to structured prose.

**Deduplication:** SHA-256 content hash, same logic as KB ingestion.

---

## Generation Pipeline

### Input

```
topic:    string (required, max 200 chars)
audience: EndConsumer | B2B
length:   Short (~200 words) | Medium (~400 words) | Long (~700 words)
```

### Two-Stage Flow

**Stage 1 — Content draft**
1. Embed `topic` via `IEmbeddingService`
2. Search `KnowledgeBaseChunks` — top-5 most similar chunks
3. Call Claude (`IClaudeService`) with prompt:
   > "You are a cosmetics product expert. Based on the context below, extract the key facts, benefits, and claims relevant to the topic '{topic}' for a '{audience}' audience. Produce a structured content draft — facts and substance only, no marketing language yet."
4. Result: raw content draft (text)

**Stage 2 — Style rewrite**
1. Search `LeafletChunks` — top-3 most similar chunks (style/tone/structure examples)
2. Call Claude with Stage 1 draft + leaflet examples + prompt:
   > "You are a marketing copywriter for a cosmetics company. Rewrite the following content draft as a polished marketing leaflet in the style of the examples provided. Audience: {audience}. Target length: {length}. Output markdown."
3. Result: final markdown leaflet

### Handler

`GenerateLeafletHandler` — single MediatR handler, sequential stages. Injects `IEmbeddingService`, `IClaudeService`, `ILeafletRepository`, `IKnowledgeBaseRepository`.

---

## API

### `POST /api/leaflet/generate`

Authentication: `[Authorize(Roles = "marketing_writer")]` — Microsoft Entra ID JWT. Requires the `marketing_writer` role for generation access.

**Request:**
```json
{
  "topic": "bisabolol",
  "audience": "EndConsumer",
  "length": "Medium"
}
```

**Response:**
```json
{
  "content": "## Bisabolol — Gentle Care for Sensitive Skin\n\n..."
}
```

### Ingestion endpoints (admin)

Leaflet ingestion is driven by the Hangfire job. No manual upload endpoint in v1 — files are dropped into the OneDrive `/Leaflets/Inbox` folder directly.

---

## MCP Tool

**`GenerateLeaflet`**

```
Description: Generate a marketing leaflet for a given topic using existing leaflets for style
             and the knowledge base for factual content.
Parameters:
  topic     (string)             — Product or ingredient topic
  audience  (string, default EndConsumer) — Target audience: EndConsumer or B2B
  length    (string, default Medium)      — Output length: Short, Medium, or Long
Returns: Markdown string
```

---

## Frontend

**Route:** `/leaflet-generator`
**Navigation:** added to sidebar

**Form:**
- Topic: text input, required, max 200 chars
- Audience: dropdown — `End Consumer` / `B2B Buyer`
- Length: segmented control — `Short` / `Medium` / `Long`
- Generate button — disabled while loading

**Result panel (shown after generation):**
- Rendered markdown
- Copy to clipboard button
- Regenerate button (resubmits same inputs)

**State:** local React state only — no persistence in v1.
**Error handling:** inline error message below form on API failure.

---

## Configuration

New `Leaflet` section in `appsettings.json`:

```json
{
  "Leaflet": {
    "OneDriveInboxPath": "/Leaflets/Inbox",
    "OneDriveArchivedPath": "/Leaflets/Archived",
    "ChunkSize": 800,
    "ChunkOverlapTokens": 80,
    "MaxRetrievedLeafletChunks": 3,
    "MaxRetrievedKbChunks": 5,
    "ClaudeModel": "claude-sonnet-4-6",
    "ClaudeMaxTokens": 2048
  }
}
```

Reuses existing `OpenAI.ApiKey` and `Anthropic.ApiKey`. No new secrets.

---

## File Structure

```
backend/src/
├── Anela.Heblo.Domain/Features/Leaflet/
│   ├── LeafletDocument.cs
│   ├── LeafletChunk.cs
│   └── ILeafletRepository.cs
│
├── Anela.Heblo.Application/Features/Leaflet/
│   ├── LeafletModule.cs
│   ├── LeafletOptions.cs
│   ├── Services/
│   │   └── PlainTextExtractor.cs
│   ├── Jobs/
│   │   └── LeafletIngestionJob.cs
│   └── UseCases/
│       ├── GenerateLeaflet/
│       │   ├── GenerateLeafletRequest.cs
│       │   └── GenerateLeafletHandler.cs
│       └── IndexLeaflet/
│           ├── IndexLeafletRequest.cs
│           └── IndexLeafletHandler.cs
│
├── Anela.Heblo.Persistence/Features/Leaflet/
│   ├── LeafletRepository.cs
│   ├── LeafletDocumentConfiguration.cs
│   └── LeafletChunkConfiguration.cs
│
└── Anela.Heblo.API/
    ├── Controllers/LeafletController.cs
    └── MCP/Tools/LeafletTools.cs

frontend/src/
└── features/leaflet-generator/
    ├── LeafletGeneratorPage.tsx
    ├── LeafletForm.tsx
    └── LeafletResult.tsx

backend/test/Anela.Heblo.Tests/Leaflet/
├── Services/PlainTextExtractorTests.cs
├── UseCases/GenerateLeafletHandlerTests.cs
├── UseCases/IndexLeafletHandlerTests.cs
└── MCP/Tools/LeafletToolsTests.cs
```

---

## Database Migration

One new migration: creates `LeafletDocuments` and `LeafletChunks` tables, enables vector extension (already enabled), adds `Embedding vector(1536)` column via raw SQL, creates HNSW index.

---

## Dependencies

No new NuGet packages. All required infrastructure already exists:
- `OpenAI` — embeddings
- `Anthropic` — generation
- `PdfPig` — PDF extraction
- `Pgvector` + `Npgsql` — vector storage

---

## Testing

| File | Coverage |
|---|---|
| `PlainTextExtractorTests` | UTF-8 decode, empty file, large file |
| `IndexLeafletHandlerTests` | Stores document + chunks, throws for unsupported MIME |
| `GenerateLeafletHandlerTests` | Both stages called, results combined, audience/length forwarded to prompt |
| `LeafletToolsTests` | Parameter mapping, JSON output, `McpException` on error |

---

## Out of Scope (v1)

- Leaflet history / saved results
- Streaming generation
- Direct file upload via UI (use OneDrive inbox instead)
- Word/PDF output format
- Template extraction from leaflets
