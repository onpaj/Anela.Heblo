using Anela.Heblo.Application.Features.Article.UseCases.ListArticles;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using Moq;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Article.UseCases;

public class ListArticlesHandlerTests
{
    private readonly Mock<IArticleRepository> _repository = new();

    private ListArticlesHandler CreateHandler() => new(_repository.Object);

    private static DomainArticle CreateArticle(string topic, ArticleStatus status, string? title = null) =>
        new()
        {
            Id = Guid.NewGuid(),
            Topic = topic,
            Title = title,
            Status = status,
            CreatedAt = DateTimeOffset.UtcNow,
            GeneratedAt = status == ArticleStatus.Generated ? DateTimeOffset.UtcNow : null
        };

    [Fact]
    public async Task Handle_ReturnsMappedListWithPaginationInfo()
    {
        var articles = new List<DomainArticle>
        {
            CreateArticle("Topic 1", ArticleStatus.Generated, "Title 1"),
            CreateArticle("Topic 2", ArticleStatus.Queued)
        };

        _repository
            .Setup(r => r.GetPagedAsync(
                It.IsAny<ArticleStatus?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((articles, 42));

        var response = await CreateHandler().Handle(
            new ListArticlesRequest { Page = 2, PageSize = 10 },
            default);

        response.Success.Should().BeTrue();
        response.Items.Should().HaveCount(2);
        response.Items[0].Topic.Should().Be("Topic 1");
        response.Items[0].Title.Should().Be("Title 1");
        response.Items[0].Status.Should().Be(ArticleStatus.Generated);
        response.Items[1].Topic.Should().Be("Topic 2");
        response.Items[1].Status.Should().Be(ArticleStatus.Queued);
        response.TotalCount.Should().Be(42);
        response.Page.Should().Be(2);
        response.PageSize.Should().Be(10);
    }

    [Fact]
    public async Task Handle_PassesStatusFilterThroughToRepository()
    {
        _repository
            .Setup(r => r.GetPagedAsync(
                It.IsAny<ArticleStatus?>(),
                It.IsAny<int>(),
                It.IsAny<int>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((new List<DomainArticle>(), 0));

        await CreateHandler().Handle(
            new ListArticlesRequest { Status = ArticleStatus.Failed, Page = 1, PageSize = 25 },
            default);

        _repository.Verify(
            r => r.GetPagedAsync(ArticleStatus.Failed, 1, 25, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
