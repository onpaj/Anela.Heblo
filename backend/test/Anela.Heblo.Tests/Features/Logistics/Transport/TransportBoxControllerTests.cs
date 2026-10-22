using Anela.Heblo.API.Controllers;
using Anela.Heblo.Application.Features.Logistics.UseCases.AddItemToBox;
using Anela.Heblo.Application.Features.Logistics.UseCases.ChangeTransportBoxState;
using Anela.Heblo.Application.Features.Logistics.UseCases.CreateNewTransportBox;
using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxByCode;
using Anela.Heblo.Application.Features.Logistics.UseCases.GetTransportBoxById;
using Anela.Heblo.Application.Features.Logistics.UseCases.RemoveItemFromBox;
using Anela.Heblo.Application.Features.Logistics.UseCases.UpdateTransportBoxDescription;
using Anela.Heblo.Application.Features.Logistics.UseCases.OpenOrResumeBoxByCode;
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

    [Fact]
    public async Task OpenOrResumeBoxByCode_Success_Returns200()
    {
        // Arrange
        var response = new OpenOrResumeBoxByCodeResponse { Success = true, Resumed = false };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<OpenOrResumeBoxByCodeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.OpenOrResumeBoxByCode(
            new OpenOrResumeBoxByCodeRequest { BoxCode = "B001" }, CancellationToken.None);

        // Assert
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task OpenOrResumeBoxByCode_DuplicateActiveBox_Returns409()
    {
        // Arrange
        var response = new OpenOrResumeBoxByCodeResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.TransportBoxDuplicateActiveBoxFound
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<OpenOrResumeBoxByCodeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.OpenOrResumeBoxByCode(
            new OpenOrResumeBoxByCodeRequest { BoxCode = "B001" }, CancellationToken.None);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(409, statusResult.StatusCode);
    }

    [Fact]
    public async Task CreateNewTransportBox_Success_ReturnsOk()
    {
        // Arrange
        var response = new CreateNewTransportBoxResponse
        {
            Success = true
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateNewTransportBoxRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.CreateNewTransportBox(new CreateNewTransportBoxRequest(), CancellationToken.None);

        // Assert
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateNewTransportBox_BoxNotFound_Returns404()
    {
        // Arrange
        var response = new CreateNewTransportBoxResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.TransportBoxNotFound
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateNewTransportBoxRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.CreateNewTransportBox(new CreateNewTransportBoxRequest(), CancellationToken.None);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateNewTransportBox_InvalidStateTransition_Returns422()
    {
        // Arrange
        var response = new CreateNewTransportBoxResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.TransportBoxInvalidStateTransition
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateNewTransportBoxRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.CreateNewTransportBox(new CreateNewTransportBoxRequest(), CancellationToken.None);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(422, statusResult.StatusCode);
    }

    [Fact]
    public async Task CreateNewTransportBox_ValidationError_Returns400()
    {
        // Arrange
        var response = new CreateNewTransportBoxResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.ValidationError
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateNewTransportBoxRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.CreateNewTransportBox(new CreateNewTransportBoxRequest(), CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task CreateNewTransportBox_FailureWithoutErrorCode_Returns400()
    {
        // Arrange
        var response = new CreateNewTransportBoxResponse
        {
            Success = false
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<CreateNewTransportBoxRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.CreateNewTransportBox(new CreateNewTransportBoxRequest(), CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task AddItemToBox_Success_ReturnsOk()
    {
        // Arrange
        var response = new AddItemToBoxResponse
        {
            Success = true
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<AddItemToBoxRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.AddItemToBox(1, new AddItemToBoxRequest(), CancellationToken.None);

        // Assert
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task AddItemToBox_BoxNotFound_Returns404()
    {
        // Arrange
        var response = new AddItemToBoxResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.TransportBoxNotFound
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<AddItemToBoxRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.AddItemToBox(1, new AddItemToBoxRequest(), CancellationToken.None);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task AddItemToBox_InvalidStateTransition_Returns422()
    {
        // Arrange
        var response = new AddItemToBoxResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.TransportBoxInvalidStateTransition
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<AddItemToBoxRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.AddItemToBox(1, new AddItemToBoxRequest(), CancellationToken.None);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(422, statusResult.StatusCode);
    }

    [Fact]
    public async Task AddItemToBox_ValidationError_Returns400()
    {
        // Arrange
        var response = new AddItemToBoxResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.ValidationError
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<AddItemToBoxRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.AddItemToBox(1, new AddItemToBoxRequest(), CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task AddItemToBox_FailureWithoutErrorCode_Returns400()
    {
        // Arrange
        var response = new AddItemToBoxResponse
        {
            Success = false
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<AddItemToBoxRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.AddItemToBox(1, new AddItemToBoxRequest(), CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task RemoveItemFromBox_Success_ReturnsOk()
    {
        // Arrange
        var response = new RemoveItemFromBoxResponse
        {
            Success = true
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RemoveItemFromBoxRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.RemoveItemFromBox(1, 2, null, CancellationToken.None);

        // Assert
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task RemoveItemFromBox_BoxNotFound_Returns404()
    {
        // Arrange
        var response = new RemoveItemFromBoxResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.TransportBoxNotFound
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RemoveItemFromBoxRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.RemoveItemFromBox(1, 2, null, CancellationToken.None);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task RemoveItemFromBox_InvalidStateTransition_Returns422()
    {
        // Arrange
        var response = new RemoveItemFromBoxResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.TransportBoxInvalidStateTransition
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RemoveItemFromBoxRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.RemoveItemFromBox(1, 2, null, CancellationToken.None);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(422, statusResult.StatusCode);
    }

    [Fact]
    public async Task RemoveItemFromBox_ValidationError_Returns400()
    {
        // Arrange
        var response = new RemoveItemFromBoxResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.ValidationError
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RemoveItemFromBoxRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.RemoveItemFromBox(1, 2, null, CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task RemoveItemFromBox_FailureWithoutErrorCode_Returns400()
    {
        // Arrange
        var response = new RemoveItemFromBoxResponse
        {
            Success = false
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<RemoveItemFromBoxRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.RemoveItemFromBox(1, 2, null, CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateTransportBoxDescription_Success_ReturnsOk()
    {
        // Arrange
        var response = new UpdateTransportBoxDescriptionResponse
        {
            Success = true
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UpdateTransportBoxDescriptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.UpdateTransportBoxDescription(1, new UpdateTransportBoxDescriptionRequest(), CancellationToken.None);

        // Assert
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateTransportBoxDescription_BoxNotFound_Returns404()
    {
        // Arrange
        var response = new UpdateTransportBoxDescriptionResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.TransportBoxNotFound
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UpdateTransportBoxDescriptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.UpdateTransportBoxDescription(1, new UpdateTransportBoxDescriptionRequest(), CancellationToken.None);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateTransportBoxDescription_InvalidStateTransition_Returns422()
    {
        // Arrange
        var response = new UpdateTransportBoxDescriptionResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.TransportBoxInvalidStateTransition
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UpdateTransportBoxDescriptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.UpdateTransportBoxDescription(1, new UpdateTransportBoxDescriptionRequest(), CancellationToken.None);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(422, statusResult.StatusCode);
    }

    [Fact]
    public async Task UpdateTransportBoxDescription_ValidationError_Returns400()
    {
        // Arrange
        var response = new UpdateTransportBoxDescriptionResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.ValidationError
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UpdateTransportBoxDescriptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.UpdateTransportBoxDescription(1, new UpdateTransportBoxDescriptionRequest(), CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task UpdateTransportBoxDescription_FailureWithoutErrorCode_Returns400()
    {
        // Arrange
        var response = new UpdateTransportBoxDescriptionResponse
        {
            Success = false
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<UpdateTransportBoxDescriptionRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.UpdateTransportBoxDescription(1, new UpdateTransportBoxDescriptionRequest(), CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetTransportBoxByCode_Success_ReturnsOk()
    {
        // Arrange
        var response = new GetTransportBoxByCodeResponse
        {
            Success = true
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetTransportBoxByCodeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.GetTransportBoxByCode("B001", CancellationToken.None);

        // Assert
        Assert.IsType<OkObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetTransportBoxByCode_BoxNotFound_Returns404()
    {
        // Arrange
        var response = new GetTransportBoxByCodeResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.TransportBoxNotFound
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetTransportBoxByCodeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.GetTransportBoxByCode("B001", CancellationToken.None);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetTransportBoxByCode_InvalidStateTransition_Returns422()
    {
        // Arrange
        var response = new GetTransportBoxByCodeResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.TransportBoxInvalidStateTransition
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetTransportBoxByCodeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.GetTransportBoxByCode("B001", CancellationToken.None);

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(422, statusResult.StatusCode);
    }

    [Fact]
    public async Task GetTransportBoxByCode_ValidationError_Returns400()
    {
        // Arrange
        var response = new GetTransportBoxByCodeResponse
        {
            Success = false,
            ErrorCode = ErrorCodes.ValidationError
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetTransportBoxByCodeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.GetTransportBoxByCode("B001", CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetTransportBoxByCode_FailureWithoutErrorCode_Returns400()
    {
        // Arrange
        var response = new GetTransportBoxByCodeResponse
        {
            Success = false
        };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetTransportBoxByCodeRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.GetTransportBoxByCode("B001", CancellationToken.None);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetTransportBoxById_BoxMissing_Returns404()
    {
        // Arrange
        var response = new GetTransportBoxByIdResponse { Success = true, TransportBox = null };
        _mediatorMock
            .Setup(m => m.Send(It.IsAny<GetTransportBoxByIdRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(response);

        // Act
        var result = await _controller.GetTransportBoxById(999, CancellationToken.None);

        // Assert
        Assert.IsType<NotFoundObjectResult>(result.Result);
    }
}
