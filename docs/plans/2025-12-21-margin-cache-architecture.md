# Margin Calculation Cache Architecture Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Implement in-memory caching architecture for margin calculation cost sources to eliminate redundant ILedgerService queries and improve performance.

**Architecture:**
- Singleton cache services per cost source (M0, M1_A, M1_B, M2) with in-memory storage
- Cache hydration at startup (Tier 2 - after catalog refresh)
- Periodic background refresh with stale-while-revalidate pattern
- Cost sources delegate to cache services instead of direct repository queries

**Tech Stack:**
- IMemoryCache for in-memory storage
- BackgroundRefreshTaskRegistry for periodic updates
- Clean Architecture with domain interfaces + application implementations

---

## Task 1: Create Domain Layer Cache Interfaces

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/Cache/ICostCache.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/Cache/CostCacheData.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/Cache/IMaterialCostCache.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/Cache/IFlatManufactureCostCache.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/Cache/IDirectManufactureCostCache.cs`
- Create: `backend/src/Anela.Heblo.Domain/Features/Catalog/Cache/ISalesCostCache.cs`

### Step 1: Create base cache interface

```csharp
namespace Anela.Heblo.Domain.Features.Catalog.Cache;

/// <summary>
/// Base interface for cost cache services providing cached cost data.
/// </summary>
public interface ICostCache
{
    /// <summary>
    /// Get cached cost data. Returns empty data during initial hydration.
    /// </summary>
    Task<CostCacheData> GetCachedDataAsync(CancellationToken ct = default);

    /// <summary>
    /// Refresh cached data from source repositories.
    /// Uses stale-while-revalidate pattern - keeps old data during refresh.
    /// </summary>
    Task RefreshAsync(CancellationToken ct = default);
}
```

File: `backend/src/Anela.Heblo.Domain/Features/Catalog/Cache/ICostCache.cs`

### Step 2: Create cache data wrapper class

```csharp
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;

namespace Anela.Heblo.Domain.Features.Catalog.Cache;

/// <summary>
/// Immutable wrapper for cached cost data with metadata.
/// </summary>
public class CostCacheData
{
    /// <summary>
    /// Pre-computed cost data per product code.
    /// Key = productCode, Value = monthly costs for that product.
    /// </summary>
    public Dictionary<string, List<MonthlyCost>> ProductCosts { get; init; } = new();

    /// <summary>
    /// Timestamp when cache was last successfully updated.
    /// </summary>
    public DateTime LastUpdated { get; init; }

    /// <summary>
    /// Start date of cached data range.
    /// </summary>
    public DateOnly DataFrom { get; init; }

    /// <summary>
    /// End date of cached data range.
    /// </summary>
    public DateOnly DataTo { get; init; }

    /// <summary>
    /// True if cache has been successfully hydrated at least once.
    /// </summary>
    public bool IsHydrated { get; init; }

    /// <summary>
    /// Creates empty cache data for cold start scenarios.
    /// </summary>
    public static CostCacheData Empty() => new()
    {
        ProductCosts = new Dictionary<string, List<MonthlyCost>>(),
        LastUpdated = DateTime.MinValue,
        DataFrom = DateOnly.MinValue,
        DataTo = DateOnly.MinValue,
        IsHydrated = false
    };
}
```

File: `backend/src/Anela.Heblo.Domain/Features/Catalog/Cache/CostCacheData.cs`

### Step 3: Create specific cache interfaces

```csharp
namespace Anela.Heblo.Domain.Features.Catalog.Cache;

/// <summary>
/// Cache service for M0 (Material) cost data.
/// </summary>
public interface IMaterialCostCache : ICostCache
{
}
```

File: `backend/src/Anela.Heblo.Domain/Features/Catalog/Cache/IMaterialCostCache.cs`

```csharp
namespace Anela.Heblo.Domain.Features.Catalog.Cache;

/// <summary>
/// Cache service for M1_A (Flat Manufacturing) cost data.
/// </summary>
public interface IFlatManufactureCostCache : ICostCache
{
}
```

File: `backend/src/Anela.Heblo.Domain/Features/Catalog/Cache/IFlatManufactureCostCache.cs`

```csharp
namespace Anela.Heblo.Domain.Features.Catalog.Cache;

/// <summary>
/// Cache service for M1_B (Direct Manufacturing) cost data.
/// </summary>
public interface IDirectManufactureCostCache : ICostCache
{
}
```

File: `backend/src/Anela.Heblo.Domain/Features/Catalog/Cache/IDirectManufactureCostCache.cs`

```csharp
namespace Anela.Heblo.Domain.Features.Catalog.Cache;

/// <summary>
/// Cache service for M2 (Sales/Marketing) cost data.
/// </summary>
public interface ISalesCostCache : ICostCache
{
}
```

File: `backend/src/Anela.Heblo.Domain/Features/Catalog/Cache/ISalesCostCache.cs`

### Step 4: Commit domain interfaces

```bash
git add backend/src/Anela.Heblo.Domain/Features/Catalog/Cache/
git commit -m "feat(catalog): add domain interfaces for cost cache architecture"
```

---

## Task 2: Create Cache Configuration Options

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CostCacheOptions.cs`

### Step 1: Create configuration class

```csharp
namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

/// <summary>
/// Configuration options for cost cache services.
/// </summary>
public class CostCacheOptions
{
    public const string SectionName = "CostCache";

    /// <summary>
    /// Interval for periodic refresh (default: 6 hours).
    /// </summary>
    public TimeSpan RefreshInterval { get; set; } = TimeSpan.FromHours(6);

    /// <summary>
    /// Rolling window size for M1_A calculation (default: 12 months).
    /// </summary>
    public int M1A_RollingWindowMonths { get; set; } = 12;

    /// <summary>
    /// Hydration tier for cost cache services (default: 2 - after catalog refresh).
    /// </summary>
    public int HydrationTier { get; set; } = 2;

    /// <summary>
    /// Number of years of historical data to cache (default: 2).
    /// </summary>
    public int HistoricalDataYears { get; set; } = 2;

    /// <summary>
    /// Minimum date for M2 margin data availability.
    /// </summary>
    public DateOnly MinM2DataDate { get; set; } = new DateOnly(2025, 1, 1);
}
```

File: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CostCacheOptions.cs`

### Step 2: Commit configuration

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CostCacheOptions.cs
git commit -m "feat(catalog): add cache configuration options"
```

---

## Task 3: Implement MaterialCostCache (M0)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Cache/MaterialCostCache.cs`

### Step 1: Create MaterialCostCache implementation

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.Cache;

/// <summary>
/// In-memory cache for M0 (Material) cost data.
/// Pre-computes material costs from purchase history/BoM.
/// </summary>
public class MaterialCostCache : IMaterialCostCache
{
    private const string CacheKey = "MaterialCostCache_Data";
    private readonly IMemoryCache _memoryCache;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILogger<MaterialCostCache> _logger;
    private readonly CostCacheOptions _options;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public MaterialCostCache(
        IMemoryCache memoryCache,
        ICatalogRepository catalogRepository,
        ILogger<MaterialCostCache> logger,
        IOptions<CostCacheOptions> options)
    {
        _memoryCache = memoryCache;
        _catalogRepository = catalogRepository;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<CostCacheData> GetCachedDataAsync(CancellationToken ct = default)
    {
        if (_memoryCache.TryGetValue(CacheKey, out CostCacheData? cachedData) && cachedData != null)
        {
            return cachedData;
        }

        // Cold start - return empty data
        _logger.LogWarning("MaterialCostCache not yet hydrated, returning empty data");
        return CostCacheData.Empty();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        // Acquire lock to prevent concurrent refreshes
        if (!await _refreshLock.WaitAsync(0, ct))
        {
            _logger.LogInformation("MaterialCostCache refresh already in progress, skipping");
            return;
        }

        try
        {
            _logger.LogInformation("Starting MaterialCostCache refresh");

            // Wait for catalog merge to complete before calculating costs
            await _catalogRepository.WaitForCurrentMergeAsync(ct);

            var products = await _catalogRepository.GetAllAsync(ct);
            var dateFrom = DateOnly.FromDateTime(DateTime.Now.AddYears(-_options.HistoricalDataYears));
            var dateTo = DateOnly.FromDateTime(DateTime.Now);

            var productCosts = new Dictionary<string, List<MonthlyCost>>();

            foreach (var product in products)
            {
                if (string.IsNullOrEmpty(product.ProductCode))
                    continue;

                var monthlyCosts = CalculateMaterialCosts(product, dateFrom, dateTo);
                productCosts[product.ProductCode] = monthlyCosts;
            }

            var newCacheData = new CostCacheData
            {
                ProductCosts = productCosts,
                LastUpdated = DateTime.UtcNow,
                DataFrom = dateFrom,
                DataTo = dateTo,
                IsHydrated = true
            };

            // Store in cache without expiration (manual refresh only)
            _memoryCache.Set(CacheKey, newCacheData);

            _logger.LogInformation(
                "MaterialCostCache refreshed successfully: {ProductCount} products, {DateRange}",
                productCosts.Count,
                $"{dateFrom} to {dateTo}");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh MaterialCostCache");
            // Keep old data on error (stale-while-revalidate)
            throw;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private List<MonthlyCost> CalculateMaterialCosts(CatalogAggregate product, DateOnly dateFrom, DateOnly dateTo)
    {
        // STUB: Returns purchase price only (per spec section 2.1)
        // Real implementation: use PurchasePriceOnlyMaterialCostSource logic
        var costs = new List<MonthlyCost>();

        if (product.PurchaseHistory == null || !product.PurchaseHistory.Any())
            return costs;

        // Calculate average purchase price per month
        var currentMonth = new DateTime(dateFrom.Year, dateFrom.Month, 1);
        var endMonth = new DateTime(dateTo.Year, dateTo.Month, 1);

        while (currentMonth <= endMonth)
        {
            var monthPurchases = product.PurchaseHistory
                .Where(p => p.Date.Year == currentMonth.Year && p.Date.Month == currentMonth.Month)
                .ToList();

            if (monthPurchases.Any())
            {
                var avgPrice = monthPurchases.Average(p => p.PricePerUnit);
                costs.Add(new MonthlyCost(currentMonth, avgPrice));
            }

            currentMonth = currentMonth.AddMonths(1);
        }

        return costs;
    }
}
```

File: `backend/src/Anela.Heblo.Application/Features/Catalog/Cache/MaterialCostCache.cs`

### Step 2: Commit MaterialCostCache

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Cache/MaterialCostCache.cs
git commit -m "feat(catalog): implement MaterialCostCache for M0 costs"
```

---

## Task 4: Implement FlatManufactureCostCache (M1_A)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Cache/FlatManufactureCostCache.cs`

### Step 1: Create FlatManufactureCostCache implementation

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.Cache;

/// <summary>
/// In-memory cache for M1_A (Flat Manufacturing) cost data.
/// Pre-computes manufacturing costs using 12-month rolling window and ManufactureDifficulty.
/// </summary>
public class FlatManufactureCostCache : IFlatManufactureCostCache
{
    private const string CacheKey = "FlatManufactureCostCache_Data";
    private readonly IMemoryCache _memoryCache;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILedgerService _ledgerService;
    private readonly IManufactureHistoryClient _manufactureHistoryClient;
    private readonly ILogger<FlatManufactureCostCache> _logger;
    private readonly CostCacheOptions _options;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public FlatManufactureCostCache(
        IMemoryCache memoryCache,
        ICatalogRepository catalogRepository,
        ILedgerService ledgerService,
        IManufactureHistoryClient manufactureHistoryClient,
        ILogger<FlatManufactureCostCache> logger,
        IOptions<CostCacheOptions> options)
    {
        _memoryCache = memoryCache;
        _catalogRepository = catalogRepository;
        _ledgerService = ledgerService;
        _manufactureHistoryClient = manufactureHistoryClient;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<CostCacheData> GetCachedDataAsync(CancellationToken ct = default)
    {
        if (_memoryCache.TryGetValue(CacheKey, out CostCacheData? cachedData) && cachedData != null)
        {
            return cachedData;
        }

        _logger.LogWarning("FlatManufactureCostCache not yet hydrated, returning empty data");
        return CostCacheData.Empty();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!await _refreshLock.WaitAsync(0, ct))
        {
            _logger.LogInformation("FlatManufactureCostCache refresh already in progress, skipping");
            return;
        }

        try
        {
            _logger.LogInformation("Starting FlatManufactureCostCache refresh");

            await _catalogRepository.WaitForCurrentMergeAsync(ct);

            var products = await _catalogRepository.GetAllAsync(ct);
            var dateFrom = DateOnly.FromDateTime(DateTime.Now.AddYears(-_options.HistoricalDataYears));
            var dateTo = DateOnly.FromDateTime(DateTime.Now);

            var productCosts = new Dictionary<string, List<MonthlyCost>>();

            // STUB: Returns constant 15 per spec comment (real implementation in future)
            // Real implementation would:
            // 1. Load ledger costs for VYROBA department
            // 2. Load manufacture history for all products
            // 3. Calculate weighted points using ManufactureDifficulty
            // 4. Distribute costs per product per month using 12-month rolling window

            foreach (var product in products)
            {
                if (string.IsNullOrEmpty(product.ProductCode))
                    continue;

                var costs = new List<MonthlyCost>();
                var currentMonth = new DateTime(dateFrom.Year, dateFrom.Month, 1);
                var endMonth = new DateTime(dateTo.Year, dateTo.Month, 1);

                while (currentMonth <= endMonth)
                {
                    // STUB: constant value
                    costs.Add(new MonthlyCost(currentMonth, 15m));
                    currentMonth = currentMonth.AddMonths(1);
                }

                productCosts[product.ProductCode] = costs;
            }

            var newCacheData = new CostCacheData
            {
                ProductCosts = productCosts,
                LastUpdated = DateTime.UtcNow,
                DataFrom = dateFrom,
                DataTo = dateTo,
                IsHydrated = true
            };

            _memoryCache.Set(CacheKey, newCacheData);

            _logger.LogInformation(
                "FlatManufactureCostCache refreshed successfully: {ProductCount} products",
                productCosts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh FlatManufactureCostCache");
            throw;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
```

File: `backend/src/Anela.Heblo.Application/Features/Catalog/Cache/FlatManufactureCostCache.cs`

### Step 2: Commit FlatManufactureCostCache

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Cache/FlatManufactureCostCache.cs
git commit -m "feat(catalog): implement FlatManufactureCostCache for M1_A costs"
```

---

## Task 5: Implement DirectManufactureCostCache (M1_B)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Cache/DirectManufactureCostCache.cs`

### Step 1: Create DirectManufactureCostCache implementation

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.Cache;

/// <summary>
/// In-memory cache for M1_B (Direct Manufacturing) cost data.
/// Pre-computes monthly direct manufacturing costs.
/// </summary>
public class DirectManufactureCostCache : IDirectManufactureCostCache
{
    private const string CacheKey = "DirectManufactureCostCache_Data";
    private readonly IMemoryCache _memoryCache;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILedgerService _ledgerService;
    private readonly IManufactureHistoryClient _manufactureHistoryClient;
    private readonly ILogger<DirectManufactureCostCache> _logger;
    private readonly CostCacheOptions _options;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public DirectManufactureCostCache(
        IMemoryCache memoryCache,
        ICatalogRepository catalogRepository,
        ILedgerService ledgerService,
        IManufactureHistoryClient manufactureHistoryClient,
        ILogger<DirectManufactureCostCache> logger,
        IOptions<CostCacheOptions> options)
    {
        _memoryCache = memoryCache;
        _catalogRepository = catalogRepository;
        _ledgerService = ledgerService;
        _manufactureHistoryClient = manufactureHistoryClient;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<CostCacheData> GetCachedDataAsync(CancellationToken ct = default)
    {
        if (_memoryCache.TryGetValue(CacheKey, out CostCacheData? cachedData) && cachedData != null)
        {
            return cachedData;
        }

        _logger.LogWarning("DirectManufactureCostCache not yet hydrated, returning empty data");
        return CostCacheData.Empty();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!await _refreshLock.WaitAsync(0, ct))
        {
            _logger.LogInformation("DirectManufactureCostCache refresh already in progress, skipping");
            return;
        }

        try
        {
            _logger.LogInformation("Starting DirectManufactureCostCache refresh");

            await _catalogRepository.WaitForCurrentMergeAsync(ct);

            var products = await _catalogRepository.GetAllAsync(ct);
            var dateFrom = DateOnly.FromDateTime(DateTime.Now.AddYears(-_options.HistoricalDataYears));
            var dateTo = DateOnly.FromDateTime(DateTime.Now);

            var productCosts = new Dictionary<string, List<MonthlyCost>>();

            // STUB: Returns constant 15 per spec comment
            // Real implementation per spec section 2.3:
            // 1. Load monthly ledger costs for VYROBA department
            // 2. Load monthly manufacture history per product
            // 3. Calculate weighted points for products manufactured in each month
            // 4. Distribute month's costs proportionally to weighted points
            // 5. Months without production = 0 cost

            foreach (var product in products)
            {
                if (string.IsNullOrEmpty(product.ProductCode))
                    continue;

                var costs = new List<MonthlyCost>();
                var currentMonth = new DateTime(dateFrom.Year, dateFrom.Month, 1);
                var endMonth = new DateTime(dateTo.Year, dateTo.Month, 1);

                while (currentMonth <= endMonth)
                {
                    // STUB: constant value
                    costs.Add(new MonthlyCost(currentMonth, 15m));
                    currentMonth = currentMonth.AddMonths(1);
                }

                productCosts[product.ProductCode] = costs;
            }

            var newCacheData = new CostCacheData
            {
                ProductCosts = productCosts,
                LastUpdated = DateTime.UtcNow,
                DataFrom = dateFrom,
                DataTo = dateTo,
                IsHydrated = true
            };

            _memoryCache.Set(CacheKey, newCacheData);

            _logger.LogInformation(
                "DirectManufactureCostCache refreshed successfully: {ProductCount} products",
                productCosts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh DirectManufactureCostCache");
            throw;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
```

File: `backend/src/Anela.Heblo.Application/Features/Catalog/Cache/DirectManufactureCostCache.cs`

### Step 2: Commit DirectManufactureCostCache

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Cache/DirectManufactureCostCache.cs
git commit -m "feat(catalog): implement DirectManufactureCostCache for M1_B costs"
```

---

## Task 6: Implement SalesCostCache (M2)

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Cache/SalesCostCache.cs`

### Step 1: Create SalesCostCache implementation

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.Cache;

/// <summary>
/// In-memory cache for M2 (Sales/Marketing) cost data.
/// Pre-computes warehouse and marketing costs allocated by sales volume.
/// </summary>
public class SalesCostCache : ISalesCostCache
{
    private const string CacheKey = "SalesCostCache_Data";
    private readonly IMemoryCache _memoryCache;
    private readonly ICatalogRepository _catalogRepository;
    private readonly ILedgerService _ledgerService;
    private readonly ILogger<SalesCostCache> _logger;
    private readonly CostCacheOptions _options;
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public SalesCostCache(
        IMemoryCache memoryCache,
        ICatalogRepository catalogRepository,
        ILedgerService ledgerService,
        ILogger<SalesCostCache> logger,
        IOptions<CostCacheOptions> options)
    {
        _memoryCache = memoryCache;
        _catalogRepository = catalogRepository;
        _ledgerService = ledgerService;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<CostCacheData> GetCachedDataAsync(CancellationToken ct = default)
    {
        if (_memoryCache.TryGetValue(CacheKey, out CostCacheData? cachedData) && cachedData != null)
        {
            return cachedData;
        }

        _logger.LogWarning("SalesCostCache not yet hydrated, returning empty data");
        return CostCacheData.Empty();
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!await _refreshLock.WaitAsync(0, ct))
        {
            _logger.LogInformation("SalesCostCache refresh already in progress, skipping");
            return;
        }

        try
        {
            _logger.LogInformation("Starting SalesCostCache refresh");

            await _catalogRepository.WaitForCurrentMergeAsync(ct);

            var products = await _catalogRepository.GetAllAsync(ct);
            var dateFrom = DateOnly.FromDateTime(DateTime.Now.AddYears(-_options.HistoricalDataYears));
            var dateTo = DateOnly.FromDateTime(DateTime.Now);

            var productCosts = new Dictionary<string, List<MonthlyCost>>();

            // STUB: Returns constant 15 per spec comment
            // Real implementation per spec section 2.4:
            // 1. Load ledger costs for SKLAD + MARKETING departments
            // 2. Load sales history for all products
            // 3. Calculate total sales volume (SumB2B + SumB2C)
            // 4. Distribute costs proportionally to sales volume per product

            foreach (var product in products)
            {
                if (string.IsNullOrEmpty(product.ProductCode))
                    continue;

                var costs = new List<MonthlyCost>();
                var currentMonth = new DateTime(dateFrom.Year, dateFrom.Month, 1);
                var endMonth = new DateTime(dateTo.Year, dateTo.Month, 1);

                while (currentMonth <= endMonth)
                {
                    // STUB: constant value
                    costs.Add(new MonthlyCost(currentMonth, 10m));
                    currentMonth = currentMonth.AddMonths(1);
                }

                productCosts[product.ProductCode] = costs;
            }

            var newCacheData = new CostCacheData
            {
                ProductCosts = productCosts,
                LastUpdated = DateTime.UtcNow,
                DataFrom = dateFrom,
                DataTo = dateTo,
                IsHydrated = true
            };

            _memoryCache.Set(CacheKey, newCacheData);

            _logger.LogInformation(
                "SalesCostCache refreshed successfully: {ProductCount} products",
                productCosts.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh SalesCostCache");
            throw;
        }
        finally
        {
            _refreshLock.Release();
        }
    }
}
```

File: `backend/src/Anela.Heblo.Application/Features/Catalog/Cache/SalesCostCache.cs`

### Step 2: Commit SalesCostCache

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Cache/SalesCostCache.cs
git commit -m "feat(catalog): implement SalesCostCache for M2 costs"
```

---

## Task 7: Modify Cost Sources to Use Cache

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Repositories/PurchasePriceOnlyMaterialCostSource.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Repositories/ManufactureCostSource.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Repositories/DirectManufactureCostSource.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/Repositories/SalesCostSource.cs`

### Step 1: Read current PurchasePriceOnlyMaterialCostSource

```bash
# Review current implementation to understand structure
```

### Step 2: Modify PurchasePriceOnlyMaterialCostSource to inject cache

Find the constructor and `GetCostsAsync` method, then modify:

```csharp
private readonly IMaterialCostCache _cache;

public PurchasePriceOnlyMaterialCostSource(IMaterialCostCache cache)
{
    _cache = cache;
}

public async Task<Dictionary<string, List<MonthlyCost>>> GetCostsAsync(
    List<string>? productCodes = null,
    DateOnly? dateFrom = null,
    DateOnly? dateTo = null,
    CancellationToken cancellationToken = default)
{
    // Delegate to cache
    var cacheData = await _cache.GetCachedDataAsync(cancellationToken);

    if (!cacheData.IsHydrated)
    {
        // Return empty during hydration
        return new Dictionary<string, List<MonthlyCost>>();
    }

    // Filter by requested product codes if specified
    if (productCodes == null || !productCodes.Any())
    {
        return cacheData.ProductCosts;
    }

    return cacheData.ProductCosts
        .Where(kvp => productCodes.Contains(kvp.Key))
        .ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
}
```

### Step 3: Modify ManufactureCostSource to inject cache

Similar changes - inject `IFlatManufactureCostCache` and delegate to `GetCachedDataAsync()`.

### Step 4: Modify DirectManufactureCostSource to inject cache

Similar changes - inject `IDirectManufactureCostCache` and delegate to `GetCachedDataAsync()`.

### Step 5: Modify SalesCostSource to inject cache

Similar changes - inject `ISalesCostCache` and delegate to `GetCachedDataAsync()`.

### Step 6: Commit cost source modifications

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/Repositories/
git commit -m "refactor(catalog): modify cost sources to use cache services"
```

---

## Task 8: Update CatalogModule DI Registration

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:32-108`

### Step 1: Add cache service registrations

Add after line 45 (after cost source registrations):

```csharp
// Register cache services (singleton - in-memory cache)
services.AddSingleton<IMaterialCostCache, MaterialCostCache>();
services.AddSingleton<IFlatManufactureCostCache, FlatManufactureCostCache>();
services.AddSingleton<IDirectManufactureCostCache, DirectManufactureCostCache>();
services.AddSingleton<ISalesCostCache, SalesCostCache>();
```

### Step 2: Add cache options configuration

Add after line 76 (after CatalogCacheOptions):

```csharp
// Configure cost cache options
services.Configure<CostCacheOptions>(options =>
{
    configuration.GetSection(CostCacheOptions.SectionName).Bind(options);
});
```

### Step 3: Commit DI registration

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
git commit -m "feat(catalog): register cache services in DI container"
```

---

## Task 9: Register Background Refresh Tasks

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs:110-241`

### Step 1: Add cache refresh task registrations

Add in `RegisterBackgroundRefreshTasks` method, before the margin calculation task (line 220):

```csharp
// Cost cache refresh tasks (Tier 2 - after catalog refresh)
services.RegisterRefreshTask<IMaterialCostCache>(
    nameof(IMaterialCostCache) + ".RefreshCache",
    (cache, ct) => cache.RefreshAsync(ct)
);

services.RegisterRefreshTask<IFlatManufactureCostCache>(
    nameof(IFlatManufactureCostCache) + ".RefreshCache",
    (cache, ct) => cache.RefreshAsync(ct)
);

services.RegisterRefreshTask<IDirectManufactureCostCache>(
    nameof(IDirectManufactureCostCache) + ".RefreshCache",
    (cache, ct) => cache.RefreshAsync(ct)
);

services.RegisterRefreshTask<ISalesCostCache>(
    nameof(ISalesCostCache) + ".RefreshCache",
    (cache, ct) => cache.RefreshAsync(ct)
);
```

### Step 2: Commit background task registration

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs
git commit -m "feat(catalog): register cache refresh background tasks"
```

---

## Task 10: Add Configuration to appsettings

**Files:**
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

### Step 1: Add CostCache section

Add new configuration section:

```json
{
  "CostCache": {
    "RefreshInterval": "06:00:00",
    "M1A_RollingWindowMonths": 12,
    "HydrationTier": 2,
    "HistoricalDataYears": 2,
    "MinM2DataDate": "2025-01-01"
  }
}
```

### Step 2: Add BackgroundRefresh configuration for cache services

Add to `BackgroundRefresh` section:

```json
{
  "BackgroundRefresh": {
    "IMaterialCostCache.RefreshCache": {
      "InitialDelay": "00:00:00",
      "RefreshInterval": "06:00:00",
      "Enabled": true,
      "HydrationTier": 2
    },
    "IFlatManufactureCostCache.RefreshCache": {
      "InitialDelay": "00:00:00",
      "RefreshInterval": "06:00:00",
      "Enabled": true,
      "HydrationTier": 2
    },
    "IDirectManufactureCostCache.RefreshCache": {
      "InitialDelay": "00:00:00",
      "RefreshInterval": "06:00:00",
      "Enabled": true,
      "HydrationTier": 2
    },
    "ISalesCostCache.RefreshCache": {
      "InitialDelay": "00:00:00",
      "RefreshInterval": "06:00:00",
      "Enabled": true,
      "HydrationTier": 2
    }
  }
}
```

### Step 3: Commit configuration

```bash
git add backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat(catalog): add cache configuration to appsettings"
```

---

## Task 11: Verify Build and Run Tests

### Step 1: Build backend

```bash
cd backend
dotnet build
```

Expected: Build succeeds with no errors

### Step 2: Run existing tests

```bash
dotnet test
```

Expected: All tests pass (cache services return empty data during tests)

### Step 3: Commit if fixes needed

```bash
# If any compilation errors, fix them
git add .
git commit -m "fix(catalog): resolve compilation errors"
```

---

## Task 12: Create Integration Test for Cache Flow

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Cache/MaterialCostCacheTests.cs`

### Step 1: Create test file

```csharp
using Anela.Heblo.Application.Features.Catalog.Cache;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Cache;

public class MaterialCostCacheTests
{
    [Fact]
    public async Task GetCachedDataAsync_BeforeHydration_ReturnsEmptyData()
    {
        // Arrange
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var catalogRepoMock = new Mock<ICatalogRepository>();
        var loggerMock = new Mock<ILogger<MaterialCostCache>>();
        var options = Options.Create(new CostCacheOptions());

        var cache = new MaterialCostCache(memoryCache, catalogRepoMock.Object, loggerMock.Object, options);

        // Act
        var result = await cache.GetCachedDataAsync();

        // Assert
        Assert.False(result.IsHydrated);
        Assert.Empty(result.ProductCosts);
    }

    [Fact]
    public async Task RefreshAsync_StoresDataInCache()
    {
        // Arrange
        var memoryCache = new MemoryCache(new MemoryCacheOptions());
        var catalogRepoMock = new Mock<ICatalogRepository>();
        var loggerMock = new Mock<ILogger<MaterialCostCache>>();
        var options = Options.Create(new CostCacheOptions());

        catalogRepoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        catalogRepoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<CatalogAggregate>());

        var cache = new MaterialCostCache(memoryCache, catalogRepoMock.Object, loggerMock.Object, options);

        // Act
        await cache.RefreshAsync();
        var result = await cache.GetCachedDataAsync();

        // Assert
        Assert.True(result.IsHydrated);
        Assert.NotNull(result.ProductCosts);
    }
}
```

File: `backend/test/Anela.Heblo.Tests/Features/Catalog/Cache/MaterialCostCacheTests.cs`

### Step 2: Run test

```bash
dotnet test --filter MaterialCostCacheTests
```

Expected: Tests pass

### Step 3: Commit test

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/Cache/
git commit -m "test(catalog): add MaterialCostCache integration tests"
```

---

## Task 13: Update Implementation Documentation

**Files:**
- Create: `docs/implementation/margins_cache_implementation.md`

### Step 1: Create implementation doc

```markdown
# Margin Calculation Cache Implementation

## Overview

Implemented in-memory caching architecture for margin calculation cost sources to eliminate redundant ILedgerService queries.

## Architecture

- **Cache Services**: Singleton services per cost source (M0, M1_A, M1_B, M2)
- **Storage**: IMemoryCache (in-memory, no expiration)
- **Hydration**: Tier 2 (after catalog refresh)
- **Refresh**: Periodic via BackgroundRefreshTaskRegistry (6 hours)
- **Pattern**: Stale-while-revalidate (keep old data during refresh)

## Components

### Domain Layer
- `ICostCache` - base cache interface
- `CostCacheData` - immutable cached data wrapper
- `IMaterialCostCache`, `IFlatManufactureCostCache`, `IDirectManufactureCostCache`, `ISalesCostCache` - specific cache interfaces

### Application Layer
- `MaterialCostCache` - M0 cache implementation
- `FlatManufactureCostCache` - M1_A cache implementation
- `DirectManufactureCostCache` - M1_B cache implementation
- `SalesCostCache` - M2 cache implementation
- `CostCacheOptions` - configuration options

### Cost Sources
Modified to inject cache services and delegate to `GetCachedDataAsync()`:
- `PurchasePriceOnlyMaterialCostSource`
- `ManufactureCostSource`
- `DirectManufactureCostSource`
- `SalesCostSource`

## Configuration

appsettings.json:
```json
{
  "CostCache": {
    "RefreshInterval": "06:00:00",
    "M1A_RollingWindowMonths": 12,
    "HydrationTier": 2,
    "HistoricalDataYears": 2
  }
}
```

## Testing

- Integration tests verify cache hydration and data retrieval
- Cache returns empty data before hydration
- Stale-while-revalidate pattern keeps old data on refresh failure

## Future Work

- Implement real M1_A calculation (currently STUB returning constant 15)
- Implement real M1_B calculation (currently STUB returning constant 15)
- Implement real M2 calculation (currently STUB returning constant 10)
- Add monitoring/health checks for cache staleness
```

File: `docs/implementation/margins_cache_implementation.md`

### Step 2: Commit documentation

```bash
git add docs/implementation/margins_cache_implementation.md
git commit -m "docs(catalog): add cache implementation documentation"
```

---

## Summary

**Implementation Complete:**
✅ Domain layer cache interfaces (ICostCache, CostCacheData, specific interfaces)
✅ Application layer cache implementations (4 cache services)
✅ Configuration options (CostCacheOptions)
✅ Cost sources modified to use cache
✅ DI registration in CatalogModule
✅ Background refresh task registration
✅ Configuration in appsettings.json
✅ Integration tests
✅ Documentation

**Performance Impact:**
- MarginCalculationService no longer triggers repeated ILedgerService queries
- Cache pre-computes costs for all products at startup (Tier 2)
- Periodic refresh (6 hours) keeps data fresh
- Stale-while-revalidate prevents request blocking during refresh

**Next Steps:**
- Replace STUB implementations with real calculations per spec
- Add cache monitoring/alerting
- Verify production performance improvements
