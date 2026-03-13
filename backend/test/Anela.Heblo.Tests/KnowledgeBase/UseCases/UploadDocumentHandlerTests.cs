using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class UploadDocumentHandlerTests
{
    private readonly Mock<IDocumentTextExtractor> _extractor = new();
    private readonly Mock<IKnowledgeBaseRepository> _repository = new();
    private readonly Mock<IDocumentIndexingService> _indexingService = new();

    public UploadDocumentHandlerTests()
    {
        // Simulate DocumentIndexingService side-effect: sets status to Indexed
        _indexingService
            .Setup(s => s.IndexChunksAsync(It.IsAny<byte[]>(), It.IsAny<string>(), It.IsAny<KnowledgeBaseDocument>(), It.IsAny<CancellationToken>()))
            .Callback<byte[], string, KnowledgeBaseDocument, CancellationToken>((_, _, doc, _) =>
            {
                doc.Status = DocumentStatus.Indexed;
                doc.IndexedAt = DateTime.UtcNow;
            });
    }

    private UploadDocumentHandler CreateHandler() =>
        new(_repository.Object, new[] { _extractor.Object }, _indexingService.Object);

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

        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("pdf content"u8.ToArray()),
            Filename = "guide.pdf",
            ContentType = "application/pdf",
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("indexed", result.Document!.Status);
        Assert.Equal("guide.pdf", result.Document.Filename);
        _repository.Verify(
            r => r.AddDocumentAsync(It.IsAny<KnowledgeBaseDocument>(), It.IsAny<CancellationToken>()),
            Times.Once);
        _indexingService.Verify(
            s => s.IndexChunksAsync(It.IsAny<byte[]>(), "application/pdf", It.IsAny<KnowledgeBaseDocument>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_OctetStreamWithTxtExtension_ResolvesToTextPlainAndIndexes()
    {
        _repository.Setup(r => r.GetDocumentByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((KnowledgeBaseDocument?)null);

        _extractor.Setup(e => e.CanHandle("text/plain")).Returns(true);

        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("plain text"u8.ToArray()),
            Filename = "readme.txt",
            ContentType = "application/octet-stream", // browser drag-and-drop may send this
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("indexed", result.Document!.Status);
        Assert.Equal("text/plain", result.Document.ContentType);
    }

    [Fact]
    public async Task Handle_OctetStreamWithDocxExtension_ResolvesToDocxContentType()
    {
        _repository.Setup(r => r.GetDocumentByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((KnowledgeBaseDocument?)null);

        const string docxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        _extractor.Setup(e => e.CanHandle(docxContentType)).Returns(true);

        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("docx bytes"u8.ToArray()),
            Filename = "document.docx",
            ContentType = "application/octet-stream",
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(docxContentType, result.Document!.ContentType);
    }

    [Fact]
    public async Task Handle_UnsupportedFileType_ReturnsUnsupportedFileTypeErrorWithoutThrowing()
    {
        _repository.Setup(r => r.GetDocumentByHashAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
                   .ReturnsAsync((KnowledgeBaseDocument?)null);

        _extractor.Setup(e => e.CanHandle(It.IsAny<string>())).Returns(false);

        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("binary"u8.ToArray()),
            Filename = "archive.zip",
            ContentType = "application/zip",
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.UnsupportedFileType, result.ErrorCode);
        _repository.Verify(
            r => r.AddDocumentAsync(It.IsAny<KnowledgeBaseDocument>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}
