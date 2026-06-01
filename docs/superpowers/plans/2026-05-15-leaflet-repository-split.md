# Leaflet Repository Split Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split `ILeafletRepository` (15 methods, 2 aggregates, 1 unit-of-work leak) into two aggregate-aligned interfaces (`ILeafletDocumentRepository`, `ILeafletGenerationRepository`) and remove `SaveChangesAsync` from the domain layer entirely. No public-API or behavioral change for end users — pure ISP/Clean Architecture refactor.

**Architecture:**
- Two narrow domain interfaces, one per aggregate root, both auto-committing per write method.
- New `UpdateFeedbackAsync` repo method encapsulates the feedback load-mutate-save cycle and returns an `UpdateFeedbackResult` enum so the handler can keep its current error semantics without touching `SaveChangesAsync`.
- `WordCount` computation moves out of `LeafletIndexingService` into `IndexLeafletHandler` (required because `AddDocumentAsync` now commits eagerly — see arch-review Decision 4).

**Tech Stack:** C# / .NET 8, EF Core 8, Npgsql + pgvector, MediatR, xUnit + FluentAssertions + Moq.

---

## File Structure

### Files to create

| Path | Responsibility |
|---|---|
| `backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletDocumentRepository.cs` | Document/Chunk aggregate interface (14 members, no `SaveChangesAsync`). |
| `backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletGenerationRepository.cs` | Generation/Feedback aggregate interface (5 members, no `SaveChangesAsync`). |
| `backend/src/Anela.Heblo.Domain/Features/Leaflet/UpdateFeedbackResult.cs` | Enum: `Updated`, `NotFound`, `AlreadySubmitted`. |
| `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs` | EF Core implementation of `ILeafletDocumentRepository`. |
| `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletGenerationRepository.cs` | EF Core implementation of `ILeafletGenerationRepository`. |

### Files to modify

| Path | Change |
|---|---|
| `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` (line 129) | Replace single registration with two narrower registrations. |
| `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/IndexLeaflet/IndexLeafletHandler.cs` | Switch to `ILeafletDocumentRepository`; compute `WordCount` before `AddDocumentAsync`; remove both `SaveChangesAsync` calls. |
| `backend/src/Anela.Heblo.Application/Features/Leaflet/Services/LeafletIndexingService.cs` | Switch to `ILeafletDocumentRepository`; remove `document.WordCount = …` mutation. |
| `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/DeleteLeafletDocument/DeleteLeafletDocumentHandler.cs` | Switch to `ILeafletDocumentRepository`. |
| `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletDocuments/GetLeafletDocumentsHandler.cs` | Switch to `ILeafletDocumentRepository`. |
| `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletDocumentContentTypes/GetLeafletDocumentContentTypesHandler.cs` | Switch to `ILeafletDocumentRepository`. |
| `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletChunkDetail/GetLeafletChunkDetailHandler.cs` | Switch to `ILeafletDocumentRepository`. |
| `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs` | Switch field `_leaflets` to `ILeafletDocumentRepository`. |
| `backend/src/Anela.Heblo.Application/Features/Leaflet/Infrastructure/Jobs/LeafletIngestionJob.cs` | Switch to `ILeafletDocumentRepository`. |
| `backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehavior.cs` | Switch to `ILeafletGenerationRepository`. |
| `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletGeneration/GetLeafletGenerationHandler.cs` | Switch to `ILeafletGenerationRepository`. |
| `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletFeedbackList/GetLeafletFeedbackListHandler.cs` | Switch to `ILeafletGenerationRepository`. |
| `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/SubmitLeafletFeedback/SubmitLeafletFeedbackHandler.cs` | Switch to `ILeafletGenerationRepository`; replace mutation+`SaveChangesAsync` with `UpdateFeedbackAsync`+switch. |
| All 13 test files under `backend/test/Anela.Heblo.Tests/Features/Leaflet/` | Re-mock against the narrower interface(s); see Tasks 12–14. |

### Files to delete (in the final task)

- `backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletRepository.cs`
- `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletRepository.cs`

---

## Pre-flight

- [ ] **Step 0: Confirm baseline build is green**

Run from repo root:
```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded.` with 0 errors. If this fails, stop and fix the baseline before starting.

- [ ] **Step 0b: Run the full Leaflet test suite to record baseline pass count**

```bash
dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Leaflet" --logger "console;verbosity=minimal"
```
Expected: All tests pass. Note the total count (e.g. "Passed: N"). Every later check should match this number ± any tests we explicitly add or remove.

---

### Task 1: Add `UpdateFeedbackResult` enum

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Leaflet/UpdateFeedbackResult.cs`

- [ ] **Step 1: Create the enum**

Write:
```csharp
namespace Anela.Heblo.Domain.Features.Leaflet;

public enum UpdateFeedbackResult
{
    Updated,
    NotFound,
    AlreadySubmitted,
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Leaflet/UpdateFeedbackResult.cs
git commit -m "feat(leaflet): add UpdateFeedbackResult enum"
```

---

### Task 2: Add `ILeafletDocumentRepository` interface

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletDocumentRepository.cs`

- [ ] **Step 1: Write the interface (signatures verbatim from current `ILeafletRepository`)**

Write the file:
```csharp
namespace Anela.Heblo.Domain.Features.Leaflet;

public interface ILeafletDocumentRepository
{
    Task AddDocumentAsync(LeafletDocument document, CancellationToken ct = default);
    Task AddChunksAsync(IEnumerable<LeafletChunk> chunks, CancellationToken ct = default);
    Task<LeafletDocument?> GetByHashAsync(string contentHash, CancellationToken ct = default);
    Task<LeafletDocument?> GetBySourcePathAsync(string sourcePath, CancellationToken ct = default);
    Task<LeafletDocument?> GetByGraphItemIdAsync(string driveId, string graphItemId, CancellationToken ct = default);
    Task DeleteDocumentAsync(Guid id, CancellationToken ct = default);
    Task<List<(LeafletChunk Chunk, double Score)>> SearchSimilarAsync(
        float[] queryEmbedding, int topK, CancellationToken ct = default);
    Task UpdateSourcePathAsync(Guid documentId, string newPath, CancellationToken ct = default);
    Task UpdateGraphItemIdAsync(Guid documentId, string driveId, string graphItemId, CancellationToken ct = default);
    Task UpdateStatusAsync(Guid documentId, LeafletDocumentStatus status, DateTime? indexedAt, CancellationToken ct = default);
    Task<(IReadOnlyList<LeafletDocument> Items, int Total)> GetDocumentsPagedAsync(
        int pageNumber, int pageSize, string sortBy, bool sortDescending,
        string? filenameFilter, LeafletDocumentStatus? statusFilter, string? contentTypeFilter,
        CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetDistinctContentTypesAsync(CancellationToken ct = default);
    Task<LeafletChunk?> GetChunkByIdAsync(Guid id, CancellationToken ct = default);
    Task<IReadOnlyDictionary<Guid, Guid>> GetFirstChunkIdsByDocumentIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletDocumentRepository.cs
git commit -m "feat(leaflet): add ILeafletDocumentRepository interface"
```

---

### Task 3: Add `ILeafletGenerationRepository` interface

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletGenerationRepository.cs`

- [ ] **Step 1: Write the interface**

Note: `UpdateFeedbackAsync` returns `UpdateFeedbackResult` (per arch-review Decision 2 / Specification Amendment 2 — the spec's FR-2 "returns false" wording is overruled by the enum form in the API/Interface Design section).

```csharp
namespace Anela.Heblo.Domain.Features.Leaflet;

public interface ILeafletGenerationRepository
{
    Task SaveGenerationAsync(LeafletGeneration generation, CancellationToken cancellationToken);
    Task<LeafletGeneration?> GetGenerationByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<(IReadOnlyList<LeafletGeneration> Items, int TotalCount)> GetGenerationsPagedAsync(
        bool? hasFeedback, string? userId, string sortBy, bool descending,
        int page, int pageSize, CancellationToken cancellationToken);
    Task<LeafletFeedbackStats> GetGenerationStatsAsync(CancellationToken cancellationToken);
    Task<UpdateFeedbackResult> UpdateFeedbackAsync(
        Guid generationId,
        int? precisionScore,
        int? styleScore,
        string? comment,
        CancellationToken cancellationToken);
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build backend/src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj
```
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletGenerationRepository.cs
git commit -m "feat(leaflet): add ILeafletGenerationRepository interface"
```

---

### Task 4: Implement `LeafletDocumentRepository`

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs`

This is a copy of the document/chunk methods from the current `LeafletRepository.cs`, **with one behavior change**: `AddDocumentAsync` now calls `_context.SaveChangesAsync(ct)` after `Add` (per FR-3/FR-4). All other methods stay identical (same SQL, same `AsNoTracking`, same command timeout, same pgvector handling).

- [ ] **Step 1: Write the implementation**

```csharp
using Anela.Heblo.Domain.Features.Leaflet;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Pgvector;

namespace Anela.Heblo.Persistence.Features.Leaflet;

public class LeafletDocumentRepository : ILeafletDocumentRepository
{
    private readonly ApplicationDbContext _context;

    public LeafletDocumentRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task AddDocumentAsync(LeafletDocument document, CancellationToken ct = default)
    {
        _context.LeafletDocuments.Add(document);
        await _context.SaveChangesAsync(ct);
    }

    public async Task AddChunksAsync(IEnumerable<LeafletChunk> chunks, CancellationToken ct = default)
    {
        var chunkList = chunks.ToList();

        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        foreach (var chunk in chunkList)
        {
            var embedding = new Vector(chunk.Embedding);
            await using var cmd = new NpgsqlCommand(
                """
                INSERT INTO "LeafletChunks" ("Id", "DocumentId", "ChunkIndex", "Content", "WordCount", "Embedding")
                VALUES (@id, @documentId, @chunkIndex, @content, @wordCount, @embedding)
                ON CONFLICT ("Id") DO NOTHING
                """,
                connection);

            cmd.Parameters.AddWithValue("id", chunk.Id);
            cmd.Parameters.AddWithValue("documentId", chunk.DocumentId);
            cmd.Parameters.AddWithValue("chunkIndex", chunk.ChunkIndex);
            cmd.Parameters.AddWithValue("content", chunk.Content);
            cmd.Parameters.AddWithValue("wordCount", chunk.WordCount);
            cmd.Parameters.AddWithValue("embedding", embedding);

            await cmd.ExecuteNonQueryAsync(ct);
        }
    }

    public async Task<LeafletDocument?> GetByHashAsync(string contentHash, CancellationToken ct = default)
    {
        return await _context.LeafletDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.ContentHash == contentHash, ct);
    }

    public async Task<LeafletDocument?> GetBySourcePathAsync(string sourcePath, CancellationToken ct = default)
    {
        return await _context.LeafletDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.SourcePath == sourcePath, ct);
    }

    public async Task<LeafletDocument?> GetByGraphItemIdAsync(string driveId, string graphItemId, CancellationToken ct = default)
    {
        return await _context.LeafletDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.DriveId == driveId && x.GraphItemId == graphItemId, ct);
    }

    public async Task DeleteDocumentAsync(Guid id, CancellationToken ct = default)
    {
        await _context.LeafletDocuments
            .Where(x => x.Id == id)
            .ExecuteDeleteAsync(ct);
    }

    public async Task<List<(LeafletChunk Chunk, double Score)>> SearchSimilarAsync(
        float[] queryEmbedding, int topK, CancellationToken ct = default)
    {
        var vector = new Vector(queryEmbedding);

        var connection = (NpgsqlConnection)_context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(ct);

        // Cosine distance: lower = more similar. Score = 1 - distance.
        // CommandTimeout set to 120s — vector similarity search can be slow without a warm HNSW index.
        await using var cmd = new NpgsqlCommand(
            """
            SELECT c."Id", c."DocumentId", c."ChunkIndex", c."Content", c."WordCount",
                   d."Filename", d."SourcePath",
                   1 - (c."Embedding" <=> @embedding) AS "Score"
            FROM "LeafletChunks" c
            JOIN "LeafletDocuments" d ON d."Id" = c."DocumentId"
            ORDER BY c."Embedding" <=> @embedding
            LIMIT @topK
            """,
            connection)
        {
            CommandTimeout = 120
        };

        cmd.Parameters.AddWithValue("embedding", vector);
        cmd.Parameters.AddWithValue("topK", topK);

        var results = new List<(LeafletChunk Chunk, double Score)>();

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var documentId = reader.GetGuid(1);

            var chunk = new LeafletChunk
            {
                Id = reader.GetGuid(0),
                DocumentId = documentId,
                ChunkIndex = reader.GetInt32(2),
                Content = reader.GetString(3),
                WordCount = reader.GetInt32(4),
                Embedding = [],
                Document = new LeafletDocument
                {
                    Id = documentId,
                    Filename = reader.GetString(5),
                    SourcePath = reader.GetString(6),
                }
            };

            results.Add((chunk, reader.GetDouble(7)));
        }

        return results;
    }

    public async Task UpdateSourcePathAsync(Guid documentId, string newPath, CancellationToken ct = default)
    {
        await _context.LeafletDocuments
            .Where(x => x.Id == documentId)
            .ExecuteUpdateAsync(s => s.SetProperty(d => d.SourcePath, newPath), ct);
    }

    public async Task UpdateGraphItemIdAsync(Guid documentId, string driveId, string graphItemId, CancellationToken ct = default)
    {
        await _context.LeafletDocuments
            .Where(x => x.Id == documentId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.DriveId, driveId)
                .SetProperty(d => d.GraphItemId, graphItemId), ct);
    }

    public async Task UpdateStatusAsync(Guid documentId, LeafletDocumentStatus status, DateTime? indexedAt, CancellationToken ct = default)
    {
        await _context.LeafletDocuments
            .Where(x => x.Id == documentId)
            .ExecuteUpdateAsync(s => s
                .SetProperty(d => d.Status, status)
                .SetProperty(d => d.IndexedAt, indexedAt), ct);
    }

    public async Task<(IReadOnlyList<LeafletDocument> Items, int Total)> GetDocumentsPagedAsync(
        int pageNumber, int pageSize, string sortBy, bool sortDescending,
        string? filenameFilter, LeafletDocumentStatus? statusFilter, string? contentTypeFilter,
        CancellationToken ct = default)
    {
        var query = _context.LeafletDocuments.AsNoTracking().AsQueryable();

        if (!string.IsNullOrEmpty(filenameFilter))
        {
            var escaped = filenameFilter.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
            query = query.Where(d => EF.Functions.Like(d.Filename, $"%{escaped}%", "\\"));
        }

        if (statusFilter.HasValue)
            query = query.Where(d => d.Status == statusFilter.Value);

        if (!string.IsNullOrEmpty(contentTypeFilter))
            query = query.Where(d => d.ContentType == contentTypeFilter);

        query = sortBy switch
        {
            "Filename" => sortDescending
                ? query.OrderByDescending(d => d.Filename)
                : query.OrderBy(d => d.Filename),
            "Status" => sortDescending
                ? query.OrderByDescending(d => d.Status)
                : query.OrderBy(d => d.Status),
            "IndexedAt" => sortDescending
                ? query.OrderByDescending(d => d.IndexedAt)
                : query.OrderBy(d => d.IndexedAt),
            _ => sortDescending
                ? query.OrderByDescending(d => d.IngestedAt)
                : query.OrderBy(d => d.IngestedAt),
        };

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return (items, total);
    }

    public async Task<IReadOnlyList<string>> GetDistinctContentTypesAsync(CancellationToken ct = default)
    {
        return await _context.LeafletDocuments
            .AsNoTracking()
            .Select(d => d.ContentType)
            .Distinct()
            .OrderBy(c => c)
            .ToListAsync(ct);
    }

    public async Task<LeafletChunk?> GetChunkByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.LeafletChunks
            .AsNoTracking()
            .Include(c => c.Document)
            .FirstOrDefaultAsync(c => c.Id == id, ct);
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> GetFirstChunkIdsByDocumentIdsAsync(
        IEnumerable<Guid> ids, CancellationToken ct = default)
    {
        var idList = ids.ToList();
        return await _context.LeafletChunks
            .Where(c => idList.Contains(c.DocumentId))
            .GroupBy(c => c.DocumentId)
            .Select(g => new { DocumentId = g.Key, ChunkId = g.OrderBy(c => c.ChunkIndex).First().Id })
            .ToDictionaryAsync(x => x.DocumentId, x => x.ChunkId, ct);
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs
git commit -m "feat(leaflet): add LeafletDocumentRepository implementation"
```

---

### Task 5: Implement `LeafletGenerationRepository`

**Files:**
- Create: `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletGenerationRepository.cs`

This holds the four existing generation methods plus the new `UpdateFeedbackAsync`. The new method loads the entity (returning the tracked instance from the change tracker if already loaded by the handler), performs the validation that previously lived in `SubmitLeafletFeedbackHandler` (already-submitted check), mutates the fields, and saves.

- [ ] **Step 1: Write the implementation**

```csharp
using Anela.Heblo.Domain.Features.Leaflet;
using Microsoft.EntityFrameworkCore;

namespace Anela.Heblo.Persistence.Features.Leaflet;

public class LeafletGenerationRepository : ILeafletGenerationRepository
{
    private readonly ApplicationDbContext _context;

    public LeafletGenerationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task SaveGenerationAsync(LeafletGeneration generation, CancellationToken cancellationToken)
    {
        _context.LeafletGenerations.Add(generation);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<LeafletGeneration?> GetGenerationByIdAsync(Guid id, CancellationToken cancellationToken)
        => await _context.LeafletGenerations.FindAsync([id], cancellationToken);

    public async Task<(IReadOnlyList<LeafletGeneration> Items, int TotalCount)> GetGenerationsPagedAsync(
        bool? hasFeedback, string? userId, string sortBy, bool descending,
        int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = _context.LeafletGenerations
            .AsNoTracking()
            .AsQueryable();

        if (hasFeedback == true)
            query = query.Where(g => g.PrecisionScore != null || g.StyleScore != null);
        else if (hasFeedback == false)
            query = query.Where(g => g.PrecisionScore == null && g.StyleScore == null);

        if (!string.IsNullOrWhiteSpace(userId))
            query = query.Where(g => g.UserId == userId);

        query = (sortBy, descending) switch
        {
            ("PrecisionScore", true) => query.OrderByDescending(g => g.PrecisionScore),
            ("PrecisionScore", false) => query.OrderBy(g => g.PrecisionScore),
            ("StyleScore", true) => query.OrderByDescending(g => g.StyleScore),
            ("StyleScore", false) => query.OrderBy(g => g.StyleScore),
            (_, true) => query.OrderByDescending(g => g.CreatedAt),
            _ => query.OrderBy(g => g.CreatedAt),
        };

        var total = await query.CountAsync(cancellationToken);
        var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total);
    }

    public async Task<LeafletFeedbackStats> GetGenerationStatsAsync(CancellationToken cancellationToken)
    {
        var total = await _context.LeafletGenerations.CountAsync(cancellationToken);
        var withFeedback = await _context.LeafletGenerations
            .CountAsync(g => g.PrecisionScore != null || g.StyleScore != null, cancellationToken);
        var avgPrecision = await _context.LeafletGenerations
            .Where(g => g.PrecisionScore != null)
            .AverageAsync(g => (double?)g.PrecisionScore, cancellationToken);
        var avgStyle = await _context.LeafletGenerations
            .Where(g => g.StyleScore != null)
            .AverageAsync(g => (double?)g.StyleScore, cancellationToken);
        return new LeafletFeedbackStats(total, withFeedback, avgPrecision, avgStyle);
    }

    public async Task<UpdateFeedbackResult> UpdateFeedbackAsync(
        Guid generationId,
        int? precisionScore,
        int? styleScore,
        string? comment,
        CancellationToken cancellationToken)
    {
        var generation = await _context.LeafletGenerations.FindAsync([generationId], cancellationToken);
        if (generation is null)
            return UpdateFeedbackResult.NotFound;

        if (generation.PrecisionScore is not null || generation.StyleScore is not null)
            return UpdateFeedbackResult.AlreadySubmitted;

        generation.PrecisionScore = precisionScore;
        generation.StyleScore = styleScore;
        generation.FeedbackComment = comment;

        await _context.SaveChangesAsync(cancellationToken);
        return UpdateFeedbackResult.Updated;
    }
}
```

- [ ] **Step 2: Verify build**

```bash
dotnet build backend/src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj
```
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletGenerationRepository.cs
git commit -m "feat(leaflet): add LeafletGenerationRepository implementation"
```

---

### Task 6: Register both new repositories in DI (alongside the old one)

We register the new ones **alongside** the existing `ILeafletRepository` registration. This lets the codebase compile and tests run while we migrate consumers one by one. The old registration is removed in Task 16.

**Files:**
- Modify: `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs`

- [ ] **Step 1: Add the two new registrations next to the old one**

Edit lines around 128–129. Replace:
```csharp
        // Leaflet repositories
        services.AddScoped<ILeafletRepository, LeafletRepository>();
```
with:
```csharp
        // Leaflet repositories
        services.AddScoped<ILeafletRepository, LeafletRepository>();
        services.AddScoped<ILeafletDocumentRepository, LeafletDocumentRepository>();
        services.AddScoped<ILeafletGenerationRepository, LeafletGenerationRepository>();
```

- [ ] **Step 2: Verify build**

```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded.` with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add backend/src/Anela.Heblo.Persistence/PersistenceModule.cs
git commit -m "feat(leaflet): register split repositories alongside legacy interface"
```

---

### Task 7: Migrate `IndexLeafletHandler` (TDD: lock in WordCount-in-handler behavior first)

This task carries the highest risk because of arch-review Decision 4: `AddDocumentAsync` now commits eagerly, so the later mutation of `document.WordCount` inside `LeafletIndexingService` would silently regress the persisted value. We move the WordCount computation up into the handler **before** `AddDocumentAsync`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/IndexLeaflet/IndexLeafletHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/IndexLeafletHandlerTests.cs`

- [ ] **Step 1: Write a failing test that captures `WordCount` set on the document handed to `AddDocumentAsync`**

Open `IndexLeafletHandlerTests.cs` and add this test inside the existing class:

```csharp
[Fact]
public async Task Handle_happy_path_stamps_WordCount_on_document_before_AddDocumentAsync()
{
    // Arrange
    _repoMock
        .Setup(r => r.GetByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((LeafletDocument?)null);
    _repoMock
        .Setup(r => r.GetBySourcePathAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync((LeafletDocument?)null);

    _extractorMock.Setup(e => e.CanHandle("application/pdf")).Returns(true);
    _extractorMock
        .Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync("alpha beta gamma delta epsilon");  // 5 words

    int? capturedWordCountAtAdd = null;
    _repoMock
        .Setup(r => r.AddDocumentAsync(It.IsAny<LeafletDocument>(), It.IsAny<CancellationToken>()))
        .Callback<LeafletDocument, CancellationToken>((doc, _) => capturedWordCountAtAdd = doc.WordCount);

    var handler = CreateHandler();
    var request = new IndexLeafletRequest
    {
        Content = new byte[] { 1, 2, 3 },
        Filename = "wc.pdf",
        SourcePath = "/inbox/wc.pdf",
        ContentType = "application/pdf",
    };

    // Act
    await handler.Handle(request, CancellationToken.None);

    // Assert
    capturedWordCountAtAdd.Should().Be(5);
}
```

- [ ] **Step 2: Run the test and verify it fails**

```bash
dotnet test backend/Anela.Heblo.sln \
  --filter "FullyQualifiedName~IndexLeafletHandlerTests.Handle_happy_path_stamps_WordCount_on_document_before_AddDocumentAsync"
```
Expected: FAIL — `Expected capturedWordCountAtAdd to be 5, but found 0.` (the current handler hard-codes `WordCount = 0` and relies on `LeafletIndexingService` to mutate it later).

- [ ] **Step 3: Update `IndexLeafletHandler.cs` — switch to narrower interface, compute WordCount up-front, drop both `SaveChangesAsync` calls**

Open `IndexLeafletHandler.cs`. Replace the **entire file** with:

```csharp
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.Leaflet.Services;
using Anela.Heblo.Domain.Features.Leaflet;
using MediatR;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet;

public class IndexLeafletHandler : IRequestHandler<IndexLeafletRequest, IndexLeafletResponse>
{
    private readonly ILeafletDocumentRepository _repo;
    private readonly IEnumerable<IDocumentTextExtractor> _extractors;
    private readonly ILeafletIndexingService _indexing;
    private readonly ILogger<IndexLeafletHandler> _logger;

    public IndexLeafletHandler(
        ILeafletDocumentRepository repo,
        IEnumerable<IDocumentTextExtractor> extractors,
        ILeafletIndexingService indexing,
        ILogger<IndexLeafletHandler> logger)
    {
        _repo = repo;
        _extractors = extractors;
        _indexing = indexing;
        _logger = logger;
    }

    public async Task<IndexLeafletResponse> Handle(IndexLeafletRequest request, CancellationToken ct)
    {
        var hash = ComputeHash(request.Content);
        var useGraphIdentity = !string.IsNullOrEmpty(request.GraphItemId) && !string.IsNullOrEmpty(request.DriveId);

        var existing = await _repo.GetByHashAsync(hash, ct);
        if (existing is not null)
        {
            _logger.LogInformation(
                "Duplicate leaflet content detected, hash={Hash}, document={Id}",
                hash, existing.Id);

            if (useGraphIdentity && existing.GraphItemId is null)
            {
                _logger.LogInformation(
                    "Backfilling DriveId/GraphItemId for legacy leaflet document {Id}",
                    existing.Id);
                await _repo.UpdateGraphItemIdAsync(existing.Id, request.DriveId!, request.GraphItemId!, ct);
            }

            return new IndexLeafletResponse
            {
                DocumentId = existing.Id,
                WasDuplicate = true,
                Status = existing.Status,
                Filename = existing.Filename,
                ContentType = existing.ContentType,
                IngestedAt = existing.IngestedAt,
                IndexedAt = existing.IndexedAt,
            };
        }

        var existingByIdentity = useGraphIdentity
            ? await _repo.GetByGraphItemIdAsync(request.DriveId!, request.GraphItemId!, ct)
            : await _repo.GetBySourcePathAsync(request.SourcePath, ct);

        if (existingByIdentity is not null)
        {
            _logger.LogInformation("Replacing old document {Id} (identity match)", existingByIdentity.Id);
            await _repo.DeleteDocumentAsync(existingByIdentity.Id, ct);
        }

        var extractor = _extractors.FirstOrDefault(e => e.CanHandle(request.ContentType))
            ?? throw new NotSupportedException($"No extractor for content type '{request.ContentType}'");

        var text = await extractor.ExtractTextAsync(request.Content, ct);
        var wordCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        var doc = new LeafletDocument
        {
            Id = Guid.NewGuid(),
            Filename = request.Filename,
            SourcePath = request.SourcePath,
            ContentType = request.ContentType,
            ContentHash = hash,
            IngestedAt = DateTime.UtcNow,
            WordCount = wordCount,
            DriveId = useGraphIdentity ? request.DriveId : null,
            GraphItemId = useGraphIdentity ? request.GraphItemId : null,
            Status = LeafletDocumentStatus.Processing,
        };

        await _repo.AddDocumentAsync(doc, ct);

        try
        {
            var chunkCount = await _indexing.IndexAsync(text, doc, ct);

            var indexedAt = DateTime.UtcNow;
            await _repo.UpdateStatusAsync(doc.Id, LeafletDocumentStatus.Indexed, indexedAt, ct);

            return new IndexLeafletResponse
            {
                DocumentId = doc.Id,
                WasDuplicate = false,
                ChunkCount = chunkCount,
                Status = LeafletDocumentStatus.Indexed,
                Filename = doc.Filename,
                ContentType = doc.ContentType,
                IngestedAt = doc.IngestedAt,
                IndexedAt = indexedAt,
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to index leaflet document {Filename}", doc.Filename);
            try
            {
                await _repo.UpdateStatusAsync(doc.Id, LeafletDocumentStatus.Failed, null, ct);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "Failed to persist Failed status for document {Filename}", doc.Filename);
            }
            throw;
        }
    }

    private static string ComputeHash(byte[] content)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(content);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
```

- [ ] **Step 4: Update existing tests in `IndexLeafletHandlerTests.cs` and `IndexLeafletStatusTransitionTests.cs` to mock the narrower interface**

In **both** files do a project-level search-and-replace on the test class only:
- `Mock<ILeafletRepository>` → `Mock<ILeafletDocumentRepository>`
- `_repoMock = new Mock<ILeafletRepository>();` → `_repoMock = new Mock<ILeafletDocumentRepository>();`
- `_repoMock = new();` (when typed via the field declaration as `Mock<ILeafletRepository>`) — declaration switches to `Mock<ILeafletDocumentRepository>`.

Also remove any `SaveChangesAsync` setups/verifies from these test classes (search the file for `SaveChangesAsync`). There are none in these two files today, but verify with:
```bash
```
*(No bash needed — visually inspect.)*

- [ ] **Step 5: Run all `IndexLeaflet*` tests**

```bash
dotnet test backend/Anela.Heblo.sln \
  --filter "FullyQualifiedName~IndexLeafletHandlerTests|FullyQualifiedName~IndexLeafletStatusTransitionTests"
```
Expected: All tests PASS, including the new `Handle_happy_path_stamps_WordCount_on_document_before_AddDocumentAsync`.

- [ ] **Step 6: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/IndexLeaflet/IndexLeafletHandler.cs \
  backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/IndexLeafletHandlerTests.cs \
  backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/IndexLeafletStatusTransitionTests.cs
git commit -m "refactor(leaflet): IndexLeafletHandler uses ILeafletDocumentRepository and stamps WordCount up-front"
```

---

### Task 8: Migrate `LeafletIndexingService` — drop the `WordCount` mutation and switch to narrower interface

The handler now sets `WordCount`. The service no longer needs to.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/Services/LeafletIndexingService.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Leaflet/Services/LeafletIndexingServiceTests.cs`

- [ ] **Step 1: Update the failing test first — `IndexAsync_sets_WordCount_from_input_text` is now obsolete**

Open `LeafletIndexingServiceTests.cs`. **Delete** the test `IndexAsync_sets_WordCount_from_input_text` (lines ~144–172). Its responsibility moved to `IndexLeafletHandlerTests.Handle_happy_path_stamps_WordCount_on_document_before_AddDocumentAsync` (added in Task 7).

Also update mock declarations in this file:
- `private readonly Mock<ILeafletRepository> _repo;` → `private readonly Mock<ILeafletDocumentRepository> _repo;`
- `_repo = new Mock<ILeafletRepository>();` → `_repo = new Mock<ILeafletDocumentRepository>();`

- [ ] **Step 2: Update `LeafletIndexingService.cs` — switch interface and remove the mutation**

Replace the file with:

```csharp
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.Leaflet;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Leaflet.Services;

public class LeafletIndexingService : ILeafletIndexingService
{
    private readonly IWordWindowChunker _chunker;
    private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddings;
    private readonly ILeafletChunkSummarizer _summarizer;
    private readonly ILeafletDocumentRepository _repo;
    private readonly ILogger<LeafletIndexingService> _logger;
    private readonly LeafletOptions _options;

    public LeafletIndexingService(
        IWordWindowChunker chunker,
        IEmbeddingGenerator<string, Embedding<float>> embeddings,
        ILeafletChunkSummarizer summarizer,
        ILeafletDocumentRepository repo,
        ILogger<LeafletIndexingService> logger,
        IOptions<LeafletOptions> options)
    {
        _chunker = chunker;
        _embeddings = embeddings;
        _summarizer = summarizer;
        _repo = repo;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<int> IndexAsync(string text, LeafletDocument document, CancellationToken ct = default)
    {
        var chunkTexts = _chunker.Chunk(text, _options.ChunkSize, _options.ChunkOverlap);
        if (chunkTexts.Count == 0)
        {
            _logger.LogWarning("Leaflet {DocumentId} produced zero chunks; skipping indexing", document.Id);
            return 0;
        }

        var chunks = new List<LeafletChunk>();
        for (var i = 0; i < chunkTexts.Count; i++)
        {
            var content = chunkTexts[i];
            var summary = await _summarizer.SummarizeAsync(content, ct);
            chunks.Add(new LeafletChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = document.Id,
                ChunkIndex = i,
                Content = content,
                Summary = summary,
                WordCount = content.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length,
                Embedding = Array.Empty<float>(),
            });
        }

        var inputs = chunks.Select(c => c.Content).ToList();
        var generated = await _embeddings.GenerateAsync(inputs, cancellationToken: ct);
        var vectors = generated.ToList();

        if (vectors.Count != chunks.Count)
        {
            throw new InvalidOperationException(
                $"Embedding count {vectors.Count} does not match chunk count {chunks.Count}");
        }

        for (var i = 0; i < chunks.Count; i++)
        {
            chunks[i].Embedding = vectors[i].Vector.ToArray();
        }

        await _repo.AddChunksAsync(chunks, ct);
        return chunks.Count;
    }
}
```

(The only changes vs. today: field type is `ILeafletDocumentRepository`, ctor parameter type matches, and the `document.WordCount = …` line on the old line 75 is **gone**.)

- [ ] **Step 3: Run all `LeafletIndexingService*` tests**

```bash
dotnet test backend/Anela.Heblo.sln \
  --filter "FullyQualifiedName~LeafletIndexingServiceTests"
```
Expected: All tests PASS (the deleted WordCount test is gone; the rest are unchanged behavior).

- [ ] **Step 4: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Leaflet/Services/LeafletIndexingService.cs \
  backend/test/Anela.Heblo.Tests/Features/Leaflet/Services/LeafletIndexingServiceTests.cs
git commit -m "refactor(leaflet): LeafletIndexingService uses ILeafletDocumentRepository and stops mutating WordCount"
```

---

### Task 9: Migrate the small read-only document handlers

Four handlers do nothing but call read-only document methods. Same trivial change for all of them: swap field/ctor type, swap usings if needed, swap test mocks.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/DeleteLeafletDocument/DeleteLeafletDocumentHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletDocuments/GetLeafletDocumentsHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletDocumentContentTypes/GetLeafletDocumentContentTypesHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletChunkDetail/GetLeafletChunkDetailHandler.cs`
- Modify (corresponding tests): `DeleteLeafletDocumentHandlerTests.cs`, `GetLeafletDocumentsHandlerTests.cs`, `GetLeafletDocumentContentTypesHandlerTests.cs`, `GetLeafletChunkDetailHandlerTests.cs`

- [ ] **Step 1: In each of the four handler files, swap `ILeafletRepository` for `ILeafletDocumentRepository`**

For each file, change exactly two lines:
- Field declaration: `private readonly ILeafletRepository _leafletRepository;` → `private readonly ILeafletDocumentRepository _leafletRepository;`
- Constructor parameter: `(ILeafletRepository leafletRepository)` → `(ILeafletDocumentRepository leafletRepository)`

(Note: `DeleteLeafletDocumentHandler` and `GetLeafletDocumentContentTypesHandler` use `_leafletRepository`; `GetLeafletChunkDetailHandler` uses `_leafletRepository` too; `GetLeafletDocumentsHandler` uses `_leafletRepository`. Field name is preserved as-is in all four — no rename per CLAUDE.md "surgical changes".)

- [ ] **Step 2: In each of the four corresponding test files, swap mock declarations**

For each test file, replace:
- `Mock<ILeafletRepository>` → `Mock<ILeafletDocumentRepository>`

- [ ] **Step 3: Build and run the four affected test classes**

```bash
dotnet test backend/Anela.Heblo.sln \
  --filter "FullyQualifiedName~DeleteLeafletDocumentHandlerTests|FullyQualifiedName~GetLeafletDocumentsHandlerTests|FullyQualifiedName~GetLeafletDocumentContentTypesHandlerTests|FullyQualifiedName~GetLeafletChunkDetailHandlerTests"
```
Expected: All tests PASS.

- [ ] **Step 4: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/DeleteLeafletDocument/DeleteLeafletDocumentHandler.cs \
  backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletDocuments/GetLeafletDocumentsHandler.cs \
  backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletDocumentContentTypes/GetLeafletDocumentContentTypesHandler.cs \
  backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletChunkDetail/GetLeafletChunkDetailHandler.cs \
  backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/DeleteLeafletDocumentHandlerTests.cs \
  backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletDocumentsHandlerTests.cs \
  backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletDocumentContentTypesHandlerTests.cs \
  backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletChunkDetailHandlerTests.cs
git commit -m "refactor(leaflet): document-only handlers depend on ILeafletDocumentRepository"
```

---

### Task 10: Migrate `GenerateLeafletHandler`

This handler only uses `SearchSimilarAsync` from the leaflet repo (line 59 of the current file). It depends on `ILeafletDocumentRepository`.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs`

- [ ] **Step 1: Change field and ctor types**

In `GenerateLeafletHandler.cs`:
- `private readonly ILeafletRepository _leaflets;` → `private readonly ILeafletDocumentRepository _leaflets;`
- Constructor parameter `ILeafletRepository leaflets` → `ILeafletDocumentRepository leaflets`

- [ ] **Step 2: Change mock declarations in the test file**

In `GenerateLeafletHandlerTests.cs`, replace `Mock<ILeafletRepository>` with `Mock<ILeafletDocumentRepository>` everywhere.

- [ ] **Step 3: Run the test class**

```bash
dotnet test backend/Anela.Heblo.sln \
  --filter "FullyQualifiedName~GenerateLeafletHandlerTests"
```
Expected: All tests PASS.

- [ ] **Step 4: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GenerateLeaflet/GenerateLeafletHandler.cs \
  backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GenerateLeafletHandlerTests.cs
git commit -m "refactor(leaflet): GenerateLeafletHandler depends on ILeafletDocumentRepository"
```

---

### Task 11: Migrate `LeafletIngestionJob`

The job only calls `UpdateSourcePathAsync` directly on the repo (line 90 of the current file). All other interaction is via MediatR.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/Infrastructure/Jobs/LeafletIngestionJob.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Leaflet/Infrastructure/LeafletIngestionJobTests.cs`

- [ ] **Step 1: Change field and ctor types**

In `LeafletIngestionJob.cs`:
- `private readonly ILeafletRepository _leafletRepository;` → `private readonly ILeafletDocumentRepository _leafletRepository;`
- Constructor parameter `ILeafletRepository leafletRepository` → `ILeafletDocumentRepository leafletRepository`

- [ ] **Step 2: Change mock declaration in the test file**

In `LeafletIngestionJobTests.cs`:
- `private readonly Mock<ILeafletRepository> _leafletRepository = new();` → `private readonly Mock<ILeafletDocumentRepository> _leafletRepository = new();`

- [ ] **Step 3: Run the test class**

```bash
dotnet test backend/Anela.Heblo.sln \
  --filter "FullyQualifiedName~LeafletIngestionJobTests"
```
Expected: All tests PASS.

- [ ] **Step 4: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Leaflet/Infrastructure/Jobs/LeafletIngestionJob.cs \
  backend/test/Anela.Heblo.Tests/Features/Leaflet/Infrastructure/LeafletIngestionJobTests.cs
git commit -m "refactor(leaflet): LeafletIngestionJob depends on ILeafletDocumentRepository"
```

---

### Task 12: Migrate generation read-side handlers and the logging behavior

Three consumers use only generation-side methods (`SaveGenerationAsync`, `GetGenerationByIdAsync`, `GetGenerationsPagedAsync`, `GetGenerationStatsAsync`).

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehavior.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletGeneration/GetLeafletGenerationHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletFeedbackList/GetLeafletFeedbackListHandler.cs`
- Modify (tests): `LeafletGenerationLoggingBehaviorTests.cs`, `GetLeafletFeedbackListHandlerTests.cs`

(There is no `GetLeafletGenerationHandlerTests.cs` today — the handler is only covered through integration paths.)

- [ ] **Step 1: In each handler/behavior file, swap `ILeafletRepository` for `ILeafletGenerationRepository`**

Two-line change per file (field type + ctor parameter type). Field names are preserved as-is.

- [ ] **Step 2: In the two test files that exist, swap mock declarations**

`LeafletGenerationLoggingBehaviorTests.cs` and `GetLeafletFeedbackListHandlerTests.cs`:
- `Mock<ILeafletRepository>` → `Mock<ILeafletGenerationRepository>`

- [ ] **Step 3: Run the affected test classes**

```bash
dotnet test backend/Anela.Heblo.sln \
  --filter "FullyQualifiedName~LeafletGenerationLoggingBehaviorTests|FullyQualifiedName~GetLeafletFeedbackListHandlerTests"
```
Expected: All tests PASS.

- [ ] **Step 4: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehavior.cs \
  backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletGeneration/GetLeafletGenerationHandler.cs \
  backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/GetLeafletFeedbackList/GetLeafletFeedbackListHandler.cs \
  backend/test/Anela.Heblo.Tests/Features/Leaflet/Pipeline/LeafletGenerationLoggingBehaviorTests.cs \
  backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/GetLeafletFeedbackListHandlerTests.cs
git commit -m "refactor(leaflet): generation read-side consumers depend on ILeafletGenerationRepository"
```

---

### Task 13: Migrate `SubmitLeafletFeedbackHandler` to `UpdateFeedbackAsync` (TDD)

This is the only handler with semantic changes: the mutate-then-`SaveChangesAsync` pattern is replaced by a single `UpdateFeedbackAsync` call whose return value drives the response.

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/SubmitLeafletFeedback/SubmitLeafletFeedbackHandler.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/SubmitLeafletFeedbackHandlerTests.cs`

- [ ] **Step 1: Rewrite `SubmitLeafletFeedbackHandlerTests.cs` against the new contract first**

Replace the entire file with:

```csharp
using Anela.Heblo.Application.Features.Leaflet.UseCases.SubmitLeafletFeedback;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Domain.Features.Users;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.UseCases;

public class SubmitLeafletFeedbackHandlerTests
{
    private readonly Mock<ILeafletGenerationRepository> _repo = new();
    private readonly Mock<ICurrentUserService> _userService = new();
    private const string UserId = "user-123";

    private SubmitLeafletFeedbackHandler CreateHandler() =>
        new(_repo.Object, _userService.Object);

    public SubmitLeafletFeedbackHandlerTests()
    {
        _userService.Setup(s => s.GetCurrentUser())
            .Returns(new CurrentUser(UserId, "Test User", "t@test.com", true));
    }

    private LeafletGeneration MakeGeneration(Guid? id = null, string? userId = null,
        int? precisionScore = null, int? styleScore = null) =>
        new()
        {
            Id = id ?? Guid.NewGuid(),
            Topic = "Vitamin C",
            UserId = userId ?? UserId,
            PrecisionScore = precisionScore,
            StyleScore = styleScore,
        };

    [Fact]
    public async Task Handle_WhenGenerationNotFound_ReturnsNotFound()
    {
        var id = Guid.NewGuid();
        _repo.Setup(r => r.GetGenerationByIdAsync(id, default)).ReturnsAsync((LeafletGeneration?)null);

        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest { GenerationId = id, PrecisionScore = 4, StyleScore = 3 }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.LeafletFeedbackNotFound);
        _repo.Verify(r => r.UpdateFeedbackAsync(
            It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ReturnsForbidden_WhenGenerationUserIdIsNull()
    {
        var generation = new LeafletGeneration { Id = Guid.NewGuid(), Topic = "Vitamin C", UserId = null };
        _repo.Setup(r => r.GetGenerationByIdAsync(generation.Id, default)).ReturnsAsync(generation);

        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest { GenerationId = generation.Id, PrecisionScore = 4, StyleScore = 3 }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _repo.Verify(r => r.UpdateFeedbackAsync(
            It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenUserDoesNotOwnGeneration_ReturnsForbidden()
    {
        var generation = MakeGeneration(userId: "other-user");
        _repo.Setup(r => r.GetGenerationByIdAsync(generation.Id, default)).ReturnsAsync(generation);

        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest { GenerationId = generation.Id, PrecisionScore = 4, StyleScore = 3 }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.Forbidden);
        _repo.Verify(r => r.UpdateFeedbackAsync(
            It.IsAny<Guid>(), It.IsAny<int?>(), It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_WhenRepoReportsAlreadySubmitted_ReturnsConflict()
    {
        var generation = MakeGeneration();
        _repo.Setup(r => r.GetGenerationByIdAsync(generation.Id, default)).ReturnsAsync(generation);
        _repo.Setup(r => r.UpdateFeedbackAsync(generation.Id, 4, 3, null, default))
            .ReturnsAsync(UpdateFeedbackResult.AlreadySubmitted);

        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest { GenerationId = generation.Id, PrecisionScore = 4, StyleScore = 3 }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.LeafletFeedbackAlreadySubmitted);
    }

    [Fact]
    public async Task Handle_WhenRepoReportsNotFound_ReturnsNotFound()
    {
        var generation = MakeGeneration();
        _repo.Setup(r => r.GetGenerationByIdAsync(generation.Id, default)).ReturnsAsync(generation);
        _repo.Setup(r => r.UpdateFeedbackAsync(generation.Id, 4, 3, null, default))
            .ReturnsAsync(UpdateFeedbackResult.NotFound);

        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest { GenerationId = generation.Id, PrecisionScore = 4, StyleScore = 3 }, default);

        result.Success.Should().BeFalse();
        result.ErrorCode.Should().Be(ErrorCodes.LeafletFeedbackNotFound);
    }

    [Fact]
    public async Task Handle_ValidRequest_CallsUpdateFeedbackWithProvidedScoresAndReturnsSuccess()
    {
        var generation = MakeGeneration();
        _repo.Setup(r => r.GetGenerationByIdAsync(generation.Id, default)).ReturnsAsync(generation);
        _repo.Setup(r => r.UpdateFeedbackAsync(generation.Id, 4, 5, "Very helpful", default))
            .ReturnsAsync(UpdateFeedbackResult.Updated);

        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest
            {
                GenerationId = generation.Id,
                PrecisionScore = 4,
                StyleScore = 5,
                Comment = "Very helpful",
            }, default);

        result.Success.Should().BeTrue();
        _repo.Verify(r => r.UpdateFeedbackAsync(generation.Id, 4, 5, "Very helpful", default), Times.Once);
    }

    [Fact]
    public async Task Handle_ValidRequestWithNoComment_PassesNullCommentToRepo()
    {
        var generation = MakeGeneration();
        _repo.Setup(r => r.GetGenerationByIdAsync(generation.Id, default)).ReturnsAsync(generation);
        _repo.Setup(r => r.UpdateFeedbackAsync(generation.Id, 3, 3, null, default))
            .ReturnsAsync(UpdateFeedbackResult.Updated);

        var result = await CreateHandler().Handle(
            new SubmitLeafletFeedbackRequest { GenerationId = generation.Id, PrecisionScore = 3, StyleScore = 3 }, default);

        result.Success.Should().BeTrue();
        _repo.Verify(r => r.UpdateFeedbackAsync(generation.Id, 3, 3, null, default), Times.Once);
    }
}
```

Note on what changed vs. the old test file:
- Mock type is `Mock<ILeafletGenerationRepository>`.
- Old `SaveChangesAsync` verifies are gone.
- Old "feedback already submitted" test that pre-populates `precisionScore`/`styleScore` on the local `MakeGeneration` is replaced by two repo-driven tests (`Handle_WhenRepoReportsAlreadySubmitted_ReturnsConflict` and the new `Handle_WhenRepoReportsNotFound_ReturnsNotFound`) — the handler now defers the already-submitted check to the repo.
- Old "asserts on local instance mutation" assertion (`generation.PrecisionScore.Should().Be(4)`) is replaced by `_repo.Verify(...UpdateFeedbackAsync(..., 4, 5, "Very helpful", ...))` — the same fact, observed on the call instead of on a mutated entity. This realises arch-review Risk Mitigation #2 (the verify-style alternative).

- [ ] **Step 2: Run the test class — it MUST fail (handler still calls `SaveChangesAsync`)**

```bash
dotnet test backend/Anela.Heblo.sln \
  --filter "FullyQualifiedName~SubmitLeafletFeedbackHandlerTests"
```
Expected: COMPILATION ERROR (`ILeafletGenerationRepository` does not have `SaveChangesAsync`; the handler still references the old type). This is the desired RED state.

- [ ] **Step 3: Replace `SubmitLeafletFeedbackHandler.cs` with the new implementation**

Write:
```csharp
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Domain.Features.Users;
using MediatR;

namespace Anela.Heblo.Application.Features.Leaflet.UseCases.SubmitLeafletFeedback;

public class SubmitLeafletFeedbackHandler
    : IRequestHandler<SubmitLeafletFeedbackRequest, SubmitLeafletFeedbackResponse>
{
    private readonly ILeafletGenerationRepository _repository;
    private readonly ICurrentUserService _currentUserService;

    public SubmitLeafletFeedbackHandler(
        ILeafletGenerationRepository repository,
        ICurrentUserService currentUserService)
    {
        _repository = repository;
        _currentUserService = currentUserService;
    }

    public async Task<SubmitLeafletFeedbackResponse> Handle(
        SubmitLeafletFeedbackRequest request,
        CancellationToken cancellationToken)
    {
        var generation = await _repository.GetGenerationByIdAsync(request.GenerationId, cancellationToken);
        if (generation is null)
            return new SubmitLeafletFeedbackResponse(ErrorCodes.LeafletFeedbackNotFound,
                new() { { "generationId", request.GenerationId.ToString() } });

        var currentUser = _currentUserService.GetCurrentUser();
        if (generation.UserId is null || currentUser.Id is null || generation.UserId != currentUser.Id)
            return new SubmitLeafletFeedbackResponse(ErrorCodes.Forbidden,
                new() { { "generationId", request.GenerationId.ToString() } });

        var result = await _repository.UpdateFeedbackAsync(
            request.GenerationId,
            request.PrecisionScore,
            request.StyleScore,
            request.Comment,
            cancellationToken);

        return result switch
        {
            UpdateFeedbackResult.Updated => new SubmitLeafletFeedbackResponse(),
            UpdateFeedbackResult.NotFound => new SubmitLeafletFeedbackResponse(
                ErrorCodes.LeafletFeedbackNotFound,
                new() { { "generationId", request.GenerationId.ToString() } }),
            UpdateFeedbackResult.AlreadySubmitted => new SubmitLeafletFeedbackResponse(
                ErrorCodes.LeafletFeedbackAlreadySubmitted,
                new() { { "generationId", request.GenerationId.ToString() } }),
            _ => throw new InvalidOperationException($"Unexpected UpdateFeedbackResult: {result}"),
        };
    }
}
```

- [ ] **Step 4: Run the test class — it MUST pass**

```bash
dotnet test backend/Anela.Heblo.sln \
  --filter "FullyQualifiedName~SubmitLeafletFeedbackHandlerTests"
```
Expected: All 7 tests PASS.

- [ ] **Step 5: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Application/Features/Leaflet/UseCases/SubmitLeafletFeedback/SubmitLeafletFeedbackHandler.cs \
  backend/test/Anela.Heblo.Tests/Features/Leaflet/UseCases/SubmitLeafletFeedbackHandlerTests.cs
git commit -m "refactor(leaflet): SubmitLeafletFeedbackHandler uses UpdateFeedbackAsync"
```

---

### Task 14: Update remaining test files that still reference `ILeafletRepository`

After Tasks 7–13, the remaining places that may still mention `ILeafletRepository` are:
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/Infrastructure/LeafletModuleIntegrationTests.cs` (registers `Mock.Of<ILeafletRepository>()` in DI on line 31).
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletRepositoryTests.cs` (instantiates the concrete `LeafletRepository`).
- `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletControllerTests.cs` (only if it mocks the repo — verify in Step 1).

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Leaflet/Infrastructure/LeafletModuleIntegrationTests.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletRepositoryTests.cs`
- Modify (only if it references the repo): `backend/test/Anela.Heblo.Tests/Features/Leaflet/LeafletControllerTests.cs`

- [ ] **Step 1: Inspect the controller test for any repo references**

```bash
```
*(Use Grep tool: `pattern: "ILeafletRepository|LeafletRepository", path: backend/test/Anela.Heblo.Tests/Features/Leaflet, output_mode: "content", -n: true"`.)*

For each remaining match, decide whether the test depends on document or generation methods (or both):
- If only document methods → switch to `Mock<ILeafletDocumentRepository>` / `LeafletDocumentRepository`.
- If only generation methods → switch to `Mock<ILeafletGenerationRepository>` / `LeafletGenerationRepository`.
- If both → register **both** mocks in the DI test fixture (the integration test fixture is the most likely case).

- [ ] **Step 2: Update `LeafletModuleIntegrationTests.cs`**

Replace the line:
```csharp
        services.AddSingleton(Mock.Of<ILeafletRepository>());
```
with:
```csharp
        services.AddSingleton(Mock.Of<ILeafletDocumentRepository>());
        services.AddSingleton(Mock.Of<ILeafletGenerationRepository>());
```
(The integration test resolves the indexing service and the logging behavior; both lean on the new interfaces.)

- [ ] **Step 3: Split `LeafletRepositoryTests.cs` into two files**

Rename the existing file to `LeafletDocumentRepositoryTests.cs` and update its class:
- Class name: `LeafletRepositoryTests` → `LeafletDocumentRepositoryTests`.
- Field: `private readonly LeafletRepository _repository;` → `private readonly LeafletDocumentRepository _repository;`.
- Constructor: `_repository = new LeafletRepository(_context);` → `_repository = new LeafletDocumentRepository(_context);`.

If the existing file contains tests for generation methods (`SaveGenerationAsync`, `GetGenerationByIdAsync`, etc.), move those tests into a new file `LeafletGenerationRepositoryTests.cs` with the analogous structure:
```csharp
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Leaflet;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet;

public class LeafletGenerationRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly LeafletGenerationRepository _repository;

    public LeafletGenerationRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"LeafletGenerationRepositoryTests_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new LeafletGenerationRepository(_context);
    }

    // ... move generation-method tests here verbatim ...

    public void Dispose() => _context.Dispose();
}
```

If `LeafletRepositoryTests.cs` contains **only** document-side tests (likely — the file we sampled only shows document tests), this step reduces to the rename above and no second file is needed.

- [ ] **Step 4: Add a focused test for the new `UpdateFeedbackAsync` happy path**

In `LeafletGenerationRepositoryTests.cs` (create the file if step 3 did not), add:

```csharp
[Fact]
public async Task UpdateFeedbackAsync_returns_NotFound_when_generation_missing()
{
    var result = await _repository.UpdateFeedbackAsync(
        Guid.NewGuid(), precisionScore: 4, styleScore: 5, comment: "x", default);

    Assert.Equal(UpdateFeedbackResult.NotFound, result);
}

[Fact]
public async Task UpdateFeedbackAsync_returns_AlreadySubmitted_when_score_already_present()
{
    var generation = new LeafletGeneration
    {
        Id = Guid.NewGuid(),
        Topic = "X",
        UserId = "u1",
        PrecisionScore = 3,
        CreatedAt = DateTimeOffset.UtcNow,
    };
    _context.LeafletGenerations.Add(generation);
    await _context.SaveChangesAsync();

    var result = await _repository.UpdateFeedbackAsync(
        generation.Id, precisionScore: 4, styleScore: 5, comment: "x", default);

    Assert.Equal(UpdateFeedbackResult.AlreadySubmitted, result);
}

[Fact]
public async Task UpdateFeedbackAsync_persists_scores_and_comment_then_returns_Updated()
{
    var generation = new LeafletGeneration
    {
        Id = Guid.NewGuid(),
        Topic = "X",
        UserId = "u1",
        CreatedAt = DateTimeOffset.UtcNow,
    };
    _context.LeafletGenerations.Add(generation);
    await _context.SaveChangesAsync();
    _context.ChangeTracker.Clear();

    var result = await _repository.UpdateFeedbackAsync(
        generation.Id, precisionScore: 4, styleScore: 5, comment: "great", default);

    Assert.Equal(UpdateFeedbackResult.Updated, result);

    _context.ChangeTracker.Clear();
    var reloaded = await _context.LeafletGenerations.FindAsync(generation.Id);
    Assert.Equal(4, reloaded!.PrecisionScore);
    Assert.Equal(5, reloaded.StyleScore);
    Assert.Equal("great", reloaded.FeedbackComment);
}
```

- [ ] **Step 5: Run the affected test classes**

```bash
dotnet test backend/Anela.Heblo.sln \
  --filter "FullyQualifiedName~LeafletDocumentRepositoryTests|FullyQualifiedName~LeafletGenerationRepositoryTests|FullyQualifiedName~LeafletModuleIntegrationTests"
```
Expected: All tests PASS, including the three new `UpdateFeedbackAsync_*` tests.

- [ ] **Step 6: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Leaflet/
git commit -m "test(leaflet): split repository tests and cover UpdateFeedbackAsync"
```

---

### Task 15: Confirm no production or test code still references `ILeafletRepository`

Time to verify Task 16's deletions are safe.

- [ ] **Step 1: Grep for any remaining references**

Run from repo root:
```bash
```
*(Use Grep tool: `pattern: "ILeafletRepository", path: backend, output_mode: "content", -n: true`.)*

Expected output: only the two soon-to-be-deleted files (`backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletRepository.cs` and `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletRepository.cs`) and the DI registration line in `PersistenceModule.cs`. Any other match is a missed migration — go back and migrate that consumer (re-run Task 9/10/11/12 as appropriate).

- [ ] **Step 2: Grep for `SaveChangesAsync` inside the application layer**

*(Use Grep tool: `pattern: "SaveChangesAsync", path: backend/src/Anela.Heblo.Application/Features/Leaflet, output_mode: "content", -n: true`.)*

Expected: zero matches. If any remain, fix in place (delete the call) before continuing.

- [ ] **Step 3: Run the full Leaflet test suite**

```bash
dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Leaflet" --logger "console;verbosity=minimal"
```
Expected: All tests PASS. Total count = (Step 0b baseline) − 1 (deleted `IndexAsync_sets_WordCount_from_input_text`) + 4 (new `Handle_happy_path_stamps_WordCount_on_document_before_AddDocumentAsync` + 3 `UpdateFeedbackAsync_*`).

---

### Task 16: Delete `ILeafletRepository` and `LeafletRepository`, drop the legacy DI registration

**Files:**
- Delete: `backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletRepository.cs`
- Delete: `backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/PersistenceModule.cs` — remove the legacy `AddScoped<ILeafletRepository, LeafletRepository>()` line added/kept in Task 6.

- [ ] **Step 1: Delete the two obsolete files**

```bash
rm backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletRepository.cs
rm backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletRepository.cs
```

- [ ] **Step 2: Remove the legacy DI line in `PersistenceModule.cs`**

Edit the Leaflet repositories block to leave only:
```csharp
        // Leaflet repositories
        services.AddScoped<ILeafletDocumentRepository, LeafletDocumentRepository>();
        services.AddScoped<ILeafletGenerationRepository, LeafletGenerationRepository>();
```

- [ ] **Step 3: Verify the solution still builds**

```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded.` with 0 errors. Any error here means a consumer was missed in earlier tasks — fix before committing.

- [ ] **Step 4: Re-run grep to confirm zero references remain**

*(Use Grep tool: `pattern: "ILeafletRepository", path: backend"`.)*
Expected: zero matches.

- [ ] **Step 5: Run the full Leaflet test suite one last time**

```bash
dotnet test backend/Anela.Heblo.sln --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Leaflet" --logger "console;verbosity=minimal"
```
Expected: All tests PASS.

- [ ] **Step 6: Run `dotnet format`**

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```
Expected: exit code 0 (no formatting fixes needed). If exit code is non-zero, run without `--verify-no-changes` and inspect the diff before committing.

- [ ] **Step 7: Commit**

```bash
git add \
  backend/src/Anela.Heblo.Domain/Features/Leaflet/ILeafletRepository.cs \
  backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletRepository.cs \
  backend/src/Anela.Heblo.Persistence/PersistenceModule.cs
git commit -m "refactor(leaflet): remove legacy ILeafletRepository and LeafletRepository"
```

---

### Task 17: Final whole-solution build, format, and test sweep

- [ ] **Step 1: Build the whole solution**

```bash
dotnet build backend/Anela.Heblo.sln
```
Expected: `Build succeeded.` with 0 errors and 0 warnings introduced by this work.

- [ ] **Step 2: Format check**

```bash
dotnet format backend/Anela.Heblo.sln --verify-no-changes
```
Expected: exit code 0.

- [ ] **Step 3: Run the entire backend test suite**

```bash
dotnet test backend/Anela.Heblo.sln --logger "console;verbosity=minimal"
```
Expected: All tests PASS — not just Leaflet tests. (The split should not affect other modules, but a clean full-suite run is the cheapest way to catch any regression in shared fixtures.)

- [ ] **Step 4: No commit** — the previous task already captured all source changes; this task is a verification gate only.

---

## Spec Coverage Check

| Spec section | Covered by |
|---|---|
| FR-1 `ILeafletDocumentRepository` interface | Task 2 |
| FR-2 `ILeafletGenerationRepository` interface (incl. `UpdateFeedbackAsync`) | Task 1 + Task 3 |
| FR-3 Remove `SaveChangesAsync` from domain interfaces | Tasks 2, 3 (interfaces); Tasks 7, 13 (handlers); Task 15 grep gate |
| FR-4 Preserve transactional batching in `IndexLeafletHandler` (incl. arch-review Decision 4 WordCount fix) | Task 7 (handler + WordCount-up-front test); Task 8 (drop service mutation) |
| FR-5 Migrate `SubmitLeafletFeedbackHandler` off `SaveChangesAsync` | Task 13 |
| FR-6 Update repository implementation in persistence layer | Tasks 4, 5; Task 16 deletes the old class |
| FR-7 Update DI registration | Task 6 (add new) + Task 16 (drop old) |
| FR-8 Update all consumer handlers/services to narrower interfaces | Tasks 7 (IndexLeaflet), 8 (IndexingService), 9 (4 read handlers), 10 (Generate), 11 (IngestionJob), 12 (3 generation consumers), 13 (Submit) |
| FR-9 Update tests | Tasks 7–14 (each consumer update includes its tests); Task 14 covers integration + repo tests |
| NFR-1 Performance | Tasks 4 + 5 preserve all SQL/AsNoTracking/CommandTimeout; only behavioral change is `AddDocumentAsync` eager commit (one extra round-trip, negligible vs. embeddings cost) |
| NFR-2 Security | No security surface change — Task 13 keeps the auth check in the handler before `UpdateFeedbackAsync` (arch-review Decision 3) |
| NFR-3 Backwards compatibility | No public-API change; verified by Task 17 full-suite test run |
| NFR-4 Test coverage | New tests added in Tasks 7, 14; deleted obsolete WordCount test in Task 8 |
| NFR-5 Code style | Task 16 Step 6 + Task 17 Step 2 enforce `dotnet format` |
| Arch-review Decision 1 (auto-commit per write method) | Task 4 (`AddDocumentAsync` eager commit); Task 5 (`SaveGenerationAsync`, `UpdateFeedbackAsync` commit internally) |
| Arch-review Decision 2 (`UpdateFeedbackResult` enum) | Tasks 1, 3, 5, 13 |
| Arch-review Decision 3 (auth in handler, not repo) | Task 13 (handler keeps owner check) |
| Arch-review Decision 4 (WordCount in handler) | Task 7 Steps 1–3; Task 8 Step 2 |
| Arch-review Risk #1 mitigation (WordCount regression test) | Task 7 Step 1 |
| Arch-review Risk #2 mitigation (test asserts via Verify, not entity mutation) | Task 13 Step 1 |

No spec requirements unaccounted for.
