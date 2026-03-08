using Anela.Heblo.Application.Features.KnowledgeBase;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class UploadDocumentHandlerTests
{
    private readonly Mock<IDocumentTextExtractor> _extractor = new();
    private readonly Mock<IEmbeddingService> _embedding = new();
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();
    private readonly DocumentChunker _chunker;

    public UploadDocumentHandlerTests()
    {
        _chunker = new DocumentChunker(Options.Create(new KnowledgeBaseOptions
        {
            ChunkSize = 5,
            ChunkOverlapTokens = 1
        }));
    }

    private UploadDocumentHandler CreateHandler() =>
        new(_repository.Object, new[] { _extractor.Object }, _chunker, _embedding.Object);

    [Fact]
    public async Task Handle_WhenDocumentAlreadyExistsByHash_ReturnExistingWithoutReindexing()
    {
        var existing = new KnowledgeBaseDocument
        {
            Id = Guid.NewGuid(),
            Filename = "test.pdf",
            Status = DocumentStatus.Indexed,
            ContentHash = "any",
            SourcePath = "upload/test.pdf",
            ContentType = "application/pdf",
            CreatedAt = DateTime.UtcNow,
        };

        _repository.Setup(r => r.GetDocumentByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync(existing);

        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("content"u8.ToArray()),
            Filename = "test.pdf",
            ContentType = "application/pdf",
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("test.pdf", result.Document!.Filename);
        _repository.Verify(
            r => r.AddDocumentAsync(It.IsAny<KnowledgeBaseDocument>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_NewDocument_IndexesAndReturnsIndexedStatus()
    {
        _repository.Setup(r => r.GetDocumentByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((KnowledgeBaseDocument?)null);

        _extractor.Setup(e => e.CanHandle("application/pdf")).Returns(true);
        _extractor.Setup(e => e.ExtractTextAsync(It.IsAny<byte[]>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync("word1 word2 word3 word4 word5 word6 word7 word8 word9 word10");

        _embedding.Setup(e => e.GenerateEmbeddingAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync(new float[] { 0.1f, 0.2f, 0.3f });

        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("pdf content"u8.ToArray()),
            Filename = "guide.pdf",
            ContentType = "application/pdf",
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(DocumentStatus.Indexed, result.Document!.Status);
        Assert.Equal("guide.pdf", result.Document.Filename);
        _repository.Verify(
            r => r.AddDocumentAsync(It.IsAny<KnowledgeBaseDocument>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _repository.Verify(
            r => r.AddChunksAsync(
                It.Is<IEnumerable<KnowledgeBaseChunk>>(chunks => chunks.Any()),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
