using Anela.Heblo.Application.Features.Article.UseCases.GetArticleTrace;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using Moq;
using Xunit;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Features.Article;

public class GetArticleTraceHandlerTests
{
    private readonly Mock<IArticleRepository> _repositoryMock = new();
    private readonly GetArticleTraceHandler _handler;

    public GetArticleTraceHandlerTests()
    {
        _handler = new GetArticleTraceHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_ReturnsSteps_WhenArticleExists()
    {
        var articleId = Guid.NewGuid();
        var article = CreateArticle(articleId, new List<ArticleGenerationStep>
        {
            CreateStep(articleId, "WriteArticle", 5, ArticleGenerationStepStatus.Succeeded),
            CreateStep(articleId, "PlanQueries", 1, ArticleGenerationStepStatus.Succeeded),
            CreateStep(articleId, "AggregateFacts", 3, ArticleGenerationStepStatus.Failed),
        });

        _repositoryMock
            .Setup(r => r.GetWithStepsAsync(articleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var result = await _handler.Handle(new GetArticleTraceRequest { Id = articleId }, CancellationToken.None);

        result.Success.Should().BeTrue();
        result.ArticleId.Should().Be(articleId);
        result.Steps.Should().HaveCount(3);

        // Verify at least one step's field mapping
        var firstStep = result.Steps.First();
        firstStep.StepName.Should().NotBeEmpty();
        firstStep.Status.Should().BeOneOf("Running", "Succeeded", "Failed");
        firstStep.StartedAt.Should().NotBe(default(DateTimeOffset));
    }

    [Fact]
    public async Task Handle_OrdersStepsBySequence_EvenWhenRepoReturnsUnordered()
    {
        var articleId = Guid.NewGuid();
        var article = CreateArticle(articleId, new List<ArticleGenerationStep>
        {
            CreateStep(articleId, "WriteArticle", 5, ArticleGenerationStepStatus.Succeeded),
            CreateStep(articleId, "PlanQueries", 1, ArticleGenerationStepStatus.Succeeded),
            CreateStep(articleId, "AggregateFacts", 3, ArticleGenerationStepStatus.Succeeded),
        });

        _repositoryMock
            .Setup(r => r.GetWithStepsAsync(articleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var result = await _handler.Handle(new GetArticleTraceRequest { Id = articleId }, CancellationToken.None);

        result.Steps.Select(s => s.Sequence).Should().BeInAscendingOrder();
        result.Steps[0].StepName.Should().Be("PlanQueries");
        result.Steps[1].StepName.Should().Be("AggregateFacts");
        result.Steps[2].StepName.Should().Be("WriteArticle");
    }

    [Fact]
    public async Task Handle_ReturnsFailureResponse_WhenArticleMissing()
    {
        var articleId = Guid.NewGuid();

        _repositoryMock
            .Setup(r => r.GetWithStepsAsync(articleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DomainArticle?)null);

        var result = await _handler.Handle(new GetArticleTraceRequest { Id = articleId }, CancellationToken.None);

        result.Success.Should().BeFalse();
        result.Steps.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_ReturnsFailureResponse_WhenIdIsEmpty()
    {
        var result = await _handler.Handle(
            new GetArticleTraceRequest { Id = Guid.Empty },
            CancellationToken.None);

        result.Success.Should().BeFalse();
        _repositoryMock.Verify(r => r.GetWithStepsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    private static DomainArticle CreateArticle(Guid id, List<ArticleGenerationStep> steps)
    {
        var article = new DomainArticle
        {
            Id = id,
            Topic = "test",
            Scope = "short",
            Length = "500",
            Status = ArticleStatus.Generated,
        };
        article.Steps.AddRange(steps);
        return article;
    }

    private static ArticleGenerationStep CreateStep(
        Guid articleId,
        string stepName,
        int sequence,
        ArticleGenerationStepStatus status) => new()
        {
            Id = Guid.NewGuid(),
            ArticleId = articleId,
            StepName = stepName,
            Sequence = sequence,
            Status = status,
            StartedAt = DateTimeOffset.UtcNow,
        };
}
