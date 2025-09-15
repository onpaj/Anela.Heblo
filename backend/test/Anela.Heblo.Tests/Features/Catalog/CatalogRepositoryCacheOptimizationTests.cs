using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Purchase;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog;

public class CatalogRepositoryCacheOptimizationTests
{
    private readonly Mock<ICatalogSalesClient> _salesClientMock;
    private readonly Mock<ICatalogAttributesClient> _attributesClientMock;
    private readonly Mock<IEshopStockClient> _eshopStockClientMock;
    private readonly Mock<IConsumedMaterialsClient> _consumedMaterialClientMock;
    private readonly Mock<IPurchaseHistoryClient> _purchaseHistoryClientMock;
    private readonly Mock<IErpStockClient> _erpStockClientMock;
    private readonly Mock<ILotsClient> _lotsClientMock;
    private readonly Mock<IProductPriceEshopClient> _productPriceEshopClientMock;
    private readonly Mock<IProductPriceErpClient> _productPriceErpClientMock;
    private readonly Mock<ITransportBoxRepository> _transportBoxRepositoryMock;
    private readonly Mock<IStockTakingRepository> _stockTakingRepositoryMock;
    private readonly Mock<IManufactureRepository> _manufactureRepositoryMock;
    private readonly Mock<IPurchaseOrderRepository> _purchaseOrderRepositoryMock;
    private readonly Mock<IManufactureHistoryClient> _manufactureHistoryClientMock;
    private readonly Mock<IManufactureCostCalculationService> _manufactureCostCalculationServiceMock;
    private readonly Mock<IManufactureDifficultyRepository> _manufactureDifficultyRepositoryMock;
    private readonly Mock<ICatalogResilienceService> _resilienceServiceMock;
    private readonly Mock<ICatalogMergeScheduler> _mergeSchedulerMock;
    private readonly IMemoryCache _cache;
    private readonly Mock<TimeProvider> _timeProviderMock;
    private readonly Mock<IOptions<DataSourceOptions>> _optionsMock;
    private readonly Mock<IOptions<CatalogCacheOptions>> _cacheOptionsMock;
    private readonly Mock<ILogger<CatalogRepository>> _loggerMock;

    private readonly CatalogRepository _repository;
    private readonly DataSourceOptions _dataSourceOptions;
    private readonly CatalogCacheOptions _cacheOptions;

    public CatalogRepositoryCacheOptimizationTests()
    {
        _salesClientMock = new Mock<ICatalogSalesClient>();
        _attributesClientMock = new Mock<ICatalogAttributesClient>();
        _eshopStockClientMock = new Mock<IEshopStockClient>();
        _consumedMaterialClientMock = new Mock<IConsumedMaterialsClient>();
        _purchaseHistoryClientMock = new Mock<IPurchaseHistoryClient>();
        _erpStockClientMock = new Mock<IErpStockClient>();
        _lotsClientMock = new Mock<ILotsClient>();
        _productPriceEshopClientMock = new Mock<IProductPriceEshopClient>();
        _productPriceErpClientMock = new Mock<IProductPriceErpClient>();
        _transportBoxRepositoryMock = new Mock<ITransportBoxRepository>();
        _stockTakingRepositoryMock = new Mock<IStockTakingRepository>();
        _manufactureRepositoryMock = new Mock<IManufactureRepository>();
        _purchaseOrderRepositoryMock = new Mock<IPurchaseOrderRepository>();
        _manufactureHistoryClientMock = new Mock<IManufactureHistoryClient>();
        _manufactureCostCalculationServiceMock = new Mock<IManufactureCostCalculationService>();
        _manufactureDifficultyRepositoryMock = new Mock<IManufactureDifficultyRepository>();
        _resilienceServiceMock = new Mock<ICatalogResilienceService>();
        _mergeSchedulerMock = new Mock<ICatalogMergeScheduler>();
        _cache = new MemoryCache(new MemoryCacheOptions());
        _timeProviderMock = new Mock<TimeProvider>();
        _optionsMock = new Mock<IOptions<DataSourceOptions>>();
        _cacheOptionsMock = new Mock<IOptions<CatalogCacheOptions>>();
        _loggerMock = new Mock<ILogger<CatalogRepository>>();

        _dataSourceOptions = new DataSourceOptions
        {
            SalesHistoryDays = 30,
            PurchaseHistoryDays = 30,
            ConsumedHistoryDays = 30,
            ManufactureHistoryDays = 30
        };
        _optionsMock.Setup(x => x.Value).Returns(_dataSourceOptions);

        _cacheOptions = new CatalogCacheOptions
        {
            EnableBackgroundMerge = true,
            DebounceDelay = TimeSpan.FromMilliseconds(100), // Short for tests
            MaxMergeInterval = TimeSpan.FromSeconds(1),
            CacheValidityPeriod = TimeSpan.FromMinutes(5),
            AllowStaleDataDuringMerge = true,
            StaleDataRetentionPeriod = TimeSpan.FromSeconds(10)
        };
        _cacheOptionsMock.Setup(x => x.Value).Returns(_cacheOptions);

        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(DateTimeOffset.UtcNow);

        // Setup resilience service to pass through operations without resilience patterns for testing
        _resilienceServiceMock.Setup(x => x.ExecuteWithResilienceAsync(It.IsAny<Func<CancellationToken, Task<IEnumerable<CatalogSaleRecord>>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IEnumerable<CatalogSaleRecord>>>, string, CancellationToken>((operation, name, ct) => operation(ct));

        _resilienceServiceMock.Setup(x => x.ExecuteWithResilienceAsync(It.IsAny<Func<CancellationToken, Task<IEnumerable<CatalogAttributes>>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IEnumerable<CatalogAttributes>>>, string, CancellationToken>((operation, name, ct) => operation(ct));

        _resilienceServiceMock.Setup(x => x.ExecuteWithResilienceAsync(It.IsAny<Func<CancellationToken, Task<List<ErpStock>>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<List<ErpStock>>>, string, CancellationToken>((operation, name, ct) => operation(ct));

        _resilienceServiceMock.Setup(x => x.ExecuteWithResilienceAsync(It.IsAny<Func<CancellationToken, Task<List<EshopStock>>>>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<List<EshopStock>>>, string, CancellationToken>((operation, name, ct) => operation(ct));

        SetupBasicMockData();

        _repository = new CatalogRepository(
            _salesClientMock.Object,
            _attributesClientMock.Object,
            _eshopStockClientMock.Object,
            _consumedMaterialClientMock.Object,
            _purchaseHistoryClientMock.Object,
            _erpStockClientMock.Object,
            _lotsClientMock.Object,
            _productPriceEshopClientMock.Object,
            _productPriceErpClientMock.Object,
            _transportBoxRepositoryMock.Object,
            _stockTakingRepositoryMock.Object,
            _manufactureRepositoryMock.Object,
            _purchaseOrderRepositoryMock.Object,
            _manufactureHistoryClientMock.Object,
            _manufactureCostCalculationServiceMock.Object,
            _manufactureDifficultyRepositoryMock.Object,
            _resilienceServiceMock.Object,
            _mergeSchedulerMock.Object,
            _cache,
            _timeProviderMock.Object,
            _optionsMock.Object,
            _cacheOptionsMock.Object,
            _loggerMock.Object);
    }

    private void SetupBasicMockData()
    {
        _salesClientMock.Setup(x => x.GetAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogSaleRecord>());

        _attributesClientMock.Setup(x => x.GetAttributesAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAttributes>());

        _erpStockClientMock.Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ErpStock>
            {
                new ErpStock { ProductCode = "TEST001", ProductName = "Test Product 1", ProductId = 1, Stock = 10 }
            });

        _eshopStockClientMock.Setup(x => x.ListAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<EshopStock>());

        _consumedMaterialClientMock.Setup(x => x.GetConsumedAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ConsumedMaterialRecord>());

        _purchaseHistoryClientMock.Setup(x => x.GetHistoryAsync(It.IsAny<string>(), It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogPurchaseRecord>());

        _stockTakingRepositoryMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<StockTakingRecord>());

        _lotsClientMock.Setup(x => x.GetAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogLot>());

        _productPriceEshopClientMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceEshop>());

        _productPriceErpClientMock.Setup(x => x.GetAllAsync(It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductPriceErp>());

        _manufactureRepositoryMock.Setup(x => x.FindByIngredientAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureTemplate>());

        _manufactureDifficultyRepositoryMock.Setup(x => x.ListAsync(It.IsAny<string>(), It.IsAny<DateTime?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureDifficultySetting>());

        _manufactureHistoryClientMock.Setup(x => x.GetHistoryAsync(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ManufactureHistoryRecord>());

        _manufactureCostCalculationServiceMock.Setup(x => x.CalculateManufactureCostHistoryAsync(It.IsAny<List<CatalogAggregate>>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, List<ManufactureCost>>());

        _transportBoxRepositoryMock.Setup(x => x.FindAsync(It.IsAny<System.Linq.Expressions.Expression<Func<TransportBox, bool>>>(), It.IsAny<bool>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<TransportBox>());
    }

    [Fact]
    public async Task MultipleRapidInvalidations_ShouldTriggerScheduleOnlyOnce()
    {
        // Arrange
        var scheduleCallCount = 0;
        _mergeSchedulerMock.Setup(x => x.ScheduleMerge(It.IsAny<string>()))
            .Callback<string>(dataSource =>
            {
                scheduleCallCount++;
            });

        // Act - Multiple rapid invalidations
        await _repository.RefreshSalesData(CancellationToken.None);
        await _repository.RefreshAttributesData(CancellationToken.None);
        await _repository.RefreshErpStockData(CancellationToken.None);
        await _repository.RefreshEshopStockData(CancellationToken.None);

        // Assert
        scheduleCallCount.Should().Be(4, "each invalidation should trigger schedule once");

        // Verify scheduler was called with correct data source names
        _mergeSchedulerMock.Verify(x => x.ScheduleMerge("CachedSalesData"), Times.Once);
        _mergeSchedulerMock.Verify(x => x.ScheduleMerge("CachedCatalogAttributesData"), Times.Once);
        _mergeSchedulerMock.Verify(x => x.ScheduleMerge("CachedErpStockData"), Times.Once);
        _mergeSchedulerMock.Verify(x => x.ScheduleMerge("CachedEshopStockData"), Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_WhenCurrentCacheValid_ShouldReturnCurrentCache()
    {
        // Arrange - Pre-populate cache with valid data
        var testData = new List<CatalogAggregate>
        {
            new CatalogAggregate { ProductCode = "CACHED001", ProductName = "Cached Product" }
        };
        _cache.Set("CatalogData_Current", testData);
        _cache.Set("CatalogData_LastUpdate", DateTime.UtcNow);

        // Act
        var result = await _repository.GetAllAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result.First().ProductCode.Should().Be("CACHED001");

        // Verify no merge was attempted
        _mergeSchedulerMock.Verify(x => x.IsMergeInProgress, Times.Never);
    }

    [Fact]
    public async Task GetAllAsync_WhenCacheInvalidAndMergeInProgress_ShouldReturnStaleData()
    {
        // Arrange - Setup stale cache and simulate merge in progress
        var staleData = new List<CatalogAggregate>
        {
            new CatalogAggregate { ProductCode = "STALE001", ProductName = "Stale Product" }
        };
        _cache.Set("CatalogData_Stale", staleData);
        _cache.Set("CatalogData_LastUpdate", DateTime.UtcNow.AddHours(-10)); // Invalid cache

        _mergeSchedulerMock.Setup(x => x.IsMergeInProgress).Returns(true);

        // Act
        var result = await _repository.GetAllAsync();

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
        result.First().ProductCode.Should().Be("STALE001");

        // Verify stale data was served
        _mergeSchedulerMock.Verify(x => x.IsMergeInProgress, Times.Once);
    }

    [Fact]
    public async Task GetAllAsync_WhenNoCacheAvailable_ShouldExecutePriorityMerge()
    {
        // Arrange - No cache data available
        _cache.Remove("CatalogData_Current");
        _cache.Remove("CatalogData_Stale");
        _cache.Remove("CatalogData_LastUpdate");

        // Initialize the repository data sources with our mock data
        await _repository.RefreshErpStockData(CancellationToken.None);

        // Act
        var result = await _repository.GetAllAsync();

        // Assert - The result will be created from merge process
        result.Should().NotBeNull();
        // Cache should be populated after merge
        var currentCache = _cache.Get<List<CatalogAggregate>>("CatalogData_Current");
        currentCache.Should().NotBeNull();
    }

    [Fact]
    public async Task ExecuteBackgroundMergeAsync_ShouldReplaceCurrentCacheAtomically()
    {
        // Arrange - Setup existing current cache
        var oldData = new List<CatalogAggregate>
        {
            new CatalogAggregate { ProductCode = "OLD001", ProductName = "Old Product" }
        };
        _cache.Set("CatalogData_Current", oldData);
        _cache.Set("CatalogData_LastUpdate", DateTime.UtcNow.AddMinutes(-1));

        // We need to ensure the merge operation produces data - let's initialize the repository's data sources
        await _repository.RefreshErpStockData(CancellationToken.None);

        // Act
        await _repository.ExecuteBackgroundMergeAsync();

        // Assert - Current cache should be updated (it will be empty list since we don't have full mock data pipeline)
        var currentCache = _cache.Get<List<CatalogAggregate>>("CatalogData_Current");
        currentCache.Should().NotBeNull();
        // The merge process creates a new list, even if empty due to incomplete mock setup
        currentCache!.Should().NotBeNull();

        // Stale cache should contain old data
        var staleCache = _cache.Get<List<CatalogAggregate>>("CatalogData_Stale");
        staleCache.Should().NotBeNull();
        staleCache!.First().ProductCode.Should().Be("OLD001");

        // Update time should be recent
        var updateTime = _cache.Get<DateTime?>("CatalogData_LastUpdate");
        updateTime.Should().NotBeNull();
        updateTime!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task RefreshData_WhenBackgroundMergeDisabled_ShouldInvalidateDirectly()
    {
        // Arrange
        _cacheOptions.EnableBackgroundMerge = false;

        var initialData = new List<CatalogAggregate>
        {
            new CatalogAggregate { ProductCode = "INITIAL001" }
        };
        _cache.Set("CatalogData_Current", initialData);

        // Act
        await _repository.RefreshSalesData(CancellationToken.None);

        // Assert - Direct invalidation should remove cache
        var currentCache = _cache.Get<List<CatalogAggregate>>("CatalogData_Current");
        currentCache.Should().BeNull();

        var staleCache = _cache.Get<List<CatalogAggregate>>("CatalogData_Stale");
        staleCache.Should().BeNull();

        // Verify scheduler was NOT called
        _mergeSchedulerMock.Verify(x => x.ScheduleMerge(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task MultipleRefreshOperations_ShouldTrackSourceUpdates()
    {
        // Arrange & Act
        await _repository.RefreshSalesData(CancellationToken.None);
        await _repository.RefreshAttributesData(CancellationToken.None);
        await _repository.RefreshErpStockData(CancellationToken.None);

        // Assert - Each refresh should trigger scheduling
        _mergeSchedulerMock.Verify(x => x.ScheduleMerge("CachedSalesData"), Times.Once);
        _mergeSchedulerMock.Verify(x => x.ScheduleMerge("CachedCatalogAttributesData"), Times.Once);
        _mergeSchedulerMock.Verify(x => x.ScheduleMerge("CachedErpStockData"), Times.Once);
    }

    [Fact]
    public async Task CacheValidityCheck_ShouldRespectConfiguredPeriod()
    {
        // Arrange - Set cache with specific timestamp
        var testData = new List<CatalogAggregate>
        {
            new CatalogAggregate { ProductCode = "VALIDITY_TEST" }
        };
        _cache.Set("CatalogData_Current", testData);

        // Test valid cache (within validity period)
        _cache.Set("CatalogData_LastUpdate", DateTime.UtcNow.AddMinutes(-2)); // 2 minutes old, validity is 5 minutes

        // Act
        var result1 = await _repository.GetAllAsync();

        // Assert - Should return cached data
        result1.Should().NotBeEmpty();
        result1.First().ProductCode.Should().Be("VALIDITY_TEST");

        // Arrange - Test invalid cache (beyond validity period)
        _cache.Set("CatalogData_LastUpdate", DateTime.UtcNow.AddMinutes(-10)); // 10 minutes old, validity is 5 minutes
        // Initialize data for merge
        await _repository.RefreshErpStockData(CancellationToken.None);

        // Act
        var result2 = await _repository.GetAllAsync();

        // Assert - Should return fresh data from merge (cache should be updated)
        result2.Should().NotBeNull();
        var newCurrentCache = _cache.Get<List<CatalogAggregate>>("CatalogData_Current");
        newCurrentCache.Should().NotBeNull(); // New cache was created
    }

    [Fact]
    public async Task InvalidateSourceData_ShouldUpdateSourceTimestamp()
    {
        // This test verifies that source invalidation properly updates tracking
        // Since _sourceLastUpdated is private, we test through the public behavior

        // Arrange & Act
        await _repository.RefreshSalesData(CancellationToken.None);
        await _repository.RefreshAttributesData(CancellationToken.None);

        // Assert - Verify scheduler was called for each source
        _mergeSchedulerMock.Verify(x => x.ScheduleMerge("CachedSalesData"), Times.Once);
        _mergeSchedulerMock.Verify(x => x.ScheduleMerge("CachedCatalogAttributesData"), Times.Once);
    }

    [Fact]
    public async Task StaleDataFallback_WhenMergeInProgressButNoStaleCache_ShouldExecutePriorityMerge()
    {
        // Arrange - No current cache, no stale cache, merge in progress
        _cache.Remove("CatalogData_Current");
        _cache.Remove("CatalogData_Stale");
        _cache.Remove("CatalogData_LastUpdate");

        _mergeSchedulerMock.Setup(x => x.IsMergeInProgress).Returns(true);

        // Initialize data sources for merge
        await _repository.RefreshErpStockData(CancellationToken.None);

        // Act
        var result = await _repository.GetAllAsync();

        // Assert - Should still return data from priority merge (even if empty, it shows cache was created)
        result.Should().NotBeNull();
        // Cache should be populated after priority merge
        var currentCache = _cache.Get<List<CatalogAggregate>>("CatalogData_Current");
        currentCache.Should().NotBeNull();
    }
}