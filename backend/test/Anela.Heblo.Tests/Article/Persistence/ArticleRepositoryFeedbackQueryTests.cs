using Anela.Heblo.Domain.Features.Article;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Features.Article;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Article.Persistence;

public class ArticleRepositoryFeedbackQueryTests : IDisposable
{
    private static readonly DateTimeOffset T1 = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly ApplicationDbContext _context;
    private readonly ArticleRepository _repository;

    public ArticleRepositoryFeedbackQueryTests()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _context = new ApplicationDbContext(options);
        _repository = new ArticleRepository(_context);
    }

    public void Dispose() => _context.Dispose();

    // Fixture set: A (both, user1), B (precision only, user1), C (style only, user2),
    // D (none, user2), E (none, user1).
    private async Task SeedAsync()
    {
        _context.Articles.AddRange(
            Make("A", "user1", 1, 5, 4),
            Make("B", "user1", 2, 3, null),
            Make("C", "user2", 3, null, 2),
            Make("D", "user2", 4, null, null),
            Make("E", "user1", 5, null, null));
        await _context.SaveChangesAsync();
    }

    private static DomainArticle Make(string topic, string requestedBy, int day, int? precision, int? style) => new()
    {
        Id = Guid.NewGuid(),
        Topic = topic,
        RequestedBy = requestedBy,
        CreatedAt = T1.AddDays(day),
        PrecisionScore = precision,
        StyleScore = style
    };

    private async Task<string[]> QueryTopicsAsync(bool? hasFeedback, string? requestedBy)
    {
        var (items, _) = await _repository.GetFeedbackPagedAsync(
            hasFeedback, requestedBy, "CreatedAt", false, 1, 50);
        return items.Select(i => i.Topic).ToArray();
    }

    [Fact]
    public async Task HasFeedbackNull_ReturnsAll()
    {
        await SeedAsync();
        (await QueryTopicsAsync(null, null)).Should().BeEquivalentTo("A", "B", "C", "D", "E");
    }

    [Fact]
    public async Task HasFeedbackTrue_ReturnsArticlesWithAnyScore()
    {
        await SeedAsync();
        (await QueryTopicsAsync(true, null)).Should().BeEquivalentTo("A", "B", "C");
    }

    [Fact]
    public async Task HasFeedbackFalse_ReturnsOnlyArticlesWithNoScores_PartialExcluded()
    {
        await SeedAsync();
        (await QueryTopicsAsync(false, null)).Should().BeEquivalentTo("D", "E");
    }

    [Fact]
    public async Task RequestedBy_FiltersByExactMatch()
    {
        await SeedAsync();
        (await QueryTopicsAsync(null, "user1")).Should().BeEquivalentTo("A", "B", "E");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task RequestedBy_NullOrWhitespace_ReturnsAll(string? requestedBy)
    {
        await SeedAsync();
        (await QueryTopicsAsync(null, requestedBy)).Should().BeEquivalentTo("A", "B", "C", "D", "E");
    }

    [Fact]
    public async Task HasFeedbackFalse_CombinedWithRequestedBy_ReturnsIntersection()
    {
        await SeedAsync();
        (await QueryTopicsAsync(false, "user2")).Should().BeEquivalentTo("D");
    }

    // Sort/paging fixtures: all have feedback, distinct non-null scores, distinct CreatedAt.
    // Precision order asc: P(1) Q(2) R(3) S(4) T(5); Style order asc: T(1) S(2) R(3) Q(4) P(5).
    private async Task SeedScoredAsync()
    {
        _context.Articles.AddRange(
            Make("R", "user1", 3, 3, 3),
            Make("P", "user1", 1, 1, 5),
            Make("T", "user1", 5, 5, 1),
            Make("Q", "user1", 2, 2, 4),
            Make("S", "user1", 4, 4, 2));
        await _context.SaveChangesAsync();
    }

    private async Task<string[]> QuerySortedTopicsAsync(string sortBy, bool descending)
    {
        var (items, _) = await _repository.GetFeedbackPagedAsync(
            true, null, sortBy, descending, 1, 50);
        return items.Select(i => i.Topic).ToArray();
    }

    [Theory]
    [InlineData("PrecisionScore", false, new[] { "P", "Q", "R", "S", "T" })]
    [InlineData("PrecisionScore", true, new[] { "T", "S", "R", "Q", "P" })]
    [InlineData("StyleScore", false, new[] { "T", "S", "R", "Q", "P" })]
    [InlineData("StyleScore", true, new[] { "P", "Q", "R", "S", "T" })]
    [InlineData("CreatedAt", false, new[] { "P", "Q", "R", "S", "T" })]
    [InlineData("CreatedAt", true, new[] { "T", "S", "R", "Q", "P" })]
    [InlineData("Bogus", false, new[] { "P", "Q", "R", "S", "T" })]
    [InlineData("Bogus", true, new[] { "T", "S", "R", "Q", "P" })]
    public async Task SortBy_OrdersResults(string sortBy, bool descending, string[] expected)
    {
        await SeedScoredAsync();
        (await QuerySortedTopicsAsync(sortBy, descending)).Should().Equal(expected);
    }

    [Fact]
    public async Task Paging_Page2Size2_ReturnsThirdAndFourthItems_TotalCountUnchanged()
    {
        await SeedAsync();
        var (items, total) = await _repository.GetFeedbackPagedAsync(
            null, null, "CreatedAt", false, 2, 2);
        items.Select(i => i.Topic).Should().Equal("C", "D");
        total.Should().Be(5);
    }

    [Fact]
    public async Task GetFeedbackStats_EmptyTable_ReturnsZerosAndNullAverages()
    {
        var stats = await _repository.GetFeedbackStatsAsync();
        stats.Should().Be(new ArticleFeedbackStats(0, 0, null, null));
    }

    [Fact]
    public async Task GetFeedbackStats_Populated_AveragesOverNonNullValuesOnly()
    {
        await SeedAsync();
        var stats = await _repository.GetFeedbackStatsAsync();
        stats.TotalArticles.Should().Be(5);
        stats.TotalWithFeedback.Should().Be(3);
        stats.AvgPrecisionScore.Should().Be(4.0);
        stats.AvgStyleScore.Should().Be(3.0);
    }
}
