using Anela.Heblo.Application.Features.Article;
using Anela.Heblo.Application.Features.Article.UseCases.Generate;
using Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Article.Pipeline;

public class GenerateArticleJobTests
{
    private readonly Mock<IArticleRepository> _repository = new();
    private readonly Mock<IPlanQueriesStep> _planQueries = new();
    private readonly Mock<IGatherContextStep> _gatherContext = new();
    private readonly Mock<IAggregateFactsStep> _aggregateFacts = new();
    private readonly Mock<IValidateFactsStep> _validateFacts = new();
    private readonly Mock<IWriteArticleStep> _writeArticle = new();

    public GenerateArticleJobTests()
    {
        _planQueries.Setup(s => s.ExecuteAsync(It.IsAny<ArticlePipelineContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _gatherContext.Setup(s => s.ExecuteAsync(It.IsAny<ArticlePipelineContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _aggregateFacts.Setup(s => s.ExecuteAsync(It.IsAny<ArticlePipelineContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _validateFacts.Setup(s => s.ExecuteAsync(It.IsAny<ArticlePipelineContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _writeArticle.Setup(s => s.ExecuteAsync(It.IsAny<ArticlePipelineContext>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
    }

    private static DomainArticle CreateArticle() =>
        new()
        {
            Id = Guid.NewGuid(),
            Topic = "Topic",
            Status = ArticleStatus.Queued
        };

    private GenerateArticleJob CreateJob()
    {
        return new GenerateArticleJob(
            _repository.Object,
            _planQueries.Object,
            _gatherContext.Object,
            _aggregateFacts.Object,
            _validateFacts.Object,
            _writeArticle.Object,
            NullLogger<GenerateArticleJob>.Instance);
    }

    [Fact]
    public async Task RunAsync_HappyPath_StatusGeneratedAndSourcesPersisted()
    {
        var article = CreateArticle();
        article.UsedKnowledgeBase = false;
        article.UsedWebSearch = false;
        _repository
            .Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        _writeArticle
            .Setup(s => s.ExecuteAsync(It.IsAny<ArticlePipelineContext>(), It.IsAny<CancellationToken>()))
            .Callback<ArticlePipelineContext, CancellationToken>((ctx, ct) =>
            {
                ctx.GeneratedTitle = "Final Title";
                ctx.GeneratedHtml = "<article>x</article>";
                ctx.SourceRefs = new List<ArticleSourceRef>
                {
                    new("Src", "https://a.com", SourceType.Web, null, null, null, null)
                };
            })
            .Returns(Task.CompletedTask);

        await CreateJob().RunAsync(article.Id, default);

        article.Status.Should().Be(ArticleStatus.Generated);
        article.Title.Should().Be("Final Title");
        article.HtmlContent.Should().Be("<article>x</article>");
        article.Sources.Should().ContainSingle();
        article.Sources[0].Title.Should().Be("Src");
        article.Sources[0].Url.Should().Be("https://a.com");
        article.Sources[0].Type.Should().Be(SourceType.Web);
        article.Sources[0].ArticleId.Should().Be(article.Id);

        // SaveChangesAsync called: after Researching, after Writing, after final
        _repository.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.AtLeast(3));
        // Regression guard: must use the tracked variant, never the read-only one
        _repository.Verify(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()), Times.Once);
        _repository.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RunAsync_ArticleNotFound_LogsAndReturnsWithoutSavingState()
    {
        var id = Guid.NewGuid();
        _repository
            .Setup(r => r.GetForUpdateAsync(id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((DomainArticle?)null);

        await CreateJob().RunAsync(id, default);

        _repository.Verify(
            r => r.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RunAsync_StepThrows_StatusFailedAndErrorMessageSet()
    {
        var article = CreateArticle();
        article.UsedKnowledgeBase = false;
        article.UsedWebSearch = false;
        _repository
            .Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        _aggregateFacts
            .Setup(s => s.ExecuteAsync(It.IsAny<ArticlePipelineContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("LLM blew up"));

        await CreateJob().RunAsync(article.Id, default);

        article.Status.Should().Be(ArticleStatus.Failed);
        article.ErrorMessage.Should().Be("LLM blew up");
    }

    [Fact]
    public async Task RunAsync_OperationCancelled_StatusFailedAndExceptionRethrown()
    {
        var article = CreateArticle();
        article.UsedKnowledgeBase = false;
        article.UsedWebSearch = false;
        _repository
            .Setup(r => r.GetForUpdateAsync(article.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        _planQueries
            .Setup(s => s.ExecuteAsync(It.IsAny<ArticlePipelineContext>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        Func<Task> act = () => CreateJob().RunAsync(article.Id, default);

        await act.Should().ThrowAsync<OperationCanceledException>();
        article.Status.Should().Be(ArticleStatus.Failed);
        article.ErrorMessage.Should().Be("Job cancelled.");
    }
}
