using Anela.Heblo.Application.Features.Logistics.Contracts;
using Anela.Heblo.Application.Features.Manufacture.Infrastructure;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Infrastructure;

public class ManufactureInventoryReservationAdapterTests
{
    private readonly Mock<IManufacturedProductInventoryRepository> _inventoryRepositoryMock;
    private readonly Mock<ILogger<ManufactureInventoryReservationAdapter>> _loggerMock;
    private readonly ManufactureInventoryReservationAdapter _adapter;

    private static readonly DateTime FixedTime = new(2025, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    public ManufactureInventoryReservationAdapterTests()
    {
        _inventoryRepositoryMock = new Mock<IManufacturedProductInventoryRepository>();
        _loggerMock = new Mock<ILogger<ManufactureInventoryReservationAdapter>>();
        _adapter = new ManufactureInventoryReservationAdapter(
            _inventoryRepositoryMock.Object,
            _loggerMock.Object);
    }

    [Fact]
    public async Task TryConsumeAsync_ItemNotFound_ReturnsInventoryNotFound()
    {
        // Arrange
        _inventoryRepositoryMock
            .Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufacturedProductInventoryItem?)null);

        // Act
        var result = await _adapter.TryConsumeAsync(
            inventoryId: 42, amount: 5m, userName: "u", timestamp: FixedTime,
            boxId: 1, boxCode: "B001", allowNegativeStock: false,
            cancellationToken: CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ConsumeInventoryOutcome.InventoryNotFound);
        _inventoryRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<ManufacturedProductInventoryItem>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TryConsumeAsync_HappyPath_DecrementsAndReturnsSuccess()
    {
        // Arrange
        var item = CreateInventoryItem("PROD-001", 100m);
        _inventoryRepositoryMock
            .Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        // Act
        var result = await _adapter.TryConsumeAsync(
            inventoryId: 42, amount: 10m, userName: "alice", timestamp: FixedTime,
            boxId: 1, boxCode: "B001", allowNegativeStock: false,
            cancellationToken: CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ConsumeInventoryOutcome.Success);
        item.Amount.Should().Be(90m);
        _inventoryRepositoryMock.Verify(
            x => x.UpdateAsync(item, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TryConsumeAsync_AmountExceedsAvailable_ReturnsInsufficientStock()
    {
        // Arrange
        var item = CreateInventoryItem("PROD-001", 5m);
        _inventoryRepositoryMock
            .Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        // Act
        var result = await _adapter.TryConsumeAsync(
            inventoryId: 42, amount: 100m, userName: "alice", timestamp: FixedTime,
            boxId: 1, boxCode: "B001", allowNegativeStock: false,
            cancellationToken: CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ConsumeInventoryOutcome.InsufficientStock);
        item.Amount.Should().Be(5m, "domain mutation must not be persisted when consume fails");
        _inventoryRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<ManufacturedProductInventoryItem>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task TryConsumeAsync_AllowNegativeStock_SucceedsWhenAmountExceedsAvailable()
    {
        // Arrange
        var item = CreateInventoryItem("PROD-001", 5m);
        _inventoryRepositoryMock
            .Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        // Act
        var result = await _adapter.TryConsumeAsync(
            inventoryId: 42, amount: 100m, userName: "alice", timestamp: FixedTime,
            boxId: 1, boxCode: "B001", allowNegativeStock: true,
            cancellationToken: CancellationToken.None);

        // Assert
        result.Outcome.Should().Be(ConsumeInventoryOutcome.Success);
        item.Amount.Should().Be(-95m);
        _inventoryRepositoryMock.Verify(
            x => x.UpdateAsync(item, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task TryConsumeAsync_DoesNotCallSaveChanges()
    {
        // Arrange — guard NFR-3: adapter must never commit
        var item = CreateInventoryItem("PROD-001", 100m);
        _inventoryRepositoryMock
            .Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        // Act
        await _adapter.TryConsumeAsync(
            inventoryId: 42, amount: 10m, userName: "alice", timestamp: FixedTime,
            boxId: 1, boxCode: "B001", allowNegativeStock: false,
            cancellationToken: CancellationToken.None);

        // Assert
        _inventoryRepositoryMock.Verify(
            x => x.SaveChangesAsync(It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RestoreAsync_HappyPath_IncrementsAndUpdates()
    {
        // Arrange
        var item = CreateInventoryItem("PROD-001", 10m);
        _inventoryRepositoryMock
            .Setup(x => x.GetByIdAsync(42, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        // Act
        await _adapter.RestoreAsync(
            inventoryId: 42, amount: 3m, userName: "alice", timestamp: FixedTime,
            boxId: 1, boxCode: "B001",
            cancellationToken: CancellationToken.None);

        // Assert
        item.Amount.Should().Be(13m);
        _inventoryRepositoryMock.Verify(
            x => x.UpdateAsync(item, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RestoreAsync_ItemNotFound_IsNoOpAndLogsWarning()
    {
        // Arrange
        _inventoryRepositoryMock
            .Setup(x => x.GetByIdAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((ManufacturedProductInventoryItem?)null);

        // Act
        await _adapter.RestoreAsync(
            inventoryId: 999, amount: 3m, userName: "alice", timestamp: FixedTime,
            boxId: 1, boxCode: "B001",
            cancellationToken: CancellationToken.None);

        // Assert
        _inventoryRepositoryMock.Verify(
            x => x.UpdateAsync(It.IsAny<ManufacturedProductInventoryItem>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Warning,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains("InventoryItem") && v.ToString()!.Contains("999")),
                It.IsAny<Exception?>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }

    private static ManufacturedProductInventoryItem CreateInventoryItem(string productCode, decimal amount)
    {
        return new ManufacturedProductInventoryItem(
            productCode: productCode,
            productName: "Test Product",
            amount: amount,
            createdBy: "system",
            createdAt: new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc));
    }
}
