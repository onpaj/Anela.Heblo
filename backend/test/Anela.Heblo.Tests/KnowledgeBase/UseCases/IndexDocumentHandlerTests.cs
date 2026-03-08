using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class IndexDocumentHandlerTests
{
    private readonly Mock<IDocumentTextExtractor> _extractor = new();
    private readonly Mock<IEmbeddingService> _embedding = new();
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();
    private readonly DocumentChunker _chunker;

    public IndexDocumentHandlerTests()
    {
        _chunker = new DocumentChunker(Options.Create(new KnowledgeBaseOptions
        {
            ChunkSize = 5,
            ChunkOverlapTokens = 1
        }));
    }

    [Fact]
    public async Task Handle_StoresDocumentAndChunksWithEmbeddings()
    {
        var pdfBytes = new byte[] { 1, 2, 3 };
        var extractedText = "word1 word2 word3 word4 word5 word6 word7 word8 word9 word10";

        _extractor.Setup(e => e.CanHandle("application/pdf")).Returns(true);
        _extractor.Setup(e => e.ExtractTextAsync(pdfBytes, default)).ReturnsAsync(extractedText);
        _embedding.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), default))
            .ReturnsAsync(new float[] { 0.1f, 0.2f });

        KnowledgeBaseDocument? savedDoc = null;
        _repository.Setup(r => r.AddDocumentAsync(It.IsAny<KnowledgeBaseDocument>(), default))
            .Callback<KnowledgeBaseDocument, CancellationToken>((doc, _) => savedDoc = doc);

        var handler = new IndexDocumentHandler(new[] { _extractor.Object }, _embedding.Object, _chunker, _repository.Object);

        await handler.Handle(new IndexDocumentRequest
        {
            Filename = "test.pdf",
            SourcePath = "/inbox/test.pdf",
            ContentType = "application/pdf",
            Content = pdfBytes,
            ContentHash = "abc123def456"
        }, default);

        Assert.NotNull(savedDoc);
        Assert.Equal("test.pdf", savedDoc!.Filename);
        Assert.Equal("abc123def456", savedDoc.ContentHash);
        _repository.Verify(r => r.AddChunksAsync(
            It.Is<IEnumerable<KnowledgeBaseChunk>>(chunks => chunks.Any()),
            default), Times.Once);
        _repository.Verify(r => r.SaveChangesAsync(default), Times.Once);
    }

    [Fact]
    public async Task Handle_ThrowsForUnsupportedContentType()
    {
        _extractor.Setup(e => e.CanHandle("image/png")).Returns(false);

        var handler = new IndexDocumentHandler(new[] { _extractor.Object }, _embedding.Object, _chunker, _repository.Object);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            handler.Handle(new IndexDocumentRequest
            {
                Filename = "photo.png",
                SourcePath = "/inbox/photo.png",
                ContentType = "image/png",
                Content = [1, 2, 3],
                ContentHash = "abc"
            }, default));
    }

    [Fact]
    public async Task Handle_ThrowsNotSupportedException_WhenNoExtractorMatchesContentType()
    {
        var request = new IndexDocumentRequest
        {
            Filename = "file.xyz",
            SourcePath = "/file.xyz",
            ContentType = "application/unknown",
            Content = [1, 2, 3],
            ContentHash = "abc123",
        };

        _extractor.Setup(e => e.CanHandle("application/unknown")).Returns(false);

        var handler = new IndexDocumentHandler(new[] { _extractor.Object }, _embedding.Object, _chunker, _repository.Object);

        await Assert.ThrowsAsync<NotSupportedException>(() =>
            handler.Handle(request, CancellationToken.None));
    }
}
