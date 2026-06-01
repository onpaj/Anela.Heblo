using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Photobank.Services;
using Anela.Heblo.Application.Features.Photobank.UseCases.GetThumbnail;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using System.Security.Claims;
using Xunit;

namespace Anela.Heblo.Tests.Features.Photobank;

public sealed class PhotobankControllerThumbnailTests
{
    private readonly Mock<IMediator> _mediatorMock = new();
    private readonly PhotobankController _controller;

    public PhotobankControllerThumbnailTests()
    {
        _controller = new PhotobankController(_mediatorMock.Object);
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

    private void SetupResponse(GetThumbnailResponse response) =>
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetThumbnailRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

    [Fact]
    public async Task GetThumbnail_ReturnsNotFound_WhenResponseNotFound()
    {
        // Arrange
        SetupResponse(new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailNotFound));

        // Act
        var result = await _controller.GetThumbnail(1, ThumbnailSize.Medium);

        // Assert
        result.Should().BeOfType<NotFoundResult>();
    }

    [Fact]
    public async Task GetThumbnail_Returns503WithRetryAfter_WhenThrottledWithHint()
    {
        // Arrange
        SetupResponse(new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailThrottled) { RetryAfterSeconds = 30 });

        // Act
        var result = await _controller.GetThumbnail(1, ThumbnailSize.Medium);

        // Assert
        var statusResult = result.Should().BeOfType<StatusCodeResult>().Subject;
        statusResult.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        _controller.Response.Headers["Retry-After"].ToString().Should().Be("30");
    }

    [Fact]
    public async Task GetThumbnail_Returns503WithoutRetryAfter_WhenThrottledWithoutHint()
    {
        // Arrange
        SetupResponse(new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailThrottled) { RetryAfterSeconds = null });

        // Act
        var result = await _controller.GetThumbnail(1, ThumbnailSize.Medium);

        // Assert
        var statusResult = result.Should().BeOfType<StatusCodeResult>().Subject;
        statusResult.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        _controller.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
    }

    [Fact]
    public async Task GetThumbnail_Returns503_WhenAuthUnavailable()
    {
        // Arrange
        SetupResponse(new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailAuthUnavailable));

        // Act
        var result = await _controller.GetThumbnail(1, ThumbnailSize.Medium);

        // Assert
        var statusResult = result.Should().BeOfType<StatusCodeResult>().Subject;
        statusResult.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        _controller.Response.Headers.ContainsKey("Retry-After").Should().BeFalse();
    }

    [Fact]
    public async Task GetThumbnail_Returns502_WhenUpstreamError()
    {
        // Arrange
        SetupResponse(new GetThumbnailResponse(ErrorCodes.PhotobankThumbnailUpstream));

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
        var stream = new MemoryStream(new byte[] { 1, 2, 3, 4, 5 });
        SetupResponse(new GetThumbnailResponse { Content = stream, ContentType = "image/jpeg", ContentLength = null });

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
        var stream = new MemoryStream(new byte[] { 1, 2, 3 });
        SetupResponse(new GetThumbnailResponse { Content = stream, ContentType = "image/jpeg", ContentLength = 12345L });

        // Act
        var result = await _controller.GetThumbnail(1, ThumbnailSize.Medium);

        // Assert
        result.Should().BeOfType<FileStreamResult>();
        _controller.Response.ContentLength.Should().Be(12345L);
    }
}
