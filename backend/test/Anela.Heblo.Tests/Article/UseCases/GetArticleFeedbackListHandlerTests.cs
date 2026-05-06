using Anela.Heblo.Application.Features.Article.UseCases.GetArticleFeedbackList;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using Moq;
using Xunit;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Article.UseCases;

public class GetArticleFeedbackListHandlerTests
{
    private readonly Mock<IArticleRepository> _repositoryMock = new();
    private readonly GetArticleFeedbackListHandler _handler;

    public GetArticleFeedbackListHandlerTests()
    {
        _handler = new GetArticleFeedbackListHandler(_repositoryMock.Object);
    }

    private void SetupRepository(
        IReadOnlyList<DomainArticle> items,
        int total,
        ArticleFeedbackStats? stats = null)
    {
        _repositoryMock
            .Setup(x => x.GetArticlesPagedAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<string>(),
                It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((items, total));

        _repositoryMock
            .Setup(x => x.GetFeedbackStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(stats ?? new ArticleFeedbackStats(total, 0, null, null));
    }

    [Fact]
    public async Task Handle_DefaultRequest_ReturnsPaginatedResults()
    {
        var articles = new List<DomainArticle>
        {
            new() { Id = Guid.NewGuid(), Topic = "Topic 1", Status = ArticleStatus.Generated, CreatedAt = DateTimeOffset.UtcNow }
        };
        SetupRepository(articles, 1);

        var result = await _handler.Handle(new GetArticleFeedbackListRequest(), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Articles.Should().HaveCount(1);
        result.TotalCount.Should().Be(1);
        result.PageNumber.Should().Be(1);
        result.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task Handle_PageSizeNotAllowed_ClampsTo20()
    {
        SetupRepository([], 0);

        var result = await _handler.Handle(
            new GetArticleFeedbackListRequest { PageSize = 99 },
            CancellationToken.None);

        result.PageSize.Should().Be(20);
    }

    [Fact]
    public async Task Handle_InvalidSortColumn_DefaultsToCreatedAt()
    {
        SetupRepository([], 0);

        await _handler.Handle(
            new GetArticleFeedbackListRequest { SortBy = "HackerInput" },
            CancellationToken.None);

        _repositoryMock.Verify(x => x.GetArticlesPagedAsync(
            It.IsAny<bool?>(), It.IsAny<string?>(), "CreatedAt",
            It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WithFeedbackFilter_PassesThroughToRepository()
    {
        SetupRepository([], 0);

        await _handler.Handle(
            new GetArticleFeedbackListRequest { HasFeedback = true },
            CancellationToken.None);

        _repositoryMock.Verify(x => x.GetArticlesPagedAsync(
            true, It.IsAny<string?>(), It.IsAny<string>(),
            It.IsAny<bool>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_ReturnsStats()
    {
        var stats = new ArticleFeedbackStats(10, 4, 3.5, 4.1);
        SetupRepository([], 10, stats);

        var result = await _handler.Handle(new GetArticleFeedbackListRequest(), CancellationToken.None);

        result.Stats.TotalArticles.Should().Be(10);
        result.Stats.TotalWithFeedback.Should().Be(4);
        result.Stats.AvgPrecisionScore.Should().Be(3.5);
        result.Stats.AvgStyleScore.Should().Be(4.1);
    }

    [Fact]
    public async Task Handle_ArticleWithFeedback_MapsAllFields()
    {
        var article = new DomainArticle
        {
            Id = Guid.NewGuid(),
            Topic = "KB topic",
            Title = "KB Article",
            Status = ArticleStatus.Generated,
            CreatedAt = DateTimeOffset.UtcNow,
            RequestedBy = "user-1",
            PrecisionScore = 4,
            StyleScore = 3,
            FeedbackComment = "Good"
        };
        SetupRepository([article], 1);

        var result = await _handler.Handle(new GetArticleFeedbackListRequest(), CancellationToken.None);

        var summary = result.Articles.Single();
        summary.PrecisionScore.Should().Be(4);
        summary.StyleScore.Should().Be(3);
        summary.FeedbackComment.Should().Be("Good");
        summary.HasFeedback.Should().BeTrue();
        summary.RequestedBy.Should().Be("user-1");
    }
}
