using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.FileStorage;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using Xunit;

namespace Anela.Heblo.Tests.Features.FileStorage;

public class DownloadFromUrlHandlerTests
{
    private readonly Mock<HttpClient> _mockHttpClient;
    private readonly Mock<ILogger<DownloadFromUrlHandler>> _mockLogger;
    private readonly MockBlobStorageService _mockBlobStorageService;
    private readonly DownloadFromUrlHandler _handler;

    public DownloadFromUrlHandlerTests()
    {
        _mockHttpClient = new Mock<HttpClient>();
        _mockLogger = new Mock<ILogger<DownloadFromUrlHandler>>();
        _mockBlobStorageService = new MockBlobStorageService();

        _handler = new DownloadFromUrlHandler(
            _mockBlobStorageService,
            _mockHttpClient.Object,
            _mockLogger.Object
        );
    }

    [Fact]
    public async Task Handle_ValidRequest_ShouldReturnSuccessResponse()
    {
        // Arrange
        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/document.pdf",
            ContainerName = "documents",
            BlobName = "test-document.pdf"
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("https://mockstorageaccount.blob.core.windows.net/documents/test-document.pdf", result.BlobUrl);
        Assert.Equal("test-document.pdf", result.BlobName);
        Assert.Equal("documents", result.ContainerName);

        // Verify blob was created in mock service
        var blob = _mockBlobStorageService.GetBlob("documents", "test-document.pdf");
        Assert.NotNull(blob);
        Assert.Equal("https://example.com/document.pdf", blob.SourceUrl);
    }

    [Fact]
    public async Task Handle_ValidRequestWithoutBlobName_ShouldGenerateBlobName()
    {
        // Arrange
        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/images/photo.jpg",
            ContainerName = "images"
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal("photo.jpg", result.BlobName);
        Assert.Contains("photo.jpg", result.BlobUrl);
    }

    [Theory]
    [InlineData("not-a-url")]
    [InlineData("")]
    [InlineData("ftp://example.com/file.txt")]
    public async Task Handle_InvalidUrl_ShouldReturnErrorResponse(string invalidUrl)
    {
        // Arrange
        var request = new DownloadFromUrlRequest
        {
            FileUrl = invalidUrl,
            ContainerName = "documents"
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InvalidUrlFormat, result.ErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")] // Too short
    [InlineData("very-long-container-name-that-exceeds-sixty-three-characters-limit")] // Too long
    [InlineData("InvalidCase")] // Has uppercase
    [InlineData("invalid--double-hyphen")] // Double hyphen
    [InlineData("-starts-with-hyphen")] // Starts with hyphen
    [InlineData("ends-with-hyphen-")] // Ends with hyphen
    [InlineData("invalid_underscore")] // Contains underscore
    public async Task Handle_InvalidContainerName_ShouldReturnErrorResponse(string invalidContainerName)
    {
        // Arrange
        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/file.txt",
            ContainerName = invalidContainerName
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InvalidContainerName, result.ErrorCode);
    }

    [Theory]
    [InlineData("valid-container")]
    [InlineData("container123")]
    [InlineData("my-container-name")]
    [InlineData("abc")] // Minimum length
    [InlineData("container-with-exactly-sixty-three-characters-in-total-length")] // Max length
    public async Task Handle_ValidContainerName_ShouldSucceed(string validContainerName)
    {
        // Arrange
        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/file.txt",
            ContainerName = validContainerName
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(validContainerName, result.ContainerName);
    }

    [Fact]
    public async Task Handle_BlobStorageThrowsException_ShouldReturnErrorResponse()
    {
        // Arrange
        var mockBlobStorageServiceWithErrors = new MockBlobStorageService(simulateErrors: true);
        var handler = new DownloadFromUrlHandler(
            mockBlobStorageServiceWithErrors,
            _mockHttpClient.Object,
            _mockLogger.Object
        );

        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/document.pdf",
            ContainerName = "documents"
        };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.FileDownloadFailed, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_UnexpectedException_ShouldReturnInternalServerError()
    {
        // Arrange
        var mockFailingBlobStorage = new Mock<IBlobStorageService>();
        mockFailingBlobStorage
            .Setup(x => x.DownloadFromUrlAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected error"));

        var handler = new DownloadFromUrlHandler(
            mockFailingBlobStorage.Object,
            _mockHttpClient.Object,
            _mockLogger.Object
        );

        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/document.pdf",
            ContainerName = "documents"
        };

        // Act
        var result = await handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InternalServerError, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_CancellationRequested_ShouldHandleCancellation()
    {
        // Arrange
        var mockHttpMessageHandler = new Mock<HttpMessageHandler>();
        var mockBlobStorage = new Mock<IBlobStorageService>();

        // Mock HttpClient to throw OperationCanceledException on cancellation
        mockHttpMessageHandler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var httpClient = new HttpClient(mockHttpMessageHandler.Object);

        var handler = new DownloadFromUrlHandler(
            mockBlobStorage.Object,
            httpClient,
            _mockLogger.Object
        );

        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/document.pdf",
            ContainerName = "documents"
        };

        var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        // Act & Assert
        await Assert.ThrowsAsync<TaskCanceledException>(() =>
            handler.Handle(request, cancellationTokenSource.Token));
    }

    [Theory]
    [InlineData("https://example.com/image.jpg", "image.jpg")]
    [InlineData("https://example.com/documents/report.pdf", "report.pdf")]
    [InlineData("https://example.com/files/data.json", "data.json")]
    [InlineData("https://example.com/path/", "downloaded-file-")]  // Should generate GUID name
    public async Task Handle_DifferentFileTypes_ShouldExtractCorrectBlobName(string fileUrl, string expectedBlobNamePrefix)
    {
        // Arrange
        var request = new DownloadFromUrlRequest
        {
            FileUrl = fileUrl,
            ContainerName = "files"
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        if (expectedBlobNamePrefix.EndsWith("-"))
        {
            // For generated names, just check it starts with the prefix
            Assert.StartsWith(expectedBlobNamePrefix, result.BlobName);
        }
        else
        {
            Assert.Equal(expectedBlobNamePrefix, result.BlobName);
        }
    }
}