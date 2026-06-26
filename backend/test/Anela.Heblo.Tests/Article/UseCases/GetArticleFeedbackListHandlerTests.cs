using Anela.Heblo.Application.Features.Article.UseCases.GetFeedbackList;
using Anela.Heblo.Application.Shared.Users;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Article.UseCases;

public class GetArticleFeedbackListHandlerTests
{
    private readonly Mock<IArticleRepository> _repository = new();
    private readonly Mock<IUserDisplayNameResolver> _userDisplayNameResolver = new();

    public GetArticleFeedbackListHandlerTests()
    {
        _userDisplayNameResolver
            .Setup(r => r.ResolveAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>());
    }

    private GetArticleFeedbackListHandler CreateHandler() =>
        new(_repository.Object, _userDisplayNameResolver.Object);

    [Fact]
    public async Task Handle_DefaultParams_RunsPagedAndStatsInParallelAndProjectsResults()
    {
        var article = new ArticleFeedbackProjection(
            Id: Guid.NewGuid(),
            Title: "Sun care title",
            Topic: "Sun care",
            RequestedBy: "alice",
            CreatedAt: DateTimeOffset.UtcNow,
            PrecisionScore: 4,
            StyleScore: 5,
            FeedbackComment: "ok");

        _repository.Setup(r => r.GetFeedbackPagedAsync(
                null, null, "CreatedAt", true, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<ArticleFeedbackProjection>)new[] { article }, 1));

        _repository.Setup(r => r.GetFeedbackStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ArticleFeedbackStats(10, 4, 4.5, 4.0));

        var response = await CreateHandler().Handle(new GetArticleFeedbackListRequest(), default);

        response.Items.Should().HaveCount(1);
        response.Items[0].Id.Should().Be(article.Id);
        response.Items[0].Topic.Should().Be("Sun care");
        response.Items[0].Title.Should().Be("Sun care title");
        response.Items[0].RequestedBy.Should().Be("alice");
        response.Items[0].PrecisionScore.Should().Be(4);
        response.Items[0].StyleScore.Should().Be(5);
        response.Items[0].HasComment.Should().BeTrue();
        response.TotalCount.Should().Be(1);
        response.Page.Should().Be(1);
        response.PageSize.Should().Be(20);
        response.Stats.TotalArticles.Should().Be(10);
        response.Stats.TotalWithFeedback.Should().Be(4);
        response.Stats.AvgPrecisionScore.Should().Be(4.5);
        response.Stats.AvgStyleScore.Should().Be(4.0);
    }

    [Fact]
    public async Task Handle_ResolvesUserNameFromRequestedBy()
    {
        var article = new ArticleFeedbackProjection(
            Id: Guid.NewGuid(),
            Title: "Sun care title",
            Topic: "Sun care",
            RequestedBy: "alice@anela.cz",
            CreatedAt: DateTimeOffset.UtcNow,
            PrecisionScore: 4,
            StyleScore: 5,
            FeedbackComment: "ok");

        _repository.Setup(r => r.GetFeedbackPagedAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<ArticleFeedbackProjection>)new[] { article }, 1));
        _repository.Setup(r => r.GetFeedbackStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ArticleFeedbackStats(1, 1, 4.0, 5.0));
        _userDisplayNameResolver
            .Setup(r => r.ResolveAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IReadOnlyDictionary<string, string?>)new Dictionary<string, string?>
            {
                ["alice@anela.cz"] = "Alice Example",
            });

        var response = await CreateHandler().Handle(new GetArticleFeedbackListRequest(), default);

        response.Items[0].UserName.Should().Be("Alice Example");
        response.Items[0].RequestedBy.Should().Be("alice@anela.cz");
    }

    [Fact]
    public async Task Handle_UnknownSortBy_FallsBackToCreatedAt()
    {
        _repository.Setup(r => r.GetFeedbackPagedAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), "CreatedAt", true, 1, 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<ArticleFeedbackProjection>)Array.Empty<ArticleFeedbackProjection>(), 0));
        _repository.Setup(r => r.GetFeedbackStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ArticleFeedbackStats(0, 0, null, null));

        await CreateHandler().Handle(
            new GetArticleFeedbackListRequest { SortBy = "totallyBogus" },
            default);

        _repository.Verify(r => r.GetFeedbackPagedAsync(
            null, null, "CreatedAt", true, 1, 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_PageSizeOutsideAllowlist_FallsBackTo20()
    {
        _repository.Setup(r => r.GetFeedbackPagedAsync(
                It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<int>(), 20, It.IsAny<CancellationToken>()))
            .ReturnsAsync(((IReadOnlyList<ArticleFeedbackProjection>)Array.Empty<ArticleFeedbackProjection>(), 0));
        _repository.Setup(r => r.GetFeedbackStatsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ArticleFeedbackStats(0, 0, null, null));

        await CreateHandler().Handle(
            new GetArticleFeedbackListRequest { PageSize = 999 },
            default);

        _repository.Verify(r => r.GetFeedbackPagedAsync(
            It.IsAny<bool?>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<bool>(),
            It.IsAny<int>(), 20, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
