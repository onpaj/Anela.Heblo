using System.Net;
using System.Text;
using Anela.Heblo.Application.Features.CatalogDocuments.Contracts;
using Anela.Heblo.Application.Features.CatalogDocuments.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Client;
using Microsoft.Identity.Web;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Application.CatalogDocuments;

public sealed class GraphCatalogDocumentsStorageTests
{
    private const string AppToken = "app-token";
    private const string DelegatedToken = "delegated-token";

    private static (GraphCatalogDocumentsStorage Storage, Mock<ITokenAcquisition> TokenAcquisition, RecordingHandler Handler)
        CreateStorage(Func<HttpRequestMessage, HttpResponseMessage> responder)
    {
        var tokenAcquisition = new Mock<ITokenAcquisition>();
        tokenAcquisition
            .Setup(t => t.GetAccessTokenForAppAsync(It.IsAny<string>(), null, null))
            .ReturnsAsync(AppToken);
        tokenAcquisition
            .Setup(t => t.GetAccessTokenForUserAsync(
                It.IsAny<IEnumerable<string>>(), null, null, null, null))
            .ReturnsAsync(DelegatedToken);

        var handler = new RecordingHandler(responder);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("MicrosoftGraph")).Returns(new HttpClient(handler));

        var storage = new GraphCatalogDocumentsStorage(
            tokenAcquisition.Object,
            factory.Object,
            NullLogger<GraphCatalogDocumentsStorage>.Instance);

        return (storage, tokenAcquisition, handler);
    }

    // ─── UploadFileAsync — delegated token ───────────────────────────────────

    [Fact]
    public async Task UploadFileAsync_UsesUserDelegatedToken_NotAppToken()
    {
        // Arrange
        var (storage, tokenAcquisition, handler) = CreateStorage(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"item-1","name":"test.pdf"}""",
                    Encoding.UTF8, "application/json")
            });

        // Act
        using var stream = new MemoryStream(new byte[100]);
        await storage.UploadFileAsync("drive-1", "folder-1", "test.pdf", stream, "application/pdf", 100);

        // Assert — token in Authorization header must be the delegated one
        handler.Requests.Should().NotBeEmpty();
        handler.Requests[0].Headers.Authorization!.Scheme.Should().Be("Bearer");
        handler.Requests[0].Headers.Authorization!.Parameter.Should().Be(DelegatedToken);

        tokenAcquisition.Verify(
            t => t.GetAccessTokenForUserAsync(
                It.IsAny<IEnumerable<string>>(), null, null, null, null),
            Times.Once,
            "upload must acquire a delegated token");
        tokenAcquisition.Verify(
            t => t.GetAccessTokenForAppAsync(It.IsAny<string>(), null, null),
            Times.Never,
            "upload must not fall back to the app token");
    }

    [Fact]
    public async Task FindFolderAsync_UsesAppToken_NotDelegatedToken()
    {
        // Arrange
        var (storage, tokenAcquisition, handler) = CreateStorage(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"value":[{"id":"f1","name":"MAT001__TDS","folder":{"childCount":0}}]}""",
                    Encoding.UTF8, "application/json")
            });

        // Act
        await storage.FindFolderAsync("drive-1", "/Materials", "MAT001__", false);

        // Assert
        handler.Requests[0].Headers.Authorization!.Parameter.Should().Be(AppToken);
        tokenAcquisition.Verify(
            t => t.GetAccessTokenForAppAsync(It.IsAny<string>(), null, null),
            Times.Once);
        tokenAcquisition.Verify(
            t => t.GetAccessTokenForUserAsync(
                It.IsAny<IEnumerable<string>>(), null, null, null, null),
            Times.Never);
    }

    [Fact]
    public async Task UploadFileAsync_WhenConsentMissing_ThrowsInvalidOperationException()
    {
        // Arrange
        var tokenAcquisition = new Mock<ITokenAcquisition>();
        tokenAcquisition
            .Setup(t => t.GetAccessTokenForUserAsync(
                It.IsAny<IEnumerable<string>>(), null, null, null, null))
            .ThrowsAsync(new MsalUiRequiredException("invalid_grant", "AADSTS65001: consent required"));

        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("MicrosoftGraph"))
            .Returns(new HttpClient(new RecordingHandler(_ =>
                throw new InvalidOperationException("Graph must not be called when token acquisition failed"))));

        var storage = new GraphCatalogDocumentsStorage(
            tokenAcquisition.Object,
            factory.Object,
            NullLogger<GraphCatalogDocumentsStorage>.Instance);

        // Act
        using var stream = new MemoryStream(new byte[100]);
        var act = () => storage.UploadFileAsync("drive-1", "folder-1", "test.pdf", stream, "application/pdf", 100);

        // Assert
        var ex = await act.Should()
            .ThrowAsync<InvalidOperationException>()
            .WithMessage("*Microsoft 365 consent required*");
        ex.And.InnerException.Should().BeOfType<MsalUiRequiredException>();
    }

    // ─── UploadFileAsync — size routing ──────────────────────────────────────

    [Fact]
    public async Task UploadFileAsync_SizeEqualsThreshold_UsesSmallFilePath()
    {
        // Arrange — 4 MB exactly; UploadFileAsync uses `<=` so this must take the small-file path
        const long threshold = 4 * 1024 * 1024;
        var (storage, _, handler) = CreateStorage(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"item-1","name":"test.pdf"}""",
                    Encoding.UTF8, "application/json")
            });

        using var stream = new MemoryStream(new byte[threshold]);

        // Act
        await storage.UploadFileAsync("drive-1", "folder-1", "test.pdf", stream, "application/pdf", threshold);

        // Assert — exactly one PUT to the .../content endpoint, no createUploadSession call
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].Method.Should().Be(HttpMethod.Put);
        handler.Requests[0].RequestUri!.ToString().Should().Contain("/content?@microsoft.graph.conflictBehavior=rename");
    }

    [Fact]
    public async Task UploadFileAsync_SizeOneBelowThreshold_UsesSmallFilePath()
    {
        // Arrange
        const long threshold = 4 * 1024 * 1024;
        var (storage, _, handler) = CreateStorage(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"item-1","name":"test.pdf"}""",
                    Encoding.UTF8, "application/json")
            });

        using var stream = new MemoryStream(new byte[threshold - 1]);

        // Act
        await storage.UploadFileAsync("drive-1", "folder-1", "test.pdf", stream, "application/pdf", threshold - 1);

        // Assert
        handler.Requests.Should().HaveCount(1);
        handler.Requests[0].Method.Should().Be(HttpMethod.Put);
        handler.Requests[0].RequestUri!.ToString().Should().Contain("/content?@microsoft.graph.conflictBehavior=rename");
    }

    [Fact]
    public async Task UploadFileAsync_SizeOneAboveThreshold_UsesLargeFileSessionPath()
    {
        // Arrange
        const long threshold = 4 * 1024 * 1024;
        const long size = threshold + 1;
        var (storage, _, handler) = CreateStorage(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"uploadUrl":"https://graph.microsoft.com/upload-session/abc"}""",
                        Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"item-1","name":"final.pdf"}""",
                    Encoding.UTF8, "application/json")
            };
        });

        using var stream = new MemoryStream(new byte[size]);

        // Act
        await storage.UploadFileAsync("drive-1", "folder-1", "test.pdf", stream, "application/pdf", size);

        // Assert — createUploadSession POST, then chunk PUT(s) to the session's uploadUrl
        handler.Requests.Should().HaveCountGreaterOrEqualTo(2);
        handler.Requests[0].Method.Should().Be(HttpMethod.Post);
        handler.Requests[0].RequestUri!.ToString().Should().Contain("/createUploadSession");
        handler.Requests.Skip(1).Should().OnlyContain(r =>
            r.Method == HttpMethod.Put &&
            r.RequestUri!.ToString() == "https://graph.microsoft.com/upload-session/abc");
    }

    // ─── UploadLargeFileAsync — chunk loop ───────────────────────────────────

    [Fact]
    public async Task UploadFileAsync_LargeFileWithPartialFinalChunk_SendsTwoChunksWithCorrectContentRange()
    {
        // Arrange — 12 MB: chunk 1 = full 10 MB, chunk 2 = remaining 2 MB
        const long chunkSize = 10 * 1024 * 1024;
        const long size = chunkSize + 2 * 1024 * 1024;

        var (storage, _, handler) = CreateStorage(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"uploadUrl":"https://graph.microsoft.com/upload-session/abc"}""",
                        Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"item-1","name":"final.pdf"}""",
                    Encoding.UTF8, "application/json")
            };
        });

        using var stream = new MemoryStream(new byte[size]);

        // Act
        await storage.UploadFileAsync("drive-1", "folder-1", "test.pdf", stream, "application/pdf", size);

        // Assert
        var chunkRequests = handler.Requests.Where(r => r.Method == HttpMethod.Put).ToList();
        chunkRequests.Should().HaveCount(2);

        var range1 = chunkRequests[0].Content!.Headers.ContentRange!;
        range1.From.Should().Be(0);
        range1.To.Should().Be(chunkSize - 1);
        range1.Length.Should().Be(size);

        var range2 = chunkRequests[1].Content!.Headers.ContentRange!;
        range2.From.Should().Be(chunkSize);
        range2.To.Should().Be(size - 1);
        range2.Length.Should().Be(size);
    }

    [Fact]
    public async Task UploadFileAsync_LargeFileWithThrottledStream_AssemblesFullChunkFromShortReads()
    {
        // Arrange — exactly one chunk (10 MB), but the stream only yields 64 KB per ReadAsync call,
        // forcing UploadLargeFileAsync's inner fill loop to run many iterations before sending the PUT.
        const long chunkSize = 10 * 1024 * 1024;
        const int maxBytesPerRead = 64 * 1024;

        var (storage, _, handler) = CreateStorage(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"uploadUrl":"https://graph.microsoft.com/upload-session/abc"}""",
                        Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"item-1","name":"final.pdf"}""",
                    Encoding.UTF8, "application/json")
            };
        });

        using var stream = new ThrottledReadStream(chunkSize, maxBytesPerRead);

        // Act
        await storage.UploadFileAsync("drive-1", "folder-1", "test.pdf", stream, "application/pdf", chunkSize);

        // Assert — exactly one chunk PUT, whose Content-Range reflects the FULL chunk length,
        // not the length of a single short read
        var chunkRequests = handler.Requests.Where(r => r.Method == HttpMethod.Put).ToList();
        chunkRequests.Should().HaveCount(1);

        var range = chunkRequests[0].Content!.Headers.ContentRange!;
        range.From.Should().Be(0);
        range.To.Should().Be(chunkSize - 1);
        range.Length.Should().Be(chunkSize);
    }

    [Fact]
    public async Task UploadFileAsync_LargeFileExactMultipleOfChunkSize_SendsExactlyTwoChunksNoTrailingRequest()
    {
        // Arrange — 20 MB = exactly two 10 MB chunks; outer `while (offset < sizeBytes)` must
        // stop after the second chunk with no trailing empty/zero-length request.
        const long chunkSize = 10 * 1024 * 1024;
        const long size = 2 * chunkSize;

        var responses = new Queue<string>(new[]
        {
            "chunk-1-name",
            "final-name.pdf"
        });

        var (storage, _, handler) = CreateStorage(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"uploadUrl":"https://graph.microsoft.com/upload-session/abc"}""",
                        Encoding.UTF8, "application/json")
                };
            }
            var name = responses.Dequeue();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""{"id":"item-1","name":"{{name}}"}""",
                    Encoding.UTF8, "application/json")
            };
        });

        using var stream = new MemoryStream(new byte[size]);

        // Act
        var result = await storage.UploadFileAsync("drive-1", "folder-1", "test.pdf", stream, "application/pdf", size);

        // Assert — exactly two chunk PUTs, no trailing third request
        var chunkRequests = handler.Requests.Where(r => r.Method == HttpMethod.Put).ToList();
        chunkRequests.Should().HaveCount(2);

        // Returned filename comes from the LAST chunk response, not the first
        result.Should().Be("final-name.pdf");

        // No Authorization header on chunk PUTs (bypass GraphApiHelpers.CreateRequest)
        chunkRequests.Should().OnlyContain(r => r.Headers.Authorization == null);
    }

    [Fact]
    public async Task UploadFileAsync_StreamExhaustedBeforeDeclaredSize_StopsEarlyWithoutThrowing()
    {
        // Documents existing (pre-existing, out-of-scope-to-fix) behavior: UploadLargeFileAsync's
        // outer loop exits as soon as the stream returns 0 bytes, even if offset < sizeBytes.
        const long chunkSize = 10 * 1024 * 1024;
        const long declaredSize = 3 * chunkSize; // 30 MB declared
        const long actualStreamBytes = chunkSize; // but stream only has 10 MB

        var (storage, _, handler) = CreateStorage(request =>
        {
            if (request.Method == HttpMethod.Post)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"uploadUrl":"https://graph.microsoft.com/upload-session/abc"}""",
                        Encoding.UTF8, "application/json")
                };
            }
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"id":"item-1","name":"final.pdf"}""",
                    Encoding.UTF8, "application/json")
            };
        });

        using var stream = new MemoryStream(new byte[actualStreamBytes]);

        // Act
        var act = () => storage.UploadFileAsync("drive-1", "folder-1", "test.pdf", stream, "application/pdf", declaredSize);

        // Assert — no exception; loop stops after the one chunk the stream actually had
        await act.Should().NotThrowAsync();
        handler.Requests.Count(r => r.Method == HttpMethod.Put).Should().Be(1);
    }

    // ─── FindFolderAsync — pagination & matching ─────────────────────────────

    [Fact]
    public async Task FindFolderAsync_NoMatchingItems_ReturnsNotFound()
    {
        var (storage, _, _) = CreateStorage(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"value":[]}""",
                    Encoding.UTF8, "application/json")
            });

        var result = await storage.FindFolderAsync("drive-1", "/Materials", "MAT001__", false);

        result.Status.Should().Be(FolderStatus.NotFound);
    }

    [Fact]
    public async Task FindFolderAsync_ExactlyOneMatch_ReturnsFoundWithMatchedFolder()
    {
        var (storage, _, _) = CreateStorage(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """{"value":[{"id":"folder-id-1","name":"PIF-2024","folder":{"childCount":0}}]}""",
                    Encoding.UTF8, "application/json")
            });

        var result = await storage.FindFolderAsync("drive-1", "/Materials", "PIF-2024", false);

        result.Status.Should().Be(FolderStatus.Found);
        result.FolderId.Should().Be("folder-id-1");
        result.FolderName.Should().Be("PIF-2024");
    }

    [Fact]
    public async Task FindFolderAsync_ExcludesNonFolderItemsMatchingPrefix()
    {
        var (storage, _, _) = CreateStorage(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "value": [
                        { "id": "file-id-1", "name": "PIF-2024-notes.txt", "folder": null, "file": { "mimeType": "text/plain" } }
                      ]
                    }
                    """,
                    Encoding.UTF8, "application/json")
            });

        var result = await storage.FindFolderAsync("drive-1", "/Materials", "PIF-2024", false);

        result.Status.Should().Be(FolderStatus.NotFound);
    }

    [Fact]
    public async Task FindFolderAsync_MultipleMatches_AllowMultipleFalse_ReturnsMultipleMatchesWithEmptyFolder()
    {
        var (storage, _, _) = CreateStorage(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "value": [
                        { "id": "folder-id-2", "name": "PIF-2024-B", "folder": {"childCount":0} },
                        { "id": "folder-id-1", "name": "PIF-2024-A", "folder": {"childCount":0} }
                      ]
                    }
                    """,
                    Encoding.UTF8, "application/json")
            });

        var result = await storage.FindFolderAsync("drive-1", "/Materials", "PIF-2024", false);

        result.Status.Should().Be(FolderStatus.MultipleMatches);
        result.FolderId.Should().BeEmpty();
        result.FolderName.Should().BeEmpty();
    }

    [Fact]
    public async Task FindFolderAsync_MultipleMatches_AllowMultipleTrue_ReturnsAlphabeticallyFirstMatch()
    {
        // Items deliberately in non-alphabetical order in the response
        var (storage, _, _) = CreateStorage(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    """
                    {
                      "value": [
                        { "id": "folder-id-2", "name": "PIF-2024-B", "folder": {"childCount":0} },
                        { "id": "folder-id-1", "name": "PIF-2024-A", "folder": {"childCount":0} }
                      ]
                    }
                    """,
                    Encoding.UTF8, "application/json")
            });

        var result = await storage.FindFolderAsync("drive-1", "/Materials", "PIF-2024", true);

        result.Status.Should().Be(FolderStatus.Found);
        result.FolderId.Should().Be("folder-id-1");
        result.FolderName.Should().Be("PIF-2024-A");
    }

    [Fact]
    public async Task FindFolderAsync_MultiPagePagination_ConsidersMatchesFromBothPages()
    {
        const string nextLinkUrl = "https://graph.microsoft.com/v1.0/next-page-2";

        var (storage, _, handler) = CreateStorage(request =>
        {
            if (request.RequestUri!.ToString() == nextLinkUrl)
            {
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"value":[{"id":"folder-id-page2","name":"PIF-2024-Page2","folder":{"childCount":0}}]}""",
                        Encoding.UTF8, "application/json")
                };
            }

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    $$"""
                    {
                      "value": [
                        { "id": "file-id-1", "name": "PIF-2024-notes.txt", "folder": null, "file": {"mimeType":"text/plain"} }
                      ],
                      "@odata.nextLink": "{{nextLinkUrl}}"
                    }
                    """,
                    Encoding.UTF8, "application/json")
            };
        });

        var result = await storage.FindFolderAsync("drive-1", "/Materials", "PIF-2024", false);

        // Only page 2 has a matching folder item; page 1's item is a file (excluded), not a folder.
        result.Status.Should().Be(FolderStatus.Found);
        result.FolderId.Should().Be("folder-id-page2");
        handler.Requests.Should().Contain(r => r.RequestUri!.ToString() == nextLinkUrl);
    }

    [Fact]
    public async Task FindFolderAsync_FirstPage404_ReturnsNotFoundWithoutPagination()
    {
        var (storage, _, handler) = CreateStorage(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var result = await storage.FindFolderAsync("drive-1", "/Materials", "PIF-2024", false);

        result.Status.Should().Be(FolderStatus.NotFound);
        handler.Requests.Should().HaveCount(1);
    }

    // ─── Recording infrastructure ─────────────────────────────────────────────

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _responder;
        public List<HttpRequestMessage> Requests { get; } = new();

        public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> responder)
            => _responder = responder;

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_responder(request));
        }
    }

    /// Wraps an in-memory byte source but returns at most <see cref="_maxBytesPerRead"/>
    /// bytes per ReadAsync call, forcing callers with a larger buffer to loop.
    private sealed class ThrottledReadStream : Stream
    {
        private readonly byte[] _data;
        private readonly int _maxBytesPerRead;
        private int _position;

        public ThrottledReadStream(long totalBytes, int maxBytesPerRead)
        {
            _data = new byte[totalBytes]; // zero-filled content; tests assert on request shape, not bytes
            _maxBytesPerRead = maxBytesPerRead;
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            var remaining = _data.Length - _position;
            var toCopy = Math.Min(Math.Min(count, _maxBytesPerRead), remaining);
            if (toCopy > 0)
                Array.Copy(_data, _position, buffer, offset, toCopy);
            _position += toCopy;
            return Task.FromResult(toCopy);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _data.Length;
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
