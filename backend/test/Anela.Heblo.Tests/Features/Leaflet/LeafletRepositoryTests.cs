using Anela.Heblo.Domain.Features.Leaflet;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Leaflet;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet;

public class LeafletRepositoryTests : IDisposable
{
    private readonly ApplicationDbContext _context;
    private readonly LeafletRepository _repository;

    public LeafletRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"LeafletRepositoryTests_{Guid.NewGuid()}")
            .Options;

        _context = new ApplicationDbContext(options);
        _repository = new LeafletRepository(_context);
    }

    [Fact]
    public async Task GetByHashAsync_returns_null_when_missing()
    {
        // Act
        var result = await _repository.GetByHashAsync("nonexistent-hash-abc123");

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByHashAsync_returns_document_when_present()
    {
        // Arrange
        var document = new LeafletDocument
        {
            Id = Guid.NewGuid(),
            Filename = "test-leaflet.pdf",
            SourcePath = "/leaflets/test-leaflet.pdf",
            ContentType = "application/pdf",
            ContentHash = "abc123def456",
            IngestedAt = DateTime.UtcNow,
            WordCount = 500
        };

        await _context.LeafletDocuments.AddAsync(document);
        await _context.SaveChangesAsync();

        // Act
        var result = await _repository.GetByHashAsync("abc123def456");

        // Assert
        Assert.NotNull(result);
        Assert.Equal(document.Id, result.Id);
        Assert.Equal(document.Filename, result.Filename);
        Assert.Equal(document.ContentHash, result.ContentHash);
    }

    [Fact(Skip = "ExecuteDeleteAsync is a relational operation not supported by the in-memory EF provider. Verified against real PostgreSQL.")]
    public async Task DeleteDocumentAsync_removes_document()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var document = new LeafletDocument
        {
            Id = documentId,
            Filename = "to-delete.pdf",
            SourcePath = "/leaflets/to-delete.pdf",
            ContentType = "application/pdf",
            ContentHash = "hash-to-delete",
            IngestedAt = DateTime.UtcNow,
            WordCount = 200
        };

        await _context.LeafletDocuments.AddAsync(document);
        await _context.SaveChangesAsync();

        // Verify it exists first
        var before = await _context.LeafletDocuments.FindAsync(documentId);
        Assert.NotNull(before);

        // Act
        await _repository.DeleteDocumentAsync(documentId);

        // Assert
        var after = await _context.LeafletDocuments.FindAsync(documentId);
        Assert.Null(after);
    }

    [Fact(Skip = "Requires PostgreSQL with pgvector")]
    public async Task AddChunksAsync_inserts_chunks_with_embeddings()
    {
        // This test requires a real PostgreSQL instance with the pgvector extension.
        await Task.CompletedTask;
    }

    [Fact(Skip = "Requires PostgreSQL with pgvector")]
    public async Task SearchSimilarAsync_returns_ranked_results()
    {
        // This test requires a real PostgreSQL instance with the pgvector extension.
        await Task.CompletedTask;
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}
