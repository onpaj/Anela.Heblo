using Anela.Heblo.Application.Features.Manufacture.Contracts;
using Anela.Heblo.Application.Features.Manufacture.Services;
using Anela.Heblo.Application.Features.Manufacture.Services.Workflows;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Services;

public class ManufactureOrderApplicationServiceTests
{
    private readonly Mock<IConfirmSemiProductManufactureWorkflow> _semiProductWorkflowMock;
    private readonly Mock<IConfirmProductCompletionWorkflow> _productCompletionWorkflowMock;
    private readonly ManufactureOrderApplicationService _service;

    private const int ValidOrderId = 1;
    private const decimal ValidQuantity = 10.5m;
    private const string ValidChangeReason = "Testing manufacture confirmation";

    public ManufactureOrderApplicationServiceTests()
    {
        _semiProductWorkflowMock = new Mock<IConfirmSemiProductManufactureWorkflow>();
        _productCompletionWorkflowMock = new Mock<IConfirmProductCompletionWorkflow>();

        _service = new ManufactureOrderApplicationService(
            _semiProductWorkflowMock.Object,
            _productCompletionWorkflowMock.Object);
    }

    #region ConfirmSemiProductManufactureAsync Tests

    [Fact]
    public async Task ConfirmSemiProductManufactureAsync_DelegatesToWorkflow_ReturnsWorkflowResult()
    {
        // Arrange
        var expected = new ConfirmSemiProductManufactureResult(true, $"Polotovar byl úspěšně vyroben se skutečným množstvím {ValidQuantity}");
        _semiProductWorkflowMock
            .Setup(x => x.ExecuteAsync(ValidOrderId, ValidQuantity, ValidChangeReason, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _service.ConfirmSemiProductManufactureAsync(ValidOrderId, ValidQuantity, ValidChangeReason);

        // Assert
        result.Should().BeSameAs(expected);
        _semiProductWorkflowMock.Verify(
            x => x.ExecuteAsync(ValidOrderId, ValidQuantity, ValidChangeReason, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ConfirmSemiProductManufactureAsync_WorkflowReturnsFailure_PropagatesFailure()
    {
        // Arrange
        var expected = new ConfirmSemiProductManufactureResult(false, "Chyba při aktualizaci množství: InternalServerError");
        _semiProductWorkflowMock
            .Setup(x => x.ExecuteAsync(ValidOrderId, ValidQuantity, ValidChangeReason, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _service.ConfirmSemiProductManufactureAsync(ValidOrderId, ValidQuantity, ValidChangeReason);

        // Assert
        result.Should().BeSameAs(expected);
        result.Success.Should().BeFalse();
    }

    #endregion

    #region ConfirmProductCompletionAsync Delegation Tests

    [Fact]
    public async Task ConfirmProductCompletionAsync_DelegatesToWorkflow_ReturnsWorkflowResult()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };
        var expected = new ConfirmProductCompletionResult();
        _productCompletionWorkflowMock
            .Setup(x => x.ExecuteAsync(ValidOrderId, productQuantities, false, ValidChangeReason, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, false, ValidChangeReason);

        // Assert
        result.Should().BeSameAs(expected);
        _productCompletionWorkflowMock.Verify(
            x => x.ExecuteAsync(ValidOrderId, productQuantities, false, ValidChangeReason, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ConfirmProductCompletionAsync_WorkflowReturnsFailure_PropagatesFailure()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };
        var expected = new ConfirmProductCompletionResult("Chyba při aktualizaci množství produktů: InternalServerError");
        _productCompletionWorkflowMock
            .Setup(x => x.ExecuteAsync(ValidOrderId, productQuantities, false, ValidChangeReason, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, false, ValidChangeReason);

        // Assert
        result.Should().BeSameAs(expected);
        result.Success.Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmProductCompletionAsync_PassesOverrideConfirmedAndChangeReason_ToWorkflow()
    {
        // Arrange
        var productQuantities = new Dictionary<int, decimal> { { 1, 5.0m } };
        var expected = new ConfirmProductCompletionResult();
        _productCompletionWorkflowMock
            .Setup(x => x.ExecuteAsync(ValidOrderId, productQuantities, true, ValidChangeReason, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expected);

        // Act
        var result = await _service.ConfirmProductCompletionAsync(ValidOrderId, productQuantities, true, ValidChangeReason);

        // Assert
        result.Should().BeSameAs(expected);
        _productCompletionWorkflowMock.Verify(
            x => x.ExecuteAsync(ValidOrderId, productQuantities, true, ValidChangeReason, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    #endregion
}
