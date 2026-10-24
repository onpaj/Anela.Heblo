using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.FileStorage;
using Anela.Heblo.Application.Features.FileStorage.Contracts;
using Anela.Heblo.Application.Features.FileStorage.Infrastructure;
using Anela.Heblo.Application.Features.FileStorage.UseCases.DownloadFromUrl;
using Anela.Heblo.Application.Shared;
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
    private readonly IOptions<FileDownloadOptions> _options;

    public DownloadFromUrlHandlerTests()
    {
        _blobStorage = new Mock<IBlobStorageService>();
        _resilience = new Mock<IDownloadResilienceService>();
        _logger = new Mock<ILogger<DownloadFromUrlHandler>>();
        _options = Options.Create(new FileDownloadOptions
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

    private DownloadFromUrlHandler BuildHandler(ILogger<DownloadFromUrlHandler>? logger = null) =>
        new DownloadFromUrlHandler(
            _blobStorage.Object,
            _resilience.Object,
            _headFactory.Object,
            _options,
            logger ?? _logger.Object);

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
            .Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName))
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
            .Setup(s => s.UploadAsync(
                It.IsAny<Stream>(),
                "documents",
                "test-document.pdf",
                "application/pdf",
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
            .Setup(s => s.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
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
    [InlineData("https://example.com/image.jpg", "image.jpg")]
    [InlineData("https://example.com/documents/report.pdf", "report.pdf")]
    [InlineData("https://example.com/files/data.json", "data.json")]
    public async Task Handle_DifferentFileTypes_ShouldExtractCorrectBlobName(string fileUrl, string expectedBlobName)
    {
        // Arrange
        var blobUrl = $"https://mock.blob.core.windows.net/files/{expectedBlobName}";
        _blobStorage
            .Setup(s => s.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
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
            .Setup(s => s.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
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
                FileStorageModule.FileDownloadClientName,
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task Handle_HeadProbeTimeout_DoesNotCancelDownload()
    {
        // Arrange — HEAD probe throws TaskCanceledException (simulates per-probe timeout)
        // but the download itself succeeds
        SetupDownload(req => req.Method == HttpMethod.Head
            ? throw new TaskCanceledException("head timed out")
            : Ok("text/csv"));

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
        Assert.Null(result.BlobUrl);
        Assert.Null(result.BlobName);
        Assert.Null(result.ContainerName);
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
            .Setup(s => s.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
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
        Assert.Null(result.BlobUrl);
        Assert.Null(result.BlobName);
        Assert.Null(result.ContainerName);
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
        Assert.Null(result.BlobUrl);
        Assert.Null(result.BlobName);
        Assert.Null(result.ContainerName);
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
    public async Task Handle_BlobStorageThrowsHttpRequestException_ReturnsFileDownloadFailed()
    {
        // Arrange
        _blobStorage
            .Setup(s => s.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
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
            .Setup(s => s.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
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
        Assert.Null(result.BlobUrl);
        Assert.Null(result.BlobName);
        Assert.Null(result.ContainerName);
    }

    // ---------------------------------------------------------------------------
    // GET + upload flow (moved from AzureBlobStorageService into the handler)
    // ---------------------------------------------------------------------------

    private sealed record UploadCall(string ContainerName, string BlobName, string ContentType, CancellationToken Token);

    private (Mock<IHttpClientFactory> Factory, List<HttpRequestMessage> Requests, List<UploadCall> Uploads) SetupDownload(
        Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new DelegatingStubHandler((req, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            requests.Add(req);
            return respond(req);
        });
        var factory = new Mock<IHttpClientFactory>();
        factory
            .Setup(f => f.CreateClient(FileStorageModule.FileDownloadClientName))
            .Returns(() => new HttpClient(handler));
        _headFactory = factory;

        var uploads = new List<UploadCall>();
        _blobStorage
            .Setup(s => s.UploadAsync(
                It.IsAny<Stream>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .Callback<Stream, string, string, string, CancellationToken>(
                (_, container, blob, contentType, ct) => uploads.Add(new UploadCall(container, blob, contentType, ct)))
            .ReturnsAsync((Stream _, string container, string blob, string _, CancellationToken _) =>
                $"https://mock.blob.core.windows.net/{container}/{blob}");

        return (factory, requests, uploads);
    }

    private static HttpResponseMessage Ok(string? contentType = null)
    {
        var content = new ByteArrayContent(new byte[] { 1, 2, 3 });
        if (contentType is not null)
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    [Fact]
    public async Task Handle_Download_ResolvesNamedClient_AndUsesGetWithUpload()
    {
        // Arrange
        var (factory, requests, uploads) = SetupDownload(_ => Ok("text/csv"));

        // Act
        var result = await BuildHandler().Handle(
            new DownloadFromUrlRequest { FileUrl = "https://example.com/export.csv", ContainerName = "exports" },
            CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        factory.Verify(f => f.CreateClient(FileStorageModule.FileDownloadClientName), Times.AtLeast(2)); // HEAD probe + GET
        Assert.Contains(requests, r => r.Method == HttpMethod.Get);
        var upload = Assert.Single(uploads);
        Assert.Equal("exports", upload.ContainerName);
        Assert.Equal("export.csv", upload.BlobName);
    }

    [Fact]
    public async Task Handle_Download_NonSuccessStatus_ReturnsFailure_AndNeverUploads()
    {
        // Arrange — HEAD and GET both return 503
        var (_, _, uploads) = SetupDownload(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        // Act
        var result = await BuildHandler().Handle(
            new DownloadFromUrlRequest { FileUrl = "https://example.com/file", ContainerName = "exports" },
            CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal(ErrorCodes.FileDownloadFailed, result.ErrorCode);
        Assert.Equal("http-status", result.Params!["cause"]);
        Assert.Empty(uploads);
    }

    [Fact]
    public async Task Handle_Download_ForwardsCancellationTokenToGetAndUpload()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        var (_, _, uploads) = SetupDownload(_ => Ok("application/pdf"));

        // Act
        var result = await BuildHandler().Handle(
            new DownloadFromUrlRequest { FileUrl = "https://example.com/a.pdf", ContainerName = "docs" },
            cts.Token);

        // Assert — the token reaching UploadAsync is the one handed to the resilience delegate
        Assert.True(result.Success);
        Assert.Equal(cts.Token, Assert.Single(uploads).Token);
    }

    [Fact]
    public async Task Handle_Download_AlreadyCancelledToken_Propagates()
    {
        // Arrange
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        SetupDownload(_ => Ok());

        // Act & Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            BuildHandler().Handle(
                new DownloadFromUrlRequest { FileUrl = "https://example.com/file", ContainerName = "exports" },
                cts.Token));
    }

    [Fact]
    public async Task Handle_Download_UrlWithFilename_UsesBlobNameFromPath()
    {
        var (_, _, uploads) = SetupDownload(_ => Ok());

        await BuildHandler().Handle(
            new DownloadFromUrlRequest { FileUrl = "https://example.com/folder/report.pdf", ContainerName = "documents" },
            CancellationToken.None);

        Assert.Equal("report.pdf", Assert.Single(uploads).BlobName);
    }

    [Fact]
    public async Task Handle_Download_RequestedBlobName_TakesPrecedenceOverUrl()
    {
        var (_, _, uploads) = SetupDownload(_ => Ok());

        await BuildHandler().Handle(
            new DownloadFromUrlRequest
            {
                FileUrl = "https://example.com/folder/report.pdf",
                ContainerName = "documents",
                BlobName = "custom.pdf",
            },
            CancellationToken.None);

        Assert.Equal("custom.pdf", Assert.Single(uploads).BlobName);
    }

    [Fact]
    public async Task Handle_Download_UrlWithNoFilename_GeneratesNameWithContentTypeExtension()
    {
        var (_, _, uploads) = SetupDownload(_ => Ok("image/png"));

        await BuildHandler().Handle(
            new DownloadFromUrlRequest { FileUrl = "https://example.com/files/", ContainerName = "documents" },
            CancellationToken.None);

        var upload = Assert.Single(uploads);
        Assert.StartsWith("downloaded-file-", upload.BlobName);
        Assert.EndsWith(".png", upload.BlobName);
        Assert.Equal("image/png", upload.ContentType);
    }

    [Fact]
    public async Task Handle_Download_ResponseContentType_IsPassedToUpload()
    {
        var (_, _, uploads) = SetupDownload(_ => Ok("application/pdf"));

        await BuildHandler().Handle(
            new DownloadFromUrlRequest { FileUrl = "https://example.com/data.bin", ContainerName = "documents" },
            CancellationToken.None);

        Assert.Equal("application/pdf", Assert.Single(uploads).ContentType);
    }

    [Theory]
    [InlineData("file.jpg", "image/jpeg")]
    [InlineData("file.xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet")]
    [InlineData("PHOTO.JPG", "image/jpeg")]
    [InlineData("file.unknown", "application/octet-stream")]
    public async Task Handle_Download_NoResponseContentType_InfersContentTypeFromExtension(
        string blobName, string expectedContentType)
    {
        var (_, _, uploads) = SetupDownload(_ => Ok());

        await BuildHandler().Handle(
            new DownloadFromUrlRequest
            {
                FileUrl = "https://example.com/file",
                ContainerName = "documents",
                BlobName = blobName,
            },
            CancellationToken.None);

        Assert.Equal(expectedContentType, Assert.Single(uploads).ContentType);
    }

    [Fact]
    public async Task Handle_Download_Success_DoesNotLogUrlQuerySecret()
    {
        var logger = new CapturingLogger<DownloadFromUrlHandler>();
        SetupDownload(_ => Ok("application/pdf"));

        var result = await BuildHandler(logger).Handle(
            new DownloadFromUrlRequest { FileUrl = "https://example.com/file.pdf?token=secret", ContainerName = "documents" },
            CancellationToken.None);

        Assert.True(result.Success);
        Assert.NotEmpty(logger.Entries);
        Assert.DoesNotContain(logger.Entries, e => e.Contains("secret"));
    }

    [Fact]
    public async Task Handle_Download_Failure_DoesNotLogUrlQuerySecret()
    {
        var logger = new CapturingLogger<DownloadFromUrlHandler>();
        SetupDownload(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await BuildHandler(logger).Handle(
            new DownloadFromUrlRequest { FileUrl = "https://example.com/file.pdf?token=secret", ContainerName = "exports" },
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.NotEmpty(logger.Entries);
        Assert.DoesNotContain(logger.Entries, e => e.Contains("secret"));
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Entries.Add(formatter(state, exception));
            if (exception is not null)
            {
                Entries.Add(exception.ToString());
            }

            if (state is IEnumerable<KeyValuePair<string, object?>> kvps)
            {
                Entries.AddRange(kvps.Select(kv => kv.Value?.ToString() ?? string.Empty));
            }
        }
    }

    private sealed class DelegatingStubHandler(
        Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> respond) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(respond(request, cancellationToken));
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
