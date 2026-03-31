using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Pipeline;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.AskQuestion;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using MediatR;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class AskQuestionHandlerTests
{
    private readonly Mock<IMediator> _mediator = new();
    private readonly Mock<IChatClient> _chatClient = new();
    private readonly Mock<IProductEnrichmentCache> _enrichmentCache = new();

    private AskQuestionHandler CreateHandler(KnowledgeBaseOptions? options = null) =>
        new(_mediator.Object, _chatClient.Object, Options.Create(options ?? new KnowledgeBaseOptions()),
            _enrichmentCache.Object);

    private void SetupEmptyCache() =>
        _enrichmentCache
            .Setup(c => c.GetProductLookupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, ProductEnrichmentEntry>());

    [Fact]
    public async Task Handle_ReturnsAnswerWithSources()
    {
        SetupEmptyCache();
        var searchResponse = new SearchDocumentsResponse
        {
            Chunks =
            [
                new ChunkResult
                {
                    ChunkId = Guid.NewGuid(),
                    DocumentId = Guid.NewGuid(),
                    Content = "Max phenoxyethanol 1.0% per EU regulation",
                    Score = 0.95,
                    SourceFilename = "EU_reg.pdf",
                    SourcePath = "/archived/EU_reg.pdf"
                }
            ]
        };

        _mediator
            .Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), default))
            .ReturnsAsync(searchResponse);

        var chatResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, "The maximum allowed concentration is 1.0%.")]);
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                default))
            .ReturnsAsync(chatResponse);

        var result = await CreateHandler().Handle(
            new AskQuestionRequest { Question = "Max phenoxyethanol?", TopK = 5 },
            default);

        Assert.Equal("The maximum allowed concentration is 1.0%.", result.Answer);
        Assert.Single(result.Sources);
        Assert.Equal("EU_reg.pdf", result.Sources[0].Filename);
    }

    [Fact]
    public async Task Handle_EmptyChunks_ReturnsFallbackAnswerWithNoSources()
    {
        SetupEmptyCache();
        _mediator
            .Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), default))
            .ReturnsAsync(new SearchDocumentsResponse { Chunks = [], BelowThresholdCount = 3 });

        var result = await CreateHandler().Handle(
            new AskQuestionRequest { Question = "Co mi poradis na akne?", TopK = 5 },
            default);

        Assert.Equal("V dostupných dokumentech jsem nenašla relevantní informaci k vaší otázce.", result.Answer);
        Assert.Empty(result.Sources);
        _chatClient.Verify(c => c.GetResponseAsync(
            It.IsAny<IEnumerable<ChatMessage>>(),
            It.IsAny<ChatOptions?>(),
            default), Times.Never);
    }

    [Fact]
    public async Task Handle_WithProducts_InjectsProductTableIntoSystemPrompt()
    {
        var products = new Dictionary<string, ProductEnrichmentEntry>
        {
            ["AKL001"] = new() { ProductCode = "AKL001", ProductName = "Sérum ABC" },
            ["KRM002"] = new() { ProductCode = "KRM002", ProductName = "Hydratační krém" }
        };
        _enrichmentCache
            .Setup(c => c.GetProductLookupAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(products);

        _mediator
            .Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), default))
            .ReturnsAsync(new SearchDocumentsResponse
            {
                Chunks = [new ChunkResult { ChunkId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Content = "some content", Score = 0.9, SourceFilename = "doc.pdf", SourcePath = "/doc.pdf" }]
            });

        IEnumerable<ChatMessage>? capturedMessages = null;
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                default))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (msgs, _, _) => capturedMessages = msgs)
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "odpověď")]));

        var options = new KnowledgeBaseOptions
        {
            AskQuestionSystemPrompt = "Kontext: {context}\nProdukty: {products}\nDotaz: {query}"
        };

        await CreateHandler(options).Handle(
            new AskQuestionRequest { Question = "Co doporučuješ?", TopK = 5 },
            default);

        var systemMessage = capturedMessages!.First(m => m.Role == ChatRole.System).Text!;
        Assert.Contains("AKL001 | Sérum ABC", systemMessage);
        Assert.Contains("KRM002 | Hydratační krém", systemMessage);
    }

    [Fact]
    public async Task Handle_EmptyProductCatalog_ProductsPlaceholderReplacedWithEmptyString()
    {
        SetupEmptyCache();
        _mediator
            .Setup(m => m.Send(It.IsAny<SearchDocumentsRequest>(), default))
            .ReturnsAsync(new SearchDocumentsResponse
            {
                Chunks = [new ChunkResult { ChunkId = Guid.NewGuid(), DocumentId = Guid.NewGuid(), Content = "some content", Score = 0.9, SourceFilename = "doc.pdf", SourcePath = "/doc.pdf" }]
            });

        IEnumerable<ChatMessage>? capturedMessages = null;
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                default))
            .Callback<IEnumerable<ChatMessage>, ChatOptions?, CancellationToken>(
                (msgs, _, _) => capturedMessages = msgs)
            .ReturnsAsync(new ChatResponse([new ChatMessage(ChatRole.Assistant, "odpověď")]));

        var options = new KnowledgeBaseOptions
        {
            AskQuestionSystemPrompt = "Kontext: {context}\nProdukty: {products}\nDotaz: {query}"
        };

        await CreateHandler(options).Handle(
            new AskQuestionRequest { Question = "Dotaz?", TopK = 5 },
            default);

        var systemMessage = capturedMessages!.First(m => m.Role == ChatRole.System).Text!;
        Assert.DoesNotContain("{products}", systemMessage);
        Assert.DoesNotContain("AKL001", systemMessage);
    }
}
