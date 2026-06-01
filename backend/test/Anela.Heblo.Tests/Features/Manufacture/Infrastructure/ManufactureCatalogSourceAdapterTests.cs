using Anela.Heblo.Application.Features.Manufacture.Infrastructure;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using FluentAssertions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Manufacture.Infrastructure;

public class ManufactureCatalogSourceAdapterTests
{
    private readonly Mock<IManufactureOrderRepository> _orderRepository = new();
    private readonly Mock<IManufactureHistoryClient> _historyClient = new();
    private readonly Mock<IManufacturedProductInventoryRepository> _inventoryRepository = new();

    private ManufactureCatalogSourceAdapter CreateAdapter() =>
        new(_orderRepository.Object, _historyClient.Object, _inventoryRepository.Object);

    [Fact]
    public async Task GetPlannedQuantitiesAsync_DelegatesToManufactureOrderRepository()
    {
        // Arrange
        var ct = CancellationToken.None;
        var expectedQuantities = new Dictionary<string, decimal>
        {
            { "PROD-1", 15.5m },
            { "PROD-2", 30.0m }
        };
        _orderRepository
            .Setup(r => r.GetPlannedQuantitiesAsync(ct))
            .ReturnsAsync(expectedQuantities);

        // Act
        var result = await CreateAdapter().GetPlannedQuantitiesAsync(ct);

        // Assert
        result.Should().BeEquivalentTo(expectedQuantities);
    }

    [Fact]
    public async Task GetPlannedQuantitiesAsync_PassesCancellationTokenToRepository()
    {
        // Arrange
        var ct = new CancellationToken(false);
        _orderRepository
            .Setup(r => r.GetPlannedQuantitiesAsync(ct))
            .ReturnsAsync(new Dictionary<string, decimal>());

        // Act
        await CreateAdapter().GetPlannedQuantitiesAsync(ct);

        // Assert
        _orderRepository.Verify(
            r => r.GetPlannedQuantitiesAsync(ct),
            Times.Once);
    }

    [Fact]
    public async Task GetManufactureHistoryAsync_DelegatesToHistoryClient()
    {
        // Arrange
        var dateFrom = new DateTime(2026, 01, 01);
        var dateTo = new DateTime(2026, 01, 31);
        var ct = CancellationToken.None;

        var historyRecords = new List<ManufactureHistoryRecord>
        {
            new() { ProductCode = "PROD-1", Amount = 10, PricePerPiece = 5m },
            new() { ProductCode = "PROD-2", Amount = 20, PricePerPiece = 10m }
        };

        _historyClient
            .Setup(c => c.GetHistoryAsync(dateFrom, dateTo, null, ct))
            .ReturnsAsync(historyRecords);

        // Act
        var result = await CreateAdapter().GetManufactureHistoryAsync(dateFrom, dateTo, ct);

        // Assert
        result.Should().HaveCount(2);
        result.Should().BeEquivalentTo(historyRecords);
    }

    [Fact]
    public async Task GetManufactureHistoryAsync_ReturnsReadOnlyList()
    {
        // Arrange
        var dateFrom = new DateTime(2026, 01, 01);
        var dateTo = new DateTime(2026, 01, 31);
        var ct = CancellationToken.None;

        var historyRecords = new List<ManufactureHistoryRecord>
        {
            new() { ProductCode = "PROD-1", Amount = 10, PricePerPiece = 5m }
        };

        _historyClient
            .Setup(c => c.GetHistoryAsync(dateFrom, dateTo, null, ct))
            .ReturnsAsync(historyRecords);

        // Act
        var result = await CreateAdapter().GetManufactureHistoryAsync(dateFrom, dateTo, ct);

        // Assert
        result.Should().BeAssignableTo<IReadOnlyList<ManufactureHistoryRecord>>();
    }

    [Fact]
    public async Task GetManufacturedInventoryAsync_DelegatesToInventoryRepository()
    {
        // Arrange
        var ct = CancellationToken.None;
        var expectedInventory = new Dictionary<string, decimal>
        {
            { "PROD-1", 50.0m },
            { "PROD-2", 75.5m }
        };
        _inventoryRepository
            .Setup(r => r.GetTotalAmountByProductCodeAsync(ct))
            .ReturnsAsync(expectedInventory);

        // Act
        var result = await CreateAdapter().GetManufacturedInventoryAsync(ct);

        // Assert
        result.Should().BeEquivalentTo(expectedInventory);
    }

    [Fact]
    public async Task GetManufacturedInventoryAsync_PassesCancellationTokenToRepository()
    {
        // Arrange
        var ct = new CancellationToken(false);
        _inventoryRepository
            .Setup(r => r.GetTotalAmountByProductCodeAsync(ct))
            .ReturnsAsync(new Dictionary<string, decimal>());

        // Act
        await CreateAdapter().GetManufacturedInventoryAsync(ct);

        // Assert
        _inventoryRepository.Verify(
            r => r.GetTotalAmountByProductCodeAsync(ct),
            Times.Once);
    }
}
