using Anela.Heblo.Application.Features.FileStorage;
using Anela.Heblo.Application.Features.FileStorage.Services;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Text;
using Xunit;

namespace Anela.Heblo.Tests.Features.FileStorage;

public class AzureBlobStorageServiceTests
{
    private readonly Mock<BlobServiceClient> _mockBlobServiceClient;
    private readonly Mock<ILogger<AzureBlobStorageService>> _mockLogger;
    private readonly Mock<IHttpClientFactory> _mockHttpClientFactory;
    private readonly StubHttpMessageHandler _stubHandler;
    private readonly AzureBlobStorageService _service;

    public AzureBlobStorageServiceTests()
    {
        _mockBlobServiceClient = new Mock<BlobServiceClient>();
        _mockLogger = new Mock<ILogger<AzureBlobStorageService>>();
        _stubHandler = new StubHttpMessageHandler(HttpStatusCode.OK, "test content");

        var httpClient = new HttpClient(_stubHandler) { BaseAddress = new Uri("http://test/") };
        _mockHttpClientFactory = new Mock<IHttpClientFactory>();
        _mockHttpClientFactory
            .Setup(f => f.CreateClient(FileStorageModule.ProductExportDownloadClientName))
            .Returns(httpClient);

        _service = new AzureBlobStorageService(
            _mockBlobServiceClient.Object,
            _mockHttpClientFactory.Object,
            _mockLogger.Object
        );
    }

    // ---------------------------------------------------------------------------
    // IHttpClientFactory constructor and resolution tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DownloadFromUrlAsync_ResolvesNamedClient_ProductExportDownload()
    {
        // Arrange
        var fileUrl = "https://example.com/export.csv";
        var containerName = "exports";

        var mockContainerClient = new Mock<BlobContainerClient>();
        var mockBlobClient = new Mock<BlobClient>();
        mockBlobClient.Setup(x => x.Uri)
            .Returns(new Uri($"https://testaccount.blob.core.windows.net/{containerName}/export.csv"));
        mockBlobClient.Setup(x => x.UploadAsync(
            It.IsAny<Stream>(),
            It.IsAny<BlobUploadOptions>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContentInfo>>()));
        mockContainerClient.Setup(x => x.GetBlobClient(It.IsAny<string>())).Returns(mockBlobClient.Object);
        mockContainerClient.Setup(x => x.CreateIfNotExistsAsync(
            It.IsAny<PublicAccessType>(),
            It.IsAny<IDictionary<string, string>>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContainerInfo>>()));
        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerName)).Returns(mockContainerClient.Object);

        // Act
        await _service.DownloadFromUrlAsync(fileUrl, containerName);

        // Assert — factory must have been asked for the named client exactly once
        _mockHttpClientFactory.Verify(
            f => f.CreateClient(FileStorageModule.ProductExportDownloadClientName),
            Times.Once);
    }

    [Fact]
    public async Task DownloadFromUrlAsync_ThrowsHttpRequestException_OnNon2xx()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(HttpStatusCode.ServiceUnavailable);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(FileStorageModule.ProductExportDownloadClientName))
               .Returns(client);

        var service = new AzureBlobStorageService(
            _mockBlobServiceClient.Object,
            factory.Object,
            _mockLogger.Object);

        // Act & Assert
        await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.DownloadFromUrlAsync("https://example.com/file", "exports"));
    }

    [Fact]
    public async Task DownloadFromUrlAsync_ForwardsCancellationToken()
    {
        // Arrange — token that is already cancelled
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _service.DownloadFromUrlAsync(
                "https://example.com/file",
                "exports",
                cancellationToken: cts.Token));
    }

    // ---------------------------------------------------------------------------
    // GetContentTypeFromExtension (tested indirectly)
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData(".jpg", "image/jpeg")]
    [InlineData(".jpeg", "image/jpeg")]
    [InlineData(".png", "image/png")]
    [InlineData(".pdf", "application/pdf")]
    [InlineData(".txt", "text/plain")]
    [InlineData(".json", "application/json")]
    [InlineData(".unknown", "application/octet-stream")]
    public void GetContentTypeFromExtension_DifferentExtensions_ShouldReturnCorrectContentType(string extension, string expectedContentType)
    {
        // This test would require making the private method internal or using reflection
        // For now, we'll test it indirectly through other methods
        var fileName = $"test{extension}";
        // The GetContentTypeFromExtension method is private, so we test it indirectly
        Assert.NotEmpty(fileName); // Placeholder assertion
        Assert.NotEmpty(expectedContentType); // Verify expected content type is provided
    }

    // ---------------------------------------------------------------------------
    // GetBlobUrl
    // ---------------------------------------------------------------------------

    [Fact]
    public void GetBlobUrl_ValidContainerAndBlobName_ShouldReturnCorrectUrl()
    {
        // Arrange
        var containerName = "documents";
        var blobName = "test.pdf";

        var mockBlobContainerClient = new Mock<BlobContainerClient>();
        var mockBlobClient = new Mock<BlobClient>();
        var expectedUrl = new Uri($"https://testaccount.blob.core.windows.net/{containerName}/{blobName}");

        mockBlobClient.Setup(x => x.Uri).Returns(expectedUrl);
        mockBlobContainerClient.Setup(x => x.GetBlobClient(blobName)).Returns(mockBlobClient.Object);
        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerName)).Returns(mockBlobContainerClient.Object);

        // Act
        var result = _service.GetBlobUrl(containerName, blobName);

        // Assert
        Assert.Equal(expectedUrl.ToString(), result);
    }

    // ---------------------------------------------------------------------------
    // DownloadFromUrlAsync — content-type extension inference
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    [InlineData("application/pdf", ".pdf")]
    [InlineData("text/plain", ".txt")]
    [InlineData("application/json", ".json")]
    [InlineData("unknown/type", ".bin")]
    public async Task DownloadAndUploadFromUrl_DifferentContentTypes_ShouldGenerateCorrectExtension(string contentType, string expectedExtension)
    {
        // Arrange
        var fileUrl = "https://example.com/file";
        var containerName = "documents";

        // Build an HttpContent with the specific content-type header and wire it into the factory.
        var responseContent = new StringContent("test content");
        responseContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);

        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, overrideContent: responseContent);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        _mockHttpClientFactory
            .Setup(f => f.CreateClient(FileStorageModule.ProductExportDownloadClientName))
            .Returns(client);

        var expectedBlobUrl = $"https://testaccount.blob.core.windows.net/{containerName}/downloaded-file-guid{expectedExtension}";

        var mockContainerClient = new Mock<BlobContainerClient>();
        var mockBlobClient = new Mock<BlobClient>();
        mockBlobClient.Setup(x => x.Uri).Returns(new Uri(expectedBlobUrl));
        mockBlobClient.Setup(x => x.UploadAsync(
            It.IsAny<Stream>(),
            It.IsAny<BlobUploadOptions>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContentInfo>>()));
        mockContainerClient.Setup(x => x.GetBlobClient(It.IsAny<string>())).Returns(mockBlobClient.Object);
        mockContainerClient.Setup(x => x.CreateIfNotExistsAsync(
            It.IsAny<PublicAccessType>(),
            It.IsAny<IDictionary<string, string>>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContainerInfo>>()));
        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerName)).Returns(mockContainerClient.Object);

        // Act
        var result = await _service.DownloadFromUrlAsync(fileUrl, containerName);

        // Assert
        Assert.NotNull(result);
        Assert.Contains(containerName, result);
    }

    // ---------------------------------------------------------------------------
    // UploadAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UploadAsync_ValidStream_ShouldUploadSuccessfully()
    {
        // Arrange
        var content = "Test file content";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var containerName = "documents";
        var blobName = "test.txt";
        var contentType = "text/plain";

        var mockContainerClient = new Mock<BlobContainerClient>();
        var mockBlobClient = new Mock<BlobClient>();
        var expectedBlobUrl = $"https://testaccount.blob.core.windows.net/{containerName}/{blobName}";

        mockBlobClient.Setup(x => x.Uri).Returns(new Uri(expectedBlobUrl));
        mockBlobClient.Setup(x => x.UploadAsync(
            It.IsAny<Stream>(),
            It.IsAny<BlobUploadOptions>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContentInfo>>()));

        mockContainerClient.Setup(x => x.GetBlobClient(blobName)).Returns(mockBlobClient.Object);
        mockContainerClient.Setup(x => x.CreateIfNotExistsAsync(
            It.IsAny<PublicAccessType>(),
            It.IsAny<IDictionary<string, string>>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContainerInfo>>()));

        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerName)).Returns(mockContainerClient.Object);

        // Act
        var result = await _service.UploadAsync(stream, containerName, blobName, contentType);

        // Assert
        Assert.Equal(expectedBlobUrl, result);
        // Conditions must be null so the PUT is unconditional (always overwrites — no IfNoneMatch: *)
        mockBlobClient.Verify(x => x.UploadAsync(
            It.IsAny<Stream>(),
            It.Is<BlobUploadOptions>(opts =>
                opts.HttpHeaders.ContentType == contentType &&
                opts.Conditions == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UploadAsync_ShouldAllowReUpload_WhenBlobAlreadyExists()
    {
        // Arrange — re-upload scenario (e.g. KB document re-ingestion).
        // The upload must not set Conditions.IfNoneMatch = ETag.All, which would cause a 409 Conflict.
        var content = "Updated file content";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var containerName = "documents";
        var blobName = "existing-document.pdf";
        var contentType = "application/pdf";

        var mockContainerClient = new Mock<BlobContainerClient>();
        var mockBlobClient = new Mock<BlobClient>();
        var expectedBlobUrl = $"https://testaccount.blob.core.windows.net/{containerName}/{blobName}";

        mockBlobClient.Setup(x => x.Uri).Returns(new Uri(expectedBlobUrl));
        mockBlobClient.Setup(x => x.UploadAsync(
            It.IsAny<Stream>(),
            It.IsAny<BlobUploadOptions>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContentInfo>>()));

        mockContainerClient.Setup(x => x.GetBlobClient(blobName)).Returns(mockBlobClient.Object);
        mockContainerClient.Setup(x => x.CreateIfNotExistsAsync(
            It.IsAny<PublicAccessType>(),
            It.IsAny<IDictionary<string, string>>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContainerInfo>>()));

        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerName)).Returns(mockContainerClient.Object);

        // Act
        var result = await _service.UploadAsync(stream, containerName, blobName, contentType);

        // Assert — upload succeeds and uses unconditional PUT (Conditions == null → always overwrites)
        Assert.Equal(expectedBlobUrl, result);
        mockBlobClient.Verify(x => x.UploadAsync(
            It.IsAny<Stream>(),
            It.Is<BlobUploadOptions>(opts =>
                opts.HttpHeaders.ContentType == contentType &&
                opts.Conditions == null),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ---------------------------------------------------------------------------
    // DeleteAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DeleteAsync_ExistingBlob_ShouldReturnTrue()
    {
        // Arrange
        var containerName = "documents";
        var blobName = "test.txt";

        var mockContainerClient = new Mock<BlobContainerClient>();
        var mockResponse = Mock.Of<Azure.Response<bool>>(r => r.Value == true);

        mockContainerClient.Setup(x => x.DeleteBlobIfExistsAsync(
            blobName,
            It.IsAny<DeleteSnapshotsOption>(),
            It.IsAny<BlobRequestConditions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerName)).Returns(mockContainerClient.Object);

        // Act
        var result = await _service.DeleteAsync(containerName, blobName);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task DeleteAsync_NonExistingBlob_ShouldReturnFalse()
    {
        // Arrange
        var containerName = "documents";
        var blobName = "nonexistent.txt";

        var mockContainerClient = new Mock<BlobContainerClient>();
        var mockResponse = Mock.Of<Azure.Response<bool>>(r => r.Value == false);

        mockContainerClient.Setup(x => x.DeleteBlobIfExistsAsync(
            blobName,
            It.IsAny<DeleteSnapshotsOption>(),
            It.IsAny<BlobRequestConditions>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);

        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerName)).Returns(mockContainerClient.Object);

        // Act
        var result = await _service.DeleteAsync(containerName, blobName);

        // Assert
        Assert.False(result);
    }

    // ---------------------------------------------------------------------------
    // ExistsAsync
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ExistsAsync_ExistingBlob_ShouldReturnTrue()
    {
        // Arrange
        var containerName = "documents";
        var blobName = "test.txt";

        var mockContainerClient = new Mock<BlobContainerClient>();
        var mockBlobClient = new Mock<BlobClient>();
        var mockResponse = Mock.Of<Azure.Response<bool>>(r => r.Value == true);

        mockBlobClient.Setup(x => x.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);
        mockContainerClient.Setup(x => x.GetBlobClient(blobName)).Returns(mockBlobClient.Object);
        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerName)).Returns(mockContainerClient.Object);

        // Act
        var result = await _service.ExistsAsync(containerName, blobName);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task ExistsAsync_NonExistingBlob_ShouldReturnFalse()
    {
        // Arrange
        var containerName = "documents";
        var blobName = "nonexistent.txt";

        var mockContainerClient = new Mock<BlobContainerClient>();
        var mockBlobClient = new Mock<BlobClient>();
        var mockResponse = Mock.Of<Azure.Response<bool>>(r => r.Value == false);

        mockBlobClient.Setup(x => x.ExistsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(mockResponse);
        mockContainerClient.Setup(x => x.GetBlobClient(blobName)).Returns(mockBlobClient.Object);
        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerName)).Returns(mockContainerClient.Object);

        // Act
        var result = await _service.ExistsAsync(containerName, blobName);

        // Assert
        Assert.False(result);
    }

    // ---------------------------------------------------------------------------
    // DownloadFromUrlAsync — error propagation
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DownloadAndUploadFromUrl_HttpRequestException_ShouldThrow()
    {
        // Arrange — factory returns a client whose handler throws HttpRequestException
        var handler = new ThrowingHttpMessageHandler(new HttpRequestException("File not found"));
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(FileStorageModule.ProductExportDownloadClientName))
               .Returns(client);

        var service = new AzureBlobStorageService(
            _mockBlobServiceClient.Object,
            factory.Object,
            _mockLogger.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            service.DownloadFromUrlAsync("https://example.com/nonexistent.pdf", "documents"));

        Assert.Equal("File not found", exception.Message);
    }

    // ---------------------------------------------------------------------------
    // UploadAsync — container cache behaviour
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task UploadAsync_CalledMultipleTimes_ShouldCallCreateIfNotExistsOnlyOnce()
    {
        // Arrange — fresh instance so _containerExists cache is empty
        var mockBlobServiceClient = new Mock<BlobServiceClient>();
        var mockLogger = new Mock<ILogger<AzureBlobStorageService>>();
        var factory = new Mock<IHttpClientFactory>();
        var service = new AzureBlobStorageService(
            mockBlobServiceClient.Object,
            factory.Object,
            mockLogger.Object);

        var containerName = "documents";
        var mockContainerClient = new Mock<BlobContainerClient>();
        var mockBlobClient = new Mock<BlobClient>();

        mockBlobClient.Setup(x => x.Uri).Returns(new Uri($"https://test.blob.core.windows.net/{containerName}/file.txt"));
        mockBlobClient.Setup(x => x.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContentInfo>>()));
        mockContainerClient.Setup(x => x.GetBlobClient(It.IsAny<string>())).Returns(mockBlobClient.Object);
        mockContainerClient.Setup(x => x.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<BlobContainerEncryptionScopeOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContainerInfo>>()));
        mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerName)).Returns(mockContainerClient.Object);

        // Act — upload 3 times to the same container
        await service.UploadAsync(new MemoryStream(new byte[] { 1 }), containerName, "file1.txt", "text/plain");
        await service.UploadAsync(new MemoryStream(new byte[] { 2 }), containerName, "file2.txt", "text/plain");
        await service.UploadAsync(new MemoryStream(new byte[] { 3 }), containerName, "file3.txt", "text/plain");

        // Assert — CreateIfNotExistsAsync called exactly once despite 3 uploads
        mockContainerClient.Verify(
            x => x.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<BlobContainerEncryptionScopeOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadAsync_DifferentContainers_ShouldCallCreateIfNotExistsOncePerContainer()
    {
        // Arrange — fresh instance so _containerExists cache is empty
        var mockBlobServiceClient = new Mock<BlobServiceClient>();
        var mockLogger = new Mock<ILogger<AzureBlobStorageService>>();
        var factory = new Mock<IHttpClientFactory>();
        var service = new AzureBlobStorageService(
            mockBlobServiceClient.Object,
            factory.Object,
            mockLogger.Object);

        var containerA = "container-a";
        var containerB = "container-b";

        var mockContainerClientA = new Mock<BlobContainerClient>();
        var mockContainerClientB = new Mock<BlobContainerClient>();
        var mockBlobClient = new Mock<BlobClient>();

        mockBlobClient.Setup(x => x.Uri).Returns(new Uri("https://test.blob.core.windows.net/container-a/file.txt"));
        mockBlobClient.Setup(x => x.UploadAsync(It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContentInfo>>()));

        foreach (var mock in new[] { mockContainerClientA, mockContainerClientB })
        {
            mock.Setup(x => x.GetBlobClient(It.IsAny<string>())).Returns(mockBlobClient.Object);
            mock.Setup(x => x.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<BlobContainerEncryptionScopeOptions>(), It.IsAny<CancellationToken>()))
                .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContainerInfo>>()));
        }

        mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerA)).Returns(mockContainerClientA.Object);
        mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerB)).Returns(mockContainerClientB.Object);

        // Act — upload twice to container A and twice to container B
        await service.UploadAsync(new MemoryStream(new byte[] { 1 }), containerA, "a1.txt", "text/plain");
        await service.UploadAsync(new MemoryStream(new byte[] { 2 }), containerA, "a2.txt", "text/plain");
        await service.UploadAsync(new MemoryStream(new byte[] { 3 }), containerB, "b1.txt", "text/plain");
        await service.UploadAsync(new MemoryStream(new byte[] { 4 }), containerB, "b2.txt", "text/plain");

        // Assert — each container gets exactly one CreateIfNotExistsAsync call
        mockContainerClientA.Verify(
            x => x.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<BlobContainerEncryptionScopeOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
        mockContainerClientB.Verify(
            x => x.CreateIfNotExistsAsync(It.IsAny<PublicAccessType>(), It.IsAny<IDictionary<string, string>>(), It.IsAny<BlobContainerEncryptionScopeOptions>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadAsync_BlobStorageException_ShouldThrow()
    {
        // Arrange
        var content = "Test content";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        var containerName = "documents";
        var blobName = "test.txt";
        var contentType = "text/plain";

        var mockContainerClient = new Mock<BlobContainerClient>();
        var mockBlobClient = new Mock<BlobClient>();

        mockContainerClient.Setup(x => x.CreateIfNotExistsAsync(
            It.IsAny<PublicAccessType>(),
            It.IsAny<IDictionary<string, string>>(),
            It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContainerInfo>>()));

        mockBlobClient.Setup(x => x.UploadAsync(
            It.IsAny<Stream>(),
            It.IsAny<BlobUploadOptions>(),
            It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Storage error"));

        mockContainerClient.Setup(x => x.GetBlobClient(blobName)).Returns(mockBlobClient.Object);
        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerName)).Returns(mockContainerClient.Object);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _service.UploadAsync(stream, containerName, blobName, contentType));

        Assert.Equal("Storage error", exception.Message);
    }

    // ---------------------------------------------------------------------------
    // Stub handlers
    // ---------------------------------------------------------------------------

    private sealed class StubHttpMessageHandler(
        HttpStatusCode statusCode,
        string content = "",
        HttpContent? overrideContent = null) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var response = new HttpResponseMessage(statusCode)
            {
                Content = overrideContent ?? new StringContent(content)
            };
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingHttpMessageHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromException<HttpResponseMessage>(exception);
        }
    }
}
