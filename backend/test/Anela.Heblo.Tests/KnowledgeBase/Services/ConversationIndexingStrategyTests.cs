using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.AI;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class ConversationIndexingStrategyTests
{
    private readonly Mock<IConversationTopicSummarizer> _summarizer;
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _embeddingGenerator;
    private readonly GeneratedEmbeddings<Embedding<float>> _generatedEmbeddings;
    private readonly ConversationIndexingStrategy _strategy;

    public ConversationIndexingStrategyTests()
    {
        _summarizer = new Mock<IConversationTopicSummarizer>();

        var floats = new float[] { 0.1f, 0.2f, 0.3f };
        _generatedEmbeddings = new GeneratedEmbeddings<Embedding<float>>(
            [new Embedding<float>(new ReadOnlyMemory<float>(floats))]);

        _embeddingGenerator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        _embeddingGenerator
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(_generatedEmbeddings);

        _strategy = new ConversationIndexingStrategy(
            _summarizer.Object,
            _embeddingGenerator.Object);
    }

    [Fact]
    public void Supports_Conversation_ReturnsTrue()
    {
        Assert.True(_strategy.Supports(DocumentType.Conversation));
    }

    [Fact]
    public void Supports_KnowledgeBase_ReturnsFalse()
    {
        Assert.False(_strategy.Supports(DocumentType.KnowledgeBase));
    }

    [Fact]
    public async Task CreateChunksAsync_EmptyTopics_ReturnsEmptyList()
    {
        _summarizer
            .Setup(s => s.SummarizeTopicsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var chunks = await _strategy.CreateChunksAsync("transcript", Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(chunks);
    }

    [Fact]
    public async Task CreateChunksAsync_NTopicSummaries_ProducesNChunks()
    {
        var topics = new List<string>
        {
            "Produkty: Sérum ABC\nProblém zákazníka: akné",
            "Produkty: Krém XYZ\nProblém zákazníka: popraskané nožky"
        };

        _summarizer
            .Setup(s => s.SummarizeTopicsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(topics);

        var floats = new float[] { 0.1f, 0.2f, 0.3f };
        var twoEmbeddings = new GeneratedEmbeddings<Embedding<float>>(
        [
            new Embedding<float>(new ReadOnlyMemory<float>(floats)),
            new Embedding<float>(new ReadOnlyMemory<float>(floats)),
        ]);
        _embeddingGenerator
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoEmbeddings);

        var documentId = Guid.NewGuid();
        var chunks = await _strategy.CreateChunksAsync("full transcript", documentId, CancellationToken.None);

        Assert.Equal(2, chunks.Count);
    }

    [Fact]
    public async Task CreateChunksAsync_AllChunksHaveFullTranscriptAsContent()
    {
        const string fullText = "Zákazník: Mám problém s akné\nAnela: Doporučuji Sérum ABC";
        var topics = new List<string>
        {
            "Problém zákazníka: akné",
            "Doporučení: Sérum ABC"
        };

        _summarizer
            .Setup(s => s.SummarizeTopicsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(topics);

        var floats = new float[] { 0.1f, 0.2f, 0.3f };
        var twoEmbeddings = new GeneratedEmbeddings<Embedding<float>>(
        [
            new Embedding<float>(new ReadOnlyMemory<float>(floats)),
            new Embedding<float>(new ReadOnlyMemory<float>(floats)),
        ]);
        _embeddingGenerator
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(twoEmbeddings);

        var chunks = await _strategy.CreateChunksAsync(fullText, Guid.NewGuid(), CancellationToken.None);

        Assert.All(chunks, chunk => Assert.Equal(fullText, chunk.Content));
    }

    [Fact]
    public async Task CreateChunksAsync_ChunkIndexMatchesTopicPosition()
    {
        var topics = new List<string> { "Topic 0", "Topic 1", "Topic 2" };

        _summarizer
            .Setup(s => s.SummarizeTopicsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(topics);

        var floats = new float[] { 0.1f, 0.2f, 0.3f };
        var threeEmbeddings = new GeneratedEmbeddings<Embedding<float>>(
        [
            new Embedding<float>(new ReadOnlyMemory<float>(floats)),
            new Embedding<float>(new ReadOnlyMemory<float>(floats)),
            new Embedding<float>(new ReadOnlyMemory<float>(floats)),
        ]);
        _embeddingGenerator
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(threeEmbeddings);

        var chunks = await _strategy.CreateChunksAsync("transcript", Guid.NewGuid(), CancellationToken.None);

        for (var i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
        }
    }

    [Fact]
    public async Task CreateChunksAsync_EmbeddingInputIsTopicSummary_NotFullText()
    {
        const string fullText = "full transcript text";
        const string topicSummary = "Problém zákazníka: akné";

        _summarizer
            .Setup(s => s.SummarizeTopicsAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { topicSummary });

        string? capturedEmbeddingInput = null;
        _embeddingGenerator
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>(
                (texts, _, _) => capturedEmbeddingInput = texts.First())
            .ReturnsAsync(_generatedEmbeddings);

        await _strategy.CreateChunksAsync(fullText, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(topicSummary, capturedEmbeddingInput);
        Assert.NotEqual(fullText, capturedEmbeddingInput);
    }
}
