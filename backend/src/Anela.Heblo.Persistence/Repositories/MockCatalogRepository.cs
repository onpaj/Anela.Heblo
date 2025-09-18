using System.Linq.Expressions;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Persistence.Repositories;

public class MockCatalogRepository : ICatalogRepository
{
    private readonly List<CatalogAggregate> _mockData;

    public MockCatalogRepository()
    {
        _mockData = GenerateMockData();
    }

    private List<CatalogAggregate> GenerateMockData()
    {
        return new List<CatalogAggregate>
        {
            // Critical stock items
            new CatalogAggregate
            {
                ProductCode = "MAT001",
                ProductName = "Základní materiál A",
                Type = ProductType.Material,
                Stock = new StockData
                {
                    Erp = 5,
                    Transport = 0,
                    Reserve = 2
                },
                Properties = new CatalogProperties
                {
                    StockMinSetup = 50,
                    OptimalStockDaysSetup = 30
                },
                MinimalOrderQuantity = "100",
                MinimalManufactureQuantity = 50,
                Location = "Sklad A",
                PurchaseHistory = new List<CatalogPurchaseRecord>
                {
                    new()
                    {
                        Date = DateTime.UtcNow.AddDays(-30),
                        SupplierName = "Dodavatel XYZ",
                        Amount = 200,
                        PricePerPiece = 25.50m,
                        PriceTotal = 5100m
                    }
                }.AsReadOnly()
            },
            new CatalogAggregate
            {
                ProductCode = "MAT002",
                ProductName = "Speciální komponenta B",
                Type = ProductType.Material,
                Stock = new StockData
                {
                    Erp = 12,
                    Transport = 0,
                    Reserve = 5,
                },
                Properties = new CatalogProperties
                {
                    StockMinSetup = 30,
                    OptimalStockDaysSetup = 80
                },
                MinimalOrderQuantity = "50",
                MinimalManufactureQuantity = 25,
                Location = "Sklad B",
                PurchaseHistory = new List<CatalogPurchaseRecord>
                {
                    new()
                    {
                        Date = DateTime.UtcNow.AddDays(-15),
                        SupplierName = "ABC Supplies",
                        Amount = 75,
                        PricePerPiece = 42.00m,
                        PriceTotal = 3150m
                    }
                }.AsReadOnly()
            },
            // Low stock items
            new CatalogAggregate
            {
                ProductCode = "GOD001",
                ProductName = "Hotový výrobek Premium",
                Type = ProductType.Goods,
                Stock = new StockData
                {
                    Erp = 45,
                    Transport = 0,
                    Reserve = 10,
                },
                Properties = new CatalogProperties
                {
                    StockMinSetup = 40,
                    OptimalStockDaysSetup = 120
                },
                MinimalOrderQuantity = "20",
                MinimalManufactureQuantity = 10,
                Location = "Expedice",
                PurchaseHistory = new List<CatalogPurchaseRecord>().AsReadOnly()
            },
            // Optimal stock items
            new CatalogAggregate
            {
                ProductCode = "MAT003",
                ProductName = "Standardní materiál C",
                Type = ProductType.Material,
                Stock = new StockData
                {
                    Erp = 150,
                    Transport = 0,
                    Reserve = 20,
                },
                Properties = new CatalogProperties
                {
                    StockMinSetup = 60,
                    OptimalStockDaysSetup = 200
                },
                MinimalOrderQuantity = "100",
                MinimalManufactureQuantity = 50,
                Location = "Sklad C",
                PurchaseHistory = new List<CatalogPurchaseRecord>
                {
                    new()
                    {
                        Date = DateTime.UtcNow.AddDays(-45),
                        SupplierName = "Mega Dodavatel",
                        Amount = 300,
                        PricePerPiece = 18.75m,
                        PriceTotal = 5625m
                    }
                }.AsReadOnly()
            },
            // Overstocked items
            new CatalogAggregate
            {
                ProductCode = "GOD002",
                ProductName = "Luxusní produkt Gold",
                Type = ProductType.Goods,
                Stock = new StockData
                {
                    Erp = 300,
                    Transport = 0,
                    Reserve = 15,
                },
                Properties = new CatalogProperties
                {
                    StockMinSetup = 25,
                    OptimalStockDaysSetup = 100
                },
                MinimalOrderQuantity = "10",
                MinimalManufactureQuantity = 5,
                Location = "VIP Sklad",
                PurchaseHistory = new List<CatalogPurchaseRecord>
                {
                    new()
                    {
                        Date = DateTime.UtcNow.AddDays(-60),
                        SupplierName = "Premium Supplier",
                        Amount = 150,
                        PricePerPiece = 125.00m,
                        PriceTotal = 18750m
                    }
                }.AsReadOnly()
            },
            // Not configured items
            new CatalogAggregate
            {
                ProductCode = "MAT004",
                ProductName = "Nekonfigurovaný materiál",
                Type = ProductType.Material,
                Stock = new StockData
                {
                    Erp = 75,
                    Transport = 0,
                    Reserve = 0,
                },
                Properties = new CatalogProperties
                {
                    StockMinSetup = 0, // Not configured
                    OptimalStockDaysSetup = 0  // Not configured
                },
                MinimalOrderQuantity = "",
                MinimalManufactureQuantity = 0,
                Location = "Temp Sklad",
                PurchaseHistory = new List<CatalogPurchaseRecord>().AsReadOnly()
            }
        };
    }

    public async Task<CatalogAggregate?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
    {
        await Task.Delay(10, cancellationToken); // Simulate async operation
        return _mockData.FirstOrDefault(x => x.Id == id);
    }

    public async Task<IEnumerable<CatalogAggregate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await Task.Delay(50, cancellationToken); // Simulate async operation
        return _mockData;
    }

    public async Task<bool> ExistsAsync(string id, CancellationToken cancellationToken = default)
    {
        await Task.Delay(10, cancellationToken);
        return _mockData.Any(x => x.Id == id);
    }

    public async Task<IEnumerable<CatalogAggregate>> FindAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default)
    {
        await Task.Delay(10, cancellationToken);
        var compiled = predicate.Compile();
        return _mockData.Where(compiled);
    }

    public async Task<CatalogAggregate?> SingleOrDefaultAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default)
    {
        await Task.Delay(10, cancellationToken);
        var compiled = predicate.Compile();
        return _mockData.SingleOrDefault(compiled);
    }

    public async Task<bool> AnyAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default)
    {
        await Task.Delay(10, cancellationToken);
        var compiled = predicate.Compile();
        return _mockData.Any(compiled);
    }

    public async Task<int> CountAsync(Expression<Func<CatalogAggregate, bool>>? predicate = null, CancellationToken cancellationToken = default)
    {
        await Task.Delay(10, cancellationToken);
        if (predicate == null)
            return _mockData.Count;

        var compiled = predicate.Compile();
        return _mockData.Count(compiled);
    }

    // Data load timestamps - always return current time for mock since all data is immediately available
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
    public DateTime? ManufactureDifficultyLoadDate => DateTime.UtcNow;
    public DateTime? ManufactureCostLoadDate => DateTime.UtcNow;
    
    // Merge operation tracking - always return current time for mock
    public DateTime? LastMergeDateTime => DateTime.UtcNow;
    public bool ChangesPendingForMerge => false;

    // Refresh methods - no-op for mock
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
        throw new NotImplementedException();
    }
}