using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetCatalogDetail;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Services;
using AutoMapper;
using FluentAssertions;
using Moq;

namespace Anela.Heblo.Tests.Features.Catalog;

public class GetCatalogDetailHandlerFullHistoryTests
{
    private readonly Mock<ICatalogRepository> _catalogRepositoryMock;
    private readonly Mock<ILotsClient> _lotsClientMock;
    private readonly Mock<IMapper> _mapperMock;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly Mock<IMarginCalculationService> _marginCalculationServiceMock;
    private readonly GetCatalogDetailHandler _handler;

    public GetCatalogDetailHandlerFullHistoryTests()
    {
        _catalogRepositoryMock = new Mock<ICatalogRepository>();
        _lotsClientMock = new Mock<ILotsClient>();
        _mapperMock = new Mock<IMapper>();
        _timeProviderMock = new Mock<TimeProvider>();
        _marginCalculationServiceMock = new Mock<IMarginCalculationService>();
        _handler = new GetCatalogDetailHandler(_catalogRepositoryMock.Object, _lotsClientMock.Object, _mapperMock.Object, _timeProviderMock.Object, _marginCalculationServiceMock.Object);

        // Setup margin calculation service to return empty history
        _marginCalculationServiceMock.Setup(x => x.GetMarginAsync(
            It.IsAny<CatalogAggregate>(),
            It.IsAny<DateOnly>(),
            It.IsAny<DateOnly>(),
            It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MonthlyMarginHistory());
    }

    [Fact]
    public async Task Handle_Should_Return_All_Records_When_MonthsBack_Is_999()
    {
        // Arrange
        var currentDate = new DateTime(2024, 6, 15);
        var request = new GetCatalogDetailRequest
        {
            ProductCode = "TEST001",
            MonthsBack = 999 // Request full history
        };

        var catalogItem = CreateTestCatalogAggregateWithExtensiveHistory();
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

        // Should return ALL 7 purchase records (including very old ones from 2020)
        result.HistoricalData.PurchaseHistory.Count.Should().Be(7);

        // Verify that we have records spanning multiple years
        var years = result.HistoricalData.PurchaseHistory.Select(p => p.Date.Year).Distinct().ToList();
        years.Should().Contain(2024);
        years.Should().Contain(2023);
        years.Should().Contain(2022);
        years.Should().Contain(2020);

        // Verify records are ordered by date descending (newest first)
        var dates = result.HistoricalData.PurchaseHistory.Select(p => p.Date).ToList();
        for (int i = 0; i < dates.Count - 1; i++)
        {
            (dates[i] >= dates[i + 1]).Should().BeTrue("Records should be ordered by date descending");
        }
    }

    [Fact]
    public async Task Handle_Should_Filter_Records_When_MonthsBack_Is_13()
    {
        // Arrange
        var currentDate = new DateTime(2024, 6, 15);
        var request = new GetCatalogDetailRequest
        {
            ProductCode = "TEST001",
            MonthsBack = 13 // Normal limited history
        };

        var catalogItem = CreateTestCatalogAggregateWithExtensiveHistory();
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

        // Should return only records from last 13 months (from 2023-05-15 onwards)
        // This should exclude the very old records from 2020-2022
        var expectedFromDate = currentDate.AddMonths(-13); // 2023-05-15

        Assert.All(result.HistoricalData.PurchaseHistory, purchase =>
        {
            Assert.True(purchase.Date >= expectedFromDate,
                $"Purchase date {purchase.Date} should be >= {expectedFromDate}");
        });

        // Should have fewer records than full history
        Assert.True(result.HistoricalData.PurchaseHistory.Count < 7,
            "Limited history should have fewer records than full history");
    }

    private CatalogAggregate CreateTestCatalogAggregateWithExtensiveHistory()
    {
        var aggregate = new CatalogAggregate
        {
            Id = "TEST001",
            ProductName = "Test Product"
        };

        // Create extensive purchase history spanning multiple years
        aggregate.PurchaseHistory = new List<CatalogPurchaseRecord>
        {
            // Recent records (within 13 months)
            new CatalogPurchaseRecord
            {
                Date = new DateTime(2024, 1, 15),
                SupplierName = "Recent Supplier A",
                Amount = 200,
                PricePerPiece = 145.50M,
                PriceTotal = 29100.0M,
                DocumentNumber = "RECENT-2024-001",
                ProductCode = "TEST001"
            },
            new CatalogPurchaseRecord
            {
                Date = new DateTime(2023, 12, 10),
                SupplierName = "Recent Supplier B",
                Amount = 150,
                PricePerPiece = 140.0M,
                PriceTotal = 21000.0M,
                DocumentNumber = "RECENT-2023-002",
                ProductCode = "TEST001"
            },
            new CatalogPurchaseRecord
            {
                Date = new DateTime(2023, 8, 5),
                SupplierName = "Recent Supplier C",
                Amount = 100,
                PricePerPiece = 135.0M,
                PriceTotal = 13500.0M,
                DocumentNumber = "RECENT-2023-003",
                ProductCode = "TEST001"
            },
            
            // Older records (should be filtered out with monthsBack=13)  
            new CatalogPurchaseRecord
            {
                Date = new DateTime(2022, 6, 20),
                SupplierName = "Old Supplier D",
                Amount = 500,
                PricePerPiece = 120.0M,
                PriceTotal = 60000.0M,
                DocumentNumber = "OLD-2022-100",
                ProductCode = "TEST001"
            },
            new CatalogPurchaseRecord
            {
                Date = new DateTime(2021, 3, 15),
                SupplierName = "Old Supplier E",
                Amount = 300,
                PricePerPiece = 110.0M,
                PriceTotal = 33000.0M,
                DocumentNumber = "OLD-2021-200",
                ProductCode = "TEST001"
            },
            new CatalogPurchaseRecord
            {
                Date = new DateTime(2020, 12, 1),
                SupplierName = "Ancient Supplier F",
                Amount = 250,
                PricePerPiece = 100.0M,
                PriceTotal = 25000.0M,
                DocumentNumber = "ANCIENT-2020-001",
                ProductCode = "TEST001"
            },
            new CatalogPurchaseRecord
            {
                Date = new DateTime(2020, 1, 10),
                SupplierName = "Very Old Supplier G",
                Amount = 180,
                PricePerPiece = 95.0M,
                PriceTotal = 17100.0M,
                DocumentNumber = "VERYOLD-2020-999",
                ProductCode = "TEST001"
            }
        };

        // Add required summary data (can be minimal for this test)
        aggregate.SaleHistorySummary = new SaleHistorySummary
        {
            MonthlyData = new Dictionary<string, MonthlySalesSummary>(),
            LastUpdated = DateTime.UtcNow
        };

        aggregate.PurchaseHistorySummary = new PurchaseHistorySummary
        {
            MonthlyData = new Dictionary<string, MonthlyPurchaseSummary>(),
            LastUpdated = DateTime.UtcNow
        };

        aggregate.ConsumedHistorySummary = new ConsumedHistorySummary
        {
            MonthlyData = new Dictionary<string, MonthlyConsumedSummary>(),
            LastUpdated = DateTime.UtcNow
        };

        return aggregate;
    }
}