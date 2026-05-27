using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.ShipmentLabels.UseCases.CreateOrderShipment;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Controllers;

public class ShipmentLabelsControllerTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly ShipmentLabelsController _controller;

    public ShipmentLabelsControllerTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _controller = new ShipmentLabelsController(_mediatorMock.Object);

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

    [Fact]
    public async Task CreateShipment_WithValidRequest_ReturnsOk()
    {
        // Arrange
        var expectedResponse = new CreateOrderShipmentResponse(
            shipmentGuid: Guid.NewGuid(),
            status: "created",
            labelReady: true,
            labels: []);

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateOrderShipmentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var body = new CreateShipmentRequest
        {
            OrderCode = "0001234",
            ForceCreate = false,
        };

        // Act
        var result = await _controller.CreateShipment(body, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        _mediatorMock.Verify(
            m => m.Send(
                It.Is<CreateOrderShipmentRequest>(r =>
                    r.OrderCode == "0001234" && r.ForceCreate == false),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateShipment_WhenValidationErrorReturned_ReturnsBadRequest()
    {
        // Arrange
        var failedResponse = new CreateOrderShipmentResponse(ErrorCodes.ValidationError);

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateOrderShipmentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedResponse);

        var body = new CreateShipmentRequest
        {
            OrderCode = "",
            ForceCreate = false,
        };

        // Act
        var result = await _controller.CreateShipment(body, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task CreateShipment_WithForceCreate_PassesFlagToMediatorRequest()
    {
        // Arrange
        var expectedResponse = new CreateOrderShipmentResponse(
            shipmentGuid: Guid.NewGuid(),
            status: "created",
            labelReady: false,
            labels: []);

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateOrderShipmentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResponse);

        var body = new CreateShipmentRequest
        {
            OrderCode = "0009999",
            ForceCreate = true,
        };

        // Act
        var result = await _controller.CreateShipment(body, CancellationToken.None);

        // Assert
        result.Result.Should().BeOfType<OkObjectResult>();
        _mediatorMock.Verify(
            m => m.Send(
                It.Is<CreateOrderShipmentRequest>(r =>
                    r.OrderCode == "0009999" && r.ForceCreate == true),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateShipment_WhenShipmentAlreadyExists_Returns409Conflict()
    {
        // Arrange
        var failedResponse = new CreateOrderShipmentResponse(ErrorCodes.ShipmentAlreadyExists);

        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateOrderShipmentRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(failedResponse);

        var body = new CreateShipmentRequest
        {
            OrderCode = "0001234",
            ForceCreate = false,
        };

        // Act
        var result = await _controller.CreateShipment(body, CancellationToken.None);

        // Assert
        var objectResult = result.Result.Should().BeOfType<ObjectResult>().Subject;
        objectResult.StatusCode.Should().Be(409);
    }
}
