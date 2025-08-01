using Anela.Heblo.Application.Domain.Catalog;
using Anela.Heblo.Application.Domain.Catalog.ConsumedMaterials;
using Anela.Heblo.Application.Domain.Catalog.PurchaseHistory;
using Anela.Heblo.Application.Domain.Catalog.Sales;

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
        Assert.Equal(2, aggregate.SaleHistorySummary.MonthlyData.Count);

        // Check January 2024 summary
        var jan2024 = aggregate.SaleHistorySummary.MonthlyData["2024-01"];
        Assert.Equal(2024, jan2024.Year);
        Assert.Equal(1, jan2024.Month);
        Assert.Equal(1800, jan2024.TotalB2B); // 1000 + 800
        Assert.Equal(700, jan2024.TotalB2C);  // 500 + 200
        Assert.Equal(18, jan2024.AmountB2B);  // 10 + 8
        Assert.Equal(7, jan2024.AmountB2C);   // 5 + 2
        Assert.Equal(2, jan2024.TransactionCount);

        // Check February 2024 summary
        var feb2024 = aggregate.SaleHistorySummary.MonthlyData["2024-02"];
        Assert.Equal(2024, feb2024.Year);
        Assert.Equal(2, feb2024.Month);
        Assert.Equal(1200, feb2024.TotalB2B);
        Assert.Equal(600, feb2024.TotalB2C);
        Assert.Equal(12, feb2024.AmountB2B);
        Assert.Equal(6, feb2024.AmountB2C);
        Assert.Equal(1, feb2024.TransactionCount);

        // Check LastUpdated was set
        Assert.True(aggregate.SaleHistorySummary.LastUpdated > DateTime.UtcNow.AddMinutes(-1));
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
        Assert.Equal(2, aggregate.PurchaseHistorySummary.MonthlyData.Count);

        // Check March 2024 summary
        var mar2024 = aggregate.PurchaseHistorySummary.MonthlyData["2024-03"];
        Assert.Equal(2024, mar2024.Year);
        Assert.Equal(3, mar2024.Month);
        Assert.Equal(150, mar2024.TotalAmount); // 100 + 50
        Assert.Equal(2000, mar2024.TotalCost);  // 1000 + 1000
        Assert.Equal(15, mar2024.AveragePricePerPiece); // (10 + 20) / 2
        Assert.Equal(2, mar2024.PurchaseCount);

        // Check supplier breakdown for March
        Assert.Equal(2, mar2024.SupplierBreakdown.Count);
        Assert.Contains("Supplier A", mar2024.SupplierBreakdown.Keys);
        Assert.Contains("Supplier B", mar2024.SupplierBreakdown.Keys);

        var supplierA = mar2024.SupplierBreakdown["Supplier A"];
        Assert.Equal(100, supplierA.Amount);
        Assert.Equal(1000, supplierA.Cost);
        Assert.Equal(1, supplierA.PurchaseCount);

        // Check April 2024 summary
        var apr2024 = aggregate.PurchaseHistorySummary.MonthlyData["2024-04"];
        Assert.Equal(2024, apr2024.Year);
        Assert.Equal(4, apr2024.Month);
        Assert.Equal(200, apr2024.TotalAmount);
        Assert.Equal(2400, apr2024.TotalCost);
        Assert.Equal(12, apr2024.AveragePricePerPiece);
        Assert.Equal(1, apr2024.PurchaseCount);

        // Check LastUpdated was set
        Assert.True(aggregate.PurchaseHistorySummary.LastUpdated > DateTime.UtcNow.AddMinutes(-1));
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
        Assert.Equal(2, aggregate.ConsumedHistorySummary.MonthlyData.Count);

        // Check May 2024 summary
        var may2024 = aggregate.ConsumedHistorySummary.MonthlyData["2024-05"];
        Assert.Equal(2024, may2024.Year);
        Assert.Equal(5, may2024.Month);
        Assert.Equal(55, may2024.TotalAmount); // 25 + 30
        Assert.Equal(2, may2024.ConsumptionCount);
        Assert.Equal(27.5, may2024.AverageConsumption); // 55 / 2

        // Check June 2024 summary
        var jun2024 = aggregate.ConsumedHistorySummary.MonthlyData["2024-06"];
        Assert.Equal(2024, jun2024.Year);
        Assert.Equal(6, jun2024.Month);
        Assert.Equal(40, jun2024.TotalAmount);
        Assert.Equal(1, jun2024.ConsumptionCount);
        Assert.Equal(40, jun2024.AverageConsumption); // 40 / 1

        // Check LastUpdated was set
        Assert.True(aggregate.ConsumedHistorySummary.LastUpdated > DateTime.UtcNow.AddMinutes(-1));
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

        Assert.Single(aggregate.SaleHistorySummary.MonthlyData);

        // Act - Set to empty list
        aggregate.SalesHistory = new List<CatalogSaleRecord>();

        // Assert
        Assert.Empty(aggregate.SaleHistorySummary.MonthlyData);
        Assert.True(aggregate.SaleHistorySummary.LastUpdated > DateTime.UtcNow.AddMinutes(-1));
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
        Assert.Single(aggregate.SaleHistorySummary.MonthlyData);

        // Wait a bit to ensure timestamp difference
        Thread.Sleep(10);

        // Act - Second update with different data
        aggregate.SalesHistory = new List<CatalogSaleRecord>
        {
            new CatalogSaleRecord { Date = new DateTime(2024, 1, 1), SumB2B = 200, SumB2C = 100, AmountB2B = 2, AmountB2C = 2 },
            new CatalogSaleRecord { Date = new DateTime(2024, 2, 1), SumB2B = 300, SumB2C = 150, AmountB2B = 3, AmountB2C = 3 }
        };

        // Assert
        Assert.Equal(2, aggregate.SaleHistorySummary.MonthlyData.Count);

        // January should reflect the new data (200 B2B, not 100)
        var jan2024 = aggregate.SaleHistorySummary.MonthlyData["2024-01"];
        Assert.Equal(200, jan2024.TotalB2B);
        Assert.Equal(100, jan2024.TotalB2C);

        // Should have February data
        var feb2024 = aggregate.SaleHistorySummary.MonthlyData["2024-02"];
        Assert.Equal(300, feb2024.TotalB2B);
        Assert.Equal(150, feb2024.TotalB2C);

        // LastUpdated should be newer
        Assert.True(aggregate.SaleHistorySummary.LastUpdated > firstUpdate);
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
        Assert.True(aggregate.SaleHistorySummary.LastUpdated > beforeUpdate);
        Assert.True(aggregate.PurchaseHistorySummary.LastUpdated > beforeUpdate);
        Assert.True(aggregate.ConsumedHistorySummary.LastUpdated > beforeUpdate);

        Assert.NotEmpty(aggregate.SaleHistorySummary.MonthlyData);
        Assert.NotEmpty(aggregate.PurchaseHistorySummary.MonthlyData);
        Assert.NotEmpty(aggregate.ConsumedHistorySummary.MonthlyData);
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

        Assert.Equal("2024-01", summary.MonthKey);
        Assert.Equal(1500, summary.TotalRevenue); // 1000 + 500
        Assert.Equal(15, summary.TotalAmount);    // 10 + 5
    }
}