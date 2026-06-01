using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using FluentAssertions;

namespace Anela.Heblo.Tests.Domain.Catalog;

public class CatalogAggregateTests
{
    [Fact]
    public void SalesHistory_WhenSet_AutomaticallyUpdatesSaleHistorySummary()
    {
        // Arrange
        var aggregate = new CatalogAggregate();
        var salesData = new List<CatalogSaleRecord>
        {
            new CatalogSaleRecord
            {
                Date = new DateTime(2024, 1, 15),
                SumB2B = 1000,
                SumB2C = 500,
                AmountB2B = 10,
                AmountB2C = 5
            },
            new CatalogSaleRecord
            {
                Date = new DateTime(2024, 1, 20),
                SumB2B = 800,
                SumB2C = 200,
                AmountB2B = 8,
                AmountB2C = 2
            },
            new CatalogSaleRecord
            {
                Date = new DateTime(2024, 2, 10),
                SumB2B = 1200,
                SumB2C = 600,
                AmountB2B = 12,
                AmountB2C = 6
            }
        };

        // Act
        aggregate.SalesHistory = salesData;

        // Assert
        aggregate.SaleHistorySummary.MonthlyData.Should().HaveCount(2);

        // Check January 2024 summary
        var jan2024 = aggregate.SaleHistorySummary.MonthlyData["2024-01"];
        jan2024.Year.Should().Be(2024);
        jan2024.Month.Should().Be(1);
        jan2024.TotalB2B.Should().Be(1800); // 1000 + 800
        jan2024.TotalB2C.Should().Be(700);  // 500 + 200
        jan2024.AmountB2B.Should().Be(18);  // 10 + 8
        jan2024.AmountB2C.Should().Be(7);   // 5 + 2
        jan2024.TransactionCount.Should().Be(2);

        // Check February 2024 summary
        var feb2024 = aggregate.SaleHistorySummary.MonthlyData["2024-02"];
        feb2024.Year.Should().Be(2024);
        feb2024.Month.Should().Be(2);
        feb2024.TotalB2B.Should().Be(1200);
        feb2024.TotalB2C.Should().Be(600);
        feb2024.AmountB2B.Should().Be(12);
        feb2024.AmountB2C.Should().Be(6);
        feb2024.TransactionCount.Should().Be(1);

        // Check LastUpdated was set
        aggregate.SaleHistorySummary.LastUpdated.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void PurchaseHistory_WhenSet_AutomaticallyUpdatesPurchaseHistorySummary()
    {
        // Arrange
        var aggregate = new CatalogAggregate();
        var purchaseData = new List<CatalogPurchaseRecord>
        {
            new CatalogPurchaseRecord
            {
                Date = new DateTime(2024, 3, 10),
                SupplierName = "Supplier A",
                Amount = 100,
                PricePerPiece = 10,
                PriceTotal = 1000
            },
            new CatalogPurchaseRecord
            {
                Date = new DateTime(2024, 3, 15),
                SupplierName = "Supplier B",
                Amount = 50,
                PricePerPiece = 20,
                PriceTotal = 1000
            },
            new CatalogPurchaseRecord
            {
                Date = new DateTime(2024, 4, 5),
                SupplierName = "Supplier A",
                Amount = 200,
                PricePerPiece = 12,
                PriceTotal = 2400
            }
        };

        // Act
        aggregate.PurchaseHistory = purchaseData;

        // Assert
        aggregate.PurchaseHistorySummary.MonthlyData.Should().HaveCount(2);

        // Check March 2024 summary
        var mar2024 = aggregate.PurchaseHistorySummary.MonthlyData["2024-03"];
        mar2024.Year.Should().Be(2024);
        mar2024.Month.Should().Be(3);
        mar2024.TotalAmount.Should().Be(150); // 100 + 50
        mar2024.TotalCost.Should().Be(2000);  // 1000 + 1000
        mar2024.AveragePricePerPiece.Should().Be(15); // (10 + 20) / 2
        mar2024.PurchaseCount.Should().Be(2);

        // Check supplier breakdown for March
        mar2024.SupplierBreakdown.Should().HaveCount(2);
        mar2024.SupplierBreakdown.Keys.Should().Contain("Supplier A");
        mar2024.SupplierBreakdown.Keys.Should().Contain("Supplier B");

        var supplierA = mar2024.SupplierBreakdown["Supplier A"];
        supplierA.Amount.Should().Be(100);
        supplierA.Cost.Should().Be(1000);
        supplierA.PurchaseCount.Should().Be(1);

        // Check April 2024 summary
        var apr2024 = aggregate.PurchaseHistorySummary.MonthlyData["2024-04"];
        apr2024.Year.Should().Be(2024);
        apr2024.Month.Should().Be(4);
        apr2024.TotalAmount.Should().Be(200);
        apr2024.TotalCost.Should().Be(2400);
        apr2024.AveragePricePerPiece.Should().Be(12);
        apr2024.PurchaseCount.Should().Be(1);

        // Check LastUpdated was set
        aggregate.PurchaseHistorySummary.LastUpdated.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void ConsumedHistory_WhenSet_AutomaticallyUpdatesConsumedHistorySummary()
    {
        // Arrange
        var aggregate = new CatalogAggregate();
        var consumedData = new List<ConsumedMaterialRecord>
        {
            new ConsumedMaterialRecord
            {
                Date = new DateTime(2024, 5, 12),
                Amount = 25
            },
            new ConsumedMaterialRecord
            {
                Date = new DateTime(2024, 5, 18),
                Amount = 30
            },
            new ConsumedMaterialRecord
            {
                Date = new DateTime(2024, 6, 8),
                Amount = 40
            }
        };

        // Act
        aggregate.ConsumedHistory = consumedData;

        // Assert
        aggregate.ConsumedHistorySummary.MonthlyData.Should().HaveCount(2);

        // Check May 2024 summary
        var may2024 = aggregate.ConsumedHistorySummary.MonthlyData["2024-05"];
        may2024.Year.Should().Be(2024);
        may2024.Month.Should().Be(5);
        may2024.TotalAmount.Should().Be(55); // 25 + 30
        may2024.ConsumptionCount.Should().Be(2);
        may2024.AverageConsumption.Should().Be(27.5); // 55 / 2

        // Check June 2024 summary
        var jun2024 = aggregate.ConsumedHistorySummary.MonthlyData["2024-06"];
        jun2024.Year.Should().Be(2024);
        jun2024.Month.Should().Be(6);
        jun2024.TotalAmount.Should().Be(40);
        jun2024.ConsumptionCount.Should().Be(1);
        jun2024.AverageConsumption.Should().Be(40); // 40 / 1

        // Check LastUpdated was set
        aggregate.ConsumedHistorySummary.LastUpdated.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void SalesHistory_WhenSetToEmpty_ClearsSummary()
    {
        // Arrange
        var aggregate = new CatalogAggregate();

        // First set some data
        aggregate.SalesHistory = new List<CatalogSaleRecord>
        {
            new CatalogSaleRecord { Date = DateTime.Now, SumB2B = 100, SumB2C = 50, AmountB2B = 1, AmountB2C = 1 }
        };

        aggregate.SaleHistorySummary.MonthlyData.Should().HaveCount(1);

        // Act - Set to empty list
        aggregate.SalesHistory = new List<CatalogSaleRecord>();

        // Assert
        aggregate.SaleHistorySummary.MonthlyData.Should().BeEmpty();
        aggregate.SaleHistorySummary.LastUpdated.Should().BeAfter(DateTime.UtcNow.AddMinutes(-1));
    }

    [Fact]
    public void PrimaryData_WhenUpdatedMultipleTimes_SummaryReflectsLatestData()
    {
        // Arrange
        var aggregate = new CatalogAggregate();

        // First update
        aggregate.SalesHistory = new List<CatalogSaleRecord>
        {
            new CatalogSaleRecord { Date = new DateTime(2024, 1, 1), SumB2B = 100, SumB2C = 50, AmountB2B = 1, AmountB2C = 1 }
        };

        var firstUpdate = aggregate.SaleHistorySummary.LastUpdated;
        aggregate.SaleHistorySummary.MonthlyData.Should().HaveCount(1);

        // Wait a bit to ensure timestamp difference
        Thread.Sleep(10);

        // Act - Second update with different data
        aggregate.SalesHistory = new List<CatalogSaleRecord>
        {
            new CatalogSaleRecord { Date = new DateTime(2024, 1, 1), SumB2B = 200, SumB2C = 100, AmountB2B = 2, AmountB2C = 2 },
            new CatalogSaleRecord { Date = new DateTime(2024, 2, 1), SumB2B = 300, SumB2C = 150, AmountB2B = 3, AmountB2C = 3 }
        };

        // Assert
        aggregate.SaleHistorySummary.MonthlyData.Should().HaveCount(2);

        // January should reflect the new data (200 B2B, not 100)
        var jan2024 = aggregate.SaleHistorySummary.MonthlyData["2024-01"];
        jan2024.TotalB2B.Should().Be(200);
        jan2024.TotalB2C.Should().Be(100);

        // Should have February data
        var feb2024 = aggregate.SaleHistorySummary.MonthlyData["2024-02"];
        feb2024.TotalB2B.Should().Be(300);
        feb2024.TotalB2C.Should().Be(150);

        // LastUpdated should be newer
        aggregate.SaleHistorySummary.LastUpdated.Should().BeAfter(firstUpdate);
    }

    [Fact]
    public void UpdateAllSummaries_UpdatesAllThreeSummaryTypes()
    {
        // Arrange
        var aggregate = new CatalogAggregate
        {
            SalesHistory = new List<CatalogSaleRecord>
            {
                new CatalogSaleRecord { Date = DateTime.Now, SumB2B = 100, SumB2C = 50, AmountB2B = 1, AmountB2C = 1 }
            },
            PurchaseHistory = new List<CatalogPurchaseRecord>
            {
                new CatalogPurchaseRecord { Date = DateTime.Now, SupplierName = "Test", Amount = 10, PricePerPiece = 5, PriceTotal = 50 }
            },
            ConsumedHistory = new List<ConsumedMaterialRecord>
            {
                new ConsumedMaterialRecord { Date = DateTime.Now, Amount = 5 }
            }
        };

        var beforeUpdate = DateTime.UtcNow.AddMinutes(-1);

        // Act
        aggregate.UpdateAllSummaries();

        // Assert
        aggregate.SaleHistorySummary.LastUpdated.Should().BeAfter(beforeUpdate);
        aggregate.PurchaseHistorySummary.LastUpdated.Should().BeAfter(beforeUpdate);
        aggregate.ConsumedHistorySummary.LastUpdated.Should().BeAfter(beforeUpdate);

        aggregate.SaleHistorySummary.MonthlyData.Should().NotBeEmpty();
        aggregate.PurchaseHistorySummary.MonthlyData.Should().NotBeEmpty();
        aggregate.ConsumedHistorySummary.MonthlyData.Should().NotBeEmpty();
    }

    [Fact]
    public void SaleHistorySummary_CalculatedProperties_WorkCorrectly()
    {
        // Arrange
        var aggregate = new CatalogAggregate();
        aggregate.SalesHistory = new List<CatalogSaleRecord>
        {
            new CatalogSaleRecord
            {
                Date = new DateTime(2024, 1, 1),
                SumB2B = 1000,
                SumB2C = 500,
                AmountB2B = 10,
                AmountB2C = 5
            }
        };

        // Act & Assert
        var summary = aggregate.SaleHistorySummary.MonthlyData["2024-01"];

        summary.MonthKey.Should().Be("2024-01");
        summary.TotalRevenue.Should().Be(1500); // 1000 + 500
        summary.TotalAmount.Should().Be(15);    // 10 + 5
    }

    [Fact]
    public void EshopPrice_WhenSet_ConveniencePropertiesReturnCorrectValues()
    {
        // Arrange
        var aggregate = new CatalogAggregate();
        var eshopPrice = new ProductPriceEshop
        {
            PriceWithVat = 100.50m,
            PurchasePrice = 80.25m
        };

        // Act
        aggregate.EshopPrice = eshopPrice;

        // Assert
        aggregate.CurrentSellingPrice.Should().Be(100.50m);
        aggregate.CurrentPurchasePrice.Should().Be(80.25m);
        aggregate.SellingPriceWithVat.Should().Be(100.50m);
        aggregate.PurchasePriceWithVat.Should().BeNull();
    }

    [Fact]
    public void ErpPrice_WhenSet_ConveniencePropertiesReturnCorrectValues()
    {
        // Arrange
        var aggregate = new CatalogAggregate();
        var erpPrice = new ProductPriceErp
        {
            PriceWithoutVat = 90.00m,
            PurchasePrice = 70.00m,
            PriceWithVat = 108.90m,
            PurchasePriceWithVat = 84.70m
        };

        // Act
        aggregate.ErpPrice = erpPrice;

        // Assert
        aggregate.CurrentSellingPrice.Should().Be(90.00m);
        aggregate.CurrentPurchasePrice.Should().Be(70.00m);
        aggregate.SellingPriceWithVat.Should().Be(108.90m);
        aggregate.PurchasePriceWithVat.Should().Be(84.70m);
    }

    [Fact]
    public void PriceConvenienceProperties_WhenBothEshopAndErpPricesSet_PrefersEshopPrices()
    {
        // Arrange
        var aggregate = new CatalogAggregate();
        var eshopPrice = new ProductPriceEshop
        {
            PriceWithVat = 100.50m,
            PurchasePrice = 80.25m
        };
        var erpPrice = new ProductPriceErp
        {
            PriceWithoutVat = 90.00m,
            PurchasePrice = 70.00m,
            PriceWithVat = 108.90m,
            PurchasePriceWithVat = 84.70m
        };

        // Act
        aggregate.EshopPrice = eshopPrice;
        aggregate.ErpPrice = erpPrice;

        // Assert - Should prefer eshop prices for current prices
        aggregate.CurrentSellingPrice.Should().Be(100.50m);
        aggregate.CurrentPurchasePrice.Should().Be(80.25m);

        // Should prefer eshop VAT price over ERP VAT price
        aggregate.SellingPriceWithVat.Should().Be(100.50m);
        aggregate.PurchasePriceWithVat.Should().Be(84.70m);
    }

    [Fact]
    public void PriceConvenienceProperties_WhenOnlyErpPriceSet_FallsBackToErpPrices()
    {
        // Arrange
        var aggregate = new CatalogAggregate();
        var erpPrice = new ProductPriceErp
        {
            PriceWithoutVat = 90.00m,
            PurchasePrice = 70.00m,
            PriceWithVat = 108.90m,
            PurchasePriceWithVat = 84.70m
        };

        // Act
        aggregate.ErpPrice = erpPrice;

        // Assert - Should fall back to ERP prices
        aggregate.CurrentSellingPrice.Should().Be(90.00m);
        aggregate.CurrentPurchasePrice.Should().Be(70.00m);
        aggregate.SellingPriceWithVat.Should().Be(108.90m);
        aggregate.PurchasePriceWithVat.Should().Be(84.70m);
    }

    [Fact]
    public void PriceConvenienceProperties_WhenNoPricesSet_ReturnsNull()
    {
        // Arrange
        var aggregate = new CatalogAggregate();

        // Assert
        aggregate.CurrentSellingPrice.Should().BeNull();
        aggregate.CurrentPurchasePrice.Should().BeNull();
        aggregate.SellingPriceWithVat.Should().BeNull();
        aggregate.PurchasePriceWithVat.Should().BeNull();
    }
}