using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.SearchDocuments;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class SearchDocumentsHandlerTests
{
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _embeddingGenerator = new();
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();
    private readonly Mock<IChatClient> _chatClient = new();

    private SearchDocumentsHandler CreateHandler(double minScore = 0.60, bool queryExpansionEnabled = false)
    {
        var options = Options.Create(new KnowledgeBaseOptions
        {
            MinSimilarityScore = minScore,
            QueryExpansionEnabled = queryExpansionEnabled,
            QueryExpansionPrompt = "Expand:"
        });
        return new SearchDocumentsHandler(_embeddingGenerator.Object, _repository.Object, options, _chatClient.Object);
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

    private void SetupChatClient(string expandedText)
    {
        var chatResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, expandedText)]);
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                default))
            .ReturnsAsync(chatResponse);
    }

    [Fact]
    public async Task Handle_ReturnsChunksOrderedByScore()
    {
        SetupEmbeddingGenerator();
        var chunk = MakeChunk("Phenoxyethanol max 1.0% in Annex V");

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
        SetupEmbeddingGenerator();
        SetupChatClient(expandedQuery);

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

        await CreateHandler(queryExpansionEnabled: true).Handle(
            new SearchDocumentsRequest { Query = "poradis neco na popraskane ruce?", TopK = 5 },
            default);

        Assert.Equal(expandedQuery, capturedEmbeddingInput);
    }

    [Fact]
    public async Task Handle_QueryExpansionDisabled_EmbedRawQuery()
    {
        const string rawQuery = "poradis neco na popraskane ruce?";
        SetupEmbeddingGenerator();

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

        await CreateHandler(queryExpansionEnabled: false).Handle(
            new SearchDocumentsRequest { Query = rawQuery, TopK = 5 },
            default);

        Assert.Equal(rawQuery, capturedEmbeddingInput);
        _chatClient.Verify(
            c => c.GetResponseAsync(It.IsAny<IEnumerable<ChatMessage>>(), It.IsAny<ChatOptions?>(), default),
            Times.Never);
    }

    [Fact]
    public async Task Handle_QueryExpansionEnabled_LlmReturnsEmpty_FallsBackToRawQuery()
    {
        const string rawQuery = "popraskane ruce?";
        SetupEmbeddingGenerator();

        var emptyResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, "   ")]);
        _chatClient
            .Setup(c => c.GetResponseAsync(
                It.IsAny<IEnumerable<ChatMessage>>(),
                It.IsAny<ChatOptions?>(),
                default))
            .ReturnsAsync(emptyResponse);

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

        await CreateHandler(queryExpansionEnabled: true).Handle(
            new SearchDocumentsRequest { Query = rawQuery, TopK = 5 },
            default);

        Assert.Equal(rawQuery, capturedEmbeddingInput);
    }
}
