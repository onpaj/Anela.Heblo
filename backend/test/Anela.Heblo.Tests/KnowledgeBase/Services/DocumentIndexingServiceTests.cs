using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class DocumentIndexingServiceTests
{
    private readonly Mock<IDocumentTextExtractor> _pdfExtractor;
    private readonly Mock<IEmbeddingGenerator<string, Embedding<float>>> _embeddingGenerator;
    private readonly Mock<IKnowledgeBaseRepository> _repository;
    private readonly Mock<IChunkSummarizer> _summarizer;
    private readonly GeneratedEmbeddings<Embedding<float>> _generatedEmbeddings;
    private readonly DocumentIndexingService _service;

    public DocumentIndexingServiceTests()
    {
        _pdfExtractor = new Mock<IDocumentTextExtractor>();
        _pdfExtractor.Setup(e => e.CanHandle("application/pdf")).Returns(true);
        _pdfExtractor.Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("word1 word2 word3");

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

        _repository = new Mock<IKnowledgeBaseRepository>();

        _summarizer = new Mock<IChunkSummarizer>();
        _summarizer
            .Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string text, CancellationToken _) => text);

        var options = Options.Create(new KnowledgeBaseOptions { ChunkSize = 512, ChunkOverlapTokens = 50 });
        var chunker = new DocumentChunker(options);
        var preprocessor = new ChatTranscriptPreprocessor(options);

        _service = new DocumentIndexingService(
            new[] { _pdfExtractor.Object },
            _embeddingGenerator.Object,
            chunker,
            _repository.Object,
            preprocessor,
            _summarizer.Object);
    }

    [Fact]
    public async Task IndexChunksAsync_CallsExtractorAndEmbedder_AndAddsChunks()
    {
        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid() };
        var content = new byte[] { 0x25, 0x50, 0x44, 0x46 };

        await _service.IndexChunksAsync(content, "application/pdf", doc, CancellationToken.None);

        _pdfExtractor.Verify(e => e.ExtractTextAsync(content, It.IsAny<CancellationToken>()), Times.Once);
        _embeddingGenerator.Verify(
            e => e.GenerateAsync(
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<EmbeddingGenerationOptions?>(),
                It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
        _repository.Verify(
            r => r.AddChunksAsync(It.IsAny<IEnumerable<KnowledgeBaseChunk>>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _summarizer.Verify(
            s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);

        Assert.Equal(DocumentStatus.Indexed, doc.Status);
        Assert.NotNull(doc.IndexedAt);
    }

    [Fact]
    public async Task IndexChunksAsync_UnsupportedContentType_ThrowsNotSupportedException()
    {
        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid() };

        await Assert.ThrowsAsync<NotSupportedException>(
            () => _service.IndexChunksAsync([], "image/png", doc, CancellationToken.None));
    }

    [Fact]
    public async Task IndexChunksAsync_EmbeddingIsGeneratedFromSummary()
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

        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid() };
        await _service.IndexChunksAsync([], "application/pdf", doc, CancellationToken.None);

        Assert.Equal(summary, capturedEmbeddingInput);
    }

    [Fact]
    public async Task IndexChunksAsync_ChunkContentIsFullCleanText_NotSummary()
    {
        const string extractedText = "word1 word2 word3";
        const string summary = "Problém zákazníka: suchá pleť";

        _pdfExtractor
            .Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(extractedText);
        _summarizer
            .Setup(s => s.SummarizeAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(summary);

        IEnumerable<KnowledgeBaseChunk>? savedChunks = null;
        _repository
            .Setup(r => r.AddChunksAsync(
                It.IsAny<IEnumerable<KnowledgeBaseChunk>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<KnowledgeBaseChunk>, CancellationToken>(
                (chunks, _) => savedChunks = chunks.ToList());

        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid() };
        await _service.IndexChunksAsync([], "application/pdf", doc, CancellationToken.None);

        Assert.NotNull(savedChunks);
        Assert.All(savedChunks!, chunk =>
        {
            Assert.Equal(extractedText, chunk.Content);
            Assert.DoesNotContain(summary, chunk.Content);
        });
    }

    [Fact]
    public async Task IndexChunksAsync_StripsBoilerplateBeforeChunking()
    {
        const string boilerplateText =
            "datum: 04.11.2025 zákazník: Zákazník-0001\nAnela: bisabolol je vhodný pro citlivou pleť";

        _pdfExtractor
            .Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(boilerplateText);

        IEnumerable<KnowledgeBaseChunk>? savedChunks = null;
        _repository
            .Setup(r => r.AddChunksAsync(
                It.IsAny<IEnumerable<KnowledgeBaseChunk>>(),
                It.IsAny<CancellationToken>()))
            .Callback<IEnumerable<KnowledgeBaseChunk>, CancellationToken>(
                (chunks, _) => savedChunks = chunks.ToList());

        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid() };
        await _service.IndexChunksAsync([], "application/pdf", doc, CancellationToken.None);

        Assert.NotNull(savedChunks);
        Assert.All(savedChunks!, chunk => Assert.DoesNotContain("datum:", chunk.Content));
    }
}
