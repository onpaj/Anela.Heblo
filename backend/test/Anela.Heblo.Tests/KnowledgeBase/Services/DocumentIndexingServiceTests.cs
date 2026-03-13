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
    private readonly DocumentIndexingService _service;

    public DocumentIndexingServiceTests()
    {
        _pdfExtractor = new Mock<IDocumentTextExtractor>();
        _pdfExtractor.Setup(e => e.CanHandle("application/pdf")).Returns(true);
        _pdfExtractor.Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("word1 word2 word3");

        _embeddingGenerator = new Mock<IEmbeddingGenerator<string, Embedding<float>>>();
        var floats = new float[] { 0.1f, 0.2f, 0.3f };
        var embeddingVector = new ReadOnlyMemory<float>(floats);
        var generatedEmbeddings = new GeneratedEmbeddings<Embedding<float>>([new Embedding<float>(embeddingVector)]);
        _embeddingGenerator
            .Setup(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(generatedEmbeddings);

        _repository = new Mock<IKnowledgeBaseRepository>();

        var options = Options.Create(new KnowledgeBaseOptions { ChunkSize = 512, ChunkOverlapTokens = 50 });
        var chunker = new DocumentChunker(options);

        _service = new DocumentIndexingService(
            new[] { _pdfExtractor.Object },
            _embeddingGenerator.Object,
            chunker,
            _repository.Object);
    }

    [Fact]
    public async Task IndexChunksAsync_CallsExtractorAndEmbedder_AndAddsChunks()
    {
        var doc = new KnowledgeBaseDocument { Id = Guid.NewGuid() };
        var content = new byte[] { 0x25, 0x50, 0x44, 0x46 };

        await _service.IndexChunksAsync(content, "application/pdf", doc, CancellationToken.None);

        _pdfExtractor.Verify(e => e.ExtractTextAsync(content, It.IsAny<CancellationToken>()), Times.Once);
        _embeddingGenerator.Verify(e => e.GenerateAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<EmbeddingGenerationOptions?>(), It.IsAny<CancellationToken>()), Times.AtLeastOnce);
        _repository.Verify(r => r.AddChunksAsync(It.IsAny<IEnumerable<KnowledgeBaseChunk>>(), It.IsAny<CancellationToken>()), Times.Once);

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
}
