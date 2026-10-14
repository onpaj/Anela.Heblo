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

    private async Task SeedAsync(params RagInteractionLog[] logs)
    {
        await using var context = CreateContext();
        context.RagInteractionLogs.AddRange(logs);
        await context.SaveChangesAsync();
    }

    private static readonly DateTimeOffset Day = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task GetFeedbackLogsPagedAsync_ProblemTagFilter_ReturnsOnlyMatchingRows()
    {
        // Arrange
        await SeedAsync(
            MakeLog(Day, problemTags: ["akné", "suchá pleť"]),
            MakeLog(Day, problemTags: ["růžovka"]),
            MakeLog(Day));
        await using var context = CreateContext();
        var repo = new RagInteractionLogRepository(context);

        // Act
        var (logs, total) = await repo.GetFeedbackLogsPagedAsync(
            new RagFeedbackLogFilter(RagFeature.KnowledgeBase, ProblemTag: "akné"), "CreatedAt", true, 1, 20);

        // Assert
        total.Should().Be(1);
        logs.Single().ProblemTags.Should().Contain("akné");
    }

    [Fact]
    public async Task GetFeedbackLogsPagedAsync_ProductTagFilter_ReturnsOnlyMatchingRows()
    {
        // Arrange
        await SeedAsync(
            MakeLog(Day, productTags: ["hořčíkové máslo"]),
            MakeLog(Day, productTags: ["klidné nožky"]));
        await using var context = CreateContext();
        var repo = new RagInteractionLogRepository(context);

        // Act
        var (logs, total) = await repo.GetFeedbackLogsPagedAsync(
            new RagFeedbackLogFilter(RagFeature.KnowledgeBase, ProductTag: "klidné nožky"), "CreatedAt", true, 1, 20);

        // Assert
        total.Should().Be(1);
        logs.Single().ProductTags.Should().Equal("klidné nožky");
    }

    [Fact]
    public async Task GetFeedbackLogsPagedAsync_DateRange_IsHalfOpen()
    {
        // Arrange
        var from = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var toExclusive = new DateTimeOffset(2026, 10, 2, 0, 0, 0, TimeSpan.Zero);
        await SeedAsync(
            MakeLog(from),                    // included: lower bound inclusive
            MakeLog(toExclusive.AddTicks(-10)), // included: just before the upper bound
            MakeLog(toExclusive),             // excluded: upper bound exclusive
            MakeLog(from.AddTicks(-10)));     // excluded: before the range
        await using var context = CreateContext();
        var repo = new RagInteractionLogRepository(context);

        // Act
        var (_, total) = await repo.GetFeedbackLogsPagedAsync(
            new RagFeedbackLogFilter(RagFeature.KnowledgeBase, CreatedFrom: from, CreatedToExclusive: toExclusive), "CreatedAt", true, 1, 20);

        // Assert
        total.Should().Be(2);
    }

    [Fact]
    public async Task GetTagCountsAsync_CountsPerKindAndScopesToFeatureAndRange()
    {
        // Arrange
        await SeedAsync(
            MakeLog(Day, problemTags: ["akné"], productTags: ["hořčíkové máslo"], recommended: ["MAS009"]),
            MakeLog(Day, problemTags: ["akné", "růžovka"], recommended: ["MAS009", "AKL001"]),
            MakeLog(Day, feature: RagFeature.SmartsuppDraftReply, problemTags: ["akné"]),
            MakeLog(Day.AddDays(-40), problemTags: ["akné"]));
        await using var context = CreateContext();
        var repo = new RagInteractionLogRepository(context);

        // Act
        var counts = await repo.GetTagCountsAsync(RagFeature.KnowledgeBase, Day.AddDays(-1), Day.AddDays(1));

        // Assert
        counts.Should().BeEquivalentTo(new[]
        {
            new RagTagCount(RagTagKind.Problem, "akné", 2),
            new RagTagCount(RagTagKind.Problem, "růžovka", 1),
            new RagTagCount(RagTagKind.Product, "hořčíkové máslo", 1),
            new RagTagCount(RagTagKind.RecommendedProduct, "MAS009", 2),
            new RagTagCount(RagTagKind.RecommendedProduct, "AKL001", 1),
        });
    }

    [Fact]
    public async Task GetTagCountsAsync_NoRange_CountsAllTime()
    {
        // Arrange
        await SeedAsync(MakeLog(Day, problemTags: ["akné"]), MakeLog(Day.AddDays(-400), problemTags: ["akné"]));
        await using var context = CreateContext();

        // Act
        var counts = await new RagInteractionLogRepository(context).GetTagCountsAsync(RagFeature.KnowledgeBase, null, null);

        // Assert
        counts.Should().ContainSingle().Which.Should().Be(new RagTagCount(RagTagKind.Problem, "akné", 2));
    }
}
