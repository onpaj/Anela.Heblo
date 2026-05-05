using System.Net;
using System.Text;
using Anela.Heblo.Application.Features.KnowledgeBase.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Web;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.KnowledgeBase.Services;

public class GraphOneDriveServiceTests
{
    private static GraphOneDriveService CreateService(HttpMessageHandler handler)
    {
        var tokenAcquisition = new Mock<ITokenAcquisition>();
        tokenAcquisition
            .Setup(t => t.GetAccessTokenForAppAsync(It.IsAny<string>(), null, null))
            .ReturnsAsync("fake-token");

        var httpClient = new HttpClient(handler);
        var factory = new Mock<IHttpClientFactory>();
        factory.Setup(f => f.CreateClient("MicrosoftGraph")).Returns(httpClient);

        var cache = new MemoryCache(new MemoryCacheOptions());

        return new GraphOneDriveService(
            tokenAcquisition.Object,
            factory.Object,
            cache,
            NullLogger<GraphOneDriveService>.Instance);
    }

    [Fact]
    public async Task MoveToArchivedAsync_ReturnsWebUrl_FromGraphPatchResponse()
    {
        // Arrange
        const string expectedWebUrl = "https://example.sharepoint.com/sites/anela/archived/doc.pdf";

        var folderJson = """{"id":"folder-id","name":"Archived","webUrl":"https://example.sharepoint.com/sites/anela/Archived"}""";
        var movedItemJson = $$$"""{"id":"item-id","name":"doc.pdf","webUrl":"{{{expectedWebUrl}}}"}""";

        var handler = new SequentialResponseHandler(
            // First call: GET folder item (path lookup) — the service does GET /root:/{path} to resolve folder id
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(folderJson, Encoding.UTF8, "application/json")
            },
            // Second call: PATCH drive item (the actual move)
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(movedItemJson, Encoding.UTF8, "application/json")
            });

        var service = CreateService(handler);

        // Act
        var result = await service.MoveToArchivedAsync(
            driveId: "drive-id",
            fileId: "file-id",
            filename: "doc.pdf",
            archivedPath: "Archived");

        // Assert
        Assert.Equal(expectedWebUrl, result);
    }

    [Fact]
    public async Task MoveToArchivedAsync_ThrowsInvalidOperationException_WhenWebUrlMissingFromPatchResponse()
    {
        // Arrange
        var folderJson = """{"id":"folder-id","name":"Archived","webUrl":"https://example.sharepoint.com/sites/anela/Archived"}""";
        var movedItemJson = """{"id":"item-id","name":"doc.pdf"}""";

        var handler = new SequentialResponseHandler(
            // First call: GET folder item (path lookup)
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(folderJson, Encoding.UTF8, "application/json")
            },
            // Second call: PATCH drive item — response is missing webUrl
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(movedItemJson, Encoding.UTF8, "application/json")
            });

        var service = CreateService(handler);

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.MoveToArchivedAsync(
                driveId: "drive-id",
                fileId: "file-id",
                filename: "doc.pdf",
                archivedPath: "Archived"));
    }

    [Fact]
    public async Task DownloadFileTextByPathAsync_ReturnsFileContent_AsString()
    {
        // Arrange
        const string expectedContent = "Hello, World!";

        var handler = new LambdaHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(expectedContent, Encoding.UTF8, "text/plain")
            });

        var service = CreateService(handler);

        // Act
        var result = await service.DownloadFileTextByPathAsync("drive-id", "Documents/style.md");

        // Assert
        Assert.Equal(expectedContent, result);
    }

    /// <summary>
    /// Feeds HTTP responses in sequence, one per call.
    /// </summary>
    private sealed class SequentialResponseHandler : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses;

        public SequentialResponseHandler(params HttpResponseMessage[] responses)
        {
            _responses = new Queue<HttpResponseMessage>(responses);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (!_responses.TryDequeue(out var response))
                throw new InvalidOperationException("No more queued HTTP responses.");

            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Delegates each HTTP call to a provided function.
    /// </summary>
    private sealed class LambdaHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public LambdaHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(_handler(request));
    }
}
