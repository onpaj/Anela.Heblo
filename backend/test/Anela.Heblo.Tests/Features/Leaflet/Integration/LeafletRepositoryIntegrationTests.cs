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
