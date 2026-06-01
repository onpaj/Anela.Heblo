using Anela.Heblo.Application.Features.Article;
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.Article.UseCases.Generate;
using Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;
using Anela.Heblo.Application.Shared.WebSearch;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Article.Pipeline;

public class GenerateArticleJobTests
{
    private readonly Mock<IArticleRepository> _repository = new();
    private readonly Mock<IChatClient> _chat = new();
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IWebSearchClient> _webSearch = new();
    private readonly Mock<IArticleStyleGuideSource> _styleGuideSource = new();
    private readonly ArticleOptions _options = new();

    private static DomainArticle CreateArticle() =>
        new()
        {
            Id = Guid.NewGuid(),
            Topic = "Topic",
            Status = ArticleStatus.Queued
        };

    private static PipelineStepRecorder CreateNoOpRecorder()
    {
        var repo = new Mock<IArticleRepository>();
        repo.Setup(r => r.AddStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return new PipelineStepRecorder(repo.Object);
    }

    private GenerateArticleJob CreateJob(
        PlanQueriesStep? planQueries = null,
        GatherContextStep? gatherContext = null,
        AggregateFactsStep? aggregateFacts = null,
        ValidateFactsStep? validateFacts = null,
        WriteArticleStep? writeArticle = null)
    {
        var optionsWrapper = Options.Create(_options);
        var recorder = CreateNoOpRecorder();
        return new GenerateArticleJob(
            _repository.Object,
            planQueries ?? new PlanQueriesStep(_chat.Object, optionsWrapper, NullLogger<PlanQueriesStep>.Instance, recorder),
            gatherContext ?? new GatherContextStep(_mediator.Object, _webSearch.Object, _styleGuideSource.Object, optionsWrapper, NullLogger<GatherContextStep>.Instance, recorder),
            aggregateFacts ?? new AggregateFactsStep(_chat.Object, optionsWrapper, NullLogger<AggregateFactsStep>.Instance, recorder),
            validateFacts ?? new ValidateFactsStep(_chat.Object, optionsWrapper, NullLogger<ValidateFactsStep>.Instance, recorder),
            writeArticle ?? new WriteArticleStep(_chat.Object, optionsWrapper, NullLogger<WriteArticleStep>.Instance, recorder),
            NullLogger<GenerateArticleJob>.Instance);
    }

    private void SetupChatResponses(params string[] responsesInOrder)
    {
        var queue = new Queue<string>(responsesInOrder);
        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                var text = queue.Count > 0 ? queue.Dequeue() : "{}";
                return new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]);
            });
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

        SetupChatResponses(
            // PlanQueries
            """{"queries":["q1","q2"]}""",
            // AggregateFacts
            """{"facts":[{"claim":"Fact A","confidence":0.9,"source_url":null,"source_title":"S"}],"summary":"sum","gaps":null}""",
            // ValidateFacts
            """{"validated_facts":[{"fact":"Fact A","note":"good","reliable":true}]}""",
            // WriteArticle
            """{"article_title":"Final Title","article_html":"<article>x</article>","sources_used":[{"title":"Src","url":"https://a.com"}]}"""
        );

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

        var callCount = 0;
        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() =>
            {
                callCount++;
                if (callCount == 1)
                {
                    // PlanQueries succeeds with valid JSON
                    return new ChatResponse([new ChatMessage(ChatRole.Assistant, """{"queries":["q1"]}""")]);
                }
                // AggregateFacts fails
                throw new InvalidOperationException("LLM blew up");
            });

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

        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        Func<Task> act = () => CreateJob().RunAsync(article.Id, default);

        await act.Should().ThrowAsync<OperationCanceledException>();
        article.Status.Should().Be(ArticleStatus.Failed);
        article.ErrorMessage.Should().Be("Job cancelled.");
    }
}
