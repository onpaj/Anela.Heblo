using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.FileStorage;

public class FileStorageControllerTests
{
    private readonly Mock<IMediator> _mockMediator;
    private readonly FileStorageController _controller;

    public FileStorageControllerTests()
    {
        _mockMediator = new Mock<IMediator>();
        _controller = new FileStorageController(_mockMediator.Object);

        // Setup HttpContext with services for BaseApiController Logger
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
    public async Task DownloadFromUrl_ValidRequest_ShouldReturnOkResult()
    {
        // Arrange
        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/document.pdf",
            ContainerName = "documents",
            BlobName = "test-document.pdf"
        };

        var expectedResponse = new DownloadFromUrlResponse
        {
            Success = true,
            BlobUrl = "https://testaccount.blob.core.windows.net/documents/test-document.pdf",
            BlobName = "test-document.pdf",
            ContainerName = "documents",
            FileSizeBytes = 1024
        };

        _mockMediator.Setup(x => x.Send(request, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.DownloadFromUrl(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<DownloadFromUrlResponse>(okResult.Value);

        Assert.True(response.Success);
        Assert.Equal(expectedResponse.BlobUrl, response.BlobUrl);
        Assert.Equal(expectedResponse.BlobName, response.BlobName);
        Assert.Equal(expectedResponse.ContainerName, response.ContainerName);
    }

    [Fact]
    public async Task DownloadFromUrl_InvalidUrlError_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new DownloadFromUrlRequest
        {
            FileUrl = "invalid-url",
            ContainerName = "documents"
        };

        var expectedResponse = new DownloadFromUrlResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.InvalidUrlFormat,
            Params = new Dictionary<string, string> { { "fileUrl", "invalid-url" } }
        };

        _mockMediator.Setup(x => x.Send(request, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.DownloadFromUrl(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var response = Assert.IsType<DownloadFromUrlResponse>(badRequestResult.Value);

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.InvalidUrlFormat, response.ErrorCode);
    }

    [Fact]
    public async Task DownloadFromUrl_InvalidContainerNameError_ShouldReturnBadRequest()
    {
        // Arrange
        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/document.pdf",
            ContainerName = "InvalidContainerName" // Contains uppercase
        };

        var expectedResponse = new DownloadFromUrlResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.InvalidContainerName,
            Params = new Dictionary<string, string> { { "containerName", "InvalidContainerName" } }
        };

        _mockMediator.Setup(x => x.Send(request, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.DownloadFromUrl(request);

        // Assert
        var badRequestResult = Assert.IsType<BadRequestObjectResult>(result.Result);
        var response = Assert.IsType<DownloadFromUrlResponse>(badRequestResult.Value);

        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.InvalidContainerName, response.ErrorCode);
    }

    [Fact]
    public async Task DownloadFromUrl_FileDownloadFailed_ShouldReturnServiceUnavailable()
    {
        // Arrange
        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/nonexistent.pdf",
            ContainerName = "documents"
        };

        var expectedResponse = new DownloadFromUrlResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.FileDownloadFailed,
            Params = new Dictionary<string, string>
            {
                { "fileUrl", "https://example.com/nonexistent.pdf" },
                { "error", "File not found" }
            }
        };

        _mockMediator.Setup(x => x.Send(request, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.DownloadFromUrl(request);

        // Assert
        var serviceUnavailableResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(503, serviceUnavailableResult.StatusCode); // ServiceUnavailable

        var response = Assert.IsType<DownloadFromUrlResponse>(serviceUnavailableResult.Value);
        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.FileDownloadFailed, response.ErrorCode);
    }

    [Fact]
    public async Task DownloadFromUrl_InternalServerError_ShouldReturnInternalServerError()
    {
        // Arrange
        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/document.pdf",
            ContainerName = "documents"
        };

        var expectedResponse = new DownloadFromUrlResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.InternalServerError,
            Params = new Dictionary<string, string>
            {
                { "fileUrl", "https://example.com/document.pdf" },
                { "error", "Unexpected error occurred" }
            }
        };

        _mockMediator.Setup(x => x.Send(request, It.IsAny<CancellationToken>()))
                    .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.DownloadFromUrl(request);

        // Assert
        var internalServerErrorResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, internalServerErrorResult.StatusCode); // InternalServerError

        var response = Assert.IsType<DownloadFromUrlResponse>(internalServerErrorResult.Value);
        Assert.False(response.Success);
        Assert.Equal(ErrorCodes.InternalServerError, response.ErrorCode);
    }

    [Fact]
    public async Task DownloadFromUrl_CancellationToken_ShouldPassThroughToMediator()
    {
        // Arrange
        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/document.pdf",
            ContainerName = "documents"
        };

        var expectedResponse = new DownloadFromUrlResponse
        {
            Success = true,
            BlobUrl = "https://testaccount.blob.core.windows.net/documents/document.pdf",
            BlobName = "document.pdf",
            ContainerName = "documents",
            FileSizeBytes = 1024
        };

        var cancellationTokenSource = new CancellationTokenSource();

        _mockMediator.Setup(x => x.Send(request, cancellationTokenSource.Token))
                    .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.DownloadFromUrl(request, cancellationTokenSource.Token);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<DownloadFromUrlResponse>(okResult.Value);

        Assert.True(response.Success);

        _mockMediator.Verify(x => x.Send(request, cancellationTokenSource.Token), Times.Once);
    }

    [Theory]
    [InlineData("https://example.com/image.jpg", "images", "photo.jpg")]
    [InlineData("https://example.com/document.pdf", "documents", null)]
    [InlineData("https://api.example.com/files/data.json", "data", "api-data.json")]
    public async Task DownloadFromUrl_DifferentScenarios_ShouldHandleCorrectly(
        string fileUrl, string containerName, string? blobName)
    {
        // Arrange
        var request = new DownloadFromUrlRequest
        {
            FileUrl = fileUrl,
            ContainerName = containerName,
            BlobName = blobName
        };

        var expectedBlobName = blobName ?? Path.GetFileName(new Uri(fileUrl).LocalPath);
        var expectedResponse = new DownloadFromUrlResponse
        {
            Success = true,
            BlobUrl = $"https://testaccount.blob.core.windows.net/{containerName}/{expectedBlobName}",
            BlobName = expectedBlobName,
            ContainerName = containerName,
            FileSizeBytes = 2048
        };

        _mockMediator.Setup(x => x.Send(It.IsAny<DownloadFromUrlRequest>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.DownloadFromUrl(request);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<DownloadFromUrlResponse>(okResult.Value);

        Assert.True(response.Success);
        Assert.Equal(expectedResponse.BlobUrl, response.BlobUrl);
        Assert.Equal(expectedResponse.BlobName, response.BlobName);
        Assert.Equal(expectedResponse.ContainerName, response.ContainerName);

        _mockMediator.Verify(x => x.Send(
            It.Is<DownloadFromUrlRequest>(r =>
                r.FileUrl == fileUrl &&
                r.ContainerName == containerName &&
                r.BlobName == blobName),
            It.IsAny<CancellationToken>()), Times.Once);
    }
}