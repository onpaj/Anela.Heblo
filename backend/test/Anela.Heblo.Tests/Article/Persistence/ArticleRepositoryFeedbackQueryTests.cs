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
}
