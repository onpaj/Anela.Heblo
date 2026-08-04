using System.Reflection;
using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.LabelIdentification.UseCases.IdentifyLabel;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class LabelIdentificationControllerTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly LabelIdentificationController _controller;

    public LabelIdentificationControllerTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _controller = new LabelIdentificationController(_mediatorMock.Object);

        var serviceCollection = new ServiceCollection();
        serviceCollection.AddLogging();
        var serviceProvider = serviceCollection.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider,
        };

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
        };
    }

    private static IFormFile CreateFormFile(byte[] bytes, string contentType)
    {
        var stream = new MemoryStream(bytes);
        return new FormFile(stream, 0, bytes.Length, "photo", "label.jpg")
        {
            Headers = new HeaderDictionary(),
            ContentType = contentType,
        };
    }

    [Fact]
    public async Task Identify_WithNullPhoto_ReturnsBadRequestWithMissingOrInvalidErrorCode()
    {
        // Act
        var result = await _controller.Identify(null, CancellationToken.None);

        // Assert
        var objectResult = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var response = objectResult.Value.Should().BeOfType<IdentifyLabelResponse>().Subject;
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.LabelPhotoMissingOrInvalid);
        _mediatorMock.Verify(m => m.Send(It.IsAny<IdentifyLabelRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Identify_WithEmptyPhoto_ReturnsBadRequestWithMissingOrInvalidErrorCode()
    {
        // Arrange
        var photo = CreateFormFile(Array.Empty<byte>(), "image/jpeg");

        // Act
        var result = await _controller.Identify(photo, CancellationToken.None);

        // Assert
        var objectResult = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var response = objectResult.Value.Should().BeOfType<IdentifyLabelResponse>().Subject;
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.LabelPhotoMissingOrInvalid);
        _mediatorMock.Verify(m => m.Send(It.IsAny<IdentifyLabelRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Identify_WithNonImageContentType_ReturnsBadRequestWithMissingOrInvalidErrorCode()
    {
        // Arrange
        var photo = CreateFormFile(new byte[] { 1, 2, 3 }, "application/pdf");

        // Act
        var result = await _controller.Identify(photo, CancellationToken.None);

        // Assert
        var objectResult = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var response = objectResult.Value.Should().BeOfType<IdentifyLabelResponse>().Subject;
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.LabelPhotoMissingOrInvalid);
        _mediatorMock.Verify(m => m.Send(It.IsAny<IdentifyLabelRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Identify_WithMissingContentType_ReturnsBadRequestWithMissingOrInvalidErrorCode()
    {
        // Arrange
        var photo = CreateFormFile(new byte[] { 1, 2, 3 }, contentType: "");

        // Act
        var result = await _controller.Identify(photo, CancellationToken.None);

        // Assert
        var objectResult = result.Result.Should().BeOfType<BadRequestObjectResult>().Subject;
        var response = objectResult.Value.Should().BeOfType<IdentifyLabelResponse>().Subject;
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.LabelPhotoMissingOrInvalid);
    }

    [Fact]
    public async Task Identify_WithValidPhoto_SendsRequestToMediatorAndReturnsOk()
    {
        // Arrange
        var bytes = new byte[] { 1, 2, 3, 4, 5 };
        var photo = CreateFormFile(bytes, "image/jpeg");
        var expectedResponse = new IdentifyLabelResponse
        {
            RawText = "Tocopherol, Limonene",
        };

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<IdentifyLabelRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        // Act
        var result = await _controller.Identify(photo, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        _mediatorMock.Verify(
            m => m.Send(
                It.Is<IdentifyLabelRequest>(r =>
                    r.ContentType == "image/jpeg" && r.SizeBytes == bytes.Length),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void Identify_HasRequestSizeLimitAttribute()
    {
        // Arrange
        var method = typeof(LabelIdentificationController).GetMethod(nameof(LabelIdentificationController.Identify));

        // Act
        var attribute = method!.GetCustomAttribute<RequestSizeLimitAttribute>();

        // Assert
        attribute.Should().NotBeNull();
    }
}
