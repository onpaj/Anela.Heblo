using Anela.Heblo.Application.Features.Article;
using Anela.Heblo.Application.Features.Article.Contracts;
using Anela.Heblo.Application.Features.Article.UseCases.Generate;
using Anela.Heblo.Application.Features.Article.UseCases.Generate.Pipeline;
using Anela.Heblo.Application.Shared.WebSearch;
using Anela.Heblo.Domain.Features.Article;
using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using DomainArticle = Anela.Heblo.Domain.Features.Article.Article;

namespace Anela.Heblo.Tests.Article.UseCases;

public class SourceEnrichmentIntegrationTests
{
    private readonly Mock<IArticleRepository> _repository = new();
    private readonly Mock<IChatClient> _chat = new();
    private readonly Mock<IArticleKnowledgeSource> _knowledgeSource = new();
    private readonly Mock<IWebSearchClient> _webSearch = new();
    private readonly Mock<IArticleStyleGuideSource> _styleGuideSource = new();
    private readonly ArticleOptions _options = new();

    [Fact]
    public async Task RunAsync_KnowledgeBaseSource_EnrichedWithChunkIdConfidenceAndExcerpt()
    {
        var articleId = Guid.NewGuid();
        var article = new DomainArticle
        {
            Id = articleId,
            Topic = "Sun care",
            Length = "medium (1000w)",
            UsedKnowledgeBase = true,
        };

        _repository.Setup(r => r.GetForUpdateAsync(articleId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(article);

        var chunkId = Guid.NewGuid();
        _knowledgeSource
            .Setup(s => s.SearchAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ArticleKnowledgeChunk>
            {
                new ArticleKnowledgeChunk
                {
                    ChunkId = chunkId,
                    Content = "All about SPF.",
                    Score = 0.91,
                    SourceFilename = "kb-spf-doc.docx"
                }
            });

        var queue = new Queue<string>(
        [
            // PlanQueriesStep
            """{"queries":["SPF basics"]}""",
            // AggregateFactsStep — SourceTitle matches the snippet SourceFilename
            """{"facts":[{"claim":"SPF protects skin from UV.","confidence":0.9,"source_url":null,"source_title":"kb-spf-doc.docx"}],"summary":"sum","gaps":null}""",
            // ValidateFactsStep
            """{"validated_facts":[{"fact":"SPF protects skin from UV.","note":"verified","reliable":true}]}""",
            // WriteArticleStep — uses the same title as the snippet/fact
            """{"article_title":"T","article_html":"<p>x</p>","sources_used":[{"title":"kb-spf-doc.docx","url":null}]}"""
        ]);

        _chat.Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new ChatResponse([new ChatMessage(ChatRole.Assistant, queue.Dequeue())]));

        var optionsWrapper = Options.Create(_options);
        var recorderRepo = new Mock<IArticleRepository>();
        recorderRepo.Setup(r => r.AddStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        recorderRepo.Setup(r => r.UpdateStepAsync(It.IsAny<ArticleGenerationStep>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        recorderRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        var recorder = new PipelineStepRecorder(recorderRepo.Object);
        var job = new GenerateArticleJob(
            _repository.Object,
            new PlanQueriesStep(_chat.Object, optionsWrapper, NullLogger<PlanQueriesStep>.Instance, recorder),
            new GatherContextStep(_knowledgeSource.Object, _webSearch.Object, _styleGuideSource.Object, optionsWrapper, NullLogger<GatherContextStep>.Instance, recorder),
            new AggregateFactsStep(_chat.Object, optionsWrapper, NullLogger<AggregateFactsStep>.Instance, recorder),
            new ValidateFactsStep(_chat.Object, optionsWrapper, NullLogger<ValidateFactsStep>.Instance, recorder),
            new WriteArticleStep(_chat.Object, optionsWrapper, NullLogger<WriteArticleStep>.Instance, recorder),
            NullLogger<GenerateArticleJob>.Instance);

        await job.RunAsync(articleId);

        article.Sources.Should().HaveCount(1);
        var source = article.Sources[0];
        source.Type.Should().Be(SourceType.KnowledgeBase);
        source.KnowledgeBaseChunkId.Should().Be(chunkId);
        source.Confidence.Should().Be(0.91);
        source.Excerpt.Should().Be("SPF protects skin from UV.");
        source.ValidationNote.Should().Be("verified");
    }
}
