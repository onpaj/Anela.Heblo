using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.KnowledgeBase;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Integration;

[Trait("Category", "Integration")]
public class KnowledgeBaseRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    private ApplicationDbContext _context = null!;
    private KnowledgeBaseRepository _repository = null!;

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
        _repository = new KnowledgeBaseRepository(_context);
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
            CREATE SCHEMA IF NOT EXISTS dbo;

            CREATE TABLE IF NOT EXISTS dbo."KnowledgeBaseDocuments" (
                "Id"          uuid NOT NULL PRIMARY KEY,
                "Filename"    varchar(500) NOT NULL,
                "SourcePath"  varchar(1000) NOT NULL UNIQUE,
                "ContentType" varchar(100) NOT NULL,
                "ContentHash" varchar(64) NOT NULL UNIQUE,
                "Status"      varchar(50) NOT NULL,
                "CreatedAt"   timestamp NOT NULL,
                "IndexedAt"   timestamp NULL
            );

            CREATE TABLE IF NOT EXISTS dbo."KnowledgeBaseChunks" (
                "Id"          uuid NOT NULL PRIMARY KEY,
                "DocumentId"  uuid NOT NULL REFERENCES dbo."KnowledgeBaseDocuments"("Id") ON DELETE CASCADE,
                "ChunkIndex"  integer NOT NULL,
                "Content"     text NOT NULL,
                "Embedding"   vector(3)
            );

            CREATE INDEX IF NOT EXISTS idx_kb_chunks_embedding
                ON dbo."KnowledgeBaseChunks"
                USING hnsw ("Embedding" vector_cosine_ops)
                WITH (m = 16, ef_construction = 64);
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static KnowledgeBaseDocument MakeDocument(string filename = "test.pdf", string hash = "abc123") =>
        new()
        {
            Id = Guid.NewGuid(),
            Filename = filename,
            SourcePath = $"/inbox/{filename}",
            ContentType = "application/pdf",
            ContentHash = hash,
            Status = DocumentStatus.Indexed,
            CreatedAt = DateTime.UtcNow,
            IndexedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task AddDocumentAndChunks_ThenRetrieveByHash()
    {
        var doc = MakeDocument("hash-test.pdf", "deadbeef001");
        await _repository.AddDocumentAsync(doc);
        // SaveChanges must precede AddChunksAsync: chunks use raw SQL with FK to the document
        await _repository.SaveChangesAsync();

        var chunk = new KnowledgeBaseChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            ChunkIndex = 0,
            Content = "Test content",
            Embedding = [0.1f, 0.2f, 0.3f]
        };

        await _repository.AddChunksAsync([chunk]);

        var found = await _repository.GetDocumentByHashAsync("deadbeef001");

        Assert.NotNull(found);
        Assert.Equal(doc.Id, found!.Id);
        Assert.Equal("hash-test.pdf", found.Filename);
    }

    [Fact]
    public async Task SearchSimilarAsync_ReturnsClosestChunkByCosineSimilarity()
    {
        var doc = MakeDocument("search-test.pdf", "deadbeef002");
        await _repository.AddDocumentAsync(doc);
        // SaveChanges must precede AddChunksAsync: chunks use raw SQL with FK to the document
        await _repository.SaveChangesAsync();

        var chunk1 = new KnowledgeBaseChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            ChunkIndex = 0,
            Content = "Close to query",
            Embedding = [1.0f, 0.0f, 0.0f]
        };

        var chunk2 = new KnowledgeBaseChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            ChunkIndex = 1,
            Content = "Far from query",
            Embedding = [0.0f, 1.0f, 0.0f]
        };

        await _repository.AddChunksAsync([chunk1, chunk2]);

        // Query vector aligned with chunk1
        var results = await _repository.SearchSimilarAsync([1.0f, 0.0f, 0.0f], topK: 2);

        Assert.Equal(2, results.Count);
        Assert.Equal(chunk1.Id, results[0].Chunk.Id);
        Assert.True(results[0].Score > results[1].Score);
    }

    [Fact]
    public async Task DeleteDocumentAsync_CascadesToChunks()
    {
        var doc = MakeDocument("delete-test.pdf", "deadbeef003");
        await _repository.AddDocumentAsync(doc);
        // SaveChanges must precede AddChunksAsync: chunks use raw SQL with FK to the document
        await _repository.SaveChangesAsync();

        var chunk = new KnowledgeBaseChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            ChunkIndex = 0,
            Content = "To be deleted",
            Embedding = [0.5f, 0.5f, 0.0f]
        };

        await _repository.AddChunksAsync([chunk]);

        await _repository.DeleteDocumentAsync(doc.Id);

        var afterDelete = await _context.KnowledgeBaseDocuments
            .Where(d => d.Id == doc.Id)
            .ToListAsync();
        var chunksAfterDelete = await _context.KnowledgeBaseChunks
            .Where(c => c.DocumentId == doc.Id)
            .ToListAsync();

        Assert.Empty(afterDelete);
        Assert.Empty(chunksAfterDelete);
    }
}
