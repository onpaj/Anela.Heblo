using Anela.Heblo.Application.Features.Purchase.DashboardTiles;
using Anela.Heblo.Domain.Features.Purchase;
using FluentAssertions;
using Moq;
using System.Text.Json;
using Xunit;

namespace Anela.Heblo.Tests.Features.Purchase.DashboardTiles;

public class PurchaseOrdersInTransitTileTests
{
    private readonly Mock<IPurchaseOrderRepository> _repositoryMock;
    private readonly PurchaseOrdersInTransitTile _tile;

    public PurchaseOrdersInTransitTileTests()
    {
        _repositoryMock = new Mock<IPurchaseOrderRepository>();
        _tile = new PurchaseOrdersInTransitTile(_repositoryMock.Object);
    }

    private static PurchaseOrder BuildOrderWithAmount(decimal amount)
    {
        var order = new PurchaseOrder(
            orderNumber: "PO-TEST",
            supplierId: 1,
            supplierName: "Test Supplier",
            orderDate: DateTime.UtcNow,
            expectedDeliveryDate: null,
            contactVia: null,
            notes: null,
            createdBy: "test");

        order.AddLine(
            materialId: "MAT-1",
            materialName: "Test Material",
            quantity: 1,
            unitPrice: amount,
            notes: null,
            updatedBy: "test");

        return order;
    }

    [Fact]
    public async Task LoadDataAsync_WithNoOrdersInTransit_ReturnsZeroNotZeroK()
    {
        // Arrange
        _repositoryMock
            .Setup(x => x.GetByStatusAsync(PurchaseOrderStatus.InTransit, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrder>());

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("data").GetProperty("formattedAmount").GetString()
            .Should().Be("0");
    }

    [Theory]
    [InlineData(999, "1.0k")]
    [InlineData(1000, "1k")]
    [InlineData(1001, "1.0k")]
    [InlineData(1500, "1.5k")]
    [InlineData(5000, "5k")]
    [InlineData(9999, "10.0k")]
    [InlineData(10000, "10k")]
    [InlineData(999999, "1000.0k")]
    public async Task LoadDataAsync_FormatsAmountInThousands(int amount, string expectedFormattedAmount)
    {
        // Arrange
        _repositoryMock
            .Setup(x => x.GetByStatusAsync(PurchaseOrderStatus.InTransit, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PurchaseOrder> { BuildOrderWithAmount(amount) });

        // Act
        var result = await _tile.LoadDataAsync();

        // Assert
        var json = JsonSerializer.Serialize(result);
        using var doc = JsonDocument.Parse(json);

        doc.RootElement.GetProperty("data").GetProperty("formattedAmount").GetString()
            .Should().Be(expectedFormattedAmount);
    }
}
