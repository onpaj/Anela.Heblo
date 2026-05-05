using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Leaflet.UseCases.DeleteLeafletDocument;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletChunkDetail;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocumentContentTypes;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GetLeafletDocuments;
using Anela.Heblo.Application.Features.Leaflet.UseCases.UploadLeaflet;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Leaflet;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet;

public class LeafletControllerTests
{
    private readonly Mock<IMediator> _mediatorMock = new();

    private LeafletController CreateController()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider,
        };

        var controller = new LeafletController(_mediatorMock.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
        };
        return controller;
    }

    [Fact]
    public async Task Generate_returns_200_with_response_on_success()
    {
        // Arrange
        var request = new GenerateLeafletRequest
        {
            Topic = "Vitamin C serum",
            Audience = AudienceType.EndConsumer,
            Length = LeafletLength.Short,
        };

        var expectedResponse = new GenerateLeafletResponse
        {
            Success = true,
            Content = "Vitamin C serum is great for your skin.",
        };

        _mediatorMock
            .Setup(m => m.Send(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var controller = CreateController();

        // Act
        var result = await controller.Generate(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<GenerateLeafletResponse>(okResult.Value);
        Assert.Equal(expectedResponse.Content, response.Content);
    }

    [Fact]
    public async Task Generate_returns_422_on_EmptyRetrievalException()
    {
        // Arrange
        var request = new GenerateLeafletRequest
        {
            Topic = "Unknown topic",
            Audience = AudienceType.EndConsumer,
            Length = LeafletLength.Short,
        };

        var exceptionMessage = "No relevant documents found for the given topic.";

        _mediatorMock
            .Setup(m => m.Send(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EmptyRetrievalException(exceptionMessage));

        var controller = CreateController();

        // Act
        var result = await controller.Generate(request, CancellationToken.None);

        // Assert
        var unprocessableResult = Assert.IsType<UnprocessableEntityObjectResult>(result);
        Assert.Equal(422, unprocessableResult.StatusCode);

        var problemDetails = Assert.IsType<ProblemDetails>(unprocessableResult.Value);
        Assert.Equal(exceptionMessage, problemDetails.Detail);
    }

    [Fact]
    public async Task Generate_returns_502_on_unexpected_exception()
    {
        // Arrange
        var request = new GenerateLeafletRequest
        {
            Topic = "Retinol cream",
            Audience = AudienceType.EndConsumer,
            Length = LeafletLength.Short,
        };

        var internalMessage = "Internal system failure with stack trace details";

        _mediatorMock
            .Setup(m => m.Send(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(internalMessage));

        var controller = CreateController();

        // Act
        var result = await controller.Generate(request, CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, objectResult.StatusCode);

        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.NotEqual(internalMessage, problemDetails.Detail);
        Assert.DoesNotContain(internalMessage, problemDetails.Detail ?? string.Empty);
    }

    [Fact]
    public async Task Generate_propagates_OperationCanceledException()
    {
        // Arrange
        var request = new GenerateLeafletRequest
        {
            Topic = "Hyaluronic acid",
            Audience = AudienceType.EndConsumer,
            Length = LeafletLength.Short,
        };

        _mediatorMock
            .Setup(m => m.Send(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var controller = CreateController();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => controller.Generate(request, CancellationToken.None));
    }

    [Fact]
    public async Task GetDocuments_returns_200_with_paged_response()
    {
        // Arrange
        var expectedResponse = new GetLeafletDocumentsResponse
        {
            Documents = [new LeafletDocumentSummary { Id = Guid.NewGuid(), Filename = "test.pdf" }],
            TotalCount = 1,
            PageNumber = 1,
            PageSize = 20,
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetLeafletDocumentsRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var controller = CreateController();

        // Act
        var result = await controller.GetDocuments(ct: CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<GetLeafletDocumentsResponse>(okResult.Value);
        Assert.Equal(1, response.TotalCount);
        Assert.Single(response.Documents);
    }

    [Fact]
    public async Task GetDocumentContentTypes_returns_200_with_content_types()
    {
        // Arrange
        var expectedResponse = new GetLeafletDocumentContentTypesResponse
        {
            ContentTypes = ["application/pdf", "text/plain"],
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetLeafletDocumentContentTypesRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var controller = CreateController();

        // Act
        var result = await controller.GetDocumentContentTypes(CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<GetLeafletDocumentContentTypesResponse>(okResult.Value);
        Assert.Equal(2, response.ContentTypes.Count);
    }

    [Fact]
    public async Task GetChunkDetail_returns_200_when_chunk_found()
    {
        // Arrange
        var chunkId = Guid.NewGuid();
        var expectedResponse = new GetLeafletChunkDetailResponse
        {
            ChunkId = chunkId,
            DocumentId = Guid.NewGuid(),
            Filename = "catalog.pdf",
            Content = "Chunk content here.",
            ChunkIndex = 0,
        };

        _mediatorMock
            .Setup(m => m.Send(It.Is<GetLeafletChunkDetailRequest>(r => r.ChunkId == chunkId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var controller = CreateController();

        // Act
        var result = await controller.GetChunkDetail(chunkId, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<GetLeafletChunkDetailResponse>(okResult.Value);
        Assert.Equal(chunkId, response.ChunkId);
    }

    [Fact]
    public async Task GetChunkDetail_returns_404_when_chunk_not_found()
    {
        // Arrange
        var chunkId = Guid.NewGuid();
        var notFoundResponse = new GetLeafletChunkDetailResponse(ErrorCodes.LeafletChunkNotFound);

        _mediatorMock
            .Setup(m => m.Send(It.Is<GetLeafletChunkDetailRequest>(r => r.ChunkId == chunkId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(notFoundResponse);

        var controller = CreateController();

        // Act
        var result = await controller.GetChunkDetail(chunkId, CancellationToken.None);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.Equal(404, notFoundResult.StatusCode);
    }

    [Fact]
    public async Task DeleteDocument_returns_200_on_success()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        _mediatorMock
            .Setup(m => m.Send(It.Is<DeleteLeafletDocumentRequest>(r => r.DocumentId == documentId), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new DeleteLeafletDocumentResponse());

        var controller = CreateController();

        // Act
        var result = await controller.DeleteDocument(documentId, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(200, okResult.StatusCode);
    }

    [Fact]
    public async Task UploadDocument_returns_400_when_file_is_null()
    {
        // Arrange
        var controller = CreateController();

        // Act
        var result = await controller.UploadDocument(null!, CancellationToken.None);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var response = Assert.IsType<UploadLeafletResponse>(badRequestResult.Value);
        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.RequiredFieldMissing, response.ErrorCode);
    }

    [Fact]
    public async Task UploadDocument_returns_200_when_file_is_provided_and_upload_succeeds()
    {
        // Arrange
        var documentId = Guid.NewGuid();
        var uploadResponse = new UploadLeafletResponse
        {
            Document = new LeafletDocumentSummary
            {
                Id = documentId,
                Filename = "brochure.pdf",
                Status = "indexed",
                ContentType = "application/pdf",
            },
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UploadLeafletRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(uploadResponse);

        var fileMock = new Mock<IFormFile>();
        fileMock.Setup(f => f.FileName).Returns("brochure.pdf");
        fileMock.Setup(f => f.ContentType).Returns("application/pdf");
        fileMock.Setup(f => f.Length).Returns(1024);
        fileMock.Setup(f => f.OpenReadStream()).Returns(new MemoryStream(new byte[] { 1, 2, 3 }));

        var controller = CreateController();

        // Act
        var result = await controller.UploadDocument(fileMock.Object, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<UploadLeafletResponse>(okResult.Value);
        Assert.True(response.Success);
        Assert.Equal(documentId, response.Document!.Id);
    }
}
