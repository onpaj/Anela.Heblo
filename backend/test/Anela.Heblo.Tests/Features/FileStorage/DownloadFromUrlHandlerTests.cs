using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.FileStorage;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Application.Shared;
using Anela.Heblo.Domain.Features.Configuration;
using Anela.Heblo.Domain.Features.FileStorage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.FileStorage;

public class DownloadFromUrlHandlerTests
{
    // ---------------------------------------------------------------------------
    // Shared test infrastructure
    // ---------------------------------------------------------------------------

    private readonly Mock<IBlobStorageService> _blobStorage;
    private readonly Mock<IDownloadResilienceService> _resilience;
    private Mock<IHttpClientFactory> _headFactory;
    private readonly Mock<ILogger<DownloadFromUrlHandler>> _logger;
    private readonly IOptions<ProductExportOptions> _options;

    public DownloadFromUrlHandlerTests()
    {
        _blobStorage = new Mock<IBlobStorageService>();
        _resilience = new Mock<IDownloadResilienceService>();
        _logger = new Mock<ILogger<DownloadFromUrlHandler>>();
        _options = Options.Create(new ProductExportOptions
        {
            HeadTimeout = TimeSpan.FromSeconds(5),
        });

        // Default resilience: execute the operation exactly once, propagate result/exception
        _resilience
            .Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<string>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<string>>, string, CancellationToken>(
                (op, _, ct) => op(ct));

        // Default HEAD probe: respond 200 with Content-Length: 1024
        _headFactory = BuildHeadFactory(HttpStatusCode.OK, contentLength: 1024);
    }

    private DownloadFromUrlHandler BuildHandler() =>
        new DownloadFromUrlHandler(
            _blobStorage.Object,
            _resilience.Object,
            _headFactory.Object,
            _options,
            _logger.Object);

    private static Mock<IHttpClientFactory> BuildHeadFactory(
        HttpStatusCode status,
        long? contentLength = null,
        Exception? throwException = null)
    {
        var handler = throwException is null
            ? new StubHttpMessageHandler(status, contentLength)
            : new StubHttpMessageHandler(throwException);
        var client = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory
            .Setup(f => f.CreateClient(FileStorageModule.ProductExportDownloadClientName))
            .Returns(client);
        return factory;
    }

    // ---------------------------------------------------------------------------
    // Existing validation tests — updated to new constructor
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ValidRequest_ShouldReturnSuccessResponse()
    {
        // Arrange
        var blobUrl = "https://mockstorageaccount.blob.core.windows.net/documents/test-document.pdf";
        _blobStorage
            .Setup(s => s.DownloadFromUrlAsync(
                "https://example.com/document.pdf",
                "documents",
                "test-document.pdf",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(blobUrl);

        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/document.pdf",
            ContainerName = "documents",
            BlobName = "test-document.pdf",
        };

        // Act
        var result = await BuildHandler().Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(blobUrl, result.BlobUrl);
        Assert.Equal("test-document.pdf", result.BlobName);
        Assert.Equal("documents", result.ContainerName);
    }

    [Fact]
    public async Task Handle_ValidRequestWithoutBlobName_ShouldGenerateBlobName()
    {
        // Arrange
        var blobUrl = "https://mockstorageaccount.blob.core.windows.net/images/photo.jpg";
        _blobStorage
            .Setup(s => s.DownloadFromUrlAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(blobUrl);

        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/images/photo.jpg",
            ContainerName = "images",
        };

        // Act
        var result = await BuildHandler().Handle(request, CancellationToken.None);

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
            ContainerName = "documents",
        };

        // Act
        var result = await BuildHandler().Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InvalidUrlFormat, result.ErrorCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("very-long-container-name-that-exceeds-sixty-three-characters-limit")]
    [InlineData("InvalidCase")]
    [InlineData("invalid--double-hyphen")]
    [InlineData("-starts-with-hyphen")]
    [InlineData("ends-with-hyphen-")]
    [InlineData("invalid_underscore")]
    public async Task Handle_InvalidContainerName_ShouldReturnErrorResponse(string invalidContainerName)
    {
        // Arrange
        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/file.txt",
            ContainerName = invalidContainerName,
        };

        // Act
        var result = await BuildHandler().Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InvalidContainerName, result.ErrorCode);
    }

    [Theory]
    [InlineData("valid-container")]
    [InlineData("container123")]
    [InlineData("my-container-name")]
    [InlineData("abc")]
    [InlineData("container-with-exactly-sixty-three-characters-in-total-length")]
    public async Task Handle_ValidContainerName_ShouldSucceed(string validContainerName)
    {
        // Arrange
        var blobUrl = $"https://mock.blob.core.windows.net/{validContainerName}/file.txt";
        _blobStorage
            .Setup(s => s.DownloadFromUrlAsync(
                It.IsAny<string>(),
                validContainerName,
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(blobUrl);

        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/file.txt",
            ContainerName = validContainerName,
        };

        // Act
        var result = await BuildHandler().Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(validContainerName, result.ContainerName);
    }

    [Theory]
    [InlineData("https://example.com/image.jpg", "image.jpg")]
    [InlineData("https://example.com/documents/report.pdf", "report.pdf")]
    [InlineData("https://example.com/files/data.json", "data.json")]
    public async Task Handle_DifferentFileTypes_ShouldExtractCorrectBlobName(string fileUrl, string expectedBlobName)
    {
        // Arrange
        var blobUrl = $"https://mock.blob.core.windows.net/files/{expectedBlobName}";
        _blobStorage
            .Setup(s => s.DownloadFromUrlAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(blobUrl);

        var request = new DownloadFromUrlRequest
        {
            FileUrl = fileUrl,
            ContainerName = "files",
        };

        // Act
        var result = await BuildHandler().Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(expectedBlobName, result.BlobName);
    }

    // ---------------------------------------------------------------------------
    // New behaviour tests
    // ---------------------------------------------------------------------------

    [Fact]
    public async Task Handle_ReturnsSuccess_OnHappyPath()
    {
        // Arrange
        const string blobUrl = "https://mock.blob.core.windows.net/exports/export.csv";
        _blobStorage
            .Setup(s => s.DownloadFromUrlAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(blobUrl);

        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/export.csv",
            ContainerName = "exports",
        };

        // Act
        var result = await BuildHandler().Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(blobUrl, result.BlobUrl);
        Assert.Equal(1024L, result.FileSizeBytes);
        _resilience.Verify(
            r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<string>>>(),
                FileStorageModule.ProductExportDownloadClientName,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_HeadProbeTimeout_DoesNotCancelDownload()
    {
        // Arrange — HEAD probe throws TaskCanceledException (simulates per-probe timeout)
        // but the download itself succeeds
        _headFactory = BuildHeadFactory(HttpStatusCode.OK, throwException: new TaskCanceledException("head timed out"));

        const string blobUrl = "https://mock.blob.core.windows.net/exports/export.csv";
        _blobStorage
            .Setup(s => s.DownloadFromUrlAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(blobUrl);

        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/export.csv",
            ContainerName = "exports",
        };

        // Act
        var result = await BuildHandler().Handle(request, CancellationToken.None);

        // Assert — HEAD probe failure must NOT cancel the download
        Assert.True(result.Success);
        Assert.Equal(0L, result.FileSizeBytes);
    }

    [Fact]
    public async Task Handle_RetryExhausted_ReturnsFailure_With_Cause_RetryExhausted()
    {
        // Arrange — resilience executes op 4 times then throws HttpRequestException
        int callCount = 0;
        _resilience
            .Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<string>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<string>>, string, CancellationToken>(
                async (op, _, ct) =>
                {
                    for (int i = 0; i < 4; i++)
                    {
                        callCount++;
                        try { await op(ct); }
                        catch { /* retry */ }
                    }

                    throw new HttpRequestException("Connection refused");
                });

        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/export.csv?token=secret",
            ContainerName = "exports",
        };

        // Act
        var result = await BuildHandler().Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.FileDownloadFailed, result.ErrorCode);
        Assert.Equal("retry-exhausted", result.Params!["cause"]);
        Assert.DoesNotContain("token=secret", result.Params["fileUrl"]);
    }

    [Fact]
    public async Task Handle_HardHttpStatus_ReturnsFailure_With_Cause_HttpStatus()
    {
        // Arrange — resilience invokes op exactly once, then rethrows HttpRequestException
        _resilience
            .Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<string>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<string>>, string, CancellationToken>(
                async (op, _, ct) =>
                {
                    await op(ct); // increments attemptCount to 1
                    throw new HttpRequestException("404 Not Found");
                });

        _blobStorage
            .Setup(s => s.DownloadFromUrlAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("ignored");

        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/export.csv",
            ContainerName = "exports",
        };

        // Act
        var result = await BuildHandler().Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("http-status", result.Params!["cause"]);
        Assert.Equal("1", result.Params["attemptCount"]);
    }

    [Fact]
    public async Task Handle_InnerTimeout_ReturnsFailure_With_Cause_Timeout()
    {
        // Arrange — resilience throws OperationCanceledException with a NEW token (not callerCt)
        using var innerCts = new CancellationTokenSource();
        await innerCts.CancelAsync();

        _resilience
            .Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<string>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException("per-attempt timeout", innerCts.Token));

        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/export.csv",
            ContainerName = "exports",
        };

        // Act
        var result = await BuildHandler().Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("timeout", result.Params!["cause"]);
    }

    [Fact]
    public async Task Handle_CallerCancellation_PropagatesException()
    {
        // Arrange — resilience throws OperationCanceledException carrying callerCt
        using var callerCts = new CancellationTokenSource();
        await callerCts.CancelAsync();

        _resilience
            .Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<string>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<string>>, string, CancellationToken>(
                (_, _, ct) => Task.FromException<string>(new OperationCanceledException(ct)));

        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/export.csv",
            ContainerName = "exports",
        };

        // Act & Assert — caller cancellation must propagate, not be swallowed
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BuildHandler().Handle(request, callerCts.Token));
    }

    [Fact]
    public async Task Handle_RedactsUrl_RemovesQueryString()
    {
        // Arrange — force a failure so we can inspect Params["fileUrl"]
        _resilience
            .Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<string>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("404"));

        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/export.csv?token=secret123",
            ContainerName = "exports",
        };

        // Act
        var result = await BuildHandler().Handle(request, CancellationToken.None);

        // Assert — query string must not appear in the logged/returned URL
        Assert.False(result.Success);
        Assert.NotNull(result.Params);
        Assert.DoesNotContain("token=secret123", result.Params!["fileUrl"]);
        Assert.DoesNotContain("?", result.Params["fileUrl"]);
        Assert.Contains("example.com/export.csv", result.Params["fileUrl"]);
    }

    [Fact]
    public async Task Handle_ValidationFailure_InvalidUrl_SetsCauseValidation()
    {
        // Arrange
        var request = new DownloadFromUrlRequest
        {
            FileUrl = "not-a-valid-url",
            ContainerName = "exports",
        };

        // Act
        var result = await BuildHandler().Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InvalidUrlFormat, result.ErrorCode);
        Assert.Equal("validation", result.Params!["cause"]);
    }

    [Fact]
    public async Task Handle_ValidationFailure_InvalidContainerName_SetsCauseValidation()
    {
        // Arrange
        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/export.csv",
            ContainerName = "INVALID_UPPERCASE",
        };

        // Act
        var result = await BuildHandler().Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.InvalidContainerName, result.ErrorCode);
        Assert.Equal("validation", result.Params!["cause"]);
    }

    [Fact]
    public async Task Handle_BlobStorageThrowsHttpRequestException_ReturnsFileDownloadFailed()
    {
        // Arrange
        _blobStorage
            .Setup(s => s.DownloadFromUrlAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("Simulated download error"));

        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/document.pdf",
            ContainerName = "documents",
        };

        // Act
        var result = await BuildHandler().Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.FileDownloadFailed, result.ErrorCode);
    }

    [Fact]
    public async Task Handle_UnexpectedException_ReturnsFileDownloadFailed()
    {
        // Arrange
        _blobStorage
            .Setup(s => s.DownloadFromUrlAsync(
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Unexpected error"));

        var request = new DownloadFromUrlRequest
        {
            FileUrl = "https://example.com/document.pdf",
            ContainerName = "documents",
        };

        // Act
        var result = await BuildHandler().Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.FileDownloadFailed, result.ErrorCode);
        Assert.Equal("retry-exhausted", result.Params!["cause"]);
    }

    // ---------------------------------------------------------------------------
    // Stub HTTP message handler for HEAD probe
    // ---------------------------------------------------------------------------

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _statusCode;
        private readonly long? _contentLength;
        private readonly Exception? _exception;

        public StubHttpMessageHandler(HttpStatusCode statusCode, long? contentLength = null)
        {
            _statusCode = statusCode;
            _contentLength = contentLength;
        }

        public StubHttpMessageHandler(Exception exception)
        {
            _statusCode = HttpStatusCode.OK;
            _exception = exception;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (_exception is not null)
                return Task.FromException<HttpResponseMessage>(_exception);

            cancellationToken.ThrowIfCancellationRequested();

            var response = new HttpResponseMessage(_statusCode)
            {
                Content = new ByteArrayContent(Array.Empty<byte>()),
            };

            if (_contentLength.HasValue)
                response.Content.Headers.ContentLength = _contentLength;

            return Task.FromResult(response);
        }
    }
}
