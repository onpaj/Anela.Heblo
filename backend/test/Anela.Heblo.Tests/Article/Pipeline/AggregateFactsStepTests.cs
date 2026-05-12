using Anela.Heblo.Application.Features.Article;
using Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Article.Pipeline;

public class AggregateFactsStepTests
{
    private readonly Mock<IChatClient> _chat = new();
    private readonly ArticleOptions _options = new();

    private static PipelineStepRecorder CreateNoOpRecorder()
    {
        var repo = new Mock<IArticleRepository>();
        repo.Setup(r => r.AddStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return new PipelineStepRecorder(repo.Object);
    }

    private AggregateFactsStep CreateStep() =>
        new(_chat.Object, Options.Create(_options), NullLogger<AggregateFactsStep>.Instance, CreateNoOpRecorder());

    private static ArticlePipelineContext CreateContext(List<ContextSnippet>? snippets = null) =>
        new()
        {
            Article = new DomainArticle { Topic = "Topic", Scope = "overview" },
            ContextSnippets = snippets ?? []
        };

    private void SetupChatResponse(string text) =>
        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]));

    [Fact]
    public async Task ExecuteAsync_ValidJson_SetsFactsOnContext()
    {
        SetupChatResponse(
            """
            {"facts":[
              {"claim":"SPF blocks UVB","confidence":0.95,"source_url":"https://a.com","source_title":"Source A"},
              {"claim":"Reapply every 2 hours","confidence":0.8,"source_url":null,"source_title":"Doc B"}
            ],"summary":"sun protection","gaps":null}
            """);

        var snippets = new List<ContextSnippet>
        {
            new() { Source = SourceType.Web, Title = "Web", Excerpt = "uvb info", Url = "https://a.com" }
        };
        var context = CreateContext(snippets);

        await CreateStep().ExecuteAsync(context, default);

        context.Facts.Should().HaveCount(2);
        context.Facts[0].Claim.Should().Be("SPF blocks UVB");
        context.Facts[0].Confidence.Should().Be(0.95);
        context.Facts[0].SourceUrl.Should().Be("https://a.com");
        context.Facts[0].SourceTitle.Should().Be("Source A");
        context.Facts[1].Claim.Should().Be("Reapply every 2 hours");
        context.Facts[1].SourceUrl.Should().BeNull();
        context.Facts[1].SourceTitle.Should().Be("Doc B");
    }

    [Fact]
    public async Task ExecuteAsync_EmptyContextSnippets_StillCallsLlmAndProducesEmptyFactsOnBadJson()
    {
        SetupChatResponse("garbage non-json output");

        var context = CreateContext(snippets: []);

        await CreateStep().ExecuteAsync(context, default);

        _chat.Verify(
            c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
        context.Facts.Should().BeEmpty();
    }
}
