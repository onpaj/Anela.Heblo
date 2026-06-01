# RAG Knowledge Base – Phase 6: Final Validation & Configuration

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Final format pass, Release build validation, architecture doc updates, and PR preparation.

**Current state (2026-03-06):**
- Branch: `feat/381-rag-knowledge-base`
- All 5 implementation phases complete, 1692 tests passing
- 12 commits on branch since diverging from main

**Recent commits on branch:**
```
c6004b2a feat: add KnowledgeBaseTools MCP tools with tests and register in McpModule
7daa91aa feat: add KnowledgeBaseController with /documents, /search, /ask endpoints
2baa5ca0 feat: add KnowledgeBaseIngestionJob Hangfire recurring job for OneDrive ingestion
06571095 feat: add IOneDriveService and Microsoft Graph implementation for OneDrive access
ecd847e4 feat: add KnowledgeBaseRepository with pgvector cosine similarity search
4dd52d54 feat: add IndexDocument use case with handler and tests
fef38aff feat: add AskQuestion use case with handler and tests
17525a8e feat: add SearchDocuments use case with handler and tests
4d51d39a feat: add IClaudeService and Anthropic implementation for Q&A generation
779c2c3d feat: add DocumentChunker with sliding window chunking and tests
d2e0211a feat: add IDocumentTextExtractor and PdfPig PDF implementation
b32174be feat: add IEmbeddingService and OpenAI text-embedding-3-small implementation
```

**Master plan:** `docs/plans/2026-03-02-rag-knowledge-base.md`, Task 21

---

## Task 1: Final format + build + test

**Step 1: Run dotnet format**

```bash
cd backend && dotnet format
```

If any files changed, stage and commit:

```bash
git add -A
git commit -m "style: apply dotnet format to KnowledgeBase feature"
```

**Step 2: Release build**

```bash
cd backend && dotnet build --configuration Release
```

Expected: 0 errors, 0 warnings.

**Step 3: Full test suite**

```bash
cd backend && dotnet test
```

Expected: 1692+ tests, all passing.

---

## Task 2: Update architecture docs

**File: `docs/architecture/application_infrastructure.md`**

Add to the PostgreSQL section:
- `pgvector` extension enabled (required for `KnowledgeBaseChunks.Embedding vector(1536)` column)

Add to the Azure App Settings / secrets section (values go in Azure Portal, NOT committed):
- `Anthropic__ApiKey` — tryAGI Anthropic SDK key for Claude Q&A
- `KnowledgeBase__OneDriveInboxPath` — OneDrive path to poll (default `/KnowledgeBase/Inbox`)
- `KnowledgeBase__OneDriveArchivedPath` — destination after ingestion (default `/KnowledgeBase/Archived`)
- `OpenAI__ApiKey` — already exists for catalog, also used for embeddings

**File: `docs/architecture/environments.md`**

Add new env variables section or extend existing one:
- `Anthropic__ApiKey` (new)
- `KnowledgeBase__OneDriveInboxPath` (new, optional — has default)
- `KnowledgeBase__OneDriveArchivedPath` (new, optional — has default)

**Step: Commit docs**

```bash
git add docs/architecture/
git commit -m "docs: update architecture docs for KnowledgeBase RAG feature"
```

---

## Task 3: Create PR

**Step 1: Push branch**

```bash
git push origin feat/381-rag-knowledge-base
```

**Step 2: Create PR**

Use `gh pr create` targeting `main`. Suggested title and body:

```
Title: feat: add RAG Knowledge Base (#381)

## Summary
- Adds a full RAG (Retrieval-Augmented Generation) pipeline as a new vertical slice
- Ingests PDFs from OneDrive via Hangfire job (every 15 min) → PdfPig extraction → sliding-window chunking → OpenAI text-embedding-3-small → pgvector storage
- Exposes semantic search and AI-grounded Q&A via REST API (`/api/knowledge-base`) and two new MCP tools (`SearchKnowledgeBase`, `AskKnowledgeBase`)
- Content-hash deduplication prevents re-embedding unchanged files that are moved/renamed

## New endpoints
- `GET /api/knowledge-base/documents` — list indexed documents
- `POST /api/knowledge-base/search` — semantic similarity search
- `POST /api/knowledge-base/ask` — AI Q&A grounded in documents

## New MCP tools
- `SearchKnowledgeBase(query, topK)` — returns ranked chunks
- `AskKnowledgeBase(question, topK)` — returns prose answer + sources

## Implementation notes
- pgvector cosine search uses raw SQL (`<=>` operator) — Pgvector.EntityFrameworkCore incompatible with Npgsql 8.0.4
- OneDrive access uses `ITokenAcquisition` + raw HttpClient (codebase convention, not `GraphServiceClient` directly)
- `MockOneDriveService` registered in mock/bypass-jwt auth mode to keep tests green
- Anthropic SDK is `tryAGI/Anthropic` v1.0.0 (not the official Anthropic.SDK)

## Before deploying to Azure
- Enable `vector` extension on PostgreSQL Flexible Server (azure.extensions parameter)
- Run EF Core migration: `dotnet ef database update`
- Add App Settings: `Anthropic__ApiKey`, optionally `KnowledgeBase__OneDriveInboxPath` / `KnowledgeBase__OneDriveArchivedPath`
- Grant Microsoft Graph `Files.ReadWrite.All` or `Sites.ReadWrite.All` on Entra ID app registration

## Tests
- 1692 total, all passing
- New: DocumentChunkerTests (4), SearchDocumentsHandlerTests, AskQuestionHandlerTests, IndexDocumentHandlerTests, KnowledgeBaseToolsTests (3)

Closes #381
```

---

## Azure Deployment Checklist (do manually in Azure Portal)

1. **Enable pgvector** on PostgreSQL Flexible Server:
   - Server parameters → `azure.extensions` → add `vector` → Save & restart

2. **Apply EF Core migration**:
   ```bash
   dotnet ef database update --project src/Anela.Heblo.Persistence --startup-project src/Anela.Heblo.API
   ```

3. **Add App Settings** (Azure Portal, never commit):
   - `Anthropic__ApiKey`
   - `KnowledgeBase__OneDriveInboxPath` (if different from default `/KnowledgeBase/Inbox`)
   - `KnowledgeBase__OneDriveArchivedPath` (if different from default `/KnowledgeBase/Archived`)

4. **Grant Graph permissions** on Entra ID app registration:
   - `Files.ReadWrite.All` (user OneDrive) OR `Sites.ReadWrite.All` (SharePoint)
   - Grant admin consent

5. **Create OneDrive folders**:
   - `/KnowledgeBase/Inbox/` — drop PDFs here to trigger ingestion
   - `/KnowledgeBase/Archived/` — processed files moved here automatically

---

## Key implementation deviations from original plan

| Original plan | Actual implementation | Reason |
|---|---|---|
| `Pgvector.EntityFrameworkCore` extension methods | Raw SQL `<=>` operator | Npgsql 8.0.4 incompatible with extension (requires ≥ 9.0.1) |
| Direct `GraphServiceClient` injection | `ITokenAcquisition` + raw `HttpClient` | Matches codebase convention; `GraphServiceClient` not directly injectable |
| `GraphOneDriveService` only | + `MockOneDriveService` for mock auth | Prevents DI failures in test environment |
| `AnthropicClient` / `MessageCreateParams` | `AnthropicApi()` / `CreateMessageAsync()` | Package is `tryAGI/Anthropic` v1.0.0, not official `Anthropic.SDK` |
| EF Core LINQ for `AddChunksAsync` | Raw `NpgsqlCommand` INSERT | Same Npgsql version constraint |
