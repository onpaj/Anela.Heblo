using Anela.Heblo.Application.Features.Article.UseCases.GetArticle;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using Moq;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Article.UseCases;

public class GetArticleHandlerTests
{
    private readonly Mock<IArticleRepository> _repository = new();

    private GetArticleHandler CreateHandler() => new(_repository.Object);

    [Fact]
    public async Task Handle_EmptyGuid_ReturnsArticleNotFoundError()
    {
        var response = await CreateHandler().Handle(new GetArticleRequest { Id = Guid.Empty }, default);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ArticleNotFound);
        response.Params.Should().ContainKey("id").WhoseValue.Should().Be("empty");
        _repository.Verify(
            r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_ArticleNotFound_ReturnsArticleNotFoundError()
    {
        var id = Guid.NewGuid();
        _repository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DomainArticle?)null);

        var response = await CreateHandler().Handle(new GetArticleRequest { Id = id }, default);

        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ArticleNotFound);
        response.Params.Should().ContainKey("id").WhoseValue.Should().Be(id.ToString());
    }

    [Fact]
    public async Task Handle_ArticleFound_MapsAllFields()
    {
        var id = Guid.NewGuid();
        var createdAt = DateTimeOffset.UtcNow.AddHours(-1);
        var generatedAt = DateTimeOffset.UtcNow;
        var sourceId = Guid.NewGuid();

        var article = new DomainArticle
        {
            Id = id,
            Topic = "Topic",
            Scope = "overview",
            Audience = "general",
            Angle = "informative",
            Length = "medium (1000w)",
            Title = "The Title",
            HtmlContent = "<p>Body</p>",
            Status = ArticleStatus.Generated,
            ErrorMessage = null,
            CreatedAt = createdAt,
            GeneratedAt = generatedAt,
            UsedKnowledgeBase = true,
            UsedWebSearch = false,
            Sources = new List<ArticleSource>
            {
                new()
                {
                    Id = sourceId,
                    ArticleId = id,
                    Title = "Source 1",
                    Url = "https://example.com",
                    Type = SourceType.Web
                }
            }
        };

        _repository
            .Setup(r => r.GetByIdAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var response = await CreateHandler().Handle(new GetArticleRequest { Id = id }, default);

        response.Success.Should().BeTrue();
        response.Id.Should().Be(id);
        response.Topic.Should().Be("Topic");
        response.Scope.Should().Be("overview");
        response.Audience.Should().Be("general");
        response.Angle.Should().Be("informative");
        response.Length.Should().Be("medium (1000w)");
        response.Title.Should().Be("The Title");
        response.HtmlContent.Should().Be("<p>Body</p>");
        response.Status.Should().Be(ArticleStatus.Generated);
        response.ErrorMessage.Should().BeNull();
        response.CreatedAt.Should().Be(createdAt);
        response.GeneratedAt.Should().Be(generatedAt);
        response.UseKnowledgeBase.Should().BeTrue();
        response.UseWebSearch.Should().BeFalse();
        response.Sources.Should().ContainSingle();
        response.Sources[0].Title.Should().Be("Source 1");
        response.Sources[0].Url.Should().Be("https://example.com");
        response.Sources[0].Type.Should().Be(nameof(SourceType.Web));
    }
}
