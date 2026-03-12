# Knowledge Base — Bug Fixes Implementation Plan

## Document Information

- **Date**: 2026-03-07
- **Status**: Ready for Implementation
- **Related Documentation**:
  - `/docs/features/knowledge-base-rag.md` — full spec with bug descriptions
  - `/docs/plans/2026-03-07-knowledge-base-missing-features.md` — missing features plan

## Overview

Fixes 11 bugs found during code review of the Knowledge Base RAG implementation. Grouped into 4 phases by priority and dependency order.

**Estimated scope:** ~200 lines changed across 10 files, 2 new files.

---

## Phase 1: Critical Fixes (must ship before production)

### Task 1.1: Fix GraphOneDriveService — wrong Graph endpoint for app-only token

**File**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs`

**Problem**: All three methods use `/me/drive/...` which requires a delegated user token. `GetAccessTokenForAppAsync` returns an app-only (client credentials) token — `/me/` calls return HTTP 403.

**Action**: Add `OneDriveUserId` to `KnowledgeBaseOptions`, replace all `/me/drive/` occurrences with `/users/{userId}/drive/`.

**Step 1 — Add property to KnowledgeBaseOptions**:

File: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/KnowledgeBaseOptions.cs`

Add:
```csharp
/// <summary>
/// UPN or object ID of the OneDrive user account used for ingestion (app-only access).
/// Example: "service@anela.cz" or a GUID object ID.
/// </summary>
public string OneDriveUserId { get; set; } = string.Empty;
```

**Step 2 — Fix ListInboxFilesAsync**:

Replace:
```csharp
var url = $"{GraphBaseUrl}/me/drive/root:/{encodedPath}:/children?$filter=file ne null";
```
With:
```csharp
var url = $"{GraphBaseUrl}/users/{Uri.EscapeDataString(_options.OneDriveUserId)}/drive/root:/{encodedPath}:/children?$filter=file ne null";
```

**Step 3 — Fix DownloadFileAsync**:

Replace:
```csharp
var url = $"{GraphBaseUrl}/me/drive/items/{fileId}/content";
```
With:
```csharp
var url = $"{GraphBaseUrl}/users/{Uri.EscapeDataString(_options.OneDriveUserId)}/drive/items/{fileId}/content";
```

**Step 4 — Fix MoveToArchivedAsync**:

Replace:
```csharp
var url = $"{GraphBaseUrl}/me/drive/items/{fileId}";
```
With:
```csharp
var url = $"{GraphBaseUrl}/users/{Uri.EscapeDataString(_options.OneDriveUserId)}/drive/items/{fileId}";
```

**Step 5 — Add config to appsettings.json**:

File: `backend/src/Anela.Heblo.API/appsettings.json`

In the `KnowledgeBase` section add:
```json
"OneDriveUserId": ""
```
(Actual value set via environment variable / Azure Key Vault: `KnowledgeBase__OneDriveUserId`)

**Verification**:
- [ ] `OneDriveUserId` is exposed in `KnowledgeBaseOptions`
- [ ] All three Graph API URLs use `/users/{userId}/drive/` prefix
- [ ] Config key documented (value never committed to source control)
- [ ] `MockOneDriveService` unchanged (still works in local dev)

---

### Task 1.2: Fix KnowledgeBaseRepository — double-open of DB connection

**File**: `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs`

**Problem**: `AddChunksAsync` and `SearchSimilarAsync` call `connection.OpenAsync(ct)` unconditionally. EF Core may have already opened the connection in the same scope, causing `InvalidOperationException: Connection already open`.

**Action**: Guard `OpenAsync` with a connection state check in both methods.

In `AddChunksAsync`, replace:
```csharp
var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
await connection.OpenAsync(ct);
```
With:
```csharp
var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
if (connection.State != System.Data.ConnectionState.Open)
    await connection.OpenAsync(ct);
```

Apply the identical change in `SearchSimilarAsync`.

**Verification**:
- [ ] Both methods guard with `ConnectionState.Open` check
- [ ] No `connection.CloseAsync()` call added (EF Core manages lifecycle)
- [ ] Existing unit tests still pass

---

## Phase 2: High Priority Fixes

### Task 2.1: Fix N+1 queries in SearchSimilarAsync

**File**: `backend/src/Anela.Heblo.Persistence/KnowledgeBase/KnowledgeBaseRepository.cs`

**Problem**: After the vector similarity SQL query, a separate `FindAsync` is called for each chunk to load its parent document — N+1 queries for topK results.

**Action**: Rewrite the SQL query to JOIN `KnowledgeBaseDocuments` and read all fields in one round-trip.

Replace the existing `SearchSimilarAsync` raw SQL and result-reading loop with:

```csharp
await using var cmd = new NpgsqlCommand(
    """
    SELECT c."Id", c."DocumentId", c."ChunkIndex", c."Content",
           1 - (c."Embedding" <=> @embedding) AS "Score",
           d."Filename", d."SourcePath"
    FROM dbo."KnowledgeBaseChunks" c
    JOIN dbo."KnowledgeBaseDocuments" d ON c."DocumentId" = d."Id"
    ORDER BY c."Embedding" <=> @embedding
    LIMIT @topK
    """,
    connection);

cmd.Parameters.AddWithValue("embedding", vector);
cmd.Parameters.AddWithValue("topK", topK);

var results = new List<(KnowledgeBaseChunk Chunk, double Score)>();

await using var reader = await cmd.ExecuteReaderAsync(ct);
while (await reader.ReadAsync(ct))
{
    var chunkId = reader.GetGuid(0);
    var documentId = reader.GetGuid(1);
    var chunkIndex = reader.GetInt32(2);
    var content = reader.GetString(3);
    var score = reader.GetDouble(4);
    var filename = reader.GetString(5);
    var sourcePath = reader.GetString(6);

    var document = new KnowledgeBaseDocument
    {
        Id = documentId,
        Filename = filename,
        SourcePath = sourcePath
    };

    var chunk = new KnowledgeBaseChunk
    {
        Id = chunkId,
        DocumentId = documentId,
        ChunkIndex = chunkIndex,
        Content = content,
        Embedding = [],
        Document = document
    };

    results.Add((chunk, score));
}
```

**Verification**:
- [ ] Single SQL query per `SearchSimilarAsync` call (no `FindAsync` per chunk)
- [ ] `KnowledgeBaseChunk.Document.Filename` and `.SourcePath` correctly populated
- [ ] `SearchDocumentsHandlerTests` pass (mock still works)

---

### Task 2.2: Add GetDocumentsHandler — fix MediatR bypass in controller

**Problem**: `KnowledgeBaseController.GetDocuments` injects `IKnowledgeBaseRepository` directly, bypassing the MediatR pattern used everywhere else in the project.

**Step 1 — Create use case files**:

New file: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetDocuments/GetDocumentsRequest.cs`

```csharp
using Anela.Heblo.Application.Shared;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;

public class GetDocumentsRequest : IRequest<GetDocumentsResponse>
{
}

public class GetDocumentsResponse : BaseResponse
{
    public List<DocumentSummary> Documents { get; set; } = [];
}

public class DocumentSummary
{
    public Guid Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime? IndexedAt { get; set; }
}
```

New file: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/GetDocuments/GetDocumentsHandler.cs`

```csharp
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;

namespace Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;

public class GetDocumentsHandler : IRequestHandler<GetDocumentsRequest, GetDocumentsResponse>
{
    private readonly IKnowledgeBaseRepository _repository;

    public GetDocumentsHandler(IKnowledgeBaseRepository repository)
    {
        _repository = repository;
    }

    public async Task<GetDocumentsResponse> Handle(
        GetDocumentsRequest request,
        CancellationToken cancellationToken)
    {
        var docs = await _repository.GetAllDocumentsAsync(cancellationToken);

        return new GetDocumentsResponse
        {
            Documents = docs.Select(d => new DocumentSummary
            {
                Id = d.Id,
                Filename = d.Filename,
                Status = d.Status,
                ContentType = d.ContentType,
                CreatedAt = d.CreatedAt,
                IndexedAt = d.IndexedAt
            }).ToList()
        };
    }
}
```

**Step 2 — Update KnowledgeBaseController**:

File: `backend/src/Anela.Heblo.API/Controllers/KnowledgeBaseController.cs`

Remove `IKnowledgeBaseRepository` injection. Replace `GetDocuments` action:

```csharp
[HttpGet("documents")]
public async Task<ActionResult<GetDocumentsResponse>> GetDocuments(CancellationToken ct)
{
    var result = await _mediator.Send(new GetDocumentsRequest(), ct);
    return HandleResponse(result);
}
```

Remove the `using Anela.Heblo.Domain.Features.KnowledgeBase;` import and the `_repository` field.

**Verification**:
- [ ] Controller no longer injects `IKnowledgeBaseRepository`
- [ ] `GET /api/knowledgebase/documents` still returns correct data
- [ ] New handler has unit test in `GetDocumentsHandlerTests.cs`

---

## Phase 3: Medium Priority Fixes

### Task 3.1: Migrate AnthropicClaudeService to IOptions

**File**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/AnthropicClaudeService.cs`

**Action**: Replace `IConfiguration` reads with `IOptions<KnowledgeBaseOptions>`.

Replace constructor signature and field setup:
```csharp
private readonly KnowledgeBaseOptions _options;
private readonly ILogger<AnthropicClaudeService> _logger;

public AnthropicClaudeService(IOptions<KnowledgeBaseOptions> options, ILogger<AnthropicClaudeService> logger)
{
    _options = options.Value;
    _logger = logger;
}
```

Replace `_configuration["KnowledgeBase:ClaudeModel"]` with `_options.ClaudeModel`.
Replace `int.TryParse(configuration["KnowledgeBase:ClaudeMaxTokens"], ...)` with `_options.ClaudeMaxTokens`.

Remove `IConfiguration` field and its using.

The Anthropic API key still comes from `IConfiguration["Anthropic:ApiKey"]` — inject `IConfiguration` only for secrets (or use `IOptions<AnthropicOptions>` if a dedicated options class is preferred).

**Verification**:
- [ ] No direct `_configuration["KnowledgeBase:*"]` reads remain
- [ ] `AskQuestionHandlerTests` still pass

---

### Task 3.2: Migrate OpenAiEmbeddingService to IOptions + fix client lifetime

**File**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/OpenAiEmbeddingService.cs`

**Action**: Replace `IConfiguration` reads with `IOptions<KnowledgeBaseOptions>` and create `EmbeddingClient` once in constructor.

```csharp
public class OpenAiEmbeddingService : IEmbeddingService
{
    private readonly EmbeddingClient _client;
    private readonly ILogger<OpenAiEmbeddingService> _logger;

    public OpenAiEmbeddingService(
        IOptions<KnowledgeBaseOptions> options,
        IConfiguration configuration,
        ILogger<OpenAiEmbeddingService> logger)
    {
        var apiKey = configuration["OpenAI:ApiKey"]
            ?? throw new InvalidOperationException("OpenAI:ApiKey is not configured.");
        _client = new EmbeddingClient(options.Value.EmbeddingModel, apiKey);
        _logger = logger;
    }

    public async Task<float[]> GenerateEmbeddingAsync(string text, CancellationToken ct = default)
    {
        _logger.LogDebug("Generating embedding for {CharCount} characters", text.Length);
        var result = await _client.GenerateEmbeddingAsync(text, cancellationToken: ct);
        return result.Value.ToFloats().ToArray();
    }
}
```

**Verification**:
- [ ] `EmbeddingClient` created once per service instance (Scoped = once per HTTP request)
- [ ] No `new EmbeddingClient(...)` inside `GenerateEmbeddingAsync`

---

### Task 3.3: Add input validation to request DTOs

**Files**:
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/SearchDocuments/SearchDocumentsRequest.cs`
- `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/UseCases/AskQuestion/AskQuestionRequest.cs`

**Action**: Add `System.ComponentModel.DataAnnotations` validation attributes.

In `SearchDocumentsRequest`:
```csharp
using System.ComponentModel.DataAnnotations;

public class SearchDocumentsRequest : IRequest<SearchDocumentsResponse>
{
    [Required, MinLength(1), MaxLength(2000)]
    public string Query { get; set; } = string.Empty;

    [Range(1, 20)]
    public int TopK { get; set; } = 5;
}
```

In `AskQuestionRequest`:
```csharp
using System.ComponentModel.DataAnnotations;

public class AskQuestionRequest : IRequest<AskQuestionResponse>
{
    [Required, MinLength(1), MaxLength(2000)]
    public string Question { get; set; } = string.Empty;

    [Range(1, 20)]
    public int TopK { get; set; } = 5;
}
```

**Verification**:
- [ ] `POST /api/knowledgebase/search` with empty `query` returns HTTP 400
- [ ] `POST /api/knowledgebase/ask` with `topK: 0` returns HTTP 400

---

### Task 3.4: Handle SourcePath conflict in ingestion job

**File**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Jobs/KnowledgeBaseIngestionJob.cs`

**Problem**: When no hash match exists but a document at the same SourcePath already exists (file replaced with different content), the INSERT fails on the UNIQUE constraint.

**Step 1 — Add repository method to IKnowledgeBaseRepository**:

File: `backend/src/Anela.Heblo.Domain/Features/KnowledgeBase/IKnowledgeBaseRepository.cs`

Add:
```csharp
Task<KnowledgeBaseDocument?> GetDocumentBySourcePathAsync(string sourcePath, CancellationToken ct = default);
Task DeleteDocumentAsync(Guid documentId, CancellationToken ct = default);
```

**Step 2 — Implement in KnowledgeBaseRepository**:

```csharp
public async Task<KnowledgeBaseDocument?> GetDocumentBySourcePathAsync(string sourcePath, CancellationToken ct = default)
{
    return await _context.KnowledgeBaseDocuments
        .FirstOrDefaultAsync(d => d.SourcePath == sourcePath, ct);
}

public async Task DeleteDocumentAsync(Guid documentId, CancellationToken ct = default)
{
    await _context.KnowledgeBaseDocuments
        .Where(d => d.Id == documentId)
        .ExecuteDeleteAsync(ct);
}
```

**Step 3 — Update ingestion job dedup logic**:

After the hash-check block (no hash match found), before dispatching `IndexDocumentRequest`:
```csharp
// Check for replaced file at same path (different content, same location)
var existingByPath = await _repository.GetDocumentBySourcePathAsync(file.Path, cancellationToken);
if (existingByPath is not null)
{
    _logger.LogInformation(
        "File {Filename} at {Path} has new content (hash changed). Deleting old document {Id} before re-indexing.",
        file.Name, file.Path, existingByPath.Id);
    await _repository.DeleteDocumentAsync(existingByPath.Id, cancellationToken);
}
```

**Verification**:
- [ ] Re-uploading a changed file at the same path succeeds (no UNIQUE violation)
- [ ] Old document and its chunks are deleted before re-indexing
- [ ] Unit test covers the replaced-file scenario

---

### Task 3.5: Fix appsettings.json OpenAI placeholder

**File**: `backend/src/Anela.Heblo.API/appsettings.json`

Replace:
```json
"OpenAI": {
  "ApiKey": "xxxxxx",
  "Organization": "xxxxxx"
}
```
With:
```json
"OpenAI": {
  "ApiKey": "",
  "Organization": ""
}
```

**Verification**:
- [ ] No placeholder strings in committed config
- [ ] Build still succeeds

---

### Task 3.6: Fix namespace in KnowledgeBaseIngestionJob

**File**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Jobs/KnowledgeBaseIngestionJob.cs`

Replace:
```csharp
namespace Anela.Heblo.Application.Features.KnowledgeBase.Infrastructure.Jobs;
```
With:
```csharp
namespace Anela.Heblo.Application.Features.KnowledgeBase.Jobs;
```

Update any `using` statements in other files that reference the old namespace (should be none, as the type is discovered by Hangfire via DI).

**Verification**:
- [ ] `dotnet build` passes with 0 errors
- [ ] `dotnet format` reports no changes

---

## Phase 4: Fix GraphOneDriveService HttpClient Lifetime

### Task 4.1: Replace per-call HttpClient with IHttpClientFactory

**File**: `backend/src/Anela.Heblo.Application/Features/KnowledgeBase/Services/GraphOneDriveService.cs`

**Problem**: `CreateHttpClient(token)` instantiates a `new HttpClient()` on every method call — socket exhaustion risk in production under load.

**Action**: Inject `IHttpClientFactory`. Since the Authorization header changes per call (fresh token), set it per-request using `HttpRequestMessage` instead of `DefaultRequestHeaders`.

```csharp
private readonly IHttpClientFactory _httpClientFactory;

public GraphOneDriveService(
    ITokenAcquisition tokenAcquisition,
    IOptions<KnowledgeBaseOptions> options,
    IHttpClientFactory httpClientFactory,
    ILogger<GraphOneDriveService> logger)
{
    _tokenAcquisition = tokenAcquisition;
    _options = options.Value;
    _httpClientFactory = httpClientFactory;
    _logger = logger;
}
```

Replace `CreateHttpClient(token)` helper with:
```csharp
private HttpRequestMessage CreateRequest(HttpMethod method, string url, string token)
{
    var request = new HttpRequestMessage(method, url);
    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
    return request;
}
```

Update each method to use:
```csharp
using var client = _httpClientFactory.CreateClient("MicrosoftGraph");
var request = CreateRequest(HttpMethod.Get, url, token);
var response = await client.SendAsync(request, ct);
```

Register named client in `KnowledgeBaseModule.cs`:
```csharp
services.AddHttpClient("MicrosoftGraph");
```

Remove the `private static HttpClient CreateHttpClient(string token)` method.

**Verification**:
- [ ] No `new HttpClient()` in `GraphOneDriveService`
- [ ] Named client `"MicrosoftGraph"` registered in DI
- [ ] All three methods use `_httpClientFactory.CreateClient(...)`

---

## Testing Checklist

After all phases are complete:

- [ ] `dotnet build` — 0 errors, 0 warnings (new)
- [ ] `dotnet format` — no changes
- [ ] `dotnet test` — all existing tests green
- [ ] New unit tests added for:
  - [ ] `GetDocumentsHandlerTests` (Task 2.2)
  - [ ] Ingestion job replaced-file scenario (Task 3.4)
- [ ] Manual smoke test of `POST /api/knowledgebase/search` with empty body → HTTP 400
- [ ] Manual smoke test of `POST /api/knowledgebase/ask` with `topK: 0` → HTTP 400

## Implementation Order

```
Task 1.1  (GraphOneDriveService URLs)     — no dependencies
Task 1.2  (connection double-open)        — no dependencies
Task 2.1  (N+1 queries)                   — no dependencies
Task 2.2  (GetDocumentsHandler)           — no dependencies
Task 3.1  (AnthropicClaudeService IOptions) — no dependencies
Task 3.2  (OpenAiEmbeddingService IOptions) — no dependencies
Task 3.3  (input validation)              — no dependencies
Task 3.4  (SourcePath conflict)           — requires Task 3.4 step 1+2 before step 3
Task 3.5  (appsettings placeholder)       — no dependencies
Task 3.6  (namespace fix)                 — no dependencies
Task 4.1  (IHttpClientFactory)            — no dependencies
```

All tasks are independent and can be implemented in any order within their phase.
