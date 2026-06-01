using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.IndexDocument;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.UseCases;

public class UploadDocumentHandlerTests
{
    private readonly Mock<IDocumentTextExtractor> _extractor = new();
    private readonly Mock<IMediator> _mediator = new();

    private UploadDocumentHandler CreateHandler() =>
        new(new[] { _extractor.Object }, _mediator.Object);

    private void SetupMediatorSuccess(
        string filename = "doc.pdf",
        string contentType = "application/pdf",
        DocumentStatus status = DocumentStatus.Indexed)
    {
        _mediator
            .Setup(m => m.Send(It.IsAny<IndexDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndexDocumentResponse
            {
                DocumentId = Guid.NewGuid(),
                Status = status,
                WasDuplicate = false,
                Filename = filename,
                ContentType = contentType,
                CreatedAt = DateTime.UtcNow,
                IndexedAt = DateTime.UtcNow,
            });
    }

    [Fact]
    public async Task Handle_NewDocument_IndexesAndReturnsIndexedStatus()
    {
        _extractor.Setup(e => e.CanHandle("application/pdf")).Returns(true);
        SetupMediatorSuccess("guide.pdf", "application/pdf");

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
        _mediator.Verify(
            m => m.Send(It.IsAny<IndexDocumentRequest>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_OctetStreamWithTxtExtension_ResolvesToTextPlainAndIndexes()
    {
        _extractor.Setup(e => e.CanHandle("text/plain")).Returns(true);
        SetupMediatorSuccess("readme.txt", "text/plain");

        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("plain text"u8.ToArray()),
            Filename = "readme.txt",
            ContentType = "application/octet-stream",
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal("indexed", result.Document!.Status);
        Assert.Equal("text/plain", result.Document.ContentType);

        _mediator.Verify(
            m => m.Send(
                It.Is<IndexDocumentRequest>(r => r.ContentType == "text/plain"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_OctetStreamWithDocxExtension_ResolvesToDocxContentType()
    {
        const string docxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        _extractor.Setup(e => e.CanHandle(docxContentType)).Returns(true);
        SetupMediatorSuccess("document.docx", docxContentType);

        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("docx bytes"u8.ToArray()),
            Filename = "document.docx",
            ContentType = "application/octet-stream",
        };

        var result = await CreateHandler().Handle(request, CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(docxContentType, result.Document!.ContentType);

        _mediator.Verify(
            m => m.Send(
                It.Is<IndexDocumentRequest>(r => r.ContentType == docxContentType),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_UnsupportedFileType_ReturnsUnsupportedFileTypeErrorWithoutThrowing()
    {
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
        _mediator.Verify(
            m => m.Send(It.IsAny<IndexDocumentRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_IndexDocumentRequest_ContainsUploadSourcePath()
    {
        _extractor.Setup(e => e.CanHandle("application/pdf")).Returns(true);
        SetupMediatorSuccess("doc.pdf", "application/pdf");

        var request = new UploadDocumentRequest
        {
            FileStream = new MemoryStream("content"u8.ToArray()),
            Filename = "doc.pdf",
            ContentType = "application/pdf",
        };

        await CreateHandler().Handle(request, CancellationToken.None);

        _mediator.Verify(
            m => m.Send(
                It.Is<IndexDocumentRequest>(r => r.SourcePath.StartsWith("upload/") && r.SourcePath.EndsWith("/doc.pdf")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
