using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Cache;
using Anela.Heblo.Application.Features.Catalog.Contracts;
using Anela.Heblo.Application.Features.Catalog.CostProviders;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.CostProviders;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.ManufactureHistory;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

/// <summary>
/// The margin history window and the cost provider window are both derived from
/// DataSourceOptions.ManufactureCostHistoryDays. If they drift apart, the uncovered months enter
/// MonthlyMarginHistory.Averages as zeros and silently scale every displayed cost down
/// (365/730 days once produced a factor of 0.6 across the whole page).
///
/// Shares the collection of FlatManufactureCostProviderTests because the provider serialises
/// refreshes on a static lock.
/// </summary>
[Collection("FlatManufactureCostProviderTests")]
public class MarginCostWindowAlignmentTests
{
    private const string ProductCode = "MAS009180";

    [Theory]
    [InlineData(100)]
    [InlineData(365)]
    [InlineData(730)]
    public async Task RefreshMarginData_AveragesOverTheMonthsTheCostProvidersEmit_ForAnyCostWindow(
        int manufactureCostHistoryDays)
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var options = new DataSourceOptions { ManufactureCostHistoryDays = manufactureCostHistoryDays };
        var product = CreateManufacturedProduct(now.UtcDateTime);

        var flatManufactureCostProvider = CreateFlatManufactureCostProvider(product, options);
        await flatManufactureCostProvider.RefreshAsync();

        var emittedCosts = (await flatManufactureCostProvider.GetCostsAsync())[ProductCode];
        var emittedMonths = emittedCosts.Select(c => c.Month).ToList();
        var costPerMonth = emittedCosts.Select(c => c.Cost).Distinct().Single();
        costPerMonth.Should().BeGreaterThan(0, "the provider must emit a real cost for the test to be meaningful");

        var repository = CreateRepository(product, flatManufactureCostProvider, options, now);

        // Act
        await repository.RefreshMarginData(CancellationToken.None);

        // Assert
        var months = product.Margins.MonthlyData.Keys.ToList();
        months.Should().NotBeEmpty();
        months.Should().BeSubsetOf(emittedMonths, "a margin month without cost data averages in as a zero");
        product.Margins.Averages.M1_A.CostLevel.Should().Be(costPerMonth,
            "the average must not be diluted by months the cost providers never covered");
    }

    private static CatalogAggregate CreateManufacturedProduct(DateTime now)
    {
        var product = new CatalogAggregate
        {
            ProductCode = ProductCode,
            Type = ProductType.Product,
            ErpPrice = new ProductPriceErp
            {
                ProductCode = ProductCode,
                PriceWithoutVat = 500m,
                PriceWithVat = 605m
            },
            ManufactureHistory = new List<CatalogManufactureRecord>
            {
                new() { Date = now.AddDays(-10), Amount = 100, ProductCode = ProductCode, PricePerPiece = 79m }
            }
        };

        product.ManufactureDifficultySettings.Assign(
            new List<ManufactureDifficultySetting>
            {
                new() { ProductCode = ProductCode, DifficultyValue = 35, ValidFrom = now.AddYears(-3), ValidTo = null }
            },
            now);

        return product;
    }

    private static FlatManufactureCostProvider CreateFlatManufactureCostProvider(
        CatalogAggregate product,
        DataSourceOptions options)
    {
        var ledgerServiceMock = new Mock<ILedgerService>();
        ledgerServiceMock
            .Setup(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "VYROBA", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CostStatistics>
            {
                new() { Date = DateTime.UtcNow, Cost = 350000m, Department = "VYROBA" }
            });

        var catalogRepositoryMock = new Mock<ICatalogRepository>();
        catalogRepositoryMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate> { product });
        catalogRepositoryMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ICatalogRepository)))
            .Returns(catalogRepositoryMock.Object);

        return new FlatManufactureCostProvider(
            new FlatManufactureCostCache(new MemoryCache(new MemoryCacheOptions())),
            serviceProviderMock.Object,
            ledgerServiceMock.Object,
            Mock.Of<ILogger<FlatManufactureCostProvider>>(),
            Options.Create(options));
    }

    private static CatalogRepository CreateRepository(
        CatalogAggregate product,
        IFlatManufactureCostProvider flatManufactureCostProvider,
        DataSourceOptions options,
        DateTimeOffset now)
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        cache.Set("CatalogData_Current", new List<CatalogAggregate> { product });
        cache.Set("CatalogData_LastUpdate", now.UtcDateTime);

        var timeProviderMock = new Mock<TimeProvider>();
        timeProviderMock.Setup(x => x.GetUtcNow()).Returns(now);

        var mergeSchedulerMock = new Mock<ICatalogMergeScheduler>();
        mergeSchedulerMock.Setup(x => x.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cacheOptions = Options.Create(new CatalogCacheOptions { EnableBackgroundMerge = false });
        var dataSourceOptions = Options.Create(options);

        var cacheStore = new CatalogCacheStore(
            cache,
            timeProviderMock.Object,
            cacheOptions,
            mergeSchedulerMock.Object,
            Mock.Of<ILogger<CatalogCacheStore>>());

        var mergeService = new CatalogMergeService(
            cacheStore,
            new BundleSalesExpander(),
            timeProviderMock.Object,
            Mock.Of<ILogger<CatalogMergeService>>());

        var refreshService = new CatalogDataRefreshService(
            Mock.Of<ICatalogSalesClient>(),
            Mock.Of<ICatalogSetPartsClient>(),
            Mock.Of<ICatalogAttributesClient>(),
            Mock.Of<IEshopStockClient>(),
            Mock.Of<IConsumedMaterialsClient>(),
            Mock.Of<IPurchaseHistoryClient>(),
            Mock.Of<IErpStockClient>(),
            Mock.Of<ILotsClient>(),
            Mock.Of<IProductPriceEshopClient>(),
            Mock.Of<IProductPriceErpClient>(),
            Mock.Of<IProductEshopUrlClient>(),
            Mock.Of<ICatalogTransportSource>(),
            Mock.Of<IStockTakingRepository>(),
            Mock.Of<ICatalogPurchaseSource>(),
            Mock.Of<ICatalogManufactureSource>(),
            Mock.Of<IManufactureDifficultyRepository>(),
            Mock.Of<ICatalogResilienceService>(),
            timeProviderMock.Object,
            dataSourceOptions,
            cacheStore,
            Mock.Of<ILogger<CatalogDataRefreshService>>());

        var marginService = new MarginCalculationService(
            CreateEmptyCostProvider<IMaterialCostProvider>(),
            flatManufactureCostProvider,
            CreateEmptyCostProvider<IDirectManufactureCostProvider>(),
            CreateEmptyCostProvider<ISalesCostProvider>(),
            Mock.Of<ILogger<MarginCalculationService>>());

        return new CatalogRepository(
            cacheStore,
            mergeService,
            refreshService,
            mergeSchedulerMock.Object,
            marginService,
            timeProviderMock.Object,
            dataSourceOptions,
            cacheOptions,
            Mock.Of<ILogger<CatalogRepository>>());
    }

    private static TProvider CreateEmptyCostProvider<TProvider>() where TProvider : class, ICostProvider
    {
        var mock = new Mock<TProvider>();
        mock.Setup(x => x.GetCostsAsync(It.IsAny<List<string>>(), It.IsAny<DateOnly?>(), It.IsAny<DateOnly?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, List<MonthlyCost>>());
        return mock.Object;
    }
}
