using System.Net;
using System.Net.Http.Headers;
using Anela.Heblo.Application.Features.Photobank.Services;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Identity.Web;
using Moq;
using Moq.Protected;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public class PhotobankGraphServiceThumbnailTests
{
    private const string DriveId = "drive-abc";
    private const string FileId = "file-xyz";

    private static PhotobankGraphService CreateService(
        Mock<HttpMessageHandler> handlerMock,
        Mock<ITokenAcquisition> tokenMock)
    {
        tokenMock
            .Setup(t => t.GetAccessTokenForAppAsync(It.IsAny<string>(), null, null))
            .ReturnsAsync("test-token");

        var httpClient = new HttpClient(handlerMock.Object)
        {
            BaseAddress = new Uri("https://graph.microsoft.com/")
        };

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock
            .Setup(f => f.CreateClient(It.IsAny<string>()))
            .Returns(httpClient);

        return new PhotobankGraphService(
            tokenMock.Object,
            factoryMock.Object,
            NullLogger<PhotobankGraphService>.Instance);
    }

    [Fact]
    public async Task GetThumbnailAsync_ReturnsGraphThumbnail_WhenGraphReturns200()
    {
        // Arrange
        var imageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0 }; // minimal JPEG header bytes
        var handlerMock = new Mock<HttpMessageHandler>();
        var tokenMock = new Mock<ITokenAcquisition>();

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(imageBytes)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        response.Content.Headers.ContentLength = imageBytes.Length;

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response);

        var service = CreateService(handlerMock, tokenMock);

        // Act
        var result = await service.GetThumbnailAsync(DriveId, FileId, ThumbnailSize.Medium);

        // Assert
        result.Should().NotBeNull();
        result!.ContentType.Should().Be("image/jpeg");
        result.ContentLength.Should().Be(imageBytes.Length);
        result.Content.Should().NotBeNull();
    }

    [Fact]
    public async Task GetThumbnailAsync_BuildsCorrectUrl_ForMediumSize()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        var tokenMock = new Mock<ITokenAcquisition>();

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([])
            });

        var service = CreateService(handlerMock, tokenMock);

        // Act
        await service.GetThumbnailAsync(DriveId, FileId, ThumbnailSize.Medium);

        // Assert
        capturedRequest.Should().NotBeNull();
        capturedRequest!.RequestUri!.ToString().Should().Contain("thumbnails/0/medium/content");
    }

    [Fact]
    public async Task GetThumbnailAsync_BuildsCorrectUrl_ForLargeSize()
    {
        // Arrange
        HttpRequestMessage? capturedRequest = null;
        var handlerMock = new Mock<HttpMessageHandler>();
        var tokenMock = new Mock<ITokenAcquisition>();

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Callback<HttpRequestMessage, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([])
            });

        var service = CreateService(handlerMock, tokenMock);

        // Act
        await service.GetThumbnailAsync(DriveId, FileId, ThumbnailSize.Large);

        // Assert
        capturedRequest!.RequestUri!.ToString().Should().Contain("thumbnails/0/large/content");
    }

    [Fact]
    public async Task GetThumbnailAsync_ReturnsNull_WhenGraphReturns404()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        var tokenMock = new Mock<ITokenAcquisition>();

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.NotFound));

        var service = CreateService(handlerMock, tokenMock);

        // Act
        var result = await service.GetThumbnailAsync(DriveId, FileId, ThumbnailSize.Medium);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetThumbnailAsync_ThrowsGraphThrottledException_WhenGraphReturns429()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        var tokenMock = new Mock<ITokenAcquisition>();

        var response429 = new HttpResponseMessage((HttpStatusCode)429);
        response429.Headers.Add("Retry-After", "30");

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(response429);

        var service = CreateService(handlerMock, tokenMock);

        // Act
        var act = async () => await service.GetThumbnailAsync(DriveId, FileId, ThumbnailSize.Medium);

        // Assert
        var ex = await act.Should().ThrowAsync<GraphThrottledException>();
        ex.Which.RetryAfter.Should().Be(TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task GetThumbnailAsync_ThrowsGraphThrottledException_WhenGraphReturns429_WithNoRetryAfterHeader()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        var tokenMock = new Mock<ITokenAcquisition>();

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage((HttpStatusCode)429));

        var service = CreateService(handlerMock, tokenMock);

        // Act
        var act = async () => await service.GetThumbnailAsync(DriveId, FileId, ThumbnailSize.Medium);

        // Assert
        var ex = await act.Should().ThrowAsync<GraphThrottledException>();
        ex.Which.RetryAfter.Should().BeNull();
    }

    [Fact]
    public async Task GetThumbnailAsync_ThrowsHttpRequestException_WhenGraphReturns500()
    {
        // Arrange
        var handlerMock = new Mock<HttpMessageHandler>();
        var tokenMock = new Mock<ITokenAcquisition>();

        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.InternalServerError));

        var service = CreateService(handlerMock, tokenMock);

        // Act
        var act = async () => await service.GetThumbnailAsync(DriveId, FileId, ThumbnailSize.Medium);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }
}
