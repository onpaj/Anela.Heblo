using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Persistence;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using System.Linq.Expressions;

namespace Anela.Heblo.Tests.Common;

/// <summary>
/// Test factory for ManufactureOrder controller tests with pre-seeded catalog data
/// </summary>
public class ManufactureOrderTestFactory : HebloWebApplicationFactory
{
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        // Call base first to get all the normal service registrations
        base.ConfigureTestServices(services);

        // Replace the catalog repository with a test-specific mock that includes the data needed for controller tests
        var catalogDescriptors = services.Where(d => d.ServiceType == typeof(ICatalogRepository)).ToList();
        foreach (var descriptor in catalogDescriptors)
        {
            services.Remove(descriptor);
        }

        services.AddTransient<ICatalogRepository, TestCatalogRepository>();
    }
}

/// <summary>
/// Test-specific catalog repository with data needed for ManufactureOrder controller tests
/// </summary>
public class TestCatalogRepository : ICatalogRepository
{
    private readonly List<CatalogAggregate> _testData;

    public TestCatalogRepository()
    {
        _testData = new List<CatalogAggregate>
        {
            new()
            {
                ProductCode = "SEMI001",
                ProductName = "Test Semi Product",
                Type = ProductType.Material,
                Properties = new CatalogProperties
                {
                    ExpirationMonths = 12
                }
            },
            new()
            {
                ProductCode = "SEMI002", 
                ProductName = "Test Semi Product 2",
                Type = ProductType.Material,
                Properties = new CatalogProperties
                {
                    ExpirationMonths = 24
                }
            }
        };
    }

    public async Task<CatalogAggregate?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken); // Simulate async operation
        return _testData.FirstOrDefault(x => x.Id == id);
    }

    public async Task<IEnumerable<CatalogAggregate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken);
        return _testData;
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken);
        return _testData.Any(x => x.Id == id);
    }

    public async Task<IEnumerable<CatalogAggregate>> FindAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken);
        var compiled = predicate.Compile();
        return _testData.Where(compiled);
    }

    public async Task<CatalogAggregate?> SingleOrDefaultAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken);
        var compiled = predicate.Compile();
        return _testData.SingleOrDefault(compiled);
    }

    public async Task<bool> AnyAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken);
        var compiled = predicate.Compile();
        return _testData.Any(compiled);
    }

    public async Task<int> CountAsync(Expression<Func<CatalogAggregate, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        await Task.Delay(1, cancellationToken);
        if (predicate == null)
            return _testData.Count;

        var compiled = predicate.Compile();
        return _testData.Count(compiled);
    }

    // Data load timestamps - always return current time for test
    public DateTime? TransportLoadDate => DateTime.UtcNow;
    public DateTime? ReserveLoadDate => DateTime.UtcNow;
    public DateTime? OrderedLoadDate => DateTime.UtcNow;
    public DateTime? SalesLoadDate => DateTime.UtcNow;
    public DateTime? AttributesLoadDate => DateTime.UtcNow;
    public DateTime? ErpStockLoadDate => DateTime.UtcNow;
    public DateTime? EshopStockLoadDate => DateTime.UtcNow;
    public DateTime? PurchaseHistoryLoadDate => DateTime.UtcNow;
    public DateTime? ManufactureHistoryLoadDate => DateTime.UtcNow;
    public DateTime? ConsumedHistoryLoadDate => DateTime.UtcNow;
    public DateTime? StockTakingLoadDate => DateTime.UtcNow;
    public DateTime? LotsLoadDate => DateTime.UtcNow;
    public DateTime? EshopPricesLoadDate => DateTime.UtcNow;
    public DateTime? ErpPricesLoadDate => DateTime.UtcNow;
    public DateTime? ManufactureDifficultySettingsLoadDate => DateTime.UtcNow;
    public DateTime? ManufactureCostLoadDate => DateTime.UtcNow;
    
    // Merge operation tracking - always return current time for test
    public DateTime? LastMergeDateTime => DateTime.UtcNow;
    public bool ChangesPendingForMerge => false;

    // Refresh methods - no-op for test
    public Task RefreshTransportData(CancellationToken ct) => Task.CompletedTask;
    public Task RefreshReserveData(CancellationToken ct) => Task.CompletedTask;
    public Task RefreshOrderedData(CancellationToken ct) => Task.CompletedTask;
    public Task RefreshSalesData(CancellationToken ct) => Task.CompletedTask;
    public Task RefreshAttributesData(CancellationToken ct) => Task.CompletedTask;
    public Task RefreshErpStockData(CancellationToken ct) => Task.CompletedTask;
    public Task RefreshEshopStockData(CancellationToken ct) => Task.CompletedTask;
    public Task RefreshPurchaseHistoryData(CancellationToken ct) => Task.CompletedTask;
    public Task RefreshManufactureHistoryData(CancellationToken ct) => Task.CompletedTask;
    public Task RefreshConsumedHistoryData(CancellationToken ct) => Task.CompletedTask;
    public Task RefreshStockTakingData(CancellationToken ct) => Task.CompletedTask;
    public Task RefreshLotsData(CancellationToken ct) => Task.CompletedTask;
    public Task RefreshEshopPricesData(CancellationToken ct) => Task.CompletedTask;
    public Task RefreshErpPricesData(CancellationToken ct) => Task.CompletedTask;
    public Task RefreshManufactureDifficultySettingsData(string? product, CancellationToken ct) => Task.CompletedTask;

    public Task<List<CatalogAggregate>> GetProductsWithSalesInPeriod(DateTime fromDate, DateTime toDate, ProductType[] productTypes,
        CancellationToken cancellationToken = default)
    {
        var filteredData = _testData
            .Where(x => productTypes.Contains(x.Type))
            .ToList();
        return Task.FromResult(filteredData);
    }
}