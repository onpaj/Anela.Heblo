using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Application.Shared.Rag;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class SearchDocumentsHandlerTests
{
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _embeddingGenerator = new();
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();
    private readonly Mock<IRagQueryExpander> _expander = new();
    private readonly Mock<ILogger<SearchDocumentsHandler>> _logger = new();

    private SearchDocumentsHandler CreateHandler(double minScore = 0.60)
    {
        var options = Options.Create(new KnowledgeBaseOptions
        {
            MinSimilarityScore = minScore,
            QueryExpansionPrompt = "Expand:"
        });
        return new SearchDocumentsHandler(_embeddingGenerator.Object, _repository.Object, options, _expander.Object, _logger.Object);
    }

    private void SetupEmbeddingGenerator()
    {
        var vector = new ReadOnlyMemory<float>(new float[] { 0.1f, 0.2f, 0.3f });
        var generated = new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(vector)]);
        _embeddingGenerator
            .Setup(s => s.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                default))
            .ReturnsAsync(generated);
    }

    private static KnowledgeBaseChunk MakeChunk(string content) => new()
    {
        Id = Guid.NewGuid(),
        DocumentId = Guid.NewGuid(),
        ChunkIndex = 0,
        Content = content,
        Embedding = [0.1f, 0.2f, 0.3f],
        Document = new KnowledgeBaseDocument { Filename = "doc.pdf", SourcePath = "/inbox/doc.pdf" }
    };

    [Fact]
    public async Task Handle_ReturnsChunksOrderedByScore()
    {
        SetupEmbeddingGenerator();
        var chunk = MakeChunk("Phenoxyethanol max 1.0% in Annex V");

        _expander
            .Setup(e => e.ExpandAsync(It.IsAny<string>(), It.IsAny<RagQueryExpansionConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string q, RagQueryExpansionConfig _, CancellationToken _) => q);

        _repository
            .Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), 5, default))
            .ReturnsAsync([(chunk, 0.95)]);

        var result = await CreateHandler().Handle(
            new SearchDocumentsRequest { Query = "phenoxyethanol concentration", TopK = 5 },
            default);

        Assert.Single(result.Chunks);
        Assert.Equal("Phenoxyethanol max 1.0% in Annex V", result.Chunks[0].Content);
        Assert.Equal(0.95, result.Chunks[0].Score);
        Assert.Equal("doc.pdf", result.Chunks[0].SourceFilename);
        Assert.Equal(0, result.BelowThresholdCount);
    }

    [Fact]
    public async Task Handle_AllResultsBelowThreshold_ReturnsEmptyWithCorrectCount()
    {
        SetupEmbeddingGenerator();
        var chunk1 = MakeChunk("Low relevance chunk A");
        var chunk2 = MakeChunk("Low relevance chunk B");

        _expander
            .Setup(e => e.ExpandAsync(It.IsAny<string>(), It.IsAny<RagQueryExpansionConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string q, RagQueryExpansionConfig _, CancellationToken _) => q);

        _repository
            .Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), 5, default))
            .ReturnsAsync([(chunk1, 0.54), (chunk2, 0.58)]);

        var result = await CreateHandler(minScore: 0.60).Handle(
            new SearchDocumentsRequest { Query = "anything", TopK = 5 },
            default);

        Assert.Empty(result.Chunks);
        Assert.Equal(2, result.BelowThresholdCount);
    }

    [Fact]
    public async Task Handle_MixedResults_OnlyAboveThresholdChunksReturned()
    {
        SetupEmbeddingGenerator();
        var goodChunk = MakeChunk("Good relevant content");
        var badChunk = MakeChunk("Weak noisy content");

        _expander
            .Setup(e => e.ExpandAsync(It.IsAny<string>(), It.IsAny<RagQueryExpansionConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string q, RagQueryExpansionConfig _, CancellationToken _) => q);

        _repository
            .Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), 5, default))
            .ReturnsAsync([(goodChunk, 0.80), (badChunk, 0.45)]);

        var result = await CreateHandler(minScore: 0.60).Handle(
            new SearchDocumentsRequest { Query = "anything", TopK = 5 },
            default);

        Assert.Single(result.Chunks);
        Assert.Equal("Good relevant content", result.Chunks[0].Content);
        Assert.Equal(0.80, result.Chunks[0].Score);
        Assert.Equal(1, result.BelowThresholdCount);
    }

    [Fact]
    public async Task Handle_QueryExpansionEnabled_EmbedExpandedQuery()
    {
        const string expandedQuery = "Problém: popraskané ruce\nKontext: suchá kůže";

        _expander
            .Setup(e => e.ExpandAsync(It.IsAny<string>(), It.IsAny<RagQueryExpansionConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expandedQuery);

        _repository
            .Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), 5, default))
            .ReturnsAsync([]);

        string? capturedEmbeddingInput = null;
        _embeddingGenerator
            .Setup(s => s.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                default))
            .Callback<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>(
                (texts, _, _) => capturedEmbeddingInput = texts.First())
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                [new Embedding<float>(new ReadOnlyMemory<float>(new float[] { 0.1f, 0.2f, 0.3f }))]));

        await CreateHandler().Handle(
            new SearchDocumentsRequest { Query = "poradis neco na popraskane ruce?", TopK = 5 },
            default);

        Assert.Equal(expandedQuery, capturedEmbeddingInput);
        _expander.Verify(
            e => e.ExpandAsync("poradis neco na popraskane ruce?", It.IsAny<RagQueryExpansionConfig>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_WhenExpanderReturnsRawQuery_RawQueryIsEmbedded()
    {
        const string rawQuery = "poradis neco na popraskane ruce?";

        _expander
            .Setup(e => e.ExpandAsync(It.IsAny<string>(), It.IsAny<RagQueryExpansionConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rawQuery);

        _repository
            .Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), 5, default))
            .ReturnsAsync([]);

        string? capturedEmbeddingInput = null;
        _embeddingGenerator
            .Setup(s => s.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                default))
            .Callback<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>(
                (texts, _, _) => capturedEmbeddingInput = texts.First())
            .ReturnsAsync(new GeneratedEmbeddings<Embedding<float>>(
                [new Embedding<float>(new ReadOnlyMemory<float>(new float[] { 0.1f, 0.2f, 0.3f }))]));

        await CreateHandler().Handle(
            new SearchDocumentsRequest { Query = rawQuery, TopK = 5 },
            default);

        Assert.Equal(rawQuery, capturedEmbeddingInput);
        _expander.Verify(
            e => e.ExpandAsync(rawQuery, It.IsAny<RagQueryExpansionConfig>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(TaskCanceledException))]
    public async Task Handle_EmbeddingGeneratorThrowsTransientException_ReturnsEmptyResponse(Type exceptionType)
    {
        _expander
            .Setup(e => e.ExpandAsync(It.IsAny<string>(), It.IsAny<RagQueryExpansionConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string q, RagQueryExpansionConfig _, CancellationToken _) => q);

        var exception = (Exception)Activator.CreateInstance(exceptionType, "Exception while reading from stream")!;
        _embeddingGenerator
            .Setup(s => s.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                default))
            .ThrowsAsync(exception);

        var result = await CreateHandler().Handle(
            new SearchDocumentsRequest { Query = "test query", TopK = 5 },
            default);

        Assert.Empty(result.Chunks);
        Assert.Equal(0, result.BelowThresholdCount);
        _repository.Verify(
            r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(TaskCanceledException))]
    public async Task Handle_EmbeddingGeneratorThrowsTransientException_LogsWarningWithExceptionType(Type exceptionType)
    {
        _expander
            .Setup(e => e.ExpandAsync(It.IsAny<string>(), It.IsAny<RagQueryExpansionConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string q, RagQueryExpansionConfig _, CancellationToken _) => q);

        var exception = (Exception)Activator.CreateInstance(exceptionType, "stream error")!;
        _embeddingGenerator
            .Setup(s => s.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                default))
            .ThrowsAsync(exception);

        await CreateHandler().Handle(
            new SearchDocumentsRequest { Query = "test query", TopK = 5 },
            default);

        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Transient embedding failure")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(TaskCanceledException))]
    public async Task Handle_RepositoryThrowsTransientException_ReturnsEmptyResponse(Type exceptionType)
    {
        SetupEmbeddingGenerator();

        _expander
            .Setup(e => e.ExpandAsync(It.IsAny<string>(), It.IsAny<RagQueryExpansionConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string q, RagQueryExpansionConfig _, CancellationToken _) => q);

        var exception = (Exception)Activator.CreateInstance(exceptionType, "stream error from pgvector")!;
        _repository
            .Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), default))
            .ThrowsAsync(exception);

        var result = await CreateHandler().Handle(
            new SearchDocumentsRequest { Query = "test query", TopK = 5 },
            default);

        Assert.Empty(result.Chunks);
        Assert.Equal(0, result.BelowThresholdCount);
    }

    [Theory]
    [InlineData(typeof(IOException))]
    [InlineData(typeof(HttpRequestException))]
    [InlineData(typeof(TimeoutException))]
    [InlineData(typeof(TaskCanceledException))]
    public async Task Handle_RepositoryThrowsTransientException_LogsWarningWithExceptionType(Type exceptionType)
    {
        SetupEmbeddingGenerator();

        _expander
            .Setup(e => e.ExpandAsync(It.IsAny<string>(), It.IsAny<RagQueryExpansionConfig>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string q, RagQueryExpansionConfig _, CancellationToken _) => q);

        var exception = (Exception)Activator.CreateInstance(exceptionType, "stream error from pgvector")!;
        _repository
            .Setup(r => r.SearchSimilarAsync(It.IsAny<float[]>(), It.IsAny<int>(), default))
            .ThrowsAsync(exception);

        await CreateHandler().Handle(
            new SearchDocumentsRequest { Query = "test query", TopK = 5 },
            default);

        _logger.Verify(
            l => l.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("Transient vector DB failure")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
