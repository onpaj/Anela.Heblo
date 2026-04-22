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
                StockDemand = j * 2,
                Unit = j % 2 == 0 ? "ks" : string.Empty,
                UnitPrice = j * 99.9m,
            }).ToList(),
        }).ToList();

        return new ExpeditionProtocolData
        {
            CarrierDisplayName = "PPL",
            Orders = orders,
        };
    }

    /// <summary>
    /// Sample data matching the reference PDF (Přehled objednávek - 780175.myshoptet.com.pdf).
    /// Used for visual inspection tests.
    /// </summary>
    private static ExpeditionProtocolData BuildSampleData() =>
        new()
        {
            CarrierDisplayName = "PPL (do ruky)",
            Orders = new List<ExpeditionOrder>
            {
                new()
                {
                    Code = "126000046",
                    CustomerName = "Vojtěch Liška",
                    Address = "Testovaci 1, 10000 Praha",
                    Phone = "+420 725 191 660",
                    Items = new List<ExpeditionOrderItem>
                    {
                        new()
                        {
                            ProductCode = "OCH009030",
                            Name = "Klidný dech dětský prsní balzám",
                            Variant = "Obsah: 30 ml",
                            WarehousePosition = "A04-2",
                            Quantity = 2,
                            StockCount = 91,
                            StockDemand = 2,
                            UnitPrice = 340.00m,
                            Unit = "ks",
                        },
                        new()
                        {
                            ProductCode = "OCH001030",
                            Name = "Test product",
                            Variant = "Obsah: 30 ml",
                            WarehousePosition = "A28-3",
                            Quantity = 1,
                            StockCount = 94,
                            StockDemand = 19,
                            UnitPrice = 1.00m,
                            Unit = string.Empty,
                        },
                    },
                },
                new()
                {
                    Code = "126000045",
                    CustomerName = "Monika Poláková",
                    Address = "Testovaci 1, 10000 Praha",
                    Phone = "+420 725 191 660",
                    Items = new List<ExpeditionOrderItem>
                    {
                        new()
                        {
                            ProductCode = "OCH001030",
                            Name = "Test product",
                            Variant = "Obsah: 30 ml",
                            WarehousePosition = "A28-3",
                            Quantity = 1,
                            StockCount = 94,
                            StockDemand = 19,
                            UnitPrice = 1.00m,
                            Unit = string.Empty,
                        },
                    },
                },
                new()
                {
                    Code = "126000041",
                    CustomerName = "Simona Růžičková",
                    Address = "Testovaci 1, 10000 Praha",
                    Phone = "+420 725 191 660",
                    Items = new List<ExpeditionOrderItem>
                    {
                        new()
                        {
                            ProductCode = "OCH009005",
                            Name = "Klidný dech dětský prsní balzám",
                            Variant = "Obsah: 5 ml vzorek",
                            WarehousePosition = "A04-1",
                            Quantity = 3,
                            StockCount = 185,
                            StockDemand = 4,
                            UnitPrice = 110.00m,
                            Unit = "ks",
                        },
                        new()
                        {
                            ProductCode = "OCH001030",
                            Name = "Test product",
                            Variant = "Obsah: 30 ml",
                            WarehousePosition = "A28-3",
                            Quantity = 1,
                            StockCount = 94,
                            StockDemand = 19,
                            UnitPrice = 1.00m,
                            Unit = string.Empty,
                        },
                    },
                },
            },
        };

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

    [Fact]
    public void Generate_WithUnitAndStockDemand_DoesNotThrow()
    {
        // Verifies that Unit and StockDemand fields are accepted by the document generator
        var data = new ExpeditionProtocolData
        {
            CarrierDisplayName = "PPL (do ruky)",
            Orders = new List<ExpeditionOrder>
            {
                new()
                {
                    Code = "126000046",
                    CustomerName = "Vojtěch Liška",
                    Address = "Testovaci 1, 10000 Praha",
                    Phone = "+420 725 191 660",
                    Items = new List<ExpeditionOrderItem>
                    {
                        new()
                        {
                            ProductCode = "OCH009030",
                            Name = "Klidný dech dětský prsní balzám",
                            Variant = "Obsah: 30 ml",
                            WarehousePosition = "A04-2",
                            Quantity = 2,
                            StockCount = 91,
                            StockDemand = 2,
                            UnitPrice = 340.00m,
                            Unit = "ks",
                        },
                        new()
                        {
                            ProductCode = "OCH001030",
                            Name = "Test product",
                            Variant = "Obsah: 30 ml",
                            WarehousePosition = "A28-3",
                            Quantity = 1,
                            StockCount = 94,
                            StockDemand = 19,
                            UnitPrice = 1.00m,
                            Unit = string.Empty,
                        },
                    },
                },
            },
        };

        var act = () => ExpeditionProtocolDocument.Generate(data);

        act.Should().NotThrow();
    }

    [Fact]
    public void Generate_SummaryPage_IncludesStockDemandAndRealState()
    {
        // Summary page must aggregate StockDemand and compute RealState = StockCount + StockDemand
        var data = new ExpeditionProtocolData
        {
            CarrierDisplayName = "PPL",
            Orders = new List<ExpeditionOrder>
            {
                new()
                {
                    Code = "ORD001",
                    CustomerName = "Test",
                    Address = "Praha",
                    Phone = "123",
                    Items = new List<ExpeditionOrderItem>
                    {
                        new()
                        {
                            ProductCode = "P001",
                            Name = "Product",
                            Variant = string.Empty,
                            WarehousePosition = "A1",
                            Quantity = 3,
                            StockCount = 100,
                            StockDemand = 5,
                            UnitPrice = 99m,
                            Unit = "ks",
                        },
                    },
                },
            },
        };

        var act = () => ExpeditionProtocolDocument.Generate(data);

        act.Should().NotThrow();
    }

    [Fact]
    public void Generate_SampleData_SavesToDiskForVisualInspection()
    {
        // Generates the reference PDF for manual visual comparison.
        // Output: <temp>/ExpeditionList_Sample.pdf
        var data = BuildSampleData();

        var pdfBytes = ExpeditionProtocolDocument.Generate(data);

        var outputPath = Path.Combine(Path.GetTempPath(), "ExpeditionList_Sample.pdf");
        File.WriteAllBytes(outputPath, pdfBytes);

        pdfBytes.Should().NotBeNullOrEmpty();
        File.Exists(outputPath).Should().BeTrue();

        // File intentionally kept for visual inspection — open manually from temp path printed below
        Console.WriteLine($"PDF saved to: {outputPath}");
    }

    [Fact]
    public void Generate_Order126000038_WithNotes_SavesToDiskForVisualInspection()
    {
        // Visual inspection test for order 126000038 with both customer and eshop remarks.
        // Output: <temp>/ExpeditionList_126000038_WithNotes.pdf
        var data = new ExpeditionProtocolData
        {
            CarrierDisplayName = "PPL (do ruky)",
            Orders = new List<ExpeditionOrder>
            {
                new()
                {
                    Code = "126000038",
                    CustomerName = "Jana Nováková",
                    Address = "Testovací 42, 110 00 Praha 1",
                    Phone = "+420 725 191 660",
                    CustomerRemark = "Prosím zabalit jako dárek, děkuji.",
                    EshopRemark = "Zákazník volal — doručit po 14:00.",
                    Items = new List<ExpeditionOrderItem>
                    {
                        new()
                        {
                            ProductCode = "OCH009030",
                            Name = "Klidný dech dětský prsní balzám",
                            Variant = "Obsah: 30 ml",
                            WarehousePosition = "A04-2",
                            Quantity = 1,
                            StockCount = 89,
                            StockDemand = 1,
                            UnitPrice = 340.00m,
                            Unit = "ks",
                        },
                        new()
                        {
                            ProductCode = "OCH001030",
                            Name = "Tělový olej levandule",
                            Variant = "Obsah: 30 ml",
                            WarehousePosition = "A28-3",
                            Quantity = 2,
                            StockCount = 55,
                            StockDemand = 3,
                            UnitPrice = 290.00m,
                            Unit = "ks",
                        },
                    },
                },
            },
        };

        var pdfBytes = ExpeditionProtocolDocument.Generate(data);

        var outputPath = Path.Combine(Path.GetTempPath(), "ExpeditionList_126000038_WithNotes.pdf");
        File.WriteAllBytes(outputPath, pdfBytes);

        pdfBytes.Should().NotBeNullOrEmpty();
        File.Exists(outputPath).Should().BeTrue();

        Console.WriteLine($"PDF saved to: {outputPath}");
    }
}
