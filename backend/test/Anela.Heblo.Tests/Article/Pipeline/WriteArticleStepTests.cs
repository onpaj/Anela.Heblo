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

public class WriteArticleStepTests
{
    private readonly Mock<IChatClient> _chat = new();
    private readonly ArticleOptions _options = new();

    private WriteArticleStep CreateStep() =>
        new(_chat.Object, Options.Create(_options), NullLogger<WriteArticleStep>.Instance);

    private static ArticlePipelineContext CreateContext(string topic = "Topic") =>
        new()
        {
            Article = new DomainArticle { Topic = topic, Length = "medium (1000w)" }
        };

    private void SetupChatResponse(string text) =>
        _chat
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]));

    [Fact]
    public async Task ExecuteAsync_ValidJson_SetsTitleHtmlAndSources()
    {
        SetupChatResponse(
            """
            {"article_title":"My Title","article_html":"<article>Body</article>","sources_used":[
              {"title":"Web Source","url":"https://example.com"},
              {"title":"KB Source","url":null}
            ]}
            """);

        var context = CreateContext();

        await CreateStep().ExecuteAsync(context, default);

        context.GeneratedTitle.Should().Be("My Title");
        context.GeneratedHtml.Should().Be("<article>Body</article>");
        context.SourceRefs.Should().HaveCount(2);
        context.SourceRefs[0].Title.Should().Be("Web Source");
        context.SourceRefs[0].Url.Should().Be("https://example.com");
        context.SourceRefs[0].Type.Should().Be(SourceType.Web);
        context.SourceRefs[1].Title.Should().Be("KB Source");
        context.SourceRefs[1].Url.Should().BeNull();
        context.SourceRefs[1].Type.Should().Be(SourceType.KnowledgeBase);
    }

    [Fact]
    public async Task ExecuteAsync_KbSourceMatchesSnippet_PopulatesChunkIdAndExcerpt()
    {
        var chunkId = Guid.NewGuid();
        SetupChatResponse(
            """
            {"article_title":"T","article_html":"<p>x</p>","sources_used":[
              {"title":"Hydration Guide","url":null}
            ]}
            """);

        var context = CreateContext();
        context.ContextSnippets.Add(new ContextSnippet
        {
            Source = SourceType.KnowledgeBase,
            Title = "Hydration Guide",
            Excerpt = "Use SPF daily for best results.",
            ChunkId = chunkId
        });
        context.Facts.Add(new AggregatedFact
        {
            Claim = "SPF prevents aging",
            Confidence = 0.88,
            SourceTitle = "Hydration Guide",
            ValidationNote = "Verified by dermatologists"
        });

        await CreateStep().ExecuteAsync(context, default);

        var src = context.SourceRefs.Single();
        src.ChunkId.Should().Be(chunkId);
        src.Excerpt.Should().Be("Use SPF daily for best results.");
        src.Confidence.Should().BeApproximately(0.88, 0.001);
        src.ValidationNote.Should().Be("Verified by dermatologists");
    }

    [Fact]
    public async Task ExecuteAsync_WebSource_HasNullChunkId()
    {
        SetupChatResponse(
            """
            {"article_title":"T","article_html":"<p>x</p>","sources_used":[
              {"title":"Example","url":"https://example.com"}
            ]}
            """);

        var context = CreateContext();

        await CreateStep().ExecuteAsync(context, default);

        var src = context.SourceRefs.Single();
        src.Type.Should().Be(SourceType.Web);
        src.ChunkId.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_KbSourceNoMatchingSnippet_HasNullChunkId()
    {
        SetupChatResponse(
            """
            {"article_title":"T","article_html":"<p>x</p>","sources_used":[
              {"title":"Unknown Source","url":null}
            ]}
            """);

        var context = CreateContext();

        await CreateStep().ExecuteAsync(context, default);

        var src = context.SourceRefs.Single();
        src.Type.Should().Be(SourceType.KnowledgeBase);
        src.ChunkId.Should().BeNull();
        src.Excerpt.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteAsync_GarbageResponse_FallsBackToTopicTitleAndWrappedHtml()
    {
        SetupChatResponse("garbage");

        var context = CreateContext("Sun Care");

        await CreateStep().ExecuteAsync(context, default);

        context.GeneratedTitle.Should().Be("Sun Care");
        context.GeneratedHtml.Should().Be("<p>garbage</p>");
        context.SourceRefs.Should().BeEmpty();
    }
}
