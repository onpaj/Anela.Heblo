using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogDetail;
using Anela.Heblo.Application.Shared;
using FluentAssertions;
using FluentAssertions;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using FluentAssertions;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using FluentAssertions;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using FluentAssertions;
using AutoMapper;
using FluentAssertions;
using Moq;
using FluentAssertions;

namespace Anela.Heblo.Tests.Features.Catalog;

public class GetCatalogDetailHandlerTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly GetCatalogDetailHandler _handler;

    public GetCatalogDetailHandlerTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _mapperMock = new Mock<IMapper>();
        _timeProviderMock = new Mock<TimeProvider>();
        _handler = new GetCatalogDetailHandler(_catalogRepositoryMock.Object, _mapperMock.Object, _timeProviderMock.Object);
    }

    [Fact]
    public async Task Handle_Should_Return_Response_With_Default_13_Months()
    {
        // Arrange
        var currentDate = new DateTime(2024, 6, 15);
        var request = new GetCatalogDetailRequest
        {
            ProductCode = "TEST001"
            // MonthsBack is default 13
        };

        var catalogItem = CreateTestCatalogAggregate();
        var catalogItemDto = new CatalogItemDto
        {
            ProductCode = "TEST001",
            ProductName = "Test Product",
            Price = new PriceDto()
        };

        _timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(new DateTimeOffset(currentDate));
        _catalogRepositoryMock
            .Setup(r => r.SingleOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItem);
        _mapperMock.Setup(m => m.Map<CatalogItemDto>(catalogItem)).Returns(catalogItemDto);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.Item.Should().Be(catalogItemDto);
        result.HistoricalData.Should().NotBeNull();

        // Verify that historical data filters to last 13 months (from 2023-05-15 onwards)
        var expectedFromDate = currentDate.AddMonths(-13); // 2023-05-15
        var expectedFromKey = $"{expectedFromDate.Year:D4}-{expectedFromDate.Month:D2}"; // "2023-05"

        // Sales history should only include months >= "2023-05"
        Assert.All(result.HistoricalData.SalesHistory, sale =>
        {
            var saleKey = $"{sale.Year:D4}-{sale.Month:D2}";
            Assert.True(string.Compare(saleKey, expectedFromKey, StringComparison.Ordinal) >= 0);
        });

        _catalogRepositoryMock.Verify(r => r.SingleOrDefaultAsync(
            It.IsAny<System.Linq.Expressions.Expression<System.Func<CatalogAggregate, bool>>>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(6)]
    [InlineData(12)]
    [InlineData(24)]
    public async Task Handle_Should_Use_Custom_MonthsBack_Value(int monthsBack)
    {
        // Arrange
        var currentDate = new DateTime(2024, 6, 15);
        var request = new GetCatalogDetailRequest
        {
            ProductCode = "TEST001",
            MonthsBack = monthsBack
        };

        var catalogItem = CreateTestCatalogAggregate();
        var catalogItemDto = new CatalogItemDto
        {
            ProductCode = "TEST001",
            ProductName = "Test Product",
            Price = new PriceDto()
        };

        _timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(new DateTimeOffset(currentDate));
        _catalogRepositoryMock
            .Setup(r => r.SingleOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItem);
        _mapperMock.Setup(m => m.Map<CatalogItemDto>(catalogItem)).Returns(catalogItemDto);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        var expectedFromDate = currentDate.AddMonths(-monthsBack);
        var expectedFromKey = $"{expectedFromDate.Year:D4}-{expectedFromDate.Month:D2}";

        // Verify all historical data respects the monthsBack parameter
        Assert.All(result.HistoricalData.SalesHistory, sale =>
        {
            var saleKey = $"{sale.Year:D4}-{sale.Month:D2}";
            Assert.True(string.Compare(saleKey, expectedFromKey, StringComparison.Ordinal) >= 0);
        });

        Assert.All(result.HistoricalData.PurchaseHistory, purchase =>
        {
            Assert.True(purchase.Date >= expectedFromDate);
        });

        Assert.All(result.HistoricalData.ConsumedHistory, consumed =>
        {
            var consumedKey = $"{consumed.Year:D4}-{consumed.Month:D2}";
            Assert.True(string.Compare(consumedKey, expectedFromKey, StringComparison.Ordinal) >= 0);
        });
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-5)]
    public async Task Handle_Should_Handle_Edge_Cases_For_MonthsBack(int monthsBack)
    {
        // Arrange
        var currentDate = new DateTime(2024, 6, 15);
        var request = new GetCatalogDetailRequest
        {
            ProductCode = "TEST001",
            MonthsBack = monthsBack
        };

        var catalogItem = CreateTestCatalogAggregate();
        var catalogItemDto = new CatalogItemDto
        {
            ProductCode = "TEST001",
            ProductName = "Test Product",
            Price = new PriceDto()
        };

        _timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(new DateTimeOffset(currentDate));
        _catalogRepositoryMock
            .Setup(r => r.SingleOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItem);
        _mapperMock.Setup(m => m.Map<CatalogItemDto>(catalogItem)).Returns(catalogItemDto);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.HistoricalData.Should().NotBeNull();

        // Should not throw exception even with edge case values
        // Results might be empty or include all data depending on the edge case
        result.HistoricalData.SalesHistory.Should().NotBeNull();
        result.HistoricalData.PurchaseHistory.Should().NotBeNull();
        result.HistoricalData.ConsumedHistory.Should().NotBeNull();
    }

    [Fact]
    public async Task Handle_Should_Throw_Exception_When_Product_Not_Found()
    {
        // Arrange
        var request = new GetCatalogDetailRequest
        {
            ProductCode = "NONEXISTENT"
        };

        _catalogRepositoryMock
            .Setup(r => r.SingleOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((CatalogAggregate?)null);

        // Act
        var response = await _handler.Handle(request, CancellationToken.None);

        // Assert
        response.Success.Should().BeFalse();
        response.ErrorCode.Should().Be(ErrorCodes.ProductNotFound);
        response.Params.Should().ContainKey("productCode");
        response.Params!["productCode"].Should().Be("NONEXISTENT");
    }

    [Fact]
    public async Task Handle_Should_Return_Properly_Ordered_Historical_Data()
    {
        // Arrange
        var currentDate = new DateTime(2024, 6, 15);
        var request = new GetCatalogDetailRequest
        {
            ProductCode = "TEST001",
            MonthsBack = 6
        };

        var catalogItem = CreateTestCatalogAggregate();
        var catalogItemDto = new CatalogItemDto
        {
            ProductCode = "TEST001",
            ProductName = "Test Product",
            Price = new PriceDto()
        };

        _timeProviderMock.Setup(tp => tp.GetUtcNow()).Returns(new DateTimeOffset(currentDate));
        _catalogRepositoryMock
            .Setup(r => r.SingleOrDefaultAsync(It.IsAny<System.Linq.Expressions.Expression<System.Func<CatalogAggregate, bool>>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(catalogItem);
        _mapperMock.Setup(m => m.Map<CatalogItemDto>(catalogItem)).Returns(catalogItemDto);

        // Act
        var result = await _handler.Handle(request, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();

        // Verify data is ordered by Year DESC, Month DESC
        for (int i = 0; i < result.HistoricalData.SalesHistory.Count - 1; i++)
        {
            var current = result.HistoricalData.SalesHistory[i];
            var next = result.HistoricalData.SalesHistory[i + 1];

            Assert.True(current.Year >= next.Year);
            if (current.Year == next.Year)
            {
                Assert.True(current.Month >= next.Month);
            }
        }
    }

    private CatalogAggregate CreateTestCatalogAggregate()
    {
        var aggregate = new CatalogAggregate
        {
            Id = "TEST001",
            ProductName = "Test Product"
        };

        // Create test summary data with months spanning multiple years
        aggregate.SaleHistorySummary = new SaleHistorySummary
        {
            MonthlyData = new Dictionary<string, MonthlySalesSummary>
            {
                ["2023-01"] = new MonthlySalesSummary { Year = 2023, Month = 1, TotalB2B = 1000, TotalB2C = 500, AmountB2B = 10, AmountB2C = 5 },
                ["2023-05"] = new MonthlySalesSummary { Year = 2023, Month = 5, TotalB2B = 1200, TotalB2C = 600, AmountB2B = 12, AmountB2C = 6 },
                ["2023-12"] = new MonthlySalesSummary { Year = 2023, Month = 12, TotalB2B = 1500, TotalB2C = 750, AmountB2B = 15, AmountB2C = 7 },
                ["2024-01"] = new MonthlySalesSummary { Year = 2024, Month = 1, TotalB2B = 1100, TotalB2C = 550, AmountB2B = 11, AmountB2C = 5 },
                ["2024-06"] = new MonthlySalesSummary { Year = 2024, Month = 6, TotalB2B = 1300, TotalB2C = 650, AmountB2B = 13, AmountB2C = 6 }
            },
            LastUpdated = DateTime.UtcNow
        };

        // Add individual purchase records instead of just summary data
        aggregate.PurchaseHistory = new List<CatalogPurchaseRecord>
        {
            new CatalogPurchaseRecord
            {
                Date = new DateTime(2023, 1, 15),
                SupplierName = "Supplier A",
                Amount = 50,
                PricePerPiece = 20.0M,
                PriceTotal = 1000.0M,
                DocumentNumber = "DOC001-2023",
                ProductCode = "TEST001"
            },
            new CatalogPurchaseRecord
            {
                Date = new DateTime(2023, 1, 28),
                SupplierName = "Supplier B",
                Amount = 50,
                PricePerPiece = 20.0M,
                PriceTotal = 1000.0M,
                DocumentNumber = "DOC002-2023",
                ProductCode = "TEST001"
            },
            new CatalogPurchaseRecord
            {
                Date = new DateTime(2023, 5, 10),
                SupplierName = "Supplier A",
                Amount = 120,
                PricePerPiece = 20.0M,
                PriceTotal = 2400.0M,
                DocumentNumber = "DOC003-2023",
                ProductCode = "TEST001"
            },
            new CatalogPurchaseRecord
            {
                Date = new DateTime(2024, 1, 5),
                SupplierName = "Supplier C",
                Amount = 110,
                PricePerPiece = 20.0M,
                PriceTotal = 2200.0M,
                DocumentNumber = "DOC004-2024",
                ProductCode = "TEST001"
            }
        };

        // Keep the summary for backward compatibility
        aggregate.PurchaseHistorySummary = new PurchaseHistorySummary
        {
            MonthlyData = new Dictionary<string, MonthlyPurchaseSummary>
            {
                ["2023-01"] = new MonthlyPurchaseSummary
                {
                    Year = 2023,
                    Month = 1,
                    TotalAmount = 100,
                    TotalCost = 2000,
                    AveragePricePerPiece = 20,
                    SupplierBreakdown = new Dictionary<string, SupplierPurchaseSummary>
                    {
                        ["Supplier1"] = new SupplierPurchaseSummary { SupplierName = "Supplier1", Amount = 100, Cost = 2000 }
                    }
                },
                ["2023-05"] = new MonthlyPurchaseSummary
                {
                    Year = 2023,
                    Month = 5,
                    TotalAmount = 120,
                    TotalCost = 2400,
                    AveragePricePerPiece = 20,
                    SupplierBreakdown = new Dictionary<string, SupplierPurchaseSummary>
                    {
                        ["Supplier1"] = new SupplierPurchaseSummary { SupplierName = "Supplier1", Amount = 120, Cost = 2400 }
                    }
                },
                ["2024-01"] = new MonthlyPurchaseSummary
                {
                    Year = 2024,
                    Month = 1,
                    TotalAmount = 110,
                    TotalCost = 2200,
                    AveragePricePerPiece = 20,
                    SupplierBreakdown = new Dictionary<string, SupplierPurchaseSummary>
                    {
                        ["Supplier1"] = new SupplierPurchaseSummary { SupplierName = "Supplier1", Amount = 110, Cost = 2200 }
                    }
                }
            },
            LastUpdated = DateTime.UtcNow
        };

        aggregate.ConsumedHistorySummary = new ConsumedHistorySummary
        {
            MonthlyData = new Dictionary<string, MonthlyConsumedSummary>
            {
                ["2023-01"] = new MonthlyConsumedSummary { Year = 2023, Month = 1, TotalAmount = 50, ConsumptionCount = 5 },
                ["2023-05"] = new MonthlyConsumedSummary { Year = 2023, Month = 5, TotalAmount = 60, ConsumptionCount = 6 },
                ["2024-01"] = new MonthlyConsumedSummary { Year = 2024, Month = 1, TotalAmount = 55, ConsumptionCount = 5 }
            },
            LastUpdated = DateTime.UtcNow
        };

        return aggregate;
    }
}