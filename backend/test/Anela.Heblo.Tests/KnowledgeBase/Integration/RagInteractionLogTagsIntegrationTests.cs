using Anela.Heblo.Domain.Features.Rag;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Rag;
using Anela.Heblo.Tests.Common;
using DotNet.Testcontainers.Configurations;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Integration;

[Trait("Category", "Integration")]
public class RagInteractionLogTagsIntegrationTests : IAsyncLifetime
{
    static RagInteractionLogTagsIntegrationTests()
    {
        TestcontainersSettings.ResourceReaperEnabled = false;
    }

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .Build();

    private NpgsqlDataSource _dataSource = null!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        var builder = new NpgsqlDataSourceBuilder(_container.GetConnectionString());
        builder.UseVector();
        _dataSource = builder.Build();

        await using var setup = CreateContext();
        await setup.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector;");
        await setup.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _dataSource.DisposeAsync();
        await _container.DisposeAsync();
    }

    private ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(_dataSource)
            .AllowManyServiceProviders()
            .Options);

    internal static RagInteractionLog MakeLog(
        DateTimeOffset createdAt,
        RagFeature feature = RagFeature.KnowledgeBase,
        List<string>? problemTags = null,
        List<string>? productTags = null,
        List<string>? recommended = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Feature = feature,
            CreatedAt = createdAt,
            Question = "q",
            Answer = "a",
            TopK = 5,
            SourceCount = 0,
            RetrievedChunksJson = "[]",
            SystemPrompt = "",
            DurationMs = 1,
            ProblemTags = problemTags ?? [],
            ProductTags = productTags ?? [],
            RecommendedProductCodes = recommended ?? [],
        };

    [Fact]
    public async Task SaveAsync_PersistsTagArrays()
    {
        // Arrange
        var log = MakeLog(DateTimeOffset.UtcNow, problemTags: ["akné", "suchá pleť"], productTags: ["hořčíkové máslo"], recommended: ["MAS009"]);
        await using (var writeContext = CreateContext())
        {
            await new RagInteractionLogRepository(writeContext).SaveAsync(log);
        }

        // Act
        await using var readContext = CreateContext();
        var loaded = await readContext.RagInteractionLogs.SingleAsync(l => l.Id == log.Id);

        // Assert
        loaded.ProblemTags.Should().Equal("akné", "suchá pleť");
        loaded.ProductTags.Should().Equal("hořčíkové máslo");
        loaded.RecommendedProductCodes.Should().Equal("MAS009");
    }

    [Fact]
    public async Task SaveAsync_EmptyTags_PersistsEmptyArraysNotNull()
    {
        // Arrange
        var log = MakeLog(DateTimeOffset.UtcNow);
        await using (var writeContext = CreateContext())
        {
            await new RagInteractionLogRepository(writeContext).SaveAsync(log);
        }

        // Act
        await using var readContext = CreateContext();
        var loaded = await readContext.RagInteractionLogs.SingleAsync(l => l.Id == log.Id);

        // Assert
        loaded.ProblemTags.Should().NotBeNull().And.BeEmpty();
        loaded.ProductTags.Should().NotBeNull().And.BeEmpty();
        loaded.RecommendedProductCodes.Should().NotBeNull().And.BeEmpty();
    }
}
