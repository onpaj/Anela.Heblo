using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Logistics.UseCases;
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Application.Shared;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Logistics.Transport;

public class TransportBoxControllerTests
{
    private readonly Mock<IMediator> _mediatorMock;
    private readonly TransportBoxController _controller;

    public TransportBoxControllerTests()
    {
        _mediatorMock = new Mock<IMediator>();
        _controller = new TransportBoxController(_mediatorMock.Object);

        var services = new ServiceCollection();
        services.AddLogging();
        var serviceProvider = services.BuildServiceProvider();

        _controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                RequestServices = serviceProvider
            }
        };
    }

    [Fact]
    public async Task ChangeTransportBoxState_Success_Returns200()
    {
        // Arrange
        var response = new ChangeTransportBoxStateResponse { Success = true };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ChangeTransportBoxStateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var request = new ChangeTransportBoxStateRequest();

        // Act
        var result = await _controller.ChangeTransportBoxState(1, request, CancellationToken.None);

        // Assert
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task ChangeTransportBoxState_BoxNotFound_Returns404()
    {
        // Arrange
        var response = new ChangeTransportBoxStateResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.TransportBoxNotFound
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ChangeTransportBoxStateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var request = new ChangeTransportBoxStateRequest();

        // Act
        var result = await _controller.ChangeTransportBoxState(999, request, CancellationToken.None);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task ChangeTransportBoxState_InvalidStateTransition_Returns422()
    {
        // Arrange
        var response = new ChangeTransportBoxStateResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.TransportBoxStateChangeError
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ChangeTransportBoxStateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var request = new ChangeTransportBoxStateRequest();

        // Act
        var result = await _controller.ChangeTransportBoxState(1, request, CancellationToken.None);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(422, statusResult.StatusCode);
    }

    [Fact]
    public async Task ChangeTransportBoxState_DuplicateActiveBox_Returns409()
    {
        // Arrange
        var response = new ChangeTransportBoxStateResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.TransportBoxDuplicateActiveBoxFound
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ChangeTransportBoxStateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var request = new ChangeTransportBoxStateRequest();

        // Act
        var result = await _controller.ChangeTransportBoxState(1, request, CancellationToken.None);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(409, statusResult.StatusCode);
    }

    [Fact]
    public async Task ChangeTransportBoxState_RequiredFieldMissing_Returns400()
    {
        // Arrange
        var response = new ChangeTransportBoxStateResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.RequiredFieldMissing
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<ChangeTransportBoxStateRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        var request = new ChangeTransportBoxStateRequest();

        // Act
        var result = await _controller.ChangeTransportBoxState(1, request, CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }
}
