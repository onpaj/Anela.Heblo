using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.UseCases.GetProductStatistics;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.ProductStatistics;

public class GetProductStatisticsHandlerTests
{
    private readonly Mock<ICatalogRepository> _repositoryMock = new();

    private GetProductStatisticsHandler CreateHandler() =>
        new(_repositoryMock.Object, NullLogger<GetProductStatisticsHandler>.Instance);

    private void SetupCatalog(params CatalogAggregate[] items)
    {
        _repositoryMock
            .Setup(r => r.GetByIdsAsync(It.IsAny<IEnumerable<string>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items.ToDictionary(i => i.ProductCode, i => i)
                as IReadOnlyDictionary<string, CatalogAggregate>);
    }

    private static CatalogAggregate Product(string code, string name) =>
        new() { ProductCode = code, ProductName = name };

    private static void WithSales(CatalogAggregate item, string monthKey, double amount)
    {
        var parts = monthKey.Split('-');
        item.SaleHistorySummary.MonthlyData[monthKey] = new MonthlySalesSummary
        {
            Year = int.Parse(parts[0]),
            Month = int.Parse(parts[1]),
            AmountB2B = amount,
            AmountB2C = 0,
        };
    }

    private static void WithConsumption(CatalogAggregate item, string monthKey, double amount)
    {
        var parts = monthKey.Split('-');
        item.ConsumedHistorySummary.MonthlyData[monthKey] = new MonthlyConsumedSummary
        {
            Year = int.Parse(parts[0]),
            Month = int.Parse(parts[1]),
            TotalAmount = amount,
        };
    }

    private static void WithPurchase(CatalogAggregate item, string monthKey, double amount)
    {
        var parts = monthKey.Split('-');
        item.PurchaseHistorySummary.MonthlyData[monthKey] = new MonthlyPurchaseSummary
        {
            Year = int.Parse(parts[0]),
            Month = int.Parse(parts[1]),
            TotalAmount = amount,
        };
    }

    private static GetProductStatisticsRequest Request(
        ProductStatisticsMetric metric,
        string from,
        string to,
        params string[] codes) =>
        new()
        {
            ProductCodes = codes.ToList(),
            Metric = metric,
            DateFrom = from,
            DateTo = to,
        };

    [Fact]
    public async Task Handle_SalesMetric_PlacesAmountInMatchingMonth()
    {
        var product = Product("PROD-A", "Krém");
        WithSales(product, "2025-02", 120);
        SetupCatalog(product);

        var result = await CreateHandler().Handle(
            Request(ProductStatisticsMetric.Sales, "2025-01", "2025-03", "PROD-A"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Months.Should().Equal("2025-01", "2025-02", "2025-03");
        result.Products.Should().ContainSingle();
        result.Products[0].ProductCode.Should().Be("PROD-A");
        result.Products[0].ProductName.Should().Be("Krém");
        result.Products[0].Values.Should().Equal(0, 120, 0);
    }

    [Fact]
    public async Task Handle_SalesMetric_SumsB2BAndB2C()
    {
        var product = Product("PROD-A", "Krém");
        product.SaleHistorySummary.MonthlyData["2025-01"] = new MonthlySalesSummary
        {
            Year = 2025,
            Month = 1,
            AmountB2B = 30,
            AmountB2C = 70,
        };
        SetupCatalog(product);

        var result = await CreateHandler().Handle(
            Request(ProductStatisticsMetric.Sales, "2025-01", "2025-01", "PROD-A"),
            CancellationToken.None);

        result.Products[0].Values.Should().Equal(100);
    }

    [Fact]
    public async Task Handle_ConsumptionMetric_ReadsConsumedSummary()
    {
        var product = Product("MAT-1", "Olej");
        WithConsumption(product, "2025-02", 45);
        SetupCatalog(product);

        var result = await CreateHandler().Handle(
            Request(ProductStatisticsMetric.Consumption, "2025-01", "2025-02", "MAT-1"),
            CancellationToken.None);

        result.Products[0].Values.Should().Equal(0, 45);
    }

    [Fact]
    public async Task Handle_PurchaseMetric_ReadsPurchaseSummary()
    {
        var product = Product("MAT-1", "Olej");
        WithPurchase(product, "2025-01", 500);
        SetupCatalog(product);

        var result = await CreateHandler().Handle(
            Request(ProductStatisticsMetric.Purchase, "2025-01", "2025-02", "MAT-1"),
            CancellationToken.None);

        result.Products[0].Values.Should().Equal(500, 0);
    }

    [Fact]
    public async Task Handle_ManufactureMetric_SumsRecordsWithinSameMonth()
    {
        var product = Product("PROD-A", "Krém");
        product.ManufactureHistory = new List<CatalogManufactureRecord>
        {
            new() { Date = new DateTime(2025, 1, 5), Amount = 10, ProductCode = "PROD-A" },
            new() { Date = new DateTime(2025, 1, 22), Amount = 15, ProductCode = "PROD-A" },
            new() { Date = new DateTime(2025, 2, 3), Amount = 7, ProductCode = "PROD-A" },
        };
        SetupCatalog(product);

        var result = await CreateHandler().Handle(
            Request(ProductStatisticsMetric.Manufacture, "2025-01", "2025-02", "PROD-A"),
            CancellationToken.None);

        result.Products[0].Values.Should().Equal(25, 7);
    }

    [Fact]
    public async Task Handle_ManufactureMetric_IgnoresRecordsOutsideRange()
    {
        var product = Product("PROD-A", "Krém");
        product.ManufactureHistory = new List<CatalogManufactureRecord>
        {
            new() { Date = new DateTime(2024, 12, 31), Amount = 99, ProductCode = "PROD-A" },
            new() { Date = new DateTime(2025, 1, 1), Amount = 5, ProductCode = "PROD-A" },
            new() { Date = new DateTime(2025, 3, 1), Amount = 88, ProductCode = "PROD-A" },
        };
        SetupCatalog(product);

        var result = await CreateHandler().Handle(
            Request(ProductStatisticsMetric.Manufacture, "2025-01", "2025-02", "PROD-A"),
            CancellationToken.None);

        result.Products[0].Values.Should().Equal(5, 0);
    }

    [Fact]
    public async Task Handle_MonthWithNoData_YieldsZeroNotGap()
    {
        var product = Product("PROD-A", "Krém");
        WithSales(product, "2025-06", 10);
        SetupCatalog(product);

        var result = await CreateHandler().Handle(
            Request(ProductStatisticsMetric.Sales, "2025-01", "2025-06", "PROD-A"),
            CancellationToken.None);

        result.Products[0].Values.Should().HaveCount(result.Months.Count);
        result.Products[0].Values.Should().Equal(0, 0, 0, 0, 0, 10);
    }

    [Fact]
    public async Task Handle_SeriesOrder_MatchesRequestedProductOrder()
    {
        var a = Product("PROD-A", "Krém");
        var b = Product("PROD-B", "Mýdlo");
        var c = Product("PROD-C", "Balzám");
        SetupCatalog(a, b, c);

        var result = await CreateHandler().Handle(
            Request(ProductStatisticsMetric.Sales, "2025-01", "2025-01", "PROD-C", "PROD-A", "PROD-B"),
            CancellationToken.None);

        result.Products.Select(p => p.ProductCode)
            .Should().Equal("PROD-C", "PROD-A", "PROD-B");
    }

    [Fact]
    public async Task Handle_UnknownProductCode_IsSkippedAndKnownOnesReturned()
    {
        var known = Product("PROD-A", "Krém");
        WithSales(known, "2025-01", 12);
        SetupCatalog(known);

        var result = await CreateHandler().Handle(
            Request(ProductStatisticsMetric.Sales, "2025-01", "2025-01", "PROD-A", "GHOST"),
            CancellationToken.None);

        result.Success.Should().BeTrue();
        result.Products.Should().ContainSingle();
        result.Products[0].ProductCode.Should().Be("PROD-A");
    }

    [Fact]
    public async Task Handle_RangeBoundaries_AreInclusiveOnBothEnds()
    {
        var product = Product("PROD-A", "Krém");
        WithSales(product, "2025-01", 1);
        WithSales(product, "2025-04", 4);
        SetupCatalog(product);

        var result = await CreateHandler().Handle(
            Request(ProductStatisticsMetric.Sales, "2025-01", "2025-04", "PROD-A"),
            CancellationToken.None);

        result.Products[0].Values.Should().Equal(1, 0, 0, 4);
    }
}
