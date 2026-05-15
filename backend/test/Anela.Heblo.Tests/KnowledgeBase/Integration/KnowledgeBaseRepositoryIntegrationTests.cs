using Anela.Heblo.Domain.Features.KnowledgeBase;
using Anela.Heblo.Domain.Shared.Rag;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.KnowledgeBase;
using DotNet.Testcontainers.Configurations;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Integration;

[Trait("Category", "Integration")]
public class KnowledgeBaseRepositoryIntegrationTests : IAsyncLifetime
{
    static KnowledgeBaseRepositoryIntegrationTests()
    {
        // Podman does not support the Ryuk/ResourceReaper container; disable it to avoid NullReferenceException
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

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

            CREATE TABLE IF NOT EXISTS public."KnowledgeBaseDocuments" (
                "Id"           uuid NOT NULL PRIMARY KEY,
                "Filename"     varchar(500) NOT NULL,
                "SourcePath"   varchar(1000) NOT NULL,
                "ContentType"  varchar(100) NOT NULL,
                "ContentHash"  varchar(64) NOT NULL UNIQUE,
                "Status"       varchar(50) NOT NULL,
                "DocumentType" integer NOT NULL DEFAULT 0,
                "CreatedAt"    timestamp NOT NULL,
                "IndexedAt"    timestamp NULL,
                "DriveId"      text NULL,
                "GraphItemId"  text NULL
            );

            CREATE TABLE IF NOT EXISTS public."KnowledgeBaseChunks" (
                "Id"           uuid NOT NULL PRIMARY KEY,
                "DocumentId"   uuid NOT NULL REFERENCES public."KnowledgeBaseDocuments"("Id") ON DELETE CASCADE,
                "ChunkIndex"   integer NOT NULL,
                "Content"      text NOT NULL DEFAULT '',
                "Summary"      text NOT NULL DEFAULT '',
                "DocumentType" integer NOT NULL DEFAULT 0,
                "Embedding"    vector(3)
            );

            CREATE INDEX IF NOT EXISTS idx_kb_chunks_embedding
                ON public."KnowledgeBaseChunks"
                USING hnsw ("Embedding" vector_cosine_ops)
                WITH (m = 16, ef_construction = 64);

            CREATE TABLE IF NOT EXISTS public."KnowledgeBaseQuestionLogs" (
                "Id"              uuid NOT NULL PRIMARY KEY,
                "Question"        text NOT NULL,
                "Answer"          text NOT NULL,
                "TopK"            integer NOT NULL,
                "SourceCount"     integer NOT NULL,
                "DurationMs"      bigint NOT NULL,
                "CreatedAt"       timestamp with time zone NOT NULL,
                "UserId"          text NULL,
                "PrecisionScore"  integer NULL,
                "StyleScore"      integer NULL,
                "FeedbackComment" text NULL
            );
            """;
        await cmd.ExecuteNonQueryAsync();
    }

    private static KnowledgeBaseDocument MakeDocument(
        string filename = "test.pdf",
        string hash = "abc123",
        string? driveId = null,
        string? graphItemId = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Filename = filename,
            SourcePath = $"/inbox/{filename}",
            ContentType = "application/pdf",
            ContentHash = hash,
            Status = DocumentStatus.Indexed,
            CreatedAt = DateTime.UtcNow,
            IndexedAt = DateTime.UtcNow,
            DriveId = driveId,
            GraphItemId = graphItemId
        };

    private static KnowledgeBaseQuestionLog MakeQuestionLog(
        int? precisionScore = null,
        int? styleScore = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Question = "q",
            Answer = "a",
            TopK = 5,
            SourceCount = 1,
            DurationMs = 100,
            CreatedAt = DateTimeOffset.UtcNow,
            UserId = null,
            PrecisionScore = precisionScore,
            StyleScore = styleScore,
            FeedbackComment = null,
        };

    [Fact]
    public async Task AddChunksAsync_PersistsSummaryAndDocumentType()
    {
        var doc = MakeDocument("summary-test.pdf", "deadbeef004");
        await _repository.AddDocumentAsync(doc);
        await _repository.SaveChangesAsync();

        var chunk = new KnowledgeBaseChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            ChunkIndex = 0,
            Content = "Full transcript text",
            Summary = "Produkty: Sérum ABC\nProblém zákazníka: akné",
            DocumentType = DocumentType.Conversation,
            Embedding = [0.1f, 0.2f, 0.3f]
        };

        await _repository.AddChunksAsync([chunk]);

        var stored = await _context.KnowledgeBaseChunks
            .FirstAsync(c => c.Id == chunk.Id);

        Assert.Equal("Produkty: Sérum ABC\nProblém zákazníka: akné", stored.Summary);
        Assert.Equal(DocumentType.Conversation, stored.DocumentType);
        Assert.Equal("Full transcript text", stored.Content);
    }

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
    public async Task GetChunkByIdAsync_ReturnsChunkWithDocument_WhenExists()
    {
        var doc = MakeDocument("chunk-detail-test.pdf", "deadbeef005");
        await _repository.AddDocumentAsync(doc);
        await _repository.SaveChangesAsync();

        var chunk = new KnowledgeBaseChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            ChunkIndex = 0,
            Content = "Chunk content for detail test",
            Embedding = [0.1f, 0.2f, 0.3f]
        };

        await _repository.AddChunksAsync([chunk]);

        var result = await _repository.GetChunkByIdAsync(chunk.Id);

        Assert.NotNull(result);
        Assert.NotNull(result!.Document);
        Assert.Equal("chunk-detail-test.pdf", result.Document!.Filename);
    }

    [Fact]
    public async Task AddChunksAsync_IsIdempotent_WhenCalledTwiceWithSameChunks()
    {
        var doc = MakeDocument("idempotent-test.pdf", "deadbeef006");
        await _repository.AddDocumentAsync(doc);
        await _repository.SaveChangesAsync();

        var chunk = new KnowledgeBaseChunk
        {
            Id = Guid.NewGuid(),
            DocumentId = doc.Id,
            ChunkIndex = 0,
            Content = "Idempotent content",
            Embedding = [0.1f, 0.2f, 0.3f]
        };

        // First insert — should succeed
        await _repository.AddChunksAsync([chunk]);

        // Second insert with same Id — ON CONFLICT DO NOTHING, must not throw
        var exception = await Record.ExceptionAsync(() => _repository.AddChunksAsync([chunk]));
        Assert.Null(exception);

        // Only one row should exist
        var rows = await _context.KnowledgeBaseChunks
            .Where(c => c.DocumentId == doc.Id)
            .ToListAsync();
        Assert.Single(rows);
    }

    [Fact]
    public async Task GetChunkByIdAsync_ReturnsNull_WhenNotExists()
    {
        var result = await _repository.GetChunkByIdAsync(Guid.NewGuid());

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDocumentByGraphItemIdAsync_ReturnsNull_WhenMissing()
    {
        var result = await _repository.GetDocumentByGraphItemIdAsync("drive-x", "item-y");

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDocumentByGraphItemIdAsync_ReturnsDocument_WhenBothFieldsMatch()
    {
        var doc = MakeDocument("graph-kb.pdf", "deadbeef007", driveId: "drive-kb", graphItemId: "item-kb-001");
        await _repository.AddDocumentAsync(doc);
        await _repository.SaveChangesAsync();

        var result = await _repository.GetDocumentByGraphItemIdAsync("drive-kb", "item-kb-001");

        Assert.NotNull(result);
        Assert.Equal(doc.Id, result!.Id);
        Assert.Equal("drive-kb", result.DriveId);
        Assert.Equal("item-kb-001", result.GraphItemId);
    }

    [Fact]
    public async Task GetDocumentByGraphItemIdAsync_ReturnsNull_WhenOnlyDriveIdMatches()
    {
        var doc = MakeDocument("graph-kb-partial.pdf", "deadbeef008", driveId: "drive-kb", graphItemId: "item-kb-002");
        await _repository.AddDocumentAsync(doc);
        await _repository.SaveChangesAsync();

        var result = await _repository.GetDocumentByGraphItemIdAsync("drive-kb", "item-different");

        Assert.Null(result);
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

    [Fact]
    public async Task GetFeedbackStatsAsync_EmptyTable_ReturnsZeroCountsAndNullAverages()
    {
        var stats = await _repository.GetFeedbackStatsAsync();

        Assert.Equal(0, stats.TotalQuestions);
        Assert.Equal(0, stats.TotalWithFeedback);
        Assert.Null(stats.AvgPrecisionScore);
        Assert.Null(stats.AvgStyleScore);
    }

    [Fact]
    public async Task GetFeedbackStatsAsync_RowsWithoutScores_ReturnsTotalsAndNullAverages()
    {
        _context.KnowledgeBaseQuestionLogs.AddRange(
            MakeQuestionLog(),
            MakeQuestionLog(),
            MakeQuestionLog());
        await _context.SaveChangesAsync();

        var stats = await _repository.GetFeedbackStatsAsync();

        Assert.Equal(3, stats.TotalQuestions);
        Assert.Equal(0, stats.TotalWithFeedback);
        Assert.Null(stats.AvgPrecisionScore);
        Assert.Null(stats.AvgStyleScore);
    }

    [Fact]
    public async Task GetFeedbackStatsAsync_MixedFeedback_ReturnsCorrectCountsAndAverages()
    {
        _context.KnowledgeBaseQuestionLogs.AddRange(
            MakeQuestionLog(),
            MakeQuestionLog(precisionScore: 5),
            MakeQuestionLog(styleScore: 3),
            MakeQuestionLog(precisionScore: 4, styleScore: 5));
        await _context.SaveChangesAsync();

        var stats = await _repository.GetFeedbackStatsAsync();

        Assert.Equal(4, stats.TotalQuestions);
        Assert.Equal(3, stats.TotalWithFeedback);
        Assert.Equal(4.5, stats.AvgPrecisionScore);
        Assert.Equal(4.0, stats.AvgStyleScore);
    }

    [Fact]
    public async Task GetFeedbackStatsAsync_RoundsAveragesToOneDecimalUsingBankersRounding()
    {
        // PrecisionScore raw average = (5 + 5 + 4) / 3 = 4.6666... -> 4.7
        // StyleScore raw average     = (1 + 2) / 2     = 1.5       -> 1.5
        _context.KnowledgeBaseQuestionLogs.AddRange(
            MakeQuestionLog(precisionScore: 5, styleScore: 1),
            MakeQuestionLog(precisionScore: 5, styleScore: 2),
            MakeQuestionLog(precisionScore: 4));
        await _context.SaveChangesAsync();

        var stats = await _repository.GetFeedbackStatsAsync();

        Assert.Equal(3, stats.TotalQuestions);
        Assert.Equal(3, stats.TotalWithFeedback);
        Assert.NotNull(stats.AvgPrecisionScore);
        Assert.NotNull(stats.AvgStyleScore);
        Assert.Equal(Math.Round((5d + 5d + 4d) / 3d, 1), stats.AvgPrecisionScore!.Value);
        Assert.Equal(Math.Round((1d + 2d) / 2d, 1), stats.AvgStyleScore!.Value);
    }

    [Fact]
    public async Task GetFeedbackStatsAsync_ExecutesAggregateSqlWithoutMaterialisingRows()
    {
        _context.KnowledgeBaseQuestionLogs.AddRange(
            MakeQuestionLog(precisionScore: 5, styleScore: 4),
            MakeQuestionLog());
        await _context.SaveChangesAsync();

        var loggerProvider = new CapturingLoggerProvider();
        var loggerFactory = LoggerFactory.Create(builder =>
        {
            builder.AddProvider(loggerProvider);
            builder.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Information);
        });

        var dataSourceBuilder = new NpgsqlDataSourceBuilder(_container.GetConnectionString());
        dataSourceBuilder.UseVector();
        await using var dataSource = dataSourceBuilder.Build();

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(dataSource)
            .UseLoggerFactory(loggerFactory)
            .EnableSensitiveDataLogging()
            .Options;

        await using var ctx = new ApplicationDbContext(options);
        var repo = new KnowledgeBaseRepository(ctx);

        var stats = await repo.GetFeedbackStatsAsync();

        Assert.Equal(2, stats.TotalQuestions);

        var sql = string.Join("\n", loggerProvider.Messages);
        Assert.Contains("COUNT(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AVG(", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("\"Question\"", sql);
        Assert.DoesNotContain("\"Answer\"", sql);
    }
}

internal sealed class CapturingLoggerProvider : ILoggerProvider
{
    public List<string> Messages { get; } = new();

    public ILogger CreateLogger(string categoryName) =>
        new CapturingLogger(Messages);

    public void Dispose() { }

    private sealed class CapturingLogger : ILogger
    {
        private readonly List<string> _messages;

        public CapturingLogger(List<string> messages) => _messages = messages;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            _messages.Add(formatter(state, exception));
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
