using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.Services.Workflows;
using Anela.Heblo.Application.Features.Manufacture.UseCases.ConfirmSemiProductManufacture;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ConfirmSemiProductManufactureHandlerTests
{
    private readonly Mock<IConfirmSemiProductManufactureWorkflow> _workflowMock = new();
    private readonly Mock<ILogger<ConfirmSemiProductManufactureHandler>> _loggerMock = new();
    private readonly ConfirmSemiProductManufactureHandler _handler;

    public ConfirmSemiProductManufactureHandlerTests()
    {
        _handler = new ConfirmSemiProductManufactureHandler(_workflowMock.Object, _loggerMock.Object);
    }

    [Fact]
    public async Task Handle_WorkflowSuccess_ReturnsSuccessResponseWithMessage()
    {
        // Arrange
        var request = new ConfirmSemiProductManufactureRequest
        {
            Id = 1,
            ActualQuantity = 10m,
            ChangeReason = "test reason",
        };
        var workflowResult = new ConfirmSemiProductManufactureResult(
            success: true,
            message: "Polotovar byl úspěšně vyroben se skutečným množstvím 10");
        _workflowMock
            .Setup(w => w.ExecuteAsync(1, 10m, "test reason", It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflowResult);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeTrue();
        response.ErrorCode.Should().BeNull();
        response.Message.Should().Be("Polotovar byl úspěšně vyroben se skutečným množstvím 10");
        _workflowMock.Verify(w => w.ExecuteAsync(1, 10m, "test reason", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WorkflowFailureWithErrorCode_ReturnsErrorResponse()
    {
        // Arrange
        var request = new ConfirmSemiProductManufactureRequest { Id = 1, ActualQuantity = 10m };
        var workflowResult = new ConfirmSemiProductManufactureResult(
            success: false,
            message: "ERP timeout",
            errorCode: ErrorCodes.ErpGatewayError);
        _workflowMock
            .Setup(w => w.ExecuteAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflowResult);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ErpGatewayError);
        response.Message.Should().Be("ERP timeout");
    }

    [Fact]
    public async Task Handle_WorkflowFailureWithoutErrorCode_DefaultsToInvalidOperation()
    {
        // Arrange
        var request = new ConfirmSemiProductManufactureRequest { Id = 1, ActualQuantity = 10m };
        var workflowResult = new ConfirmSemiProductManufactureResult(
            success: false,
            message: "Unknown failure",
            errorCode: null);
        _workflowMock
            .Setup(w => w.ExecuteAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(workflowResult);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InvalidOperation);
        response.Message.Should().Be("Unknown failure");
    }

    [Fact]
    public async Task Handle_WorkflowThrowsException_ReturnsInternalServerErrorResponse()
    {
        // Arrange
        var request = new ConfirmSemiProductManufactureRequest { Id = 1, ActualQuantity = 10m };
        _workflowMock
            .Setup(w => w.ExecuteAsync(It.IsAny<int>(), It.IsAny<decimal>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.InternalServerError);
        response.Message.Should().Be("Došlo k neočekávané chybě při potvrzení výroby polotovaru");
    }
}
