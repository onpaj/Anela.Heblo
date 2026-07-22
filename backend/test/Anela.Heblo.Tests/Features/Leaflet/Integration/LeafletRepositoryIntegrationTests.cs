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

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(_container.GetConnectionString());
        dataSourceBuilder.UseVector();
        var dataSource = dataSourceBuilder.Build();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(dataSource)
            .Options;

        _context = new ApplicationDbContext(options);

        await SetupSchemaAsync();
        _repository = new LeafletDocumentRepository(_context);
    }

    public async Task DisposeAsync()
    {
        await _context.DisposeAsync();
        await _container.DisposeAsync();
    }

    private async Task SetupSchemaAsync()
    {
        await using var conn = new NpgsqlConnection(_container.GetConnectionString());
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE EXTENSION IF NOT EXISTS vector;

            CREATE TABLE IF NOT EXISTS public."LeafletDocuments" (
                "Id"          uuid NOT NULL PRIMARY KEY,
                "Filename"    text NOT NULL,
                "SourcePath"  text NOT NULL,
                "ContentType" text NOT NULL,
                "ContentHash" varchar(64) NOT NULL,
                "IngestedAt"  timestamp NOT NULL,
                "WordCount"   integer NOT NULL,
                "DriveId"     text NULL,
                "GraphItemId" text NULL,
                "Status"      varchar(16) NOT NULL DEFAULT 'processing',
                "IndexedAt"   timestamp NULL
            );

            CREATE TABLE IF NOT EXISTS public."LeafletChunks" (
                "Id"          uuid NOT NULL PRIMARY KEY,
                "DocumentId"  uuid NOT NULL REFERENCES public."LeafletDocuments"("Id") ON DELETE CASCADE,
                "ChunkIndex"  integer NOT NULL,
                "Content"     text NOT NULL DEFAULT '',
                "Summary"     text NOT NULL DEFAULT '',
                "WordCount"   integer NOT NULL,
                "Embedding"   vector(3)
            );

            CREATE INDEX IF NOT EXISTS idx_leaflet_chunks_embedding
                ON public."LeafletChunks"
                USING hnsw ("Embedding" vector_cosine_ops)
                WITH (m = 16, ef_construction = 64);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

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

    [Fact]
    public async Task AddChunksAsync_PersistsSummary()
    {
        // Arrange: insert a document via EF + SaveChanges, then build a chunk with non-empty Summary
        var doc = MakeDocument("summary-test.pdf", "leaflet-hash-001");
        // AddDocumentAsync commits eagerly; raw SQL chunks require the FK row to already exist
        await _repository.AddDocumentAsync(doc);

        var chunk = new LeafletChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            ChunkIndex = 0,
            Content = "Full chunk content",
            Summary = "Test summary content",
            WordCount = 3,
            Embedding = [0.1f, 0.2f, 0.3f]
        };

        // Act
        await _repository.AddChunksAsync([chunk]);

        // Assert: read back via EF AsNoTracking
        var stored = await _context.LeafletChunks
            .AsNoTracking()
            .FirstAsync(c => c.Id == chunk.Id);
        Assert.Equal("Test summary content", stored.Summary);
    }

    [Fact]
    public async Task AddChunksAsync_IsIdempotent_WhenCalledTwiceWithSameId()
    {
        // Arrange
        var doc = MakeDocument("idempotent-test.pdf", "leaflet-hash-002");
        await _repository.AddDocumentAsync(doc);

        var chunk = new LeafletChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            ChunkIndex = 0,
            Content = "Idempotent content",
            Summary = "Idempotent summary",
            WordCount = 2,
            Embedding = [0.1f, 0.2f, 0.3f]
        };

        // Act: first insert
        await _repository.AddChunksAsync([chunk]);

        // Second insert with same Id — ON CONFLICT DO NOTHING, must not throw
        var exception = await Record.ExceptionAsync(() => _repository.AddChunksAsync([chunk]));
        Assert.Null(exception);

        // Assert: only one row should exist
        var rows = await _context.LeafletChunks
            .Where(c => c.DocumentId == doc.Id)
            .ToListAsync();
        Assert.Single(rows);
    }

    [Fact]
    public async Task AddChunksAsync_PersistsAllRows_WhenMultipleChunks()
    {
        // Arrange
        var doc = MakeDocument("multi-chunk-test.pdf", "leaflet-hash-010");
        await _repository.AddDocumentAsync(doc);

        var chunks = Enumerable.Range(0, 5)
            .Select(i => new LeafletChunk
            {
                Id = Guid.NewGuid(),
                DocumentId = doc.Id,
                ChunkIndex = i,
                Content = $"Content {i}",
                Summary = $"Summary {i}",
                WordCount = i + 1,
                Embedding = [0.1f * i, 0.2f * i, 0.3f * i]
            })
            .ToList();

        // Act: single call inserts all five rows in one multi-row INSERT
        await _repository.AddChunksAsync(chunks);

        // Assert: every row persisted with its own distinct values
        var stored = await _context.LeafletChunks
            .AsNoTracking()
            .Where(c => c.DocumentId == doc.Id)
            .OrderBy(c => c.ChunkIndex)
            .ToListAsync();

        Assert.Equal(5, stored.Count);
        for (var i = 0; i < 5; i++)
        {
            Assert.Equal(chunks[i].Id, stored[i].Id);
            Assert.Equal(i, stored[i].ChunkIndex);
            Assert.Equal($"Content {i}", stored[i].Content);
            Assert.Equal($"Summary {i}", stored[i].Summary);
            Assert.Equal(i + 1, stored[i].WordCount);
        }
    }

    [Fact]
    public async Task AddChunksAsync_IsNoOp_WhenInputEmpty()
    {
        // Arrange
        var doc = MakeDocument("empty-input-test.pdf", "leaflet-hash-011");
        await _repository.AddDocumentAsync(doc);

        // Act: empty enumerable must not throw and must issue no INSERT
        var exception = await Record.ExceptionAsync(
            () => _repository.AddChunksAsync(Array.Empty<LeafletChunk>()));

        // Assert
        Assert.Null(exception);
        var rows = await _context.LeafletChunks
            .Where(c => c.DocumentId == doc.Id)
            .ToListAsync();
        Assert.Empty(rows);
    }

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

    [Fact]
    public async Task AddDocumentAndChunks_CanBeRetrievedByHash()
    {
        // Arrange
        var doc = MakeDocument("hash-test.pdf", "leaflet-hash-003");
        await _repository.AddDocumentAsync(doc);

        var chunk = new LeafletChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            ChunkIndex = 0,
            Content = "Test content",
            Summary = "Test summary",
            WordCount = 2,
            Embedding = [0.1f, 0.2f, 0.3f]
        };

        // Act
        await _repository.AddChunksAsync([chunk]);

        var found = await _repository.GetByHashAsync("leaflet-hash-003");

        // Assert
        Assert.NotNull(found);
        Assert.Equal(doc.Id, found!.Id);
        Assert.Equal("hash-test.pdf", found.Filename);
    }

    [Fact]
    public async Task SearchSimilarAsync_ReturnsClosestChunkByCosineSimilarity()
    {
        // Arrange
        var doc = MakeDocument("search-test.pdf", "leaflet-hash-004");
        await _repository.AddDocumentAsync(doc);

        var chunk1 = new LeafletChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            ChunkIndex = 0,
            Content = "Close to query",
            Summary = "Close summary",
            WordCount = 3,
            Embedding = [1.0f, 0.0f, 0.0f]
        };

        var chunk2 = new LeafletChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            ChunkIndex = 1,
            Content = "Far from query",
            Summary = "Far summary",
            WordCount = 3,
            Embedding = [0.0f, 1.0f, 0.0f]
        };

        await _repository.AddChunksAsync([chunk1, chunk2]);

        // Act: query vector aligned with chunk1
        var results = await _repository.SearchSimilarAsync([1.0f, 0.0f, 0.0f], topK: 2);

        // Assert
        Assert.Equal(2, results.Count);
        Assert.Equal(chunk1.Id, results[0].Chunk.Id);
        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public async Task SearchSimilarAsync_ReturnsChunkWithSummary()
    {
        // Arrange
        var doc = MakeDocument("search-summary-test.pdf", "leaflet-hash-009");
        await _repository.AddDocumentAsync(doc);

        var chunk = new LeafletChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            ChunkIndex = 0,
            Content = "Chunk with expected summary",
            Summary = "Expected search summary",
            WordCount = 4,
            Embedding = [1.0f, 0.0f, 0.0f]
        };

        await _repository.AddChunksAsync([chunk]);

        // Act
        var results = await _repository.SearchSimilarAsync([1.0f, 0.0f, 0.0f], topK: 1);

        // Assert
        Assert.Single(results);
        Assert.Equal("Expected search summary", results[0].Chunk.Summary);
    }

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

    [Fact]
    public async Task GetChunkByIdAsync_ReturnsChunkWithDocument_WhenExists()
    {
        // Arrange
        var doc = MakeDocument("chunk-detail-test.pdf", "leaflet-hash-005");
        await _repository.AddDocumentAsync(doc);

        var chunk = new LeafletChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            ChunkIndex = 0,
            Content = "Chunk content for detail test",
            Summary = "Detail summary",
            WordCount = 5,
            Embedding = [0.1f, 0.2f, 0.3f]
        };

        await _repository.AddChunksAsync([chunk]);

        // Act
        var result = await _repository.GetChunkByIdAsync(chunk.Id);

        // Assert
        Assert.NotNull(result);
        Assert.NotNull(result!.Document);
        Assert.Equal("chunk-detail-test.pdf", result.Document!.Filename);
    }

    [Fact]
    public async Task DeleteDocumentAsync_CascadesToChunks()
    {
        // Arrange
        var doc = MakeDocument("delete-test.pdf", "leaflet-hash-006");
        await _repository.AddDocumentAsync(doc);

        var chunk = new LeafletChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            ChunkIndex = 0,
            Content = "To be deleted",
            Summary = "Deletion summary",
            WordCount = 3,
            Embedding = [0.5f, 0.5f, 0.0f]
        };

        await _repository.AddChunksAsync([chunk]);

        // Act
        await _repository.DeleteDocumentAsync(doc.Id);

        // Assert
        var afterDelete = await _context.LeafletDocuments
            .Where(d => d.Id == doc.Id)
            .ToListAsync();
        var chunksAfterDelete = await _context.LeafletChunks
            .Where(c => c.DocumentId == doc.Id)
            .ToListAsync();

        Assert.Empty(afterDelete);
        Assert.Empty(chunksAfterDelete);
    }

    [Fact]
    public async Task GetChunkByIdAsync_ReturnsNull_WhenNotExists()
    {
        // Act
        var result = await _repository.GetChunkByIdAsync(Guid.NewGuid());

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByGraphItemIdAsync_ReturnsNull_WhenMissing()
    {
        // Act
        var result = await _repository.GetByGraphItemIdAsync("drive-x", "item-y");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByGraphItemIdAsync_ReturnsDocument_WhenBothFieldsMatch()
    {
        // Arrange
        var doc = MakeDocument("graph-leaflet.pdf", "leaflet-hash-007", driveId: "drive-leaflet", graphItemId: "item-leaflet-001");
        await _repository.AddDocumentAsync(doc);

        // Act
        var result = await _repository.GetByGraphItemIdAsync("drive-leaflet", "item-leaflet-001");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(doc.Id, result!.Id);
        Assert.Equal("drive-leaflet", result.DriveId);
        Assert.Equal("item-leaflet-001", result.GraphItemId);
    }

    [Fact]
    public async Task GetByGraphItemIdAsync_ReturnsNull_WhenOnlyDriveIdMatches()
    {
        // Arrange
        var doc = MakeDocument("graph-leaflet-partial.pdf", "leaflet-hash-008", driveId: "drive-leaflet", graphItemId: "item-leaflet-002");
        await _repository.AddDocumentAsync(doc);

        // Act
        var result = await _repository.GetByGraphItemIdAsync("drive-leaflet", "item-different");

        // Assert
        Assert.Null(result);
    }
}
