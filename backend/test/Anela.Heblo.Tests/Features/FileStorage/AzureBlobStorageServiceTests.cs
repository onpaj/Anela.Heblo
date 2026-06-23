using Anela.Heblo.Application.Features.FileStorage;
using Anela.Heblo.Application.Features.FileStorage.Services;
using Azure;
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
            .Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName))
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
            f => f.CreateClient(FileStorageModule.FileDownloadClientName),
            Times.Once);
    }

    [Fact]
    public async Task DownloadFromUrlAsync_ThrowsHttpRequestException_OnNon2xx()
    {
        // Arrange
        var handler = new StubHttpMessageHandler(HttpStatusCode.ServiceUnavailable);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName))
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
    // FR-4: GetContentTypeFromExtension — all switch arms (via UploadAsync BlobUploadOptions)
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("file.jpg", "image/jpeg")]
    [InlineData("file.jpeg", "image/jpeg")]
    [InlineData("file.png", "image/png")]
    [InlineData("file.gif", "image/gif")]
    [InlineData("file.webp", "image/webp")]
    [InlineData("file.pdf", "application/pdf")]
    [InlineData("file.txt", "text/plain")]
    [InlineData("file.json", "application/json")]
    [InlineData("file.xml", "application/xml")]
    [InlineData("file.zip", "application/zip")]
    [InlineData("file.doc", "application/msword")]
    [InlineData("file.docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [InlineData("file.xls", "application/vnd.ms-excel")]
    [InlineData("file.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("file.unknown", "application/octet-stream")]
    public async Task DownloadFromUrlAsync_NoResponseContentType_InfersContentTypeFromExtension(
        string blobName, string expectedContentType)
    {
        // Arrange — response has NO Content-Type header (ByteArrayContent), so production code
        // falls back to GetContentTypeFromExtension(blobName).
        var fileUrl = "https://example.com/file";
        var containerName = "documents";

        var handler = BuildNoContentTypeHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        _mockHttpClientFactory
            .Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName))
            .Returns(client);

        SetupContainerAndBlobClient(containerName, out _, out var blobMock, out _);

        // Act — pass explicit blobName so extension is known
        await _service.DownloadFromUrlAsync(fileUrl, containerName, blobName);

        // Assert — UploadAsync received the content type inferred from the extension
        blobMock.Verify(x => x.UploadAsync(
            It.IsAny<Stream>(),
            It.Is<BlobUploadOptions>(opts => opts.HttpHeaders.ContentType == expectedContentType),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DownloadFromUrlAsync_NoResponseContentType_UppercaseExtension_InfersContentType()
    {
        // Arrange — GetContentTypeFromExtension lowercases the extension before matching.
        var fileUrl = "https://example.com/file";
        var containerName = "documents";

        var handler = BuildNoContentTypeHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        _mockHttpClientFactory
            .Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName))
            .Returns(client);

        SetupContainerAndBlobClient(containerName, out _, out var blobMock, out _);

        // Act — uppercase extension must still resolve to image/jpeg
        await _service.DownloadFromUrlAsync(fileUrl, containerName, "PHOTO.JPG");

        // Assert
        blobMock.Verify(x => x.UploadAsync(
            It.IsAny<Stream>(),
            It.Is<BlobUploadOptions>(opts => opts.HttpHeaders.ContentType == "image/jpeg"),
            It.IsAny<CancellationToken>()), Times.Once);
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
            .Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName))
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
        factory.Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName))
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
    // FR-1: blobName derived from the URL path filename
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DownloadFromUrlAsync_UrlWithFilename_UsesBlobNameFromPath()
    {
        // Arrange
        var fileUrl = "https://example.com/folder/report.pdf";
        var containerName = "documents";

        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "test content");
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        _mockHttpClientFactory
            .Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName))
            .Returns(client);

        SetupContainerAndBlobClient(containerName, out _, out _, out var capturedBlobNames);

        // Act
        await _service.DownloadFromUrlAsync(fileUrl, containerName);

        // Assert — blob name comes from the URL path filename, not a generated GUID
        Assert.Contains("report.pdf", capturedBlobNames);
    }

    // ---------------------------------------------------------------------------
    // FR-2: no filename in URL → generated "downloaded-file-{guid}{ext}" blob name
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DownloadFromUrlAsync_UrlWithNoFilename_KnownContentType_UsesPrefixAndExtension()
    {
        // Arrange — URL path ends with '/', so Path.GetFileName returns empty and a name is generated.
        var fileUrl = "https://example.com/files/";
        var containerName = "documents";

        var responseContent = new StringContent("test content");
        responseContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/png");
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, overrideContent: responseContent);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        _mockHttpClientFactory
            .Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName))
            .Returns(client);

        SetupContainerAndBlobClient(containerName, out _, out _, out var capturedBlobNames);

        // Act
        await _service.DownloadFromUrlAsync(fileUrl, containerName);

        // Assert — generated name uses the "downloaded-file-" prefix and the content-type extension
        var generatedName = Assert.Single(capturedBlobNames);
        Assert.StartsWith("downloaded-file-", generatedName);
        Assert.EndsWith(".png", generatedName);
    }

    [Fact]
    public async Task DownloadFromUrlAsync_UrlWithNoFilename_UnknownContentType_UsesBinExtension()
    {
        // Arrange — unknown content type maps to the ".bin" fallback in GetExtensionFromContentType.
        var fileUrl = "https://example.com/files/";
        var containerName = "documents";

        var responseContent = new StringContent("test content");
        responseContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/x-unknown");
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, overrideContent: responseContent);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        _mockHttpClientFactory
            .Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName))
            .Returns(client);

        SetupContainerAndBlobClient(containerName, out _, out _, out var capturedBlobNames);

        // Act
        await _service.DownloadFromUrlAsync(fileUrl, containerName);

        // Assert
        var generatedName = Assert.Single(capturedBlobNames);
        Assert.StartsWith("downloaded-file-", generatedName);
        Assert.EndsWith(".bin", generatedName);
    }

    // ---------------------------------------------------------------------------
    // FR-3: GetExtensionFromContentType — all switch arms (via generated blob name)
    // ---------------------------------------------------------------------------

    [Theory]
    [InlineData("image/jpeg", ".jpg")]
    [InlineData("image/png", ".png")]
    [InlineData("image/gif", ".gif")]
    [InlineData("image/webp", ".webp")]
    [InlineData("application/pdf", ".pdf")]
    [InlineData("text/plain", ".txt")]
    [InlineData("application/json", ".json")]
    [InlineData("application/xml", ".xml")]
    [InlineData("application/x-unknown", ".bin")]
    public async Task DownloadFromUrlAsync_NoFilenameUrl_ContentTypeToExtension_AllArms(
        string contentType, string expectedExtension)
    {
        // Arrange — URL ends with '/' to force the generated "downloaded-file-{guid}{ext}" branch.
        var fileUrl = "https://example.com/files/";
        var containerName = "documents";

        var responseContent = new StringContent("test content");
        responseContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(contentType);
        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, overrideContent: responseContent);
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        _mockHttpClientFactory
            .Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName))
            .Returns(client);

        SetupContainerAndBlobClient(containerName, out _, out _, out var capturedBlobNames);

        // Act
        await _service.DownloadFromUrlAsync(fileUrl, containerName);

        // Assert
        var generatedName = Assert.Single(capturedBlobNames);
        Assert.EndsWith(expectedExtension, generatedName);
    }

    // ---------------------------------------------------------------------------
    // FR-5: container cache — CreateIfNotExists runs once across repeat downloads
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task DownloadFromUrlAsync_CalledTwice_SameContainer_CallsCreateIfNotExistsOnce()
    {
        // Arrange — fresh instance so the _containerExists cache starts empty.
        var mockBlobServiceClient = new Mock<BlobServiceClient>();
        var mockLogger = new Mock<ILogger<AzureBlobStorageService>>();

        var handler = new StubHttpMessageHandler(HttpStatusCode.OK, "test content");
        var client = new HttpClient(handler) { BaseAddress = new Uri("http://test/") };
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName)).Returns(client);

        var service = new AzureBlobStorageService(
            mockBlobServiceClient.Object,
            factory.Object,
            mockLogger.Object);

        var containerName = "documents";
        var mockContainerClient = new Mock<BlobContainerClient>();
        var mockBlobClient = new Mock<BlobClient>();

        mockBlobClient.Setup(x => x.Uri)
            .Returns(new Uri($"https://test.blob.core.windows.net/{containerName}/file.pdf"));
        mockBlobClient.Setup(x => x.UploadAsync(
                It.IsAny<Stream>(), It.IsAny<BlobUploadOptions>(), It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContentInfo>>()));
        mockContainerClient.Setup(x => x.GetBlobClient(It.IsAny<string>())).Returns(mockBlobClient.Object);
        mockContainerClient.Setup(x => x.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContainerInfo>>()));
        mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerName))
            .Returns(mockContainerClient.Object);

        // Act — two downloads to the same container
        await service.DownloadFromUrlAsync("https://example.com/a.pdf", containerName);
        await service.DownloadFromUrlAsync("https://example.com/b.pdf", containerName);

        // Assert — second download hits the cached "already-seen" branch, so no second create
        mockContainerClient.Verify(
            x => x.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ---------------------------------------------------------------------------
    // FR-6: ListVirtualDirectoriesAsync — trailing-slash trimming
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task ListVirtualDirectoriesAsync_TrimsTrailingSlash_FromPrefixes()
    {
        // Arrange
        var containerName = "documents";
        var mockContainerClient = new Mock<BlobContainerClient>();

        var items = new[]
        {
            BlobsModelFactory.BlobHierarchyItem("invoices/", null),
            BlobsModelFactory.BlobHierarchyItem("reports/", null),
        };

        mockContainerClient.Setup(x => x.GetBlobsByHierarchyAsync(
                It.IsAny<BlobTraits>(),
                It.IsAny<BlobStates>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncPageable(items));
        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerName))
            .Returns(mockContainerClient.Object);

        // Act
        var result = await _service.ListVirtualDirectoriesAsync(containerName);

        // Assert — each prefix has exactly one trailing slash trimmed
        Assert.Equal(2, result.Count);
        Assert.Contains("invoices", result);
        Assert.Contains("reports", result);
    }

    [Fact]
    public async Task ListVirtualDirectoriesAsync_EmptyContainer_ReturnsEmptyList()
    {
        // Arrange
        var containerName = "documents";
        var mockContainerClient = new Mock<BlobContainerClient>();

        mockContainerClient.Setup(x => x.GetBlobsByHierarchyAsync(
                It.IsAny<BlobTraits>(),
                It.IsAny<BlobStates>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns(CreateAsyncPageable(Array.Empty<BlobHierarchyItem>()));
        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerName))
            .Returns(mockContainerClient.Object);

        // Act
        var result = await _service.ListVirtualDirectoriesAsync(containerName);

        // Assert
        Assert.Empty(result);
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
    // Shared test helpers
    // ---------------------------------------------------------------------------

    private void SetupContainerAndBlobClient(
        string containerName,
        out Mock<BlobContainerClient> containerMock,
        out Mock<BlobClient> blobMock,
        out List<string> capturedBlobNames)
    {
        var names = new List<string>();
        capturedBlobNames = names;

        var container = new Mock<BlobContainerClient>();
        var blob = new Mock<BlobClient>();

        blob.Setup(x => x.Uri)
            .Returns(new Uri($"https://testaccount.blob.core.windows.net/{containerName}/blob"));
        blob.Setup(x => x.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<BlobUploadOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContentInfo>>()));

        container.Setup(x => x.GetBlobClient(It.IsAny<string>()))
            .Callback<string>(name => names.Add(name))
            .Returns(blob.Object);
        container.Setup(x => x.CreateIfNotExistsAsync(
                It.IsAny<PublicAccessType>(),
                It.IsAny<IDictionary<string, string>>(),
                It.IsAny<BlobContainerEncryptionScopeOptions>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.FromResult(Mock.Of<Azure.Response<BlobContainerInfo>>()));

        _mockBlobServiceClient.Setup(x => x.GetBlobContainerClient(containerName))
            .Returns(container.Object);

        containerMock = container;
        blobMock = blob;
    }

    private static StubHttpMessageHandler BuildNoContentTypeHandler(string responseBody = "test content")
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(responseBody));
        return new StubHttpMessageHandler(HttpStatusCode.OK, overrideContent: content);
    }

    private static AsyncPageable<T> CreateAsyncPageable<T>(IEnumerable<T> items) where T : notnull
    {
        var page = Page<T>.FromValues(items.ToList(), continuationToken: null, response: Mock.Of<Azure.Response>());
        return AsyncPageable<T>.FromPages(new[] { page });
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
