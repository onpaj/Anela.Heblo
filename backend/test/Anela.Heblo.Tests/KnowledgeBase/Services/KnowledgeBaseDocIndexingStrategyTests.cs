using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class KnowledgeBaseDocIndexingStrategyTests
{
    private readonly Mock<IChunkSummarizer> _summarizer;
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _embeddingGenerator;
    private readonly GeneratedEmbeddings<Embedding<float>> _generatedEmbeddings;
    private readonly KnowledgeBaseDocIndexingStrategy _strategy;

    public KnowledgeBaseDocIndexingStrategyTests()
    {
        _summarizer = new Mock<IChunkSummarizer>();
        _summarizer
            .Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) => text);

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

        var options = Options.Create(new KnowledgeBaseOptions { ChunkSize = 512, ChunkOverlapTokens = 50 });
        var chunker = new DocumentChunker(options);

        _strategy = new KnowledgeBaseDocIndexingStrategy(
            chunker,
            _summarizer.Object,
            _embeddingGenerator.Object);
    }

    [Fact]
    public void Supports_KnowledgeBase_ReturnsTrue()
    {
        Assert.True(_strategy.Supports(DocumentType.KnowledgeBase));
    }

    [Fact]
    public void Supports_Conversation_ReturnsFalse()
    {
        Assert.False(_strategy.Supports(DocumentType.Conversation));
    }

    [Fact]
    public async Task CreateChunksAsync_ProducesChunksWithEmbeddings()
    {
        var documentId = Guid.NewGuid();
        var text = "word1 word2 word3";

        var chunks = await _strategy.CreateChunksAsync(text, documentId, CancellationToken.None);

        Assert.NotEmpty(chunks);
        _embeddingGenerator.Verify(
            e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        Assert.All(chunks, chunk =>
        {
            Assert.Equal(documentId, chunk.DocumentId);
            Assert.NotEmpty(chunk.Embedding);
        });
    }

    [Fact]
    public async Task CreateChunksAsync_EmbeddingIsGeneratedFromSummary()
    {
        const string summary = "Problém zákazníka: akné";

        _summarizer
            .Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        string? capturedEmbeddingInput = null;
        _embeddingGenerator
            .Setup(e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<string>, EmbeddingGenerationOptions?, CancellationToken>(
                (texts, _, _) => capturedEmbeddingInput = texts.First())
            .ReturnsAsync(_generatedEmbeddings);

        var documentId = Guid.NewGuid();
        await _strategy.CreateChunksAsync("word1 word2 word3", documentId, CancellationToken.None);

        Assert.Equal(summary, capturedEmbeddingInput);
    }

    [Fact]
    public async Task CreateChunksAsync_ChunkContentIsChunkText_NotSummary()
    {
        const string extractedText = "word1 word2 word3";
        const string summary = "Problém zákazníka: suchá pleť";

        _summarizer
            .Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        var documentId = Guid.NewGuid();
        var chunks = await _strategy.CreateChunksAsync(extractedText, documentId, CancellationToken.None);

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk =>
        {
            Assert.Equal(extractedText, chunk.Content);
            Assert.DoesNotContain(summary, chunk.Content);
        });
    }

    [Fact]
    public async Task CreateChunksAsync_ChunkIndexIsSequential()
    {
        var options = Options.Create(new KnowledgeBaseOptions { ChunkSize = 5, ChunkOverlapTokens = 1 });
        var chunker = new DocumentChunker(options);
        var strategy = new KnowledgeBaseDocIndexingStrategy(chunker, _summarizer.Object, _embeddingGenerator.Object);

        var words = string.Join(" ", Enumerable.Range(1, 20).Select(i => $"w{i}"));
        var chunks = await strategy.CreateChunksAsync(words, Guid.NewGuid(), CancellationToken.None);

        Assert.True(chunks.Count > 1);
        for (var i = 0; i < chunks.Count; i++)
        {
            Assert.Equal(i, chunks[i].ChunkIndex);
        }
    }
}
