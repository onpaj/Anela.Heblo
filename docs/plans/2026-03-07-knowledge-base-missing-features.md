# Knowledge Base — Missing Features Implementation Plan

## Document Information

- **Date**: 2026-03-07
- **Status**: Ready for Implementation
- **Related Documentation**:
  - `/docs/features/knowledge-base-rag.md` — full spec including missing features list
  - `/docs/plans/2026-03-07-knowledge-base-bug-fixes.md` — bug fixes plan (implement first)
  - `/docs/design/layout_definition.md` — page layout standards
  - `/docs/architecture/filesystem.md` — directory organization

## Prerequisites

Complete **all tasks in `2026-03-07-knowledge-base-bug-fixes.md`** before starting this plan. In particular, `GetDocumentsHandler` (bug fix Task 2.2) is required before the frontend hooks will work correctly.

## Overview

Implements 5 missing feature areas for the Knowledge Base RAG system. Ordered by priority.

**Estimated scope:** ~600 lines backend, ~900 lines frontend, across ~25 new files.

---

## Feature 1: Frontend UI (High Priority)

### Overview

Add a Knowledge Base page to the React frontend with three tabs: Documents (list of indexed docs), Search (semantic search UI), and Ask (Q&A interface). The backend endpoints exist; this feature wires them to the UI.

### Task 1.1: Regenerate OpenAPI client

The `KnowledgeBaseController` was added in the backend but the TypeScript client has not been regenerated.

**Action**: Run API client generation per `/docs/development/api-client-generation.md`:

```bash
cd backend && dotnet build
cd frontend && npm run generate-api-client
```

Verify that `frontend/src/api/generated/api-client.ts` contains:
- `knowledgeBase_GetDocuments()`
- `knowledgeBase_Search(body: SearchDocumentsRequest)`
- `knowledgeBase_Ask(body: AskQuestionRequest)`

### Task 1.2: Create API hooks

**New file**: `frontend/src/api/hooks/useKnowledgeBase.ts`

```typescript
import { useQuery, useMutation } from '@tanstack/react-query';
import { getAuthenticatedApiClient } from '../client';

// ---- Types (mirror backend DTOs) ----

export interface DocumentSummary {
  id: string;
  filename: string;
  status: 'processing' | 'indexed' | 'failed';
  contentType: string;
  createdAt: string;
  indexedAt?: string;
}

export interface ChunkResult {
  chunkId: string;
  documentId: string;
  content: string;
  score: number;
  sourceFilename: string;
  sourcePath: string;
}

export interface SearchResponse {
  chunks: ChunkResult[];
}

export interface SourceReference {
  documentId: string;
  filename: string;
  excerpt: string;
  score: number;
}

export interface AskResponse {
  answer: string;
  sources: SourceReference[];
}

// ---- Query key factory ----

export const knowledgeBaseKeys = {
  documents: ['knowledgeBase', 'documents'] as const,
};

// ---- Hooks ----

export const useKnowledgeBaseDocumentsQuery = () => {
  return useQuery({
    queryKey: knowledgeBaseKeys.documents,
    queryFn: async (): Promise<DocumentSummary[]> => {
      const apiClient = await getAuthenticatedApiClient();
      const relativeUrl = '/api/knowledgebase/documents';
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, { method: 'GET' });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return response.json();
    },
    staleTime: 30 * 1000,
  });
};

export const useKnowledgeBaseSearchMutation = () => {
  return useMutation({
    mutationFn: async ({
      query,
      topK = 5,
    }: {
      query: string;
      topK?: number;
    }): Promise<SearchResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      const relativeUrl = '/api/knowledgebase/search';
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ query, topK }),
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return response.json();
    },
  });
};

export const useKnowledgeBaseAskMutation = () => {
  return useMutation({
    mutationFn: async ({
      question,
      topK = 5,
    }: {
      question: string;
      topK?: number;
    }): Promise<AskResponse> => {
      const apiClient = await getAuthenticatedApiClient();
      const relativeUrl = '/api/knowledgebase/ask';
      const fullUrl = `${(apiClient as any).baseUrl}${relativeUrl}`;
      const response = await (apiClient as any).http.fetch(fullUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ question, topK }),
      });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return response.json();
    },
  });
};
```

**Verification**:
- [ ] `useKnowledgeBaseDocumentsQuery` returns `DocumentSummary[]`
- [ ] `useKnowledgeBaseSearchMutation` accepts `{ query, topK? }` and returns `SearchResponse`
- [ ] `useKnowledgeBaseAskMutation` accepts `{ question, topK? }` and returns `AskResponse`
- [ ] All hooks use absolute URLs with `(apiClient as any).baseUrl` prefix

### Task 1.3: Create KnowledgeBasePage component

**New file**: `frontend/src/pages/KnowledgeBasePage.tsx`

Page layout: header + three tabs (Documents / Search / Ask). Each tab is a separate component.

```tsx
import React, { useState } from 'react';
import { Database, Search, MessageSquare } from 'lucide-react';
import KnowledgeBaseDocumentsTab from '../components/knowledge-base/KnowledgeBaseDocumentsTab';
import KnowledgeBaseSearchTab from '../components/knowledge-base/KnowledgeBaseSearchTab';
import KnowledgeBaseAskTab from '../components/knowledge-base/KnowledgeBaseAskTab';

type Tab = 'documents' | 'search' | 'ask';

const KnowledgeBasePage: React.FC = () => {
  const [activeTab, setActiveTab] = useState<Tab>('documents');

  return (
    <div className="flex flex-col h-full p-6">
      <div className="mb-6">
        <h1 className="text-2xl font-semibold text-gray-900 flex items-center gap-2">
          <Database size={24} />
          Znalostní báze
        </h1>
        <p className="text-sm text-gray-500 mt-1">
          Firemní dokumenty indexované pro AI vyhledávání
        </p>
      </div>

      {/* Tab navigation */}
      <div className="flex gap-1 mb-6 border-b border-gray-200">
        {([
          { id: 'documents' as Tab, label: 'Dokumenty', icon: Database },
          { id: 'search' as Tab, label: 'Vyhledávání', icon: Search },
          { id: 'ask' as Tab, label: 'Dotaz AI', icon: MessageSquare },
        ] as const).map(({ id, label, icon: Icon }) => (
          <button
            key={id}
            onClick={() => setActiveTab(id)}
            className={`flex items-center gap-2 px-4 py-2 text-sm font-medium border-b-2 -mb-px transition-colors ${
              activeTab === id
                ? 'border-blue-600 text-blue-600'
                : 'border-transparent text-gray-500 hover:text-gray-700'
            }`}
          >
            <Icon size={16} />
            {label}
          </button>
        ))}
      </div>

      <div className="flex-1 overflow-auto">
        {activeTab === 'documents' && <KnowledgeBaseDocumentsTab />}
        {activeTab === 'search' && <KnowledgeBaseSearchTab />}
        {activeTab === 'ask' && <KnowledgeBaseAskTab />}
      </div>
    </div>
  );
};

export default KnowledgeBasePage;
```

### Task 1.4: Create tab components

Create directory: `frontend/src/components/knowledge-base/`

---

**New file**: `frontend/src/components/knowledge-base/KnowledgeBaseDocumentsTab.tsx`

Shows a table of indexed documents with status badges. Status color mapping:
- `indexed` → green badge
- `processing` → yellow badge
- `failed` → red badge

Key elements:
- Table columns: Filename, Typ, Status, Indexováno
- Empty state when no documents
- Error state with retry button
- Loading skeleton (3 rows)
- Date formatted via `toLocaleString('cs-CZ', ...)`

Uses: `useKnowledgeBaseDocumentsQuery()`

---

**New file**: `frontend/src/components/knowledge-base/KnowledgeBaseSearchTab.tsx`

Semantic search UI.

Key elements:
- Search input (text) + TopK number input (1–20, default 5) + Search button
- Results list: each chunk shows content excerpt, source filename, similarity score (percentage badge)
- Empty state before first search
- Loading state during search (spinner)
- Error display

Uses: `useKnowledgeBaseSearchMutation()`

---

**New file**: `frontend/src/components/knowledge-base/KnowledgeBaseAskTab.tsx`

Q&A interface.

Key elements:
- Textarea for question input + TopK input + Submit button
- Answer displayed as prose in a highlighted box
- Sources accordion (collapsed by default, shows excerpt + score for each source)
- Loading state with "AI generuje odpověď..." message
- Error display

Uses: `useKnowledgeBaseAskMutation()`

### Task 1.5: Register route and navigation

**File**: `frontend/src/App.tsx` (or wherever routes are defined — check existing routing setup)

Add route:
```tsx
import KnowledgeBasePage from './pages/KnowledgeBasePage';
// ...
<Route path="/knowledge-base" element={<KnowledgeBasePage />} />
```

**File**: Navigation component (sidebar) — add menu item:
```tsx
{ path: '/knowledge-base', label: 'Znalostní báze', icon: Database }
```

Check existing nav items file for exact format (e.g. `frontend/src/components/Sidebar.tsx` or similar).

### Task 1.6: Write unit tests for hooks

**New file**: `frontend/src/api/hooks/__tests__/useKnowledgeBase.test.ts`

Test cases:
- `useKnowledgeBaseDocumentsQuery` — mocked fetch returns documents array
- `useKnowledgeBaseSearchMutation` — sends correct POST body, returns chunks
- `useKnowledgeBaseAskMutation` — sends correct POST body, returns answer + sources

Follow pattern from `frontend/src/api/hooks/__tests__/useInventory.test.tsx`.

**Verification**:
- [ ] `npm run build` passes (no TypeScript errors)
- [ ] `npm test -- useKnowledgeBase` passes
- [ ] `/knowledge-base` route renders without errors in local dev
- [ ] Documents tab loads data, shows status badges
- [ ] Search tab sends POST and renders results
- [ ] Ask tab renders prose answer and sources

---

## Feature 2: Document Delete (Medium Priority)

Allows removing a document and all its chunks from the knowledge base.

### Task 2.1: Add DeleteDocumentAsync to repository interface

**File**: `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/IKnowledgeBaseRepository.cs`

Add method:
```csharp
Task DeleteDocumentAsync(Guid documentId, CancellationToken ct = default);
```

> Note: If already added as part of bug fix Task 3.4, skip this step.

### Task 2.2: Implement in KnowledgeBaseRepository

**File**: `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs`

```csharp
public async Task DeleteDocumentAsync(Guid documentId, CancellationToken ct = default)
{
    await _context.KnowledgeBaseDocuments
        .Where(d => d.Id == documentId)
        .ExecuteDeleteAsync(ct);
    // KnowledgeBaseChunks are deleted via CASCADE in DB
}
```

> Note: Cascade delete is already configured in `KnowledgeBaseChunkConfiguration` (`OnDelete(DeleteBehavior.Cascade)`). The HNSW index on chunks is maintained automatically by PostgreSQL.

### Task 2.3: Create DeleteDocument use case

**New file**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/DeleteDocument/DeleteDocumentRequest.cs`

```csharp
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.DeleteDocument;

public class DeleteDocumentRequest : IRequest
{
    public Guid DocumentId { get; set; }
}
```

**New file**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/DeleteDocument/DeleteDocumentHandler.cs`

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.DeleteDocument;

public class DeleteDocumentHandler : IRequestHandler<DeleteDocumentRequest>
{
    private readonly IKnowledgeBaseRepository _repository;
    private readonly ILogger<DeleteDocumentHandler> _logger;

    public DeleteDocumentHandler(IKnowledgeBaseRepository repository, ILogger<DeleteDocumentHandler> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task Handle(DeleteDocumentRequest request, CancellationToken cancellationToken)
    {
        _logger.LogInformation("Deleting knowledge base document {DocumentId}", request.DocumentId);
        await _repository.DeleteDocumentAsync(request.DocumentId, cancellationToken);
        _logger.LogInformation("Document {DocumentId} deleted", request.DocumentId);
    }
}
```

### Task 2.4: Add DELETE endpoint to controller

**File**: `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs`

```csharp
[HttpDelete("documents/{id:guid}")]
public async Task<IActionResult> DeleteDocument(Guid id, CancellationToken ct)
{
    await _mediator.Send(new DeleteDocumentRequest { DocumentId = id }, ct);
    return NoContent();
}
```

### Task 2.5: Add Delete button to frontend Documents tab

**File**: `frontend/src/components/knowledge-base/KnowledgeBaseDocumentsTab.tsx`

Add:
- Delete icon button in each table row
- Confirmation dialog (reuse `ConfirmTriggerJobDialog` or create `ConfirmDeleteDocumentDialog`)
- `useDeleteKnowledgeBaseDocumentMutation` hook that calls `DELETE /api/knowledgebase/documents/{id}`
- On success: invalidate `knowledgeBaseKeys.documents` query

**New hook** in `frontend/src/api/hooks/useKnowledgeBase.ts`:
```typescript
export const useDeleteKnowledgeBaseDocumentMutation = () => {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: async (documentId: string): Promise<void> => {
      const apiClient = await getAuthenticatedApiClient();
      const fullUrl = `${(apiClient as any).baseUrl}/api/knowledgebase/documents/${documentId}`;
      const response = await (apiClient as any).http.fetch(fullUrl, { method: 'DELETE' });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
    },
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: knowledgeBaseKeys.documents });
    },
  });
};
```

**Verification**:
- [ ] `DELETE /api/knowledgebase/documents/{id}` returns 204
- [ ] Chunks are deleted (verify via DB or GET documents)
- [ ] Frontend shows confirmation dialog before deleting
- [ ] Documents list refreshes after delete

---

## Feature 3: Additional Document Formats (Medium Priority)

Adds support for `.docx` (Word) and `.txt` files alongside the existing PDF support.

### Task 3.1: Refactor extractor registration to support multiple extractors

**Problem**: Currently `IDocumentTextExtractor` is registered as a single service. To support multiple formats, `IndexDocumentHandler` needs to pick the right extractor.

**Step 1 — Update DI registration in `KnowledgeBaseModule.cs`**:

Replace:
```csharp
services.AddScoped<IDocumentTextExtractor, PdfTextExtractor>();
```
With:
```csharp
services.AddScoped<IDocumentTextExtractor, PdfTextExtractor>();
services.AddScoped<IDocumentTextExtractor, WordDocumentExtractor>();
services.AddScoped<IDocumentTextExtractor, PlainTextExtractor>();
```

**Step 2 — Update `IndexDocumentHandler`**:

Change constructor injection from single `IDocumentTextExtractor` to `IEnumerable<IDocumentTextExtractor>`:

```csharp
private readonly IEnumerable<IDocumentTextExtractor> _extractors;

public IndexDocumentHandler(
    IEnumerable<IDocumentTextExtractor> extractors,
    IEmbeddingService embeddingService,
    DocumentChunker chunker,
    IKnowledgeBaseRepository repository,
    ILogger<IndexDocumentHandler>? logger = null)
{
    _extractors = extractors;
    // ...
}
```

In `Handle`, replace `_extractor.CanHandle(...)` check with:
```csharp
var extractor = _extractors.FirstOrDefault(e => e.CanHandle(request.ContentType))
    ?? throw new NotSupportedException($"Content type '{request.ContentType}' is not supported.");
```

### Task 3.2: Add NuGet package for Word documents

**File**: `backend/src/Anela.Heblo.Application/Anela.Heblo.Application.csproj`

```xml
<PackageReference Include="DocumentFormat.OpenXml" Version="3.1.0" />
```

### Task 3.3: Implement WordDocumentExtractor

**New file**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/WordDocumentExtractor.cs`

```csharp
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Wordprocessing;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class WordDocumentExtractor : IDocumentTextExtractor
{
    private readonly ILogger<WordDocumentExtractor> _logger;

    public WordDocumentExtractor(ILogger<WordDocumentExtractor> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(string contentType) =>
        contentType.Equals("application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("application/msword", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(byte[] content, CancellationToken ct = default)
    {
        _logger.LogDebug("Extracting text from Word document ({Bytes} bytes)", content.Length);

        using var stream = new MemoryStream(content);
        using var doc = WordprocessingDocument.Open(stream, isEditable: false);

        var body = doc.MainDocumentPart?.Document?.Body;
        if (body is null)
            return Task.FromResult(string.Empty);

        var paragraphs = body.Descendants<Paragraph>()
            .Select(p => p.InnerText)
            .Where(t => !string.IsNullOrWhiteSpace(t));

        var text = string.Join("\n\n", paragraphs);

        _logger.LogDebug("Extracted {CharCount} characters from Word document", text.Length);
        return Task.FromResult(text);
    }
}
```

### Task 3.4: Implement PlainTextExtractor

**New file**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/PlainTextExtractor.cs`

```csharp
using System.Text;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.KnowledgeBase.Services;

public class PlainTextExtractor : IDocumentTextExtractor
{
    private readonly ILogger<PlainTextExtractor> _logger;

    public PlainTextExtractor(ILogger<PlainTextExtractor> logger)
    {
        _logger = logger;
    }

    public bool CanHandle(string contentType) =>
        contentType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
        || contentType.Equals("application/markdown", StringComparison.OrdinalIgnoreCase);

    public Task<string> ExtractTextAsync(byte[] content, CancellationToken ct = default)
    {
        _logger.LogDebug("Extracting text from plain text file ({Bytes} bytes)", content.Length);
        var text = Encoding.UTF8.GetString(content);
        return Task.FromResult(text);
    }
}
```

### Task 3.5: Update tests for multiple extractors

**File**: `backend/test/Anela.Heblo.Tests/KnowledgeBase/UseCases/IndexDocumentHandlerTests.cs`

Update constructor to inject `IEnumerable<IDocumentTextExtractor>`:
```csharp
private readonly Mock<IDocumentTextExtractor> _extractor = new();

// In handler construction:
var handler = new IndexDocumentHandler(
    new[] { _extractor.Object },   // IEnumerable
    _embedding.Object, _chunker, _repository.Object);
```

Add test: `Handle_ThrowsForUnsupportedContentType_WhenNoExtractorMatches`.

**Verification**:
- [ ] PDF ingestion still works (existing extractor unchanged)
- [ ] `.docx` file ingested correctly (Word extractor)
- [ ] `.txt` file ingested correctly (PlainText extractor)
- [ ] Unsupported type still throws `NotSupportedException`
- [ ] `dotnet test` all green

---

## Feature 4: Retry Logic for External APIs (Medium Priority)

Adds Polly-based retry for transient failures in `OpenAiEmbeddingService` and `AnthropicClaudeService`. The `Polly.Extensions` package is already present in the project.

### Task 4.1: Create retry pipeline helper in KnowledgeBaseModule

**File**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseModule.cs`

Add a named `HttpClient` with retry policy for external AI APIs:

```csharp
using Polly;
using Polly.Extensions.Http;

// In AddKnowledgeBaseModule:
services.AddHttpClient("ExternalAiApis")
    .AddPolicyHandler(
        HttpPolicyExtensions
            .HandleTransientHttpError()
            .WaitAndRetryAsync(
                retryCount: 3,
                sleepDurationProvider: attempt => TimeSpan.FromSeconds(Math.Pow(2, attempt)),
                onRetry: (outcome, timespan, attempt, ctx) =>
                {
                    // Logging via ILogger is not directly available here;
                    // use IServiceProvider or pass logger via context if needed
                }));
```

### Task 4.2: Use IHttpClientFactory in OpenAiEmbeddingService

**File**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/OpenAiEmbeddingService.cs`

> Note: `EmbeddingClient` from the `OpenAI` package wraps its own `HttpClient` and does not support injecting `IHttpClientFactory` directly. Instead, wrap the call in a Polly `ResiliencePipeline`.

Add dependency on `ResiliencePipelineProvider` or use `Pipeline.Create<HttpResponseMessage>(...)` inline. Simplest approach — inject `IConfiguration` and build a `ResiliencePipeline<float[]>` in constructor:

```csharp
private readonly ResiliencePipeline _retryPipeline;

public OpenAiEmbeddingService(...)
{
    _retryPipeline = new ResiliencePipelineBuilder()
        .AddRetry(new Polly.Retry.RetryStrategyOptions
        {
            MaxRetryAttempts = 3,
            Delay = TimeSpan.FromSeconds(2),
            BackoffType = DelayBackoffType.Exponential,
            ShouldHandle = new PredicateBuilder().Handle<HttpRequestException>()
        })
        .Build();
}

public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
{
    return await _retryPipeline.ExecuteAsync(async token =>
    {
        var result = await _client.GenerateEmbeddingAsync(text, cancellationToken: token);
        return result.Value.ToFloats().ToArray();
    }, ct);
}
```

### Task 4.3: Use ResiliencePipeline in AnthropicClaudeService

**File**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/AnthropicClaudeService.cs`

Apply same pattern as Task 4.2 — wrap `api.CreateMessageAsync(...)` call in a `ResiliencePipeline` with 3 retries, exponential backoff.

**Verification**:
- [ ] `dotnet build` passes
- [ ] Unit tests pass (ResiliencePipeline does not affect mock behavior)
- [ ] Manual test: temporarily set invalid API key, observe retry attempts in logs

---

## Feature 5: Integration Tests (Medium Priority)

Adds real integration tests using Testcontainers for PostgreSQL + pgvector.

### Task 5.1: Check existing integration test infrastructure

Before adding new tests, read the existing test project setup:
- `backend/test/Anela.Heblo.Tests/` — check for `IntegrationTestBase`, `WebApplicationFactory`, or `Testcontainers` usage

If no integration test infrastructure exists, set up Testcontainers.

### Task 5.2: Add Testcontainers NuGet packages

**File**: `backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`

```xml
<PackageReference Include="Testcontainers.PostgreSql" Version="3.10.0" />
```

### Task 5.3: Create KnowledgeBaseRepositoryIntegrationTests

**New file**: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration/KnowledgeBaseRepositoryIntegrationTests.cs`

Test class uses `IAsyncLifetime` to start/stop a PostgreSQL container with pgvector extension.

Test cases:
- `AddDocumentAndChunks_StoresAndRetrieves` — index a document, verify it can be found by hash
- `SearchSimilarAsync_ReturnsCorrectChunks` — store 3 chunks with different embeddings, search with a query embedding, verify top-1 result is the closest chunk
- `DeleteDocumentAsync_DeletesChunksViaCascade` — store document + chunks, delete document, verify chunks gone
- `GetDocumentByHashAsync_ReturnsDuplicate` — store document, search by hash, verify match

Setup:
```csharp
public class KnowledgeBaseRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        // Run migrations via EF Core
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), o => o.UseVector())
            .Options;
        await using var ctx = new ApplicationDbContext(options);
        await ctx.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();
}
```

### Task 5.4: Create PdfTextExtractor integration test

**New file**: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Integration/PdfTextExtractorTests.cs`

Use a small known PDF fixture (add to `backend/test/Anela.Heblo.Tests/Fixtures/sample.pdf`).

Test: extract text from fixture, verify expected strings are present.

### Task 5.5: Add DocumentChunker edge case tests

**File**: `backend/test/Anela.Heblo.Tests/KnowledgeBase/Services/DocumentChunkerTests.cs`

Add cases:
- `Chunk_CzechUnicodeText_ChunksCorrectly` — text with diacritics
- `Chunk_VeryLongDocument_ProducesExpectedChunkCount` — 10,000-word text, verify `ceil(words / step)` chunks
- `Chunk_SingleWord_ReturnsSingleChunk`
- `Chunk_ExactlyChunkSizeWords_ReturnsSingleChunk`
- `Chunk_ExactlyChunkSizePlusOneWord_ReturnsTwoChunks`

**Verification**:
- [ ] `dotnet test --filter "Integration"` passes with running containers
- [ ] `dotnet test` (all tests) passes
- [ ] Integration tests are tagged/categorized to exclude from fast CI runs if needed

---

## Implementation Order

```
Feature 1 (Frontend UI)
  └─ Task 1.1 (regenerate client)
  └─ Task 1.2 (hooks)
  └─ Task 1.3 (page)
  └─ Task 1.4 (tab components)     — requires 1.2
  └─ Task 1.5 (route + nav)        — requires 1.3
  └─ Task 1.6 (hook tests)         — requires 1.2

Feature 2 (Delete)
  └─ Task 2.1 (repo interface)
  └─ Task 2.2 (repo impl)          — requires 2.1
  └─ Task 2.3 (handler)            — requires 2.1
  └─ Task 2.4 (controller)         — requires 2.3
  └─ Task 2.5 (frontend)           — requires 2.4 and Feature 1 Task 1.2

Feature 3 (Formats)
  └─ Task 3.1 (refactor DI)
  └─ Task 3.2 (NuGet)
  └─ Task 3.3 (Word extractor)     — requires 3.2
  └─ Task 3.4 (Plain text)
  └─ Task 3.5 (tests)              — requires 3.1

Feature 4 (Retry)
  └─ Task 4.1 (Polly pipeline)
  └─ Task 4.2 (OpenAI retry)       — complete Bug fix Task 3.2 first
  └─ Task 4.3 (Anthropic retry)    — complete Bug fix Task 3.1 first

Feature 5 (Integration tests)
  └─ Task 5.1 (check infra)
  └─ Task 5.2 (Testcontainers pkg)
  └─ Task 5.3 (repository tests)   — requires 5.2
  └─ Task 5.4 (PDF extractor test)
  └─ Task 5.5 (chunker edge cases) — no dependencies
```

Features 1 and 2 are independent of Features 3–5 and can be developed in parallel.

## Final Checklist

- [ ] `dotnet build` — 0 errors
- [ ] `dotnet format` — no changes
- [ ] `dotnet test` — all tests green
- [ ] `npm run build` — 0 TypeScript errors
- [ ] `npm run lint` — no violations
- [ ] `npm test` — all frontend tests green
- [ ] Manual smoke: upload PDF → wait 15 min → document appears in UI with `indexed` status
- [ ] Manual smoke: search for content from the PDF → relevant chunks returned
- [ ] Manual smoke: ask question → Claude answers with source citation
- [ ] Manual smoke: delete document → removed from list, chunks deleted
- [ ] Manual smoke: upload `.docx` → indexed successfully
