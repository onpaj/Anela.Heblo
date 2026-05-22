using System.Net;
using System.Text;
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
}
