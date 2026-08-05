using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogMergeServiceTests
{
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly Mock<ICatalogMergeScheduler> _schedulerMock = new();
    private readonly Mock<TimeProvider> _timeProviderMock = new();
    private readonly CatalogCacheOptions _cacheOptions = new() { EnableBackgroundMerge = true };

    private (CatalogCacheStore store, CatalogMergeService service) Create()
    {
        _timeProviderMock.Setup(t => t.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
        var store = new CatalogCacheStore(
            _cache,
            _timeProviderMock.Object,
            Options.Create(_cacheOptions),
            _schedulerMock.Object,
            Mock.Of<ILogger<CatalogCacheStore>>());
        var service = new CatalogMergeService(
            store,
            _timeProviderMock.Object,
            Mock.Of<ILogger<CatalogMergeService>>());
        return (store, service);
    }

    [Fact]
    public async Task ExecutePriorityMergeAsync_WithErpStockOnly_SeedsProductsFromErpStock()
    {
        var (store, service) = Create();
        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "P1", ProductName = "Product 1", ProductId = 1, Stock = 5 },
            new() { ProductCode = "P2", ProductName = "Product 2", ProductId = 2, Stock = 10 },
        });

        var result = await service.ExecutePriorityMergeAsync();

        result.Should().HaveCount(2);
        result.Should().Contain(p => p.ProductCode == "P1" && p.ProductName == "Product 1");
        store.LastMergeDateTime.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecutePriorityMergeAsync_PrefixedErpProductCode_BecomesProductTypeSet()
    {
        var (store, service) = Create();
        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "BAL001", ProductName = "Bundle 1", ProductId = 10, Stock = 0, ProductTypeId = (int)ProductType.Product },
            new() { ProductCode = "REG001", ProductName = "Regular 1", ProductId = 11, Stock = 0, ProductTypeId = (int)ProductType.Product },
        });

        var result = await service.ExecutePriorityMergeAsync();

        result.Single(p => p.ProductCode == "BAL001").Type.Should().Be(ProductType.Set);
        result.Single(p => p.ProductCode == "REG001").Type.Should().Be(ProductType.Product);
    }

    [Fact]
    public async Task ExecutePriorityMergeAsync_SecondMergePass_DoesNotMutatePreviousInstances()
    {
        var (store, service) = Create();
        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "P1", ProductName = "Product 1", ProductId = 1, Stock = 5 },
        });

        var firstResult = await service.ExecutePriorityMergeAsync();
        var firstProduct = firstResult.Single(p => p.ProductCode == "P1");
        var firstStock = firstProduct.Stock;
        var firstProperties = firstProduct.Properties;

        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "P1", ProductName = "Product 1 Updated", ProductId = 1, Stock = 99 },
        });

        var secondResult = await service.ExecutePriorityMergeAsync();
        var secondProduct = secondResult.Single(p => p.ProductCode == "P1");

        // Previously captured instance must remain exactly as it was after the first pass.
        firstProduct.Stock.Erp.Should().Be(5);
        firstProduct.ProductName.Should().Be("Product 1");

        // The second pass reflects the new source data.
        secondProduct.Stock.Erp.Should().Be(99);
        secondProduct.ProductName.Should().Be("Product 1 Updated");

        // Nothing is shared between the two generations.
        secondProduct.Should().NotBeSameAs(firstProduct);
        secondProduct.Stock.Should().NotBeSameAs(firstStock);
        secondProduct.Properties.Should().NotBeSameAs(firstProperties);
    }

    [Fact]
    public async Task ExecutePriorityMergeAsync_ProductMissingFromAttributesMapOnSubsequentPass_KeepsPreviousAttributeValues()
    {
        var (store, service) = Create();
        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "P1", ProductName = "Product 1", ProductId = 1, Stock = 5 },
        });
        store.SetCatalogAttributesData(new List<CatalogAttributes>
        {
            new() { ProductCode = "P1", OptimalStockDays = 7, StockMin = 3 },
        });

        var firstResult = await service.ExecutePriorityMergeAsync();
        firstResult.Single(p => p.ProductCode == "P1").Properties.OptimalStockDaysSetup.Should().Be(7);

        // Second pass: the attributes source no longer has an entry for this product.
        store.SetCatalogAttributesData(new List<CatalogAttributes>());

        var secondResult = await service.ExecutePriorityMergeAsync();
        var secondProduct = secondResult.Single(p => p.ProductCode == "P1");

        secondProduct.Properties.OptimalStockDaysSetup.Should().Be(7);
        secondProduct.Properties.StockMinSetup.Should().Be(3);
    }

    [Fact]
    public async Task ExecutePriorityMergeAsync_SalesHistoryChangesBetweenPasses_SummaryInstanceIsIsolated()
    {
        var (store, service) = Create();
        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "P1", ProductName = "Product 1", ProductId = 1, Stock = 5 },
        });
        store.SetSalesData(new List<CatalogSaleRecord>
        {
            new() { ProductCode = "P1", Date = new DateTime(2026, 1, 15), SumB2B = 100, AmountB2B = 1 },
        });

        var firstResult = await service.ExecutePriorityMergeAsync();
        var firstProduct = firstResult.Single(p => p.ProductCode == "P1");
        var firstSummary = firstProduct.SaleHistorySummary;
        firstSummary.MonthlyData.Should().ContainKey("2026-01");

        store.SetSalesData(new List<CatalogSaleRecord>
        {
            new() { ProductCode = "P1", Date = new DateTime(2026, 2, 20), SumB2B = 200, AmountB2B = 2 },
        });

        var secondResult = await service.ExecutePriorityMergeAsync();
        var secondProduct = secondResult.Single(p => p.ProductCode == "P1");

        // The instance captured after pass 1 must not be corrupted by pass 2's history update.
        firstSummary.MonthlyData.Should().ContainKey("2026-01").And.NotContainKey("2026-02");

        secondProduct.SaleHistorySummary.MonthlyData.Should().ContainKey("2026-02").And.NotContainKey("2026-01");
        secondProduct.SaleHistorySummary.Should().NotBeSameAs(firstSummary);
    }

    [Fact]
    public async Task ExecutePriorityMergeAsync_ManufactureDifficultySettingsChange_PreviousInstanceUnaffected()
    {
        var (store, service) = Create();
        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "P1", ProductName = "Product 1", ProductId = 1, Stock = 5 },
        });
        store.SetManufactureDifficultySettingsData(new Dictionary<string, List<ManufactureDifficultySetting>>
        {
            ["P1"] = new List<ManufactureDifficultySetting>
            {
                new() { ProductCode = "P1", DifficultyValue = 3 },
            },
        });

        var firstResult = await service.ExecutePriorityMergeAsync();
        var firstProduct = firstResult.Single(p => p.ProductCode == "P1");
        var firstSettings = firstProduct.ManufactureDifficultySettings;
        firstProduct.ManufactureDifficulty.Should().Be(3);

        store.SetManufactureDifficultySettingsData(new Dictionary<string, List<ManufactureDifficultySetting>>
        {
            ["P1"] = new List<ManufactureDifficultySetting>
            {
                new() { ProductCode = "P1", DifficultyValue = 9 },
            },
        });

        var secondResult = await service.ExecutePriorityMergeAsync();
        var secondProduct = secondResult.Single(p => p.ProductCode == "P1");

        firstProduct.ManufactureDifficulty.Should().Be(3);
        secondProduct.ManufactureDifficulty.Should().Be(9);
        secondProduct.ManufactureDifficultySettings.Should().NotBeSameAs(firstSettings);
    }

    [Fact]
    public async Task ExecutePriorityMergeAsync_StockTakingHistoryMissingFromSubsequentPass_CarriesForwardOnANewListInstance()
    {
        var (store, service) = Create();
        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "P1", ProductName = "Product 1", ProductId = 1, Stock = 5 },
        });
        store.SetStockTakingData(new List<StockTakingRecord>
        {
            new() { Code = "P1", Type = StockTakingType.Erp, AmountNew = 5, Date = new DateTime(2026, 1, 1) },
        });

        var firstResult = await service.ExecutePriorityMergeAsync();
        var firstProduct = firstResult.Single(p => p.ProductCode == "P1");
        firstProduct.StockTakingHistory.Should().HaveCount(1);
        var firstHistoryList = firstProduct.StockTakingHistory;

        // Second pass: no stock-taking source data at all for this product.
        store.SetStockTakingData(new List<StockTakingRecord>());

        var secondResult = await service.ExecutePriorityMergeAsync();
        var secondProduct = secondResult.Single(p => p.ProductCode == "P1");

        secondProduct.StockTakingHistory.Should().HaveCount(1);
        secondProduct.StockTakingHistory.Should().NotBeSameAs(firstHistoryList);
    }

    [Fact]
    public async Task ExecutePriorityMergeAsync_AfterCacheSwap_StaleGenerationIsIsolatedFromNextMergePass()
    {
        var (store, service) = Create();
        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "P1", ProductName = "Product 1", ProductId = 1, Stock = 5 },
        });

        await service.ExecutePriorityMergeAsync();
        var currentAfterFirstPass = store.TryGetCurrent()!.Single();

        store.SetErpStockData(new List<ErpStock>
        {
            new() { ProductCode = "P1", ProductName = "Product 1 Updated", ProductId = 1, Stock = 99 },
        });

        await service.ExecutePriorityMergeAsync();
        var staleAfterSecondPass = store.TryGetStale()!.Single();
        var currentAfterSecondPass = store.TryGetCurrent()!.Single();

        // The first pass's instance was promoted to Stale untouched...
        staleAfterSecondPass.Should().BeSameAs(currentAfterFirstPass);
        staleAfterSecondPass.Stock.Erp.Should().Be(5);

        // ...and shares nothing with the instance the second pass produced.
        staleAfterSecondPass.Should().NotBeSameAs(currentAfterSecondPass);
        staleAfterSecondPass.Stock.Should().NotBeSameAs(currentAfterSecondPass.Stock);
        currentAfterSecondPass.Stock.Erp.Should().Be(99);
    }
}
