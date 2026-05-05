# Leaflet Generator

## Problem Statement / Motivation

Anela Heblo has accumulated two valuable sources of content:
1. A customer chat Knowledge Base (RAG system with pgvector)
2. A library of previous marketing leaflets (PDF and plain text)

Currently there is no way to leverage these assets to quickly produce new marketing copy. The goal is an AI-powered assistant that accepts a topic, target audience, and desired length and returns a polished markdown marketing leaflet — accessible via a UI page, REST API, and MCP tool.

---

## Proposed Solution

### Architecture

Two-stage RAG generation using two independent pgvector stores:

- **Stage 1 (Content draft):** Search the existing Knowledge Base for facts → Claude produces a structured content outline grounded in product/ingredient data
- **Stage 2 (Style rewrite):** Search a new Leaflet store for style examples → Claude rewrites the draft as a polished leaflet in the tone of previous leaflets

The Leaflet store mirrors the existing KnowledgeBase tables (`LeafletDocuments` + `LeafletChunks`) with its own HNSW cosine index and OneDrive ingestion job. Leaflets are ingested from `/Leaflets/Inbox` on OneDrive, supporting PDF (existing `PdfTextExtractor`) and plain text (new `PlainTextExtractor`).

### Input

```
topic:    string (required, max 200 chars)
audience: EndConsumer | B2B
length:   Short (~200 words) | Medium (~400 words) | Long (~700 words)
```

### Output

Markdown string — rendered in the UI with a copy-to-clipboard button.

### Key Technical Details

- Separate `LeafletDocuments` + `LeafletChunks` DB tables (avoids pgvector HNSW post-filtering degradation when mixing document types)
- Chunk size 800 words / 80-word overlap (larger than KB's 512 — leaflets are structured prose)
- `GenerateLeafletHandler` orchestrates both stages sequentially, reusing `IEmbeddingService` (OpenAI), `IChatClient` (Claude via `Microsoft.Extensions.AI`), `IKnowledgeBaseRepository`, and new `ILeafletRepository`
- New `PlainTextExtractor` implementing `IDocumentTextExtractor` (reads UTF-8, no new NuGet packages)
- `LeafletOptions` typed config, registered via `LeafletModule`
- MCP tool: `GenerateLeaflet` — same thin-wrapper pattern as `AskKnowledgeBase`

---

## Acceptance Criteria

- [ ] `LeafletDocuments` and `LeafletChunks` DB tables created via EF Core migration
- [ ] `PlainTextExtractor` handles `.txt` and `.md` files
- [ ] `LeafletIngestionJob` ingests PDF and plain text from `/Leaflets/Inbox` OneDrive folder, archives to `/Leaflets/Archived`
- [ ] `GenerateLeafletHandler` performs two-stage generation (KB facts → leaflet style rewrite)
- [ ] `POST /api/leaflet/generate` returns `{ content: "..." }` markdown
- [ ] `GenerateLeaflet` MCP tool registered and functional
- [ ] UI page `/leaflet-generator` with topic input, audience dropdown, length selector, rendered result, copy button
- [ ] Unit tests: `PlainTextExtractorTests`, `GenerateLeafletHandlerTests`, `IndexLeafletHandlerTests`, `LeafletToolsTests`
- [ ] `dotnet format` passes
- [ ] `dotnet build` passes (including OpenAPI client generation)
- [ ] `npm run build` passes

---

## File Map

### Backend — New Files

```
backend/src/Anela.Heblo.Domain/Features/Leaflet/
  LeafletDocument.cs           — Domain entity (mirrors KnowledgeBaseDocument)
  LeafletChunk.cs              — Domain entity (mirrors KnowledgeBaseChunk)
  ILeafletRepository.cs        — Repository interface

backend/src/Anela.Heblo.Application/Features/Leaflet/
  LeafletModule.cs             — DI registration (options, repository, job, handler, extractor)
  LeafletOptions.cs            — Typed config (OneDrive paths, chunk size, model, topK)
  Services/
    PlainTextExtractor.cs      — IDocumentTextExtractor for text/plain and text/markdown
  Jobs/
    LeafletIngestionJob.cs     — Hangfire job, polls OneDrive /Leaflets/Inbox
  UseCases/GenerateLeaflet/
    GenerateLeafletRequest.cs  — topic, audience (enum), length (enum)
    GenerateLeafletResponse.cs — { Content: string }
    GenerateLeafletHandler.cs  — two-stage orchestration
  UseCases/IndexLeaflet/
    IndexLeafletRequest.cs     — filename, sourcePath, content (bytes), contentType
    IndexLeafletResponse.cs    — documentId, status, wasDuplicate
    IndexLeafletHandler.cs     — mirrors IndexDocumentHandler

backend/src/Anela.Heblo.Persistence/Features/Leaflet/
  LeafletRepository.cs                 — EF Core + raw Npgsql for vector ops
  LeafletDocumentConfiguration.cs      — EF entity config
  LeafletChunkConfiguration.cs         — EF entity config (ignores Embedding)

backend/src/Anela.Heblo.API/
  Controllers/LeafletController.cs     — POST /api/leaflet/generate
  MCP/Tools/LeafletTools.cs            — GenerateLeaflet MCP tool
```

### Backend — Modified Files

```
backend/src/Anela.Heblo.API/Program.cs (or equivalent module registration)
  — Register LeafletModule

backend/src/Anela.Heblo.Persistence/PersistenceModule.cs (or DbContext)
  — Register LeafletRepository, add entity configurations to DbContext

appsettings.json
  — Add Leaflet config section
```

### DB Migration

```
backend/src/Anela.Heblo.Persistence/Migrations/
  <timestamp>_AddLeafletStore.cs       — creates LeafletDocuments, LeafletChunks, HNSW index
```

### Frontend — New Files

```
frontend/src/features/leaflet-generator/
  LeafletGeneratorPage.tsx     — page with form + result panel
  LeafletForm.tsx              — topic input, audience dropdown, length selector
  LeafletResult.tsx            — rendered markdown + copy button + regenerate button
```

### Frontend — Modified Files

```
frontend/src/app/routes.tsx (or sidebar nav)
  — Add /leaflet-generator route and nav entry
```

### Tests — New Files

```
backend/test/Anela.Heblo.Tests/Leaflet/
  Services/PlainTextExtractorTests.cs
  UseCases/GenerateLeafletHandlerTests.cs
  UseCases/IndexLeafletHandlerTests.cs
  MCP/Tools/LeafletToolsTests.cs
```

---

## Out of Scope

- Leaflet history / saved results
- Streaming generation
- Direct file upload via UI (use OneDrive Inbox folder)
- Word / PDF output
- Template extraction from leaflets
- Frontend E2E tests (Playwright) — added to nightly suite separately