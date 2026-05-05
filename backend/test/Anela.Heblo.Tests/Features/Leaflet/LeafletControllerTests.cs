using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Leaflet.UseCases.GenerateLeaflet;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Leaflet;

public class LeafletControllerTests
{
    private readonly Mock<IMediator> _mediatorMock = new();

    private LeafletController CreateController()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceProvider,
        };

        var controller = new LeafletController(_mediatorMock.Object);
        controller.ControllerContext = new ControllerContext
        {
            HttpContext = httpContext,
        };
        return controller;
    }

    [Fact]
    public async Task Generate_returns_200_with_response_on_success()
    {
        // Arrange
        var request = new GenerateLeafletRequest
        {
            Topic = "Vitamin C serum",
            Audience = AudienceType.EndConsumer,
            Length = LeafletLength.Short,
        };

        var expectedResponse = new GenerateLeafletResponse
        {
            Success = true,
            Content = "Vitamin C serum is great for your skin.",
        };

        _mediatorMock
            .Setup(m => m.Send(request, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var controller = CreateController();

        // Act
        var result = await controller.Generate(request, CancellationToken.None);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<GenerateLeafletResponse>(okResult.Value);
        Assert.Equal(expectedResponse.Content, response.Content);
    }

    [Fact]
    public async Task Generate_returns_422_on_EmptyRetrievalException()
    {
        // Arrange
        var request = new GenerateLeafletRequest
        {
            Topic = "Unknown topic",
            Audience = AudienceType.EndConsumer,
            Length = LeafletLength.Short,
        };

        var exceptionMessage = "No relevant documents found for the given topic.";

        _mediatorMock
            .Setup(m => m.Send(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new EmptyRetrievalException(exceptionMessage));

        var controller = CreateController();

        // Act
        var result = await controller.Generate(request, CancellationToken.None);

        // Assert
        var unprocessableResult = Assert.IsType<UnprocessableEntityObjectResult>(result);
        Assert.Equal(422, unprocessableResult.StatusCode);

        var problemDetails = Assert.IsType<ProblemDetails>(unprocessableResult.Value);
        Assert.Equal(exceptionMessage, problemDetails.Detail);
    }

    [Fact]
    public async Task Generate_returns_502_on_unexpected_exception()
    {
        // Arrange
        var request = new GenerateLeafletRequest
        {
            Topic = "Retinol cream",
            Audience = AudienceType.EndConsumer,
            Length = LeafletLength.Short,
        };

        var internalMessage = "Internal system failure with stack trace details";

        _mediatorMock
            .Setup(m => m.Send(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException(internalMessage));

        var controller = CreateController();

        // Act
        var result = await controller.Generate(request, CancellationToken.None);

        // Assert
        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(502, objectResult.StatusCode);

        var problemDetails = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.NotEqual(internalMessage, problemDetails.Detail);
        Assert.DoesNotContain(internalMessage, problemDetails.Detail ?? string.Empty);
    }

    [Fact]
    public async Task Generate_propagates_OperationCanceledException()
    {
        // Arrange
        var request = new GenerateLeafletRequest
        {
            Topic = "Hyaluronic acid",
            Audience = AudienceType.EndConsumer,
            Length = LeafletLength.Short,
        };

        _mediatorMock
            .Setup(m => m.Send(request, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new OperationCanceledException());

        var controller = CreateController();

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(
            () => controller.Generate(request, CancellationToken.None));
    }
}
