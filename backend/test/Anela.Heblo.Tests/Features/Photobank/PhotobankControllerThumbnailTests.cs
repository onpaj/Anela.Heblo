using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Domain.Features.Photobank;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Security.Claims;

namespace Anela.Heblo.Tests.Features.Photobank;

public sealed class PhotobankControllerThumbnailTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly Mock<IPhotobankRepository> _repositoryMock;
    private readonly Mock<IPhotobankGraphService> _graphServiceMock;
    private readonly PhotobankController _controller;

    public PhotobankControllerThumbnailTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _repositoryMock = new Mock<IPhotobankRepository>();
        _graphServiceMock = new Mock<IPhotobankGraphService>();

        _controller = new PhotobankController(
            _mediatorMock.Object,
            _repositoryMock.Object,
            _graphServiceMock.Object);

        SetupHttpContext();
    }

    private void SetupHttpContext()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.NameIdentifier, "test-user") })),
            RequestServices = services.BuildServiceProvider()
        };

        _controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
    }

    [Fact]
    public async Task GetThumbnail_ReturnsNotFound_WhenPhotoNotInDatabase()
    {
        // Arrange
        _repositoryMock
            .Setup(r => r.GetLocatorAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PhotoLocator?)null);

        // Act
        var result = await _controller.GetThumbnail(1, ThumbnailSize.Medium);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetThumbnail_ReturnsNotFound_WhenGraphReturnsNullThumbnail()
    {
        // Arrange
        var locator = new PhotoLocator("driveId", "fileId", DateTime.UtcNow);

        _repositoryMock
            .Setup(r => r.GetLocatorAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(locator);

        _graphServiceMock
            .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GraphThumbnail?)null);

        // Act
        var result = await _controller.GetThumbnail(1, ThumbnailSize.Medium);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetThumbnail_Returns503WithRetryAfter_WhenGraphThrottles()
    {
        // Arrange
        var locator = new PhotoLocator("driveId", "fileId", DateTime.UtcNow);

        _repositoryMock
            .Setup(r => r.GetLocatorAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(locator);

        _graphServiceMock
            .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GraphThrottledException(TimeSpan.FromSeconds(29.3)));

        // Act
        var result = await _controller.GetThumbnail(1, ThumbnailSize.Medium);

        // Assert
        var statusResult = result.Should().BeOfType<StatusCodeResult>().Subject;
        statusResult.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        _controller.Response.Headers["Retry-After"].ToString().Should().Be("30");
    }

    [Fact]
    public async Task GetThumbnail_Returns503WithoutRetryAfter_WhenThrottledWithNullRetryAfter()
    {
        // Arrange
        var locator = new PhotoLocator("driveId", "fileId", DateTime.UtcNow);

        _repositoryMock
            .Setup(r => r.GetLocatorAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(locator);

        _graphServiceMock
            .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new GraphThrottledException(null));

        // Act
        var result = await _controller.GetThumbnail(1, ThumbnailSize.Medium);

        // Assert
        var statusResult = result.Should().BeOfType<StatusCodeResult>().Subject;
        statusResult.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        _controller.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
    }

    [Fact]
    public async Task GetThumbnail_Returns502_WhenGraphThrowsHttpRequestException()
    {
        // Arrange
        var locator = new PhotoLocator("driveId", "fileId", DateTime.UtcNow);

        _repositoryMock
            .Setup(r => r.GetLocatorAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(locator);

        _graphServiceMock
            .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("upstream error"));

        // Act
        var result = await _controller.GetThumbnail(1, ThumbnailSize.Medium);

        // Assert
        var statusResult = result.Should().BeOfType<StatusCodeResult>().Subject;
        statusResult.StatusCode.Should().Be(StatusCodes.Status502BadGateway);
    }

    [Fact]
    public async Task GetThumbnail_Returns200WithCacheHeaders_WhenSuccessful()
    {
        // Arrange
        var locator = new PhotoLocator("driveId", "fileId", DateTime.UtcNow);
        var imageBytes = new byte[] { 1, 2, 3, 4, 5 };
        var stream = new MemoryStream(imageBytes);
        var thumbnail = new GraphThumbnail(stream, "image/jpeg", null);

        _repositoryMock
            .Setup(r => r.GetLocatorAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(locator);

        _graphServiceMock
            .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
            .ReturnsAsync(thumbnail);

        // Act
        var result = await _controller.GetThumbnail(1, ThumbnailSize.Medium);

        // Assert
        var fileResult = result.Should().BeOfType<FileStreamResult>().Subject;
        fileResult.ContentType.Should().Be("image/jpeg");
        fileResult.FileStream.Should().BeSameAs(stream);
        _controller.Response.Headers["Cache-Control"].ToString().Should().Be("public, max-age=31536000, immutable");
    }

    [Fact]
    public async Task GetThumbnail_ForwardsContentLength_WhenAvailable()
    {
        // Arrange
        var locator = new PhotoLocator("driveId", "fileId", DateTime.UtcNow);
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        var thumbnail = new GraphThumbnail(stream, "image/jpeg", 12345L);

        _repositoryMock
            .Setup(r => r.GetLocatorAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(locator);

        _graphServiceMock
            .Setup(g => g.GetThumbnailAsync(locator.DriveId, locator.SharePointFileId, ThumbnailSize.Medium, It.IsAny<CancellationToken>()))
            .ReturnsAsync(thumbnail);

        // Act
        await _controller.GetThumbnail(1, ThumbnailSize.Medium);

        // Assert
        result.Should().BeOfType<FileStreamResult>();
        _controller.Response.ContentLength.Should().Be(12345L);
    }
}
