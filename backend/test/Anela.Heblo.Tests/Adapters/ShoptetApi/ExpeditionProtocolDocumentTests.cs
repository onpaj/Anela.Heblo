using Anela.Heblo.Adapters.ShoptetApi.Expedition;
using FluentAssertions;
using QuestPDF.Infrastructure;

namespace Anela.Heblo.Tests.Adapters.ShoptetApi;

public class ExpeditionProtocolDocumentTests
{
    static ExpeditionProtocolDocumentTests()
    {
        // Required by QuestPDF before first use
        QuestPDF.Settings.License = LicenseType.Community;
    }

    private static ExpeditionProtocolData BuildData(int orderCount = 1, int itemsPerOrder = 1)
    {
        var orders = Enumerable.Range(1, orderCount).Select(i => new ExpeditionOrder
        {
            Code = $"ORDER{i:D3}",
            CustomerName = $"Customer {i}",
            Address = $"Street {i}, 100 00 Praha",
            Phone = $"+420 60{i:D7}",
            Items = Enumerable.Range(1, itemsPerOrder).Select(j => new ExpeditionOrderItem
            {
                ProductCode = $"P{j:D3}",
                Name = $"Product {j}",
                Variant = j % 2 == 0 ? $"Variant {j}" : string.Empty,
                WarehousePosition = $"A{j}",
                Quantity = j,
                StockCount = j * 10,
                UnitPrice = j * 99.9m,
            }).ToList(),
        }).ToList();

        return new ExpeditionProtocolData
        {
            CarrierDisplayName = "PPL",
            Orders = orders,
        };
    }

    [Fact]
    public void Generate_WithValidData_ReturnsPdfBytes()
    {
        // Arrange
        var data = BuildData();

        // Act
        var pdfBytes = ExpeditionProtocolDocument.Generate(data);

        // Assert
        pdfBytes.Should().NotBeNullOrEmpty();
        // PDF magic bytes: %PDF
        pdfBytes[0].Should().Be(0x25); // %
        pdfBytes[1].Should().Be(0x50); // P
        pdfBytes[2].Should().Be(0x44); // D
        pdfBytes[3].Should().Be(0x46); // F
    }

    [Fact]
    public void Generate_WithMultipleOrders_DoesNotThrow()
    {
        // Arrange
        var data = BuildData(orderCount: 5, itemsPerOrder: 3);

        // Act
        var act = () => ExpeditionProtocolDocument.Generate(data);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Generate_WithEmptyOrders_DoesNotThrow()
    {
        // Arrange
        var data = new ExpeditionProtocolData
        {
            CarrierDisplayName = "Zasilkovna",
            Orders = new List<ExpeditionOrder>(),
        };

        // Act
        var act = () => ExpeditionProtocolDocument.Generate(data);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Generate_WithOrderHavingNoItems_DoesNotThrow()
    {
        // Arrange
        var data = new ExpeditionProtocolData
        {
            CarrierDisplayName = "GLS",
            Orders = new List<ExpeditionOrder>
            {
                new()
                {
                    Code = "Z001",
                    CustomerName = "Test Customer",
                    Address = "Test Street 1, 100 00 Praha",
                    Phone = "+420 600000001",
                    Items = new List<ExpeditionOrderItem>(),
                },
            },
        };

        // Act
        var act = () => ExpeditionProtocolDocument.Generate(data);

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Generate_ResultSizeIsReasonable()
    {
        // A non-trivial PDF should be larger than 1 KB
        var data = BuildData(orderCount: 2, itemsPerOrder: 2);

        var pdfBytes = ExpeditionProtocolDocument.Generate(data);

        pdfBytes.Length.Should().BeGreaterThan(1024);
    }
}
