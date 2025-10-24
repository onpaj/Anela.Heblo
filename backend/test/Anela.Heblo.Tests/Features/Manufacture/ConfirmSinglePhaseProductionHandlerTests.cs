using Anela.Heblo.Application.Features.Manufacture.UseCases.ConfirmSinglePhaseProduction;
using Anela.Heblo.Domain.Features.Manufacture;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture;

public class ConfirmSinglePhaseProductionHandlerTests
{
    private readonly Mock<IManufactureOrderRepository> _repositoryMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly ConfirmSinglePhaseProductionHandler _handler;

    public ConfirmSinglePhaseProductionHandlerTests()
    {
        _repositoryMock = new Mock<IManufactureOrderRepository>();
        _timeProviderMock = new Mock<TimeProvider>();

        _handler = new ConfirmSinglePhaseProductionHandler(
            _repositoryMock.Object,
            _timeProviderMock.Object);
    }

    [Fact]
    public async Task Handle_ValidSinglePhaseOrder_ShouldCompleteSuccessfully()
    {
        // Arrange
        var currentTime = new DateTime(2023, 10, 20);
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(new DateTimeOffset(currentTime));

        var order = new ManufactureOrder
        {
            Id = 1,
            ManufactureType = ManufactureType.SinglePhase,
            State = ManufactureOrderState.Planned,
            Products = new List<ManufactureOrderProduct>
            {
                new() { Id = 1, ProductCode = "PROD001", PlannedQuantity = 50 },
                new() { Id = 2, ProductCode = "PROD002", PlannedQuantity = 30 }
            }
        };

        _repositoryMock.Setup(x => x.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var request = new ConfirmSinglePhaseProductionRequest
        {
            OrderId = 1,
            ProductActualQuantities = new Dictionary<int, decimal> { { 1, 45 }, { 2, 28 } },
            UserId = "testuser"
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.True(result.Success);
        Assert.Equal(1, result.OrderId);
        Assert.Equal(currentTime, result.CompletedAt);

        // Verify order state transitions
        Assert.Equal(ManufactureOrderState.Completed, order.State);
        Assert.Equal(currentTime, order.StateChangedAt);
        Assert.Equal("testuser", order.StateChangedByUser);

        // Verify product quantities updated
        Assert.Equal(45, order.Products[0].ActualQuantity);
        Assert.Equal(28, order.Products[1].ActualQuantity);

        _repositoryMock.Verify(x => x.UpdateOrderAsync(order, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_OrderNotFound_ShouldReturnError()
    {
        // Arrange
        _repositoryMock.Setup(x => x.GetOrderByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufactureOrder?)null);

        var request = new ConfirmSinglePhaseProductionRequest
        {
            OrderId = 999,
            ProductActualQuantities = new Dictionary<int, decimal> { { 1, 45 } },
            UserId = "testuser"
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Manufacture order not found", result.ErrorMessage);
    }

    [Fact]
    public async Task Handle_MultiPhaseOrder_ShouldReturnError()
    {
        // Arrange
        var order = new ManufactureOrder
        {
            Id = 1,
            ManufactureType = ManufactureType.MultiPhase,
            State = ManufactureOrderState.Planned
        };

        _repositoryMock.Setup(x => x.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var request = new ConfirmSinglePhaseProductionRequest
        {
            OrderId = 1,
            ProductActualQuantities = new Dictionary<int, decimal> { { 1, 45 } },
            UserId = "testuser"
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Order is not single-phase", result.ErrorMessage);
    }

    [Fact]
    public async Task Handle_InvalidState_ShouldReturnError()
    {
        // Arrange
        var order = new ManufactureOrder
        {
            Id = 1,
            ManufactureType = ManufactureType.SinglePhase,
            State = ManufactureOrderState.Completed
        };

        _repositoryMock.Setup(x => x.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var request = new ConfirmSinglePhaseProductionRequest
        {
            OrderId = 1,
            ProductActualQuantities = new Dictionary<int, decimal> { { 1, 45 } },
            UserId = "testuser"
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Order must be in Planned state", result.ErrorMessage);
    }

    [Fact]
    public async Task Handle_ProductNotFound_ShouldReturnError()
    {
        // Arrange
        var order = new ManufactureOrder
        {
            Id = 1,
            ManufactureType = ManufactureType.SinglePhase,
            State = ManufactureOrderState.Planned,
            Products = new List<ManufactureOrderProduct>
            {
                new() { Id = 1, ProductCode = "PROD001", PlannedQuantity = 50 }
            }
        };

        _repositoryMock.Setup(x => x.GetOrderByIdAsync(1, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var request = new ConfirmSinglePhaseProductionRequest
        {
            OrderId = 1,
            ProductActualQuantities = new Dictionary<int, decimal> { { 999, 45 } }, // Non-existent product ID
            UserId = "testuser"
        };

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        Assert.False(result.Success);
        Assert.Equal("Product with ID 999 not found in order", result.ErrorMessage);
    }
}