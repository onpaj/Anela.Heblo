using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Anela.Heblo.Application.Features.Leaflet.UseCases.IndexLeaflet;
using Anela.Heblo.Application.Features.Leaflet.UseCases.UploadLeaflet;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using FluentAssertions;
using MediatR;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet.UseCases;

public class UploadLeafletHandlerTests
{
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly Mock<IDocumentTextExtractor> _extractorMock = new();

    public UploadLeafletHandlerTests()
    {
        _extractorMock.Setup(e => e.CanHandle("application/pdf")).Returns(true);
    }

    private UploadLeafletHandler CreateHandler(IEnumerable<IDocumentTextExtractor>? extractors = null) =>
        new(extractors ?? new[] { _extractorMock.Object }, _mediatorMock.Object);

    private static Stream CreatePdfStream(byte[] content)
    {
        var ms = new MemoryStream(content);
        ms.Position = 0;
        return ms;
    }

    private void SetupSuccessfulIndexResponse(Guid documentId)
    {
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<IndexLeafletRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new IndexLeafletResponse
            {
                DocumentId = documentId,
                WasDuplicate = false,
                Success = true,
                Status = LeafletDocumentStatus.Indexed,
                Filename = "test.pdf",
                ContentType = "application/pdf",
                IngestedAt = DateTime.UtcNow,
                IndexedAt = DateTime.UtcNow,
            });
    }

    [Fact]
    public async Task Handle_happy_path_returns_document_summary()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        SetupSuccessfulIndexResponse(documentId);

        var handler = CreateHandler();
        var request = new UploadLeafletRequest
        {
            FileStream = CreatePdfStream(new byte[] { 1, 2, 3 }),
            Filename = "test.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 3,
        };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.Document.Should().NotBeNull();
        response.Document!.Id.Should().Be(documentId);
    }

    [Fact]
    public async Task Handle_unsupported_content_type_returns_error_without_calling_mediator()
    {
        // Arrange
        var handler = CreateHandler();
        var request = new UploadLeafletRequest
        {
            FileStream = CreatePdfStream(new byte[] { 1, 2, 3 }),
            Filename = "archive.zip",
            ContentType = "application/zip",
            FileSizeBytes = 3,
        };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.UnsupportedFileType);

        _mediatorMock.Verify(
            m => m.Send(It.IsAny<IndexLeafletRequest>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Handle_octet_stream_with_pdf_extension_resolves_to_pdf_content_type()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        SetupSuccessfulIndexResponse(documentId);

        string? capturedContentType = null;
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<IndexLeafletRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<IndexLeafletResponse>, CancellationToken>(
                (req, _) => capturedContentType = ((IndexLeafletRequest)req).ContentType)
            .ReturnsAsync(new IndexLeafletResponse
            {
                DocumentId = documentId,
                Success = true,
                Status = LeafletDocumentStatus.Indexed,
                Filename = "file.pdf",
                ContentType = "application/pdf",
                IngestedAt = DateTime.UtcNow,
            });

        var handler = CreateHandler();
        var request = new UploadLeafletRequest
        {
            FileStream = CreatePdfStream(new byte[] { 1, 2, 3 }),
            Filename = "file.pdf",
            ContentType = "application/octet-stream",
            FileSizeBytes = 3,
        };

        // Act
        var response = await handler.Handle(request, CancellationToken.None);

        // Assert
        capturedContentType.Should().Be("application/pdf");
        response.Success.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_propagates_exception_when_indexing_throws()
    {
        // Arrange — IndexLeafletHandler throws on failure; UploadLeafletHandler must not swallow it
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<IndexLeafletRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Indexing failed"));

        var handler = CreateHandler();
        var request = new UploadLeafletRequest
        {
            FileStream = CreatePdfStream(new byte[] { 1, 2, 3 }),
            Filename = "test.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 3,
        };

        // Act & Assert
        await handler.Invoking(h => h.Handle(request, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Handle_source_path_includes_upload_prefix_and_filename()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        IndexLeafletRequest? capturedRequest = null;

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<IndexLeafletRequest>(), It.IsAny<CancellationToken>()))
            .Callback<IRequest<IndexLeafletResponse>, CancellationToken>(
                (req, _) => capturedRequest = (IndexLeafletRequest)req)
            .ReturnsAsync(new IndexLeafletResponse
            {
                DocumentId = documentId,
                Success = true,
                Status = LeafletDocumentStatus.Indexed,
                Filename = "report.pdf",
                ContentType = "application/pdf",
                IngestedAt = DateTime.UtcNow,
            });

        var handler = CreateHandler();
        var request = new UploadLeafletRequest
        {
            FileStream = CreatePdfStream(new byte[] { 10, 20, 30 }),
            Filename = "report.pdf",
            ContentType = "application/pdf",
            FileSizeBytes = 3,
        };

        // Act
        await handler.Handle(request, CancellationToken.None);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.SourcePath.Should().StartWith("upload/");
        capturedRequest.SourcePath.Should().EndWith("/report.pdf");
    }
}
