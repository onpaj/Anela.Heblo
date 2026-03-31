using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.DeleteDocument;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetChunkDetail;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.GetDocuments;
using Anela.Heblo.Application.Features.KnowledgeBase.UseCases.UploadDocument;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.KnowledgeBase;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Controllers;

public class KnowledgeBaseControllerTests
{
    private readonly Mock<IMediator> _mockMediator;
    private readonly KnowledgeBaseController _controller;

    public KnowledgeBaseControllerTests()
    {
        _mockMediator = new Mock<IMediator>();
        _controller = new KnowledgeBaseController(_mockMediator.Object);

        // Setup HttpContext with services for BaseApiController.Logger
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                RequestServices = serviceProvider
            }
        };
    }

    [Fact]
    public async Task GetDocuments_Returns200_WithDocumentList()
    {
        // Arrange
        var expectedResponse = new GetDocumentsResponse
        {
            Success = true,
            Documents =
            [
                new DocumentSummary
                {
                    Id = Guid.NewGuid(),
                    Filename = "safety-data.pdf",
                    Status = "indexed",
                    ContentType = "application/pdf",
                    CreatedAt = DateTime.UtcNow,
                    IndexedAt = DateTime.UtcNow,
                }
            ]
        };

        _mockMediator
            .Setup(m => m.Send(It.IsAny<GetDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetDocuments(ct: CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<GetDocumentsResponse>(okResult.Value);

        Assert.True(response.Success);
        Assert.Single(response.Documents);
        Assert.Equal("safety-data.pdf", response.Documents[0].Filename);

        _mockMediator.Verify(m => m.Send(It.IsAny<GetDocumentsRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadDocument_WithValidFile_Returns200()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var expectedResponse = new UploadDocumentResponse
        {
            Success = true,
            Document = new DocumentSummary
            {
                Id = docId,
                Filename = "guide.pdf",
                Status = "pending",
                ContentType = "application/pdf",
                CreatedAt = DateTime.UtcNow,
            }
        };

        var mockFile = new Mock<IFormFile>();
        var stream = new MemoryStream(new byte[] { 0x25, 0x50, 0x44, 0x46 }); // PDF magic bytes
        mockFile.Setup(f => f.OpenReadStream()).Returns(stream);
        mockFile.Setup(f => f.FileName).Returns("guide.pdf");
        mockFile.Setup(f => f.ContentType).Returns("application/pdf");
        mockFile.Setup(f => f.Length).Returns(stream.Length);

        _mockMediator
            .Setup(m => m.Send(It.IsAny<UploadDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.UploadDocument(mockFile.Object, "KnowledgeBase", CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<UploadDocumentResponse>(okResult.Value);

        Assert.True(response.Success);
        Assert.Equal("guide.pdf", response.Document!.Filename);

        _mockMediator.Verify(m => m.Send(
            It.Is<UploadDocumentRequest>(r => r.Filename == "guide.pdf" && r.ContentType == "application/pdf"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadDocument_WithNullFile_Returns400()
    {
        // Act
        var result = await _controller.UploadDocument(null!, "KnowledgeBase", CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);

        _mockMediator.Verify(m => m.Send(It.IsAny<UploadDocumentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UploadDocument_WithInvalidDocumentType_Returns400()
    {
        // Arrange
        var mockFile = new Mock<IFormFile>();
        mockFile.Setup(f => f.OpenReadStream()).Returns(new MemoryStream());
        mockFile.Setup(f => f.FileName).Returns("guide.pdf");
        mockFile.Setup(f => f.ContentType).Returns("application/pdf");
        mockFile.Setup(f => f.Length).Returns(10);

        // Act
        var result = await _controller.UploadDocument(mockFile.Object, "InvalidValue", CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
        _mockMediator.Verify(m => m.Send(It.IsAny<UploadDocumentRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task DeleteDocument_WithValidId_Returns200()
    {
        // Arrange
        var docId = Guid.NewGuid();
        var expectedResponse = new DeleteDocumentResponse { Success = true };

        _mockMediator
            .Setup(m => m.Send(It.IsAny<DeleteDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.DeleteDocument(docId, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<DeleteDocumentResponse>(okResult.Value);

        Assert.True(response.Success);

        _mockMediator.Verify(m => m.Send(
            It.Is<DeleteDocumentRequest>(r => r.DocumentId == docId),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetChunkDetail_Returns200_WithChunkDetail()
    {
        // Arrange
        var chunkId = Guid.NewGuid();
        var expectedResponse = new GetChunkDetailResponse
        {
            Success = true,
            ChunkId = chunkId,
            DocumentId = Guid.NewGuid(),
            Filename = "conversation-2024.txt",
            DocumentType = DocumentType.Conversation,
            IndexedAt = DateTime.UtcNow,
            ChunkIndex = 0,
            Summary = "summary text",
            Content = "full conversation text",
        };

        _mockMediator
            .Setup(m => m.Send(It.Is<GetChunkDetailRequest>(r => r.ChunkId == chunkId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.GetChunkDetail(chunkId, default);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<GetChunkDetailResponse>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal(chunkId, response.ChunkId);
        Assert.Equal("conversation-2024.txt", response.Filename);
    }

    [Fact]
    public async Task GetChunkDetail_Returns404_WhenChunkNotFound()
    {
        // Arrange
        var chunkId = Guid.NewGuid();
        var notFoundResponse = new GetChunkDetailResponse(ErrorCodes.KnowledgeBaseChunkNotFound);

        _mockMediator
            .Setup(m => m.Send(It.Is<GetChunkDetailRequest>(r => r.ChunkId == chunkId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(notFoundResponse);

        // Act
        var result = await _controller.GetChunkDetail(chunkId, default);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task DeleteDocument_WithUnknownId_Returns404()
    {
        // Arrange
        var unknownId = Guid.NewGuid();
        var failResponse = new DeleteDocumentResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.ResourceNotFound
        };

        _mockMediator
            .Setup(m => m.Send(It.IsAny<DeleteDocumentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failResponse);

        // Act
        var result = await _controller.DeleteDocument(unknownId, CancellationToken.None);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }
}
