### task: add-batch-boundary-and-reader-mapping-tests


#### Context (self-contained — restate, do not assume prior sections are visible)

You are adding 5 new `[Fact]` test methods to an **existing** file:
`backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletRepositoryIntegrationTests.cs`

This file already has this exact shape (do not change any of the following —
reuse it as-is):

```csharp
using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Leaflet;
using DotNet.Testcontainers.Configurations;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.Integration;

[Trait("Category", "Integration")]
public class LeafletRepositoryIntegrationTests : IAsyncLifetime
{
    static LeafletRepositoryIntegrationTests()
    {
        // Podman does not support the Ryuk/ResourceReaper container; disable it to avoid NullReferenceException
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    private ApplicationDbContext _context = null!;
    private LeafletDocumentRepository _repository = null!;

    public async Task InitializeAsync() { /* starts container, builds ApplicationDbContext via
        NpgsqlDataSourceBuilder(...).UseVector(), calls SetupSchemaAsync(), constructs _repository */ }
    public async Task DisposeAsync() { /* disposes _context and _container */ }
    private async Task SetupSchemaAsync() { /* hand-rolled DDL for "LeafletDocuments" + "LeafletChunks"
        tables + HNSW index on a vector(3) "Embedding" column — do not change */ }

    private static LeafletDocument MakeDocument(
        string filename = "test.pdf",
        string hash = "abc123",
        string? driveId = null,
        string? graphItemId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Filename = filename,
            SourcePath = $"/leaflets/{filename}",
            ContentType = "application/pdf",
            ContentHash = hash,
            Status = LeafletDocumentStatus.Indexed,
            IngestedAt = DateTime.UtcNow,
            WordCount = 100,
            DriveId = driveId,
            GraphItemId = graphItemId,
            IndexedAt = DateTime.UtcNow
        };

    // ... 14 existing [Fact] tests, including:
    //   AddChunksAsync_PersistsSummary
    //   AddChunksAsync_IsIdempotent_WhenCalledTwiceWithSameId
    //   AddChunksAsync_PersistsAllRows_WhenMultipleChunks   <-- ends the AddChunksAsync_* block
    //   AddChunksAsync_IsNoOp_WhenInputEmpty
    //   AddDocumentAndChunks_CanBeRetrievedByHash
    //   SearchSimilarAsync_ReturnsClosestChunkByCosineSimilarity
    //   SearchSimilarAsync_ReturnsChunkWithSummary            <-- ends the SearchSimilarAsync_* block
    //   GetChunkByIdAsync_ReturnsChunkWithDocument_WhenExists
    //   ... (DeleteDocumentAsync_CascadesToChunks, GetChunkByIdAsync_ReturnsNull_WhenNotExists,
    //        GetByGraphItemIdAsync_* x3)
}
```

The domain types you'll use (`backend/src/Anela.Heblo.Domain/Features/Leaflet/`):

```csharp
// LeafletDocument.cs
public class LeafletDocument
{
    public Guid Id { get; set; }
    public string Filename { get; set; } = string.Empty;
    public string SourcePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public DateTime IngestedAt { get; set; }
    public int WordCount { get; set; }
    public string? DriveId { get; set; }
    public string? GraphItemId { get; set; }
    public LeafletDocumentStatus Status { get; set; } = LeafletDocumentStatus.Processing;
    public DateTime? IndexedAt { get; set; }
    public ICollection<LeafletChunk> Chunks { get; set; } = new List<LeafletChunk>();
}

// LeafletChunk.cs
public class LeafletChunk
{
    public Guid Id { get; set; }
    public Guid DocumentId { get; set; }
    public int ChunkIndex { get; set; }
    public string Content { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public int WordCount { get; set; }
    public float[] Embedding { get; set; } = Array.Empty<float>();
    public LeafletDocument Document { get; set; } = null!;
}
```

The repository methods under test
(`backend/src/Anela.Heblo.Persistence/Features/Leaflet/LeafletDocumentRepository.cs`):

- `public async Task AddChunksAsync(IEnumerable<LeafletChunk> chunks, CancellationToken ct = default)` —
  loops `for (var offset = 0; offset < chunkList.Count; offset += MaxRowsPerBatch)` where
  `MaxRowsPerBatch = 1000`, building one multi-row raw `INSERT ... ON CONFLICT ("Id") DO NOTHING`
  per batch, each with its own fresh `NpgsqlCommand` and its own `@id0..`-style parameter set.
- `public async Task<List<(LeafletChunk Chunk, double Score)>> SearchSimilarAsync(float[] queryEmbedding, int topK, CancellationToken ct = default)` —
  raw SQL: `SELECT c."Id", c."DocumentId", c."ChunkIndex", c."Content", c."Summary", c."WordCount", d."Filename", d."SourcePath", 1 - (c."Embedding" <=> @embedding) AS "Score" FROM "LeafletChunks" c JOIN "LeafletDocuments" d ON d."Id" = c."DocumentId" ORDER BY c."Embedding" <=> @embedding LIMIT @topK`,
  with `CommandTimeout = 120`. The reader always sets `chunk.Embedding = []` (never reads an
  embedding column back) — this is intentional, existing behavior.

`_repository.AddDocumentAsync(doc)` commits eagerly (`SaveChangesAsync` inside the method), so it
is safe to call before `AddChunksAsync` to satisfy the FK.

#### Step 1 — write the two `AddChunksAsync` batch-boundary tests (FR-1.1, FR-1.2)

Open `backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletRepositoryIntegrationTests.cs`.
Find the existing method `AddChunksAsync_IsNoOp_WhenInputEmpty` (it ends right before
`AddDocumentAndChunks_CanBeRetrievedByHash`). Insert the following two methods immediately after
`AddChunksAsync_IsNoOp_WhenInputEmpty`'s closing brace, before `AddDocumentAndChunks_CanBeRetrievedByHash`:

```csharp
    [Fact]
    public async Task AddChunksAsync_PersistsAll_AtExactBatchBoundary()
    {
        // Arrange: exactly MaxRowsPerBatch (1000) chunks — a single batch, boundary edge.
        var doc = MakeDocument("batch-boundary-test.pdf", "leaflet-hash-100");
        await _repository.AddDocumentAsync(doc);

        var chunks = Enumerable.Range(0, 1000)
            .Select(i => new LeafletChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = doc.Id,
                ChunkIndex = i,
                Content = $"Boundary content {i}",
                Summary = $"Boundary summary {i}",
                WordCount = i + 1,
                Embedding = [(float)i, 0f, 0f]
            })
            .ToList();

        // Act
        await _repository.AddChunksAsync(chunks);

        // Assert: all 1000 rows persisted, first and last of the single batch round-trip correctly.
        var stored = await _context.LeafletChunks
            .AsNoTracking()
            .Where(c => c.DocumentId == doc.Id)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync();

        Assert.Equal(1000, stored.Count);

        var first = stored[0];
        Assert.Equal(0, first.ChunkIndex);
        Assert.Equal("Boundary content 0", first.Content);
        Assert.Equal("Boundary summary 0", first.Summary);
        Assert.Equal(1, first.WordCount);

        var last = stored[999];
        Assert.Equal(999, last.ChunkIndex);
        Assert.Equal("Boundary content 999", last.Content);
        Assert.Equal("Boundary summary 999", last.Summary);
        Assert.Equal(1000, last.WordCount);
    }

    [Fact]
    public async Task AddChunksAsync_PersistsAll_AcrossTwoBatches()
    {
        // Arrange: MaxRowsPerBatch (1000) + 1 = 1001 chunks — forces a second INSERT with a
        // fresh NpgsqlCommand and its own @id0-style parameter set (proves no parameter-name collision).
        var doc = MakeDocument("two-batch-test.pdf", "leaflet-hash-101");
        await _repository.AddDocumentAsync(doc);

        var chunks = Enumerable.Range(0, 1001)
            .Select(i => new LeafletChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = doc.Id,
                ChunkIndex = i,
                Content = $"TwoBatch content {i}",
                Summary = $"TwoBatch summary {i}",
                WordCount = i + 1,
                Embedding = [(float)i, 0f, 0f]
            })
            .ToList();

        // Act: single call internally issues two INSERTs (batch 1: ChunkIndex 0..999, batch 2: 1000).
        await _repository.AddChunksAsync(chunks);

        // Assert: no row silently dropped or duplicated across the boundary.
        var stored = await _context.LeafletChunks
            .AsNoTracking()
            .Where(c => c.DocumentId == doc.Id)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync();

        Assert.Equal(1001, stored.Count);

        // Last row of batch 1.
        var lastOfBatch1 = stored[999];
        Assert.Equal(999, lastOfBatch1.ChunkIndex);
        Assert.Equal("TwoBatch content 999", lastOfBatch1.Content);
        Assert.Equal("TwoBatch summary 999", lastOfBatch1.Summary);
        Assert.Equal(1000, lastOfBatch1.WordCount);

        // Sole row of batch 2 — proves the second NpgsqlCommand's own parameter set executed
        // correctly and did not collide with or overwrite batch 1's parameters.
        var onlyRowOfBatch2 = stored[1000];
        Assert.Equal(1000, onlyRowOfBatch2.ChunkIndex);
        Assert.Equal("TwoBatch content 1000", onlyRowOfBatch2.Content);
        Assert.Equal("TwoBatch summary 1000", onlyRowOfBatch2.Summary);
        Assert.Equal(1001, onlyRowOfBatch2.WordCount);
    }
```

Note (do not act on this — informational only, satisfies FR-1's acceptance criterion): the
connection-state guard (`if (connection.State != System.Data.ConnectionState.Open)`) is already
exercised by the existing `AddChunksAsync_PersistsSummary` test (closed → open branch, first call
in a fresh container) and `AddChunksAsync_IsIdempotent_WhenCalledTwiceWithSameId` (its second call
hits the already-open branch). No dedicated test for this guard is added or needed here.

Run:
```
cd backend
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~LeafletRepositoryIntegrationTests&FullyQualifiedName~AddChunksAsync_PersistsAll"
```
Expected: this should **fail to compile or fail to run** only if you made a typo; assuming the
code above is pasted correctly, expect:
```
Passed!  - Failed:     0, Passed:     2, Skipped:     0, Total:     2
```
(Each test starts its own Postgres container, so this may take ~5-15 seconds per test.)

#### Step 2 — write the three `SearchSimilarAsync` reader-mapping tests (FR-2.1, FR-2.2, FR-2.3)

Find the existing method `SearchSimilarAsync_ReturnsChunkWithSummary` (it ends right before
`GetChunkByIdAsync_ReturnsChunkWithDocument_WhenExists`). Insert the following three methods
immediately after `SearchSimilarAsync_ReturnsChunkWithSummary`'s closing brace, before
`GetChunkByIdAsync_ReturnsChunkWithDocument_WhenExists`:

```csharp
    [Fact]
    public async Task SearchSimilarAsync_MapsAllReaderColumns_AcrossTwoDocuments()
    {
        // Arrange: two distinct documents, one chunk each, orthogonal embeddings so similarity
        // ordering is unambiguous.
        var doc1 = MakeDocument("mapping-doc-one.pdf", "leaflet-hash-102");
        var doc2 = MakeDocument("mapping-doc-two.pdf", "leaflet-hash-103");
        await _repository.AddDocumentAsync(doc1);
        await _repository.AddDocumentAsync(doc2);

        var chunk1 = new LeafletChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = doc1.Id,
            ChunkIndex = 7,
            Content = "Doc one content",
            Summary = "Doc one summary",
            WordCount = 11,
            Embedding = [1.0f, 0.0f, 0.0f]
        };
        var chunk2 = new LeafletChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = doc2.Id,
            ChunkIndex = 3,
            Content = "Doc two content",
            Summary = "Doc two summary",
            WordCount = 13,
            Embedding = [0.0f, 1.0f, 0.0f]
        };
        await _repository.AddChunksAsync([chunk1, chunk2]);

        // Act: query aligned with chunk1 => chunk1 must be the top (closest) result.
        var results = await _repository.SearchSimilarAsync([1.0f, 0.0f, 0.0f], topK: 2);

        // Assert
        Assert.Equal(2, results.Count);
        var top = results[0];

        Assert.Equal(chunk1.Id, top.Chunk.Id);
        Assert.Equal(doc1.Id, top.Chunk.DocumentId);
        Assert.Equal(7, top.Chunk.ChunkIndex);
        Assert.Equal("Doc one content", top.Chunk.Content);
        Assert.Equal("Doc one summary", top.Chunk.Summary);
        Assert.Equal(11, top.Chunk.WordCount);

        // Proves the JOIN on ordinals 6/7 resolves to the correct (own) document per row,
        // not a fixed/first document.
        Assert.Equal(doc1.Id, top.Chunk.Document.Id);
        Assert.Equal("mapping-doc-one.pdf", top.Chunk.Document.Filename);
        Assert.Equal("/leaflets/mapping-doc-one.pdf", top.Chunk.Document.SourcePath);

        // Intentional current behavior: the reader never populates Embedding.
        Assert.Empty(top.Chunk.Embedding);

        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public async Task SearchSimilarAsync_TopKLimitsResultCount()
    {
        // Arrange: 3 chunks with distinct embeddings.
        var doc = MakeDocument("topk-test.pdf", "leaflet-hash-104");
        await _repository.AddDocumentAsync(doc);

        var chunks = new[]
        {
            new LeafletChunk { Id = Guid.NewGuid(), DocumentId = doc.Id, ChunkIndex = 0, Content = "A", Summary = "A", WordCount = 1, Embedding = [1.0f, 0.0f, 0.0f] },
            new LeafletChunk { Id = Guid.NewGuid(), DocumentId = doc.Id, ChunkIndex = 1, Content = "B", Summary = "B", WordCount = 1, Embedding = [0.0f, 1.0f, 0.0f] },
            new LeafletChunk { Id = Guid.NewGuid(), DocumentId = doc.Id, ChunkIndex = 2, Content = "C", Summary = "C", WordCount = 1, Embedding = [0.0f, 0.0f, 1.0f] },
        };
        await _repository.AddChunksAsync(chunks);

        // Act
        var results = await _repository.SearchSimilarAsync([1.0f, 0.0f, 0.0f], topK: 1);

        // Assert: LIMIT @topK is actually wired, not just present in the SQL text.
        Assert.Single(results);
    }

    [Fact]
    public async Task SearchSimilarAsync_ReturnsEmptyList_WhenNoChunks()
    {
        // Arrange: a document with zero chunks.
        var doc = MakeDocument("no-chunks-test.pdf", "leaflet-hash-105");
        await _repository.AddDocumentAsync(doc);

        // Act
        var results = await _repository.SearchSimilarAsync([1.0f, 0.0f, 0.0f], topK: 5);

        // Assert: zero-iteration reader loop returns an empty, non-null list, no exception.
        Assert.NotNull(results);
        Assert.Empty(results);
    }
```

Note (informational only, satisfies FR-2's acceptance criterion): `CommandTimeout = 120` is a
fixed literal, structurally exercised by every call to `SearchSimilarAsync` above (satisfying line
coverage). No dedicated timeout-triggering test is added — that would require actually waiting out
or mocking a 120-second timeout, which is not a reasonable use of test time.

Run:
```
cd backend
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~LeafletRepositoryIntegrationTests&FullyQualifiedName~SearchSimilarAsync"
```
Expected:
```
Passed!  - Failed:     0, Passed:     5, Skipped:     0, Total:     5
```
(5 = the 2 existing `SearchSimilarAsync_*` tests plus the 3 new ones.)

#### Step 3 — run the full extended file and confirm nothing regressed

```
cd backend
dotnet test Anela.Heblo.sln --filter "FullyQualifiedName~LeafletRepositoryIntegrationTests"
```
Expected:
```
Passed!  - Failed:     0, Passed:    19, Skipped:     0, Total:    19
```
(19 = 14 existing + 5 new: 2 batch-boundary + 3 reader-mapping.)

Also run the non-Integration suite to confirm this change did not affect anything else (this file
is Integration-tagged, so it should not appear, but this validates the build overall):
```
cd backend
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes
```
Expected: build succeeds with 0 errors; `dotnet format --verify-no-changes` exits 0 (no formatting
diffs). If `dotnet format` reports diffs, run `dotnet format Anela.Heblo.sln` (without
`--verify-no-changes`) to apply them, then re-run the verify command.

#### Step 4 — commit

```
git add backend/test/Anela.Heblo.Tests/Features/Leaflet/Integration/LeafletRepositoryIntegrationTests.cs
git commit -m "Add AddChunksAsync batch-boundary and SearchSimilarAsync reader-mapping tests

Closes the coverage gap on LeafletDocumentRepository's multi-batch INSERT
loop (exact 1000-row boundary and the 1001-row two-batch path) and on the
raw-SQL reader mapping in SearchSimilarAsync (all 9 reader ordinals, the
cross-document JOIN, the intentional empty Embedding, topK limiting, and
the empty-result path). Test-only change; Category=Integration, excluded
from CI coverage runs per existing workflow filters."
```

---
