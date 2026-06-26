using Anela.Heblo.Application.Features.Article;
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;
using Anela.Heblo.Application.Shared.WebSearch;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Article.Pipeline;

public class GatherContextStepTests
{
    private readonly Mock<IArticleKnowledgeSource> _knowledgeSource = new();
    private readonly Mock<IWebSearchClient> _webSearch = new();
    private readonly Mock<IArticleStyleGuideSource> _styleGuideSource = new();
    private readonly ArticleOptions _options = new();

    private static PipelineStepRecorder CreateNoOpRecorder()
    {
        var repo = new Mock<IArticleRepository>();
        repo.Setup(r => r.AddStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.UpdateStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        repo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        return new PipelineStepRecorder(repo.Object);
    }

    private GatherContextStep CreateStep() =>
        new(_knowledgeSource.Object, _webSearch.Object, _styleGuideSource.Object,
            Options.Create(_options), NullLogger<GatherContextStep>.Instance, CreateNoOpRecorder());

    private static ArticlePipelineContext CreateContext(
        bool useKb,
        bool useWeb,
        params string[] queries)
    {
        var article = new DomainArticle
        {
            Topic = "Topic",
            UsedKnowledgeBase = useKb,
            UsedWebSearch = useWeb
        };
        return new ArticlePipelineContext
        {
            Article = article,
            SearchQueries = queries.ToList()
        };
    }

    [Fact]
    public async Task ExecuteAsync_KnowledgeBaseEnabled_AddsKbSnippets()
    {
        var chunkId = Guid.NewGuid();
        _knowledgeSource
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ArticleKnowledgeChunk>
            {
                new ArticleKnowledgeChunk
                {
                    ChunkId = chunkId,
                    Content = "KB content here",
                    Score = 0.9,
                    SourceFilename = "kb-source.pdf"
                }
            });

        var context = CreateContext(useKb: true, useWeb: false, "query");

        await CreateStep().ExecuteAsync(context, default);

        _knowledgeSource.Verify(
            s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
        context.ContextSnippets.Should().ContainSingle();
        context.ContextSnippets[0].Source.Should().Be(SourceType.KnowledgeBase);
        context.ContextSnippets[0].Title.Should().Be("kb-source.pdf");
        context.ContextSnippets[0].Excerpt.Should().Be("KB content here");
        context.ContextSnippets[0].ChunkId.Should().Be(chunkId);
    }

    [Fact]
    public async Task ExecuteAsync_WebSearchDisabled_DoesNotCallWebSearch()
    {
        var context = CreateContext(useKb: false, useWeb: false, "query");

        await CreateStep().ExecuteAsync(context, default);

        _webSearch.Verify(
            w => w.SearchAsync(It.IsAny<string>(), It.IsAny<WebSearchOptions>(), It.IsAny<CancellationToken>()),
            Times.Never);
        context.ContextSnippets.Should().BeEmpty();
    }

    [Fact]
    public async Task ExecuteAsync_KbThrows_OtherBranchesCompleteSuccessfully()
    {
        _knowledgeSource
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("KB down"));

        _webSearch
            .Setup(w => w.SearchAsync(It.IsAny<string>(), It.IsAny<WebSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebSearchResult
            {
                Hits = new List<WebSearchHit>
                {
                    new() { Title = "Web", Url = "https://example.com", Snippet = "snippet" }
                }
            });

        var context = CreateContext(useKb: true, useWeb: true, "query");

        await CreateStep().ExecuteAsync(context, default);

        context.ContextSnippets.Should().ContainSingle();
        context.ContextSnippets[0].Source.Should().Be(SourceType.Web);
    }

    [Fact]
    public async Task ExecuteAsync_WebSearchThrows_KbSnippetsStillPresent()
    {
        _knowledgeSource
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ArticleKnowledgeChunk>
            {
                new ArticleKnowledgeChunk
                {
                    ChunkId = Guid.NewGuid(),
                    Content = "kb content",
                    Score = 0.9,
                    SourceFilename = "doc.pdf"
                }
            });

        _webSearch
            .Setup(w => w.SearchAsync(It.IsAny<string>(), It.IsAny<WebSearchOptions>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Web down"));

        var context = CreateContext(useKb: true, useWeb: true, "query");

        await CreateStep().ExecuteAsync(context, default);

        context.ContextSnippets.Should().ContainSingle();
        context.ContextSnippets[0].Source.Should().Be(SourceType.KnowledgeBase);
    }

    [Fact]
    public async Task ExecuteAsync_DuplicateWebUrls_DeduplicatesByUrl()
    {
        _webSearch
            .Setup(w => w.SearchAsync(It.IsAny<string>(), It.IsAny<WebSearchOptions>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new WebSearchResult
            {
                Hits = new List<WebSearchHit>
                {
                    new() { Title = "First", Url = "https://example.com/page", Snippet = "first" },
                    new() { Title = "Duplicate", Url = "https://example.com/page", Snippet = "second" }
                }
            });

        var context = CreateContext(useKb: false, useWeb: true, "query");

        await CreateStep().ExecuteAsync(context, default);

        context.ContextSnippets.Should().ContainSingle();
        context.ContextSnippets[0].Title.Should().Be("First");
    }

    [Fact]
    public async Task ExecuteAsync_StyleGuideConfigured_LoadsTextViaContract()
    {
        const string driveId = "drive-1";
        const string itemPath = "/style.md";
        const string guide = "Tone: friendly";

        _styleGuideSource
            .Setup(s => s.DownloadStyleGuideTextAsync(driveId, itemPath, It.IsAny<CancellationToken>()))
            .ReturnsAsync(guide);

        var article = new DomainArticle
        {
            Topic = "Topic",
            UsedKnowledgeBase = false,
            UsedWebSearch = false,
            StyleGuideDriveId = driveId,
            StyleGuideItemPath = itemPath
        };
        var context = new ArticlePipelineContext
        {
            Article = article,
            SearchQueries = new List<string>()
        };

        await CreateStep().ExecuteAsync(context, default);

        context.StyleGuideText.Should().Be(guide);
        _styleGuideSource.Verify(
            s => s.DownloadStyleGuideTextAsync(driveId, itemPath, It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
