# Decompose CatalogRepository Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Split the 962-line `CatalogRepository` (25 ctor params, five responsibilities) into three focused collaborators — `CatalogCacheStore`, `CatalogMergeService`, `CatalogDataRefreshService` — plus an `IHostedService` that wires the merge callback at startup. `ICatalogRepository`'s 89 consumers must compile unchanged.

**Architecture:** Pure internal refactor behind the existing `ICatalogRepository` seam. `CatalogCacheStore` is the *only* type that touches `IMemoryCache` and owns the dual-key (current/stale) catalog + 19 per-source caches under one semaphore. `CatalogMergeService` (singleton) owns the merge mapping and the priority/background merge entry points. `CatalogDataRefreshService` (transient) owns the 19 `Refresh*Data` methods and the cross-module stock helpers. The merge-callback wiring (currently in `CatalogRepository`'s constructor, where it leaks transient state into a singleton scheduler) moves to `CatalogMergeCallbackWiring : IHostedService`. The slimmed `CatalogRepository` becomes a delegating facade (≤ 250 LOC).

**Tech Stack:** .NET 8 / C# 12, xUnit + Moq + FluentAssertions for tests, `Microsoft.Extensions.Caching.Memory`, `Microsoft.Extensions.Hosting` (for `IHostedService`), MediatR (for handler consumers — unchanged).

---

## File Structure

**Files created (5):**
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogCacheStore.cs` — singleton, owns all `IMemoryCache` access for the catalog. ~450 LOC budget.
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs` — singleton, owns `Merge()`, `ExecuteBackgroundMergeAsync`, `ExecutePriorityMergeAsync`. ~250 LOC budget.
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs` — transient, owns the 19 `Refresh*Data` methods and the cross-module stock helpers. ~350 LOC budget.
- `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeCallbackWiring.cs` — `IHostedService`, wires `ICatalogMergeScheduler.SetMergeCallback(CatalogMergeService.ExecuteBackgroundMergeAsync)` once at startup. ~25 LOC.
- `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogCacheStoreTests.cs` — new test suite. (Plus `CatalogMergeServiceTests.cs` and `CatalogDataRefreshServiceTests.cs` siblings created in their tasks.)

**Files modified (5):**
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs` — reduced to delegating facade (target ≤ 250 LOC).
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — registers the three new types + the hosted service.
- `backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs` — removes `ManufactureCostLoadDate` (FR-5).
- `backend/src/Anela.Heblo.Persistence/Repositories/MockCatalogRepository.cs` — removes `ManufactureCostLoadDate` to match the trimmed interface.
- `backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs` — constructor list shrinks to the four new collaborators; tests that exercise refresh/merge behavior migrate (see Task 9).

**Files moved/migrated (test migrations only — no `git mv`, copy + rewrite):**
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs` — cache-store-specific tests migrate into `CatalogCacheStoreTests.cs`. Remaining tests update their constructor list.
- `backend/test/Anela.Heblo.Tests/Controllers/CatalogRepositoryDebugTest.cs` — update constructor list.

**No frontend changes. No migration. No config schema change.**

---

## Key Invariants (do not violate)

1. **Cache key strings stay identical.** The 19 per-source keys must continue to be the same string as today's `nameof(CachedXxxData)` (e.g. `"CachedSalesData"`, `"CachedCatalogAttributesData"`, …). The aggregate keys (`"CatalogData_Current"`, `"CatalogData_Stale"`, `"CatalogData_LastUpdate"`, `"LastMergeDateTime"`) and load-date suffix (`"{key}_LoadDate"`) also stay identical.
2. **Refresh task IDs stay identical.** `CatalogModule.RegisterBackgroundRefreshTasks` must keep calling `RegisterRefreshTask<ICatalogRepository>(nameof(ICatalogRepository.RefreshXxxData))` — these resolve to task IDs like `"ICatalogRepository.RefreshTransportData"` and back the existing `BackgroundRefresh:ICatalogRepository:*` settings.
3. **Only `CatalogCacheStore` references `IMemoryCache`.** Verified by a final `grep "IMemoryCache" backend/src/Anela.Heblo.Application/Features/Catalog/` showing matches only inside `CatalogCacheStore.cs`.
4. **Merge callback wired exactly once.** `CatalogRepository` must not call `_mergeScheduler.SetMergeCallback(...)` after this refactor.
5. **`Task.Run(() => Merge(), ct)` wrapper preserved** in `CatalogMergeService.ExecuteBackgroundMergeAsync` (offloads CPU work from the timer thread).
6. **`RefreshSalesData` retains stale cache on exception** (the existing `try/catch` and `LogWarning` line — preserve verbatim, only the class containing it changes).
7. **`InvalidateSourceData`'s `EnableBackgroundMerge=false` branch** (current `CatalogRepository.cs:549–556`) — evict current, stale, and update-time keys. Move verbatim into `CatalogCacheStore`.

---

## Task 1: Pre-flight — verify baseline build and tests pass

**Files:**
- None modified

- [ ] **Step 1: Build the solution to confirm green baseline**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet build Anela.Heblo.sln -c Debug 2>&1 | tail -20
```
Expected: `Build succeeded` with 0 errors.

- [ ] **Step 2: Run the catalog test suites that will be touched**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Catalog" \
  --no-build --logger "console;verbosity=minimal" 2>&1 | tail -10
```
Expected: All tests pass. Capture the number of passing tests as the baseline.

- [ ] **Step 3: Verify final cleanup targets exist exactly as the spec says**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi
grep -n "CachedManufactureCostData\|ManufactureCostLoadDate" backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs backend/src/Anela.Heblo.Persistence/Repositories/MockCatalogRepository.cs
```
Expected output (line numbers approximate):
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs:768: private const string CachedManufactureCostDataKey = "CachedManufactureCostData";`
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs:770: [Obsolete(...)]`
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs:771: private IDictionary<string, List<ManufactureCost>> CachedManufactureCostData`
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs:813: public DateTime? ManufactureCostLoadDate => GetLoadDateFromCache(CachedManufactureCostDataKey);`
- `backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs:46: DateTime? ManufactureCostLoadDate { get; }`
- `backend/src/Anela.Heblo.Persistence/Repositories/MockCatalogRepository.cs:407: public DateTime? ManufactureCostLoadDate => DateTime.UtcNow;`

Confirms FR-5 deletion targets.

- [ ] **Step 4: Verify no external readers of `ManufactureCostLoadDate`**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi
grep -rn "ManufactureCostLoadDate" --include="*.cs" backend/ frontend/ 2>/dev/null
```
Expected: only the three definition sites above. If any other `.cs` file reads it, **stop and report** — that caller must be cleaned up before this refactor or the property must be retained on the interface and return `null`.

- [ ] **Step 5: Verify `IManufactureClient` truly has no callers inside `CatalogRepository` body**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi
grep -n "_manufactureClient" backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs
```
Expected: only the field declaration and the null-check assignment in the constructor (currently lines 40 and 103). Confirms it can be dropped from `CatalogDataRefreshService`'s constructor in Task 4.

- [ ] **Step 6: Commit a marker (optional) — none. Move to Task 2.**

This task makes no code changes; no commit.

---

## Task 2: Create `CatalogCacheStore` — extract cache layer

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogCacheStore.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogCacheStoreTests.cs`

`CatalogCacheStore` owns the 19 per-source caches plus the dual-key aggregate cache. It is a sealed concrete class (per arch-review Decision 1 — no `ICatalogCacheStore` interface). Tests construct it with a real `MemoryCache` and a mocked `ICatalogMergeScheduler`.

- [ ] **Step 1: Write the failing test** — atomic replace promotes current to stale, then installs new payload

Create `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogCacheStoreTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogCacheStoreTests
{
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly Mock<ICatalogMergeScheduler> _schedulerMock = new();
    private readonly Mock<TimeProvider> _timeProviderMock = new();
    private readonly CatalogCacheOptions _options = new() { EnableBackgroundMerge = true };

    private CatalogCacheStore CreateStore()
    {
        _timeProviderMock.Setup(t => t.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
        var optionsMock = Options.Create(_options);
        return new CatalogCacheStore(
            _cache,
            _timeProviderMock.Object,
            optionsMock,
            _schedulerMock.Object,
            Mock.Of<ILogger<CatalogCacheStore>>());
    }

    [Fact]
    public async Task ReplaceCacheAtomicallyAsync_WithExistingCurrent_PromotesToStaleThenInstallsNew()
    {
        var store = CreateStore();
        var oldList = new List<CatalogAggregate> { new() { ProductCode = "OLD" } };
        var newList = new List<CatalogAggregate> { new() { ProductCode = "NEW" } };

        await store.ReplaceCacheAtomicallyAsync(oldList);
        await store.ReplaceCacheAtomicallyAsync(newList);

        store.TryGetCurrent().Should().BeSameAs(newList);
        store.TryGetStale().Should().BeSameAs(oldList);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogCacheStoreTests" 2>&1 | tail -15
```
Expected: FAIL with `CS0246: The type or namespace name 'CatalogCacheStore' could not be found`.

- [ ] **Step 3: Implement `CatalogCacheStore.cs`**

Create `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogCacheStore.cs`:

```csharp
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Manufacture;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

public sealed class CatalogCacheStore
{
    // Aggregate cache keys — must match the strings the legacy CatalogRepository used
    private const string CurrentCatalogCacheKey = "CatalogData_Current";
    private const string StaleCatalogCacheKey = "CatalogData_Stale";
    private const string CacheUpdateTimeKey = "CatalogData_LastUpdate";
    private const string LastMergeDateTimeKey = "LastMergeDateTime";

    // Per-source cache keys — kept as literal strings (NOT nameof of properties on this type)
    // so they continue to match what CatalogRepository wrote into IMemoryCache before this refactor.
    public static class SourceKeys
    {
        public const string Sales = "CachedSalesData";
        public const string Attributes = "CachedCatalogAttributesData";
        public const string InTransport = "CachedInTransportData";
        public const string Manufactured = "CachedManufacturedData";
        public const string InReserve = "CachedInReserveData";
        public const string InQuarantine = "CachedInQuarantineData";
        public const string Ordered = "CachedOrderedData";
        public const string Planned = "CachedPlannedData";
        public const string ErpStock = "CachedErpStockData";
        public const string EshopStock = "CachedEshopStockData";
        public const string PurchaseHistory = "CachedPurchaseHistoryData";
        public const string ManufactureHistory = "CachedManufactureHistoryData";
        public const string Consumed = "CachedConsumedData";
        public const string StockTaking = "CachedStockTakingData";
        public const string Lots = "CachedLotsData";
        public const string EshopPrice = "CachedEshopPriceData";
        public const string ErpPrice = "CachedErpPriceData";
        public const string EshopUrl = "CachedEshopUrlData";
        public const string ManufactureDifficultySettings = "CachedManufactureDifficultySettingsData";
    }

    private readonly IMemoryCache _cache;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<CatalogCacheOptions> _cacheOptions;
    private readonly ICatalogMergeScheduler _mergeScheduler;
    private readonly ILogger<CatalogCacheStore> _logger;
    private readonly SemaphoreSlim _cacheReplacementSemaphore = new(1, 1);

    public CatalogCacheStore(
        IMemoryCache cache,
        TimeProvider timeProvider,
        IOptions<CatalogCacheOptions> cacheOptions,
        ICatalogMergeScheduler mergeScheduler,
        ILogger<CatalogCacheStore> logger)
    {
        _cache = cache;
        _timeProvider = timeProvider;
        _cacheOptions = cacheOptions;
        _mergeScheduler = mergeScheduler;
        _logger = logger;
    }

    public async Task ReplaceCacheAtomicallyAsync(List<CatalogAggregate> newData)
    {
        await _cacheReplacementSemaphore.WaitAsync();
        try
        {
            var currentCache = _cache.Get<List<CatalogAggregate>>(CurrentCatalogCacheKey);
            if (currentCache != null)
            {
                var staleExpiry = _cacheOptions.Value.StaleDataRetentionPeriod;
                _cache.Set(StaleCatalogCacheKey, currentCache, staleExpiry);
            }
            _cache.Set(CurrentCatalogCacheKey, newData);
            _cache.Set(CacheUpdateTimeKey, DateTime.UtcNow);

            _logger.LogDebug("Cache updated atomically with {ProductCount} products", newData.Count);
        }
        finally
        {
            _cacheReplacementSemaphore.Release();
        }
    }

    public bool IsCacheValid()
    {
        var lastUpdate = _cache.Get<DateTime?>(CacheUpdateTimeKey);
        if (!lastUpdate.HasValue) return false;
        return DateTime.UtcNow - lastUpdate.Value < _cacheOptions.Value.CacheValidityPeriod;
    }

    public List<CatalogAggregate>? TryGetCurrent() =>
        _cache.Get<List<CatalogAggregate>>(CurrentCatalogCacheKey);

    public List<CatalogAggregate>? TryGetStale() =>
        _cache.Get<List<CatalogAggregate>>(StaleCatalogCacheKey);

    public List<CatalogAggregate> GetCatalogData()
    {
        if (_cache.TryGetValue(CurrentCatalogCacheKey, out List<CatalogAggregate>? current) && current != null)
            return current;

        if (_cache.TryGetValue(StaleCatalogCacheKey, out List<CatalogAggregate>? stale) && stale != null)
        {
            try { _mergeScheduler.ScheduleMerge("CacheRead"); }
            catch (Exception ex) { _logger.LogWarning(ex, "Failed to schedule background merge when serving stale data"); }
            return stale;
        }

        _logger.LogWarning("No catalog data available in cache, triggering background merge");
        try { _mergeScheduler.ScheduleMerge("CacheEmpty"); }
        catch (Exception ex) { _logger.LogError(ex, "Failed to schedule background merge for missing catalog data"); }

        return new List<CatalogAggregate>();
    }

    // ===== Per-source typed accessors =====

    public IList<CatalogSaleRecord> GetSalesData() =>
        _cache.Get<List<CatalogSaleRecord>>(SourceKeys.Sales) ?? new List<CatalogSaleRecord>();
    public void SetSalesData(IList<CatalogSaleRecord> value) => Write(SourceKeys.Sales, value);

    public IList<CatalogAttributes> GetCatalogAttributesData() =>
        _cache.Get<List<CatalogAttributes>>(SourceKeys.Attributes) ?? new List<CatalogAttributes>();
    public void SetCatalogAttributesData(IList<CatalogAttributes> value) => Write(SourceKeys.Attributes, value);

    public IDictionary<string, int> GetInTransportData() =>
        _cache.Get<Dictionary<string, int>>(SourceKeys.InTransport) ?? new Dictionary<string, int>();
    public void SetInTransportData(IDictionary<string, int> value) => Write(SourceKeys.InTransport, value);

    public IDictionary<string, decimal> GetManufacturedData() =>
        _cache.Get<Dictionary<string, decimal>>(SourceKeys.Manufactured) ?? new Dictionary<string, decimal>();
    public void SetManufacturedData(IDictionary<string, decimal> value) => Write(SourceKeys.Manufactured, value);

    public IDictionary<string, int> GetInReserveData() =>
        _cache.Get<Dictionary<string, int>>(SourceKeys.InReserve) ?? new Dictionary<string, int>();
    public void SetInReserveData(IDictionary<string, int> value) => Write(SourceKeys.InReserve, value);

    public IDictionary<string, int> GetInQuarantineData() =>
        _cache.Get<Dictionary<string, int>>(SourceKeys.InQuarantine) ?? new Dictionary<string, int>();
    public void SetInQuarantineData(IDictionary<string, int> value) => Write(SourceKeys.InQuarantine, value);

    public IDictionary<string, decimal> GetOrderedData() =>
        _cache.Get<Dictionary<string, decimal>>(SourceKeys.Ordered) ?? new Dictionary<string, decimal>();
    public void SetOrderedData(IDictionary<string, decimal> value) => Write(SourceKeys.Ordered, value);

    public IDictionary<string, decimal> GetPlannedData() =>
        _cache.Get<Dictionary<string, decimal>>(SourceKeys.Planned) ?? new Dictionary<string, decimal>();
    public void SetPlannedData(IDictionary<string, decimal> value) => Write(SourceKeys.Planned, value);

    public IList<ErpStock> GetErpStockData() =>
        _cache.Get<List<ErpStock>>(SourceKeys.ErpStock) ?? new List<ErpStock>();
    public void SetErpStockData(IList<ErpStock> value) => Write(SourceKeys.ErpStock, value);

    public IList<EshopStock> GetEshopStockData() =>
        _cache.Get<List<EshopStock>>(SourceKeys.EshopStock) ?? new List<EshopStock>();
    public void SetEshopStockData(IList<EshopStock> value) => Write(SourceKeys.EshopStock, value);

    public IList<CatalogPurchaseRecord> GetPurchaseHistoryData() =>
        _cache.Get<List<CatalogPurchaseRecord>>(SourceKeys.PurchaseHistory) ?? new List<CatalogPurchaseRecord>();
    public void SetPurchaseHistoryData(IList<CatalogPurchaseRecord> value) => Write(SourceKeys.PurchaseHistory, value);

    public IList<ManufactureHistoryRecord> GetManufactureHistoryData() =>
        _cache.Get<List<ManufactureHistoryRecord>>(SourceKeys.ManufactureHistory) ?? new List<ManufactureHistoryRecord>();
    public void SetManufactureHistoryData(IList<ManufactureHistoryRecord> value) => Write(SourceKeys.ManufactureHistory, value);

    public IList<ConsumedMaterialRecord> GetConsumedData() =>
        _cache.Get<List<ConsumedMaterialRecord>>(SourceKeys.Consumed) ?? new List<ConsumedMaterialRecord>();
    public void SetConsumedData(IList<ConsumedMaterialRecord> value) => Write(SourceKeys.Consumed, value);

    public IList<StockTakingRecord> GetStockTakingData() =>
        _cache.Get<List<StockTakingRecord>>(SourceKeys.StockTaking) ?? new List<StockTakingRecord>();
    public void SetStockTakingData(IList<StockTakingRecord> value) => Write(SourceKeys.StockTaking, value);

    public IList<CatalogLot> GetLotsData() =>
        _cache.Get<List<CatalogLot>>(SourceKeys.Lots) ?? new List<CatalogLot>();
    public void SetLotsData(IList<CatalogLot> value) => Write(SourceKeys.Lots, value);

    public IList<ProductPriceEshop> GetEshopPriceData() =>
        _cache.Get<List<ProductPriceEshop>>(SourceKeys.EshopPrice) ?? new List<ProductPriceEshop>();
    public void SetEshopPriceData(IList<ProductPriceEshop> value) => Write(SourceKeys.EshopPrice, value);

    public IList<ProductPriceErp> GetErpPriceData() =>
        _cache.Get<List<ProductPriceErp>>(SourceKeys.ErpPrice) ?? new List<ProductPriceErp>();
    public void SetErpPriceData(IList<ProductPriceErp> value) => Write(SourceKeys.ErpPrice, value);

    public IList<ProductEshopUrl> GetEshopUrlData() =>
        _cache.Get<List<ProductEshopUrl>>(SourceKeys.EshopUrl) ?? new List<ProductEshopUrl>();
    public void SetEshopUrlData(IList<ProductEshopUrl> value) => Write(SourceKeys.EshopUrl, value);

    public IDictionary<string, List<ManufactureDifficultySetting>> GetManufactureDifficultySettingsData() =>
        _cache.Get<Dictionary<string, List<ManufactureDifficultySetting>>>(SourceKeys.ManufactureDifficultySettings)
        ?? new Dictionary<string, List<ManufactureDifficultySetting>>();
    public void SetManufactureDifficultySettingsData(IDictionary<string, List<ManufactureDifficultySetting>> value) =>
        Write(SourceKeys.ManufactureDifficultySettings, value);

    // ===== Load-date + merge timestamp =====

    public DateTime? GetLoadDate(string sourceKey) =>
        _cache.Get<DateTime?>($"{sourceKey}_LoadDate");

    public DateTime? LastMergeDateTime => _cache.Get<DateTime?>(LastMergeDateTimeKey);

    public void SetLastMergeDateTime()
    {
        var mergeDateTime = _timeProvider.GetUtcNow().DateTime;
        _cache.Set(LastMergeDateTimeKey, mergeDateTime, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _cacheOptions.Value.CacheValidityPeriod
        });
    }

    // ===== Private write path — preserves semantics of the old property setters =====

    private void Write<TValue>(string sourceKey, TValue value)
    {
        _cache.Set(sourceKey, value);
        InvalidateSourceData(sourceKey);
        SetLoadDateInCache(sourceKey);
    }

    private void InvalidateSourceData(string dataSource)
    {
        if (!_cacheOptions.Value.EnableBackgroundMerge)
        {
            _cache.Remove(CurrentCatalogCacheKey);
            _cache.Remove(StaleCatalogCacheKey);
            _cache.Remove(CacheUpdateTimeKey);
            return;
        }
        _mergeScheduler.ScheduleMerge(dataSource);
        _logger.LogDebug("Invalidated source data: {DataSource}", dataSource);
    }

    private void SetLoadDateInCache(string dataKey)
    {
        var loadDate = _timeProvider.GetUtcNow().DateTime;
        _cache.Set($"{dataKey}_LoadDate", loadDate, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = _cacheOptions.Value.CacheValidityPeriod
        });
    }
}
```

- [ ] **Step 4: Run the existing test to verify it now passes**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogCacheStoreTests.ReplaceCacheAtomicallyAsync_WithExistingCurrent_PromotesToStaleThenInstallsNew" 2>&1 | tail -10
```
Expected: PASS.

- [ ] **Step 5: Add three more failing tests covering the invariants**

Append to `CatalogCacheStoreTests.cs`:

```csharp
    [Fact]
    public void GetCatalogData_WithCurrentPopulated_ReturnsCurrent_DoesNotScheduleMerge()
    {
        var store = CreateStore();
        var list = new List<CatalogAggregate> { new() { ProductCode = "A" } };
        store.ReplaceCacheAtomicallyAsync(list).GetAwaiter().GetResult();

        var result = store.GetCatalogData();

        result.Should().BeSameAs(list);
        _schedulerMock.Verify(s => s.ScheduleMerge(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task GetCatalogData_WithOnlyStale_ReturnsStaleAndSchedulesMerge()
    {
        var store = CreateStore();
        var oldList = new List<CatalogAggregate> { new() { ProductCode = "STALE" } };
        var newList = new List<CatalogAggregate> { new() { ProductCode = "NEW" } };
        await store.ReplaceCacheAtomicallyAsync(oldList);
        await store.ReplaceCacheAtomicallyAsync(newList);
        // Manually evict current to simulate expiry
        ((MemoryCache)_cache).Remove("CatalogData_Current");

        var result = store.GetCatalogData();

        result.Should().BeSameAs(oldList);
        _schedulerMock.Verify(s => s.ScheduleMerge("CacheRead"), Times.Once);
    }

    [Fact]
    public void GetCatalogData_EmptyCache_ReturnsEmptyAndSchedulesMerge()
    {
        var store = CreateStore();

        var result = store.GetCatalogData();

        result.Should().BeEmpty();
        _schedulerMock.Verify(s => s.ScheduleMerge("CacheEmpty"), Times.Once);
    }

    [Fact]
    public void SetSalesData_WithBackgroundMergeEnabled_SchedulesMergeAndRecordsLoadDate()
    {
        var store = CreateStore();
        var sales = new List<CatalogSaleRecord> { new() { ProductCode = "X" } };

        store.SetSalesData(sales);

        store.GetSalesData().Should().BeEquivalentTo(sales);
        store.GetLoadDate(CatalogCacheStore.SourceKeys.Sales).Should().NotBeNull();
        _schedulerMock.Verify(s => s.ScheduleMerge(CatalogCacheStore.SourceKeys.Sales), Times.Once);
    }

    [Fact]
    public void SetSalesData_WithBackgroundMergeDisabled_EvictsAggregateCacheInstead()
    {
        _options.EnableBackgroundMerge = false;
        var store = CreateStore();
        store.ReplaceCacheAtomicallyAsync(new List<CatalogAggregate> { new() { ProductCode = "A" } }).GetAwaiter().GetResult();

        store.SetSalesData(new List<CatalogSaleRecord>());

        store.TryGetCurrent().Should().BeNull();
        store.IsCacheValid().Should().BeFalse();
        _schedulerMock.Verify(s => s.ScheduleMerge(It.IsAny<string>()), Times.Never);
    }
```

- [ ] **Step 6: Run all `CatalogCacheStoreTests`**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogCacheStoreTests" --logger "console;verbosity=minimal" 2>&1 | tail -15
```
Expected: 5 tests passing.

- [ ] **Step 7: Build the whole solution to confirm `CatalogRepository.cs` still compiles**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet build Anela.Heblo.sln 2>&1 | tail -5
```
Expected: `Build succeeded`, 0 errors. (The store is unused so far; this just confirms no breakage.)

- [ ] **Step 8: Commit**

```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogCacheStore.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogCacheStoreTests.cs
git commit -m "refactor(catalog): introduce CatalogCacheStore wrapping IMemoryCache access"
```

---

## Task 3: Create `CatalogMergeService` — extract merge orchestration

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeCallbackWiring.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeServiceTests.cs`

`CatalogMergeService` owns the `Merge()` mapping and the priority/background merge entry points. It reads everything through `CatalogCacheStore` typed getters — no `IMemoryCache` reference.

- [ ] **Step 1: Write the failing test — merge with empty current cache seeds from ERP stock**

Create `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeServiceTests.cs`:

```csharp
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
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
            _schedulerMock.Object,
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
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogMergeServiceTests" 2>&1 | tail -10
```
Expected: FAIL with `CS0246: The type or namespace name 'CatalogMergeService' could not be found`.

- [ ] **Step 3: Implement `CatalogMergeService.cs`**

Create `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs`:

```csharp
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Microsoft.Extensions.Logging;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

public sealed class CatalogMergeService
{
    private readonly CatalogCacheStore _cacheStore;
    private readonly ICatalogMergeScheduler _mergeScheduler;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<CatalogMergeService> _logger;

    public CatalogMergeService(
        CatalogCacheStore cacheStore,
        ICatalogMergeScheduler mergeScheduler,
        TimeProvider timeProvider,
        ILogger<CatalogMergeService> logger)
    {
        _cacheStore = cacheStore;
        _mergeScheduler = mergeScheduler;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    public async Task ExecuteBackgroundMergeAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            var newCatalogData = await Task.Run(() => Merge(), cancellationToken);
            await _cacheStore.ReplaceCacheAtomicallyAsync(newCatalogData);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Background merge failed");
            throw;
        }
    }

    public async Task<List<CatalogAggregate>> ExecutePriorityMergeAsync()
    {
        _logger.LogInformation("Executing priority merge - no cache available");
        var newCatalogData = await Task.Run(() => Merge());
        await _cacheStore.ReplaceCacheAtomicallyAsync(newCatalogData);
        return newCatalogData;
    }

    internal List<CatalogAggregate> Merge()
    {
        var current = _cacheStore.TryGetCurrent();
        var erpStockList = _cacheStore.GetErpStockData();

        List<CatalogAggregate> products = (current == null || current.Count == 0)
            ? erpStockList.Select(s => new CatalogAggregate { ProductCode = s.ProductCode }).ToList()
            : current;

        var attributesMap = _cacheStore.GetCatalogAttributesData().ToDictionary(k => k.ProductCode, v => v);
        var eshopProductsMap = _cacheStore.GetEshopStockData().ToDictionary(k => k.Code, v => v);
        var erpProductsMap = erpStockList.ToDictionary(k => k.ProductCode, v => v);
        var consumedMap = _cacheStore.GetConsumedData().GroupBy(p => p.ProductCode).ToDictionary(k => k.Key, v => v.ToList());
        var purchaseMap = _cacheStore.GetPurchaseHistoryData().GroupBy(p => p.ProductCode).ToDictionary(k => k.Key, v => v.ToList());
        var manufactureMap = _cacheStore.GetManufactureHistoryData().GroupBy(p => p.ProductCode).ToDictionary(k => k.Key, v => v.ToList());
        var stockTakingMap = _cacheStore.GetStockTakingData().GroupBy(p => p.Code).ToDictionary(k => k.Key, v => v.ToList());
        var lotsMap = _cacheStore.GetLotsData().GroupBy(p => p.ProductCode).ToDictionary(k => k.Key, v => v.ToList());
        var eshopPriceMap = _cacheStore.GetEshopPriceData().ToDictionary(k => k.ProductCode, v => v);
        var erpPriceMap = _cacheStore.GetErpPriceData().ToDictionary(k => k.ProductCode, v => v);
        var eshopUrlMap = _cacheStore.GetEshopUrlData().ToDictionary(k => k.ProductCode, v => v.Url);
        var salesData = _cacheStore.GetSalesData();
        var transportMap = _cacheStore.GetInTransportData();
        var manufacturedMap = _cacheStore.GetManufacturedData();
        var reserveMap = _cacheStore.GetInReserveData();
        var quarantineMap = _cacheStore.GetInQuarantineData();
        var orderedMap = _cacheStore.GetOrderedData();
        var plannedMap = _cacheStore.GetPlannedData();
        var difficultyMap = _cacheStore.GetManufactureDifficultySettingsData();

        foreach (var product in products)
        {
            if (erpProductsMap.TryGetValue(product.ProductCode, out var erpProduct))
            {
                product.ProductName = erpProduct.ProductName;
                product.ErpId = erpProduct.ProductId;
                product.Stock.Erp = erpProduct.Stock;
                product.Type = GetProductType(erpProduct);
                product.MinimalOrderQuantity = erpProduct.MOQ;
                product.HasLots = erpProduct.HasLots;
                product.HasExpiration = erpProduct.HasExpiration;
                product.Volume = erpProduct.Volume;
                product.NetWeight = erpProduct.Weight;
                product.Note = erpProduct.Note;
                product.SupplierCode = erpProduct.SupplierCode;
                product.SupplierName = erpProduct.SupplierName;
            }

            product.SalesHistory = salesData.Where(w => w.ProductCode == product.ProductCode).ToList();

            if (attributesMap.TryGetValue(product.ProductCode, out var attributes))
            {
                product.Properties.OptimalStockDaysSetup = attributes.OptimalStockDays;
                product.Properties.StockMinSetup = attributes.StockMin;
                product.Properties.BatchSize = attributes.BatchSize;
                product.Properties.ExpirationMonths = attributes.ExpirationMonths;
                product.Properties.SeasonMonths = attributes.SeasonMonthsArray;
                product.MinimalManufactureQuantity = attributes.MinimalManufactureQuantity;
                product.Properties.AllowedResiduePercentage = attributes.AllowedResiduePercentage;
                product.Properties.Cooling = attributes.Cooling;
            }

            product.Stock.Transport = transportMap.ContainsKey(product.ProductCode) ? transportMap[product.ProductCode] : 0;
            product.Stock.Manufactured = manufacturedMap.ContainsKey(product.ProductCode) ? manufacturedMap[product.ProductCode] : 0;
            product.Stock.Reserve = reserveMap.ContainsKey(product.ProductCode) ? reserveMap[product.ProductCode] : 0;
            product.Stock.Quarantine = quarantineMap.ContainsKey(product.ProductCode) ? quarantineMap[product.ProductCode] : 0;
            product.Stock.Ordered = orderedMap.ContainsKey(product.ProductCode) ? orderedMap[product.ProductCode] : 0;
            product.Stock.Planned = plannedMap.ContainsKey(product.ProductCode) ? plannedMap[product.ProductCode] : 0;

            if (eshopProductsMap.TryGetValue(product.ProductCode, out var eshopProduct))
            {
                product.Stock.Eshop = eshopProduct.Stock;
                product.Stock.PrimaryStockSource = StockSource.Eshop;
                product.Location = eshopProduct.Location;
                product.Image = eshopProduct.Image;
                product.DefaultImage = eshopProduct.DefaultImage;
                product.GrossWeight = eshopProduct.Weight;
                product.Height = eshopProduct.Height;
                product.Width = eshopProduct.Width;
                product.Depth = eshopProduct.Depth;
                product.AtypicalShipping = eshopProduct.AtypicalShipping;
            }

            if (consumedMap.TryGetValue(product.ProductCode, out var consumed))
                product.ConsumedHistory = consumed.ToList();

            if (purchaseMap.TryGetValue(product.ProductCode, out var purchases))
                product.PurchaseHistory = purchases.ToList();

            if (manufactureMap.TryGetValue(product.ProductCode, out var manufactures))
                product.ManufactureHistory = manufactures.ToList();

            if (stockTakingMap.TryGetValue(product.ProductCode, out var stockTakings))
                product.StockTakingHistory = stockTakings.OrderByDescending(o => o.Date).ToList();

            if (lotsMap.TryGetValue(product.ProductCode, out var lots))
                product.Stock.Lots = lots.ToList();

            if (eshopPriceMap.TryGetValue(product.ProductCode, out var eshopPrice))
                product.EshopPrice = eshopPrice;

            if (erpPriceMap.TryGetValue(product.ProductCode, out var erpPrice))
                product.ErpPrice = erpPrice;

            if (eshopUrlMap.TryGetValue(product.ProductCode, out var eshopUrl))
                product.Url = eshopUrl;

            if (difficultyMap.TryGetValue(product.ProductCode, out var difficultySettings))
                product.ManufactureDifficultySettings.Assign(difficultySettings.ToList(), _timeProvider.GetUtcNow().UtcDateTime);
        }

        _cacheStore.SetLastMergeDateTime();
        return products.ToList();
    }

    private static ProductType GetProductType(ErpStock s)
    {
        var type = (ProductType?)s.ProductTypeId ?? ProductType.UNDEFINED;
        if (type == ProductType.Product && (s.ProductCode.StartsWith("BAL") || s.ProductCode.StartsWith("SET")))
            return ProductType.Set;
        return type;
    }
}
```

- [ ] **Step 4: Run the test to verify it passes**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogMergeServiceTests" --logger "console;verbosity=minimal" 2>&1 | tail -10
```
Expected: PASS.

- [ ] **Step 5: Add a second test — ERP-only seed populates names, sets, and `LastMergeDateTime`**

Append to `CatalogMergeServiceTests.cs`:

```csharp
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
```

- [ ] **Step 6: Create the hosted-service wiring**

Create `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeCallbackWiring.cs`:

```csharp
using Microsoft.Extensions.Hosting;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

public sealed class CatalogMergeCallbackWiring : IHostedService
{
    private readonly ICatalogMergeScheduler _scheduler;
    private readonly CatalogMergeService _mergeService;

    public CatalogMergeCallbackWiring(
        ICatalogMergeScheduler scheduler,
        CatalogMergeService mergeService)
    {
        _scheduler = scheduler;
        _mergeService = mergeService;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _scheduler.SetMergeCallback(_mergeService.ExecuteBackgroundMergeAsync);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
```

- [ ] **Step 7: Run the merge-service tests**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogMergeServiceTests" --logger "console;verbosity=minimal" 2>&1 | tail -10
```
Expected: 2 tests passing.

- [ ] **Step 8: Build the solution**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet build Anela.Heblo.sln 2>&1 | tail -5
```
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 9: Commit**

```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeCallbackWiring.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeServiceTests.cs
git commit -m "refactor(catalog): introduce CatalogMergeService + IHostedService callback wiring"
```

---

## Task 4: Create `CatalogDataRefreshService` — extract refresh methods

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogDataRefreshServiceTests.cs`

`CatalogDataRefreshService` owns all 19 `Refresh*Data` methods, the cross-module stock helpers (`GetProductsInTransport/Reserve/Quarantine/Ordered/Planned`), and the resilience wrapping for the four sources that need it. It is transient (matches scoped repository dependencies). `IManufactureClient` is **dropped** (verified unused in Task 1 Step 5).

- [ ] **Step 1: Write the failing test — `RefreshTransportData` aggregates transport-box items by product**

Create `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogDataRefreshServiceTests.cs`:

```csharp
using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Purchase;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogDataRefreshServiceTests
{
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly Mock<ICatalogMergeScheduler> _schedulerMock = new();
    private readonly Mock<TimeProvider> _timeProviderMock = new();
    private readonly Mock<ICatalogResilienceService> _resilienceMock = new();
    private readonly Mock<ICatalogSalesClient> _salesMock = new();
    private readonly Mock<ICatalogAttributesClient> _attributesMock = new();
    private readonly Mock<IEshopStockClient> _eshopStockMock = new();
    private readonly Mock<IConsumedMaterialsClient> _consumedMock = new();
    private readonly Mock<IPurchaseHistoryClient> _purchaseHistoryMock = new();
    private readonly Mock<IErpStockClient> _erpStockMock = new();
    private readonly Mock<ILotsClient> _lotsMock = new();
    private readonly Mock<IProductPriceEshopClient> _eshopPriceMock = new();
    private readonly Mock<IProductPriceErpClient> _erpPriceMock = new();
    private readonly Mock<IProductEshopUrlClient> _eshopUrlMock = new();
    private readonly Mock<ITransportBoxRepository> _transportBoxMock = new();
    private readonly Mock<IStockTakingRepository> _stockTakingMock = new();
    private readonly Mock<IPurchaseOrderRepository> _purchaseOrderMock = new();
    private readonly Mock<IManufactureOrderRepository> _manufactureOrderMock = new();
    private readonly Mock<IManufactureHistoryClient> _manufactureHistoryMock = new();
    private readonly Mock<IManufactureDifficultyRepository> _difficultyMock = new();
    private readonly Mock<IManufacturedProductInventoryRepository> _manufacturedInventoryMock = new();

    private CatalogCacheStore _store = default!;

    private CatalogDataRefreshService CreateService()
    {
        _timeProviderMock.Setup(t => t.GetUtcNow()).Returns(DateTimeOffset.UtcNow);
        _store = new CatalogCacheStore(
            _cache,
            _timeProviderMock.Object,
            Options.Create(new CatalogCacheOptions { EnableBackgroundMerge = true }),
            _schedulerMock.Object,
            Mock.Of<ILogger<CatalogCacheStore>>());

        return new CatalogDataRefreshService(
            _salesMock.Object,
            _attributesMock.Object,
            _eshopStockMock.Object,
            _consumedMock.Object,
            _purchaseHistoryMock.Object,
            _erpStockMock.Object,
            _lotsMock.Object,
            _eshopPriceMock.Object,
            _erpPriceMock.Object,
            _eshopUrlMock.Object,
            _transportBoxMock.Object,
            _stockTakingMock.Object,
            _purchaseOrderMock.Object,
            _manufactureOrderMock.Object,
            _manufactureHistoryMock.Object,
            _difficultyMock.Object,
            _manufacturedInventoryMock.Object,
            _resilienceMock.Object,
            _timeProviderMock.Object,
            Options.Create(new DataSourceOptions
            {
                SalesHistoryDays = 30,
                PurchaseHistoryDays = 30,
                ConsumedHistoryDays = 30,
                ManufactureHistoryDays = 30,
            }),
            _store,
            Mock.Of<ILogger<CatalogDataRefreshService>>());
    }

    [Fact]
    public async Task RefreshTransportData_SumsItemAmountsPerProduct()
    {
        var box = new TransportBox();
        // NOTE: TransportBox.Items population is project-specific; this test relies on
        // FindAsync returning a pre-built list. Construct boxes through the public API
        // available in the test project.
        var boxes = new List<TransportBox> { box };
        _transportBoxMock
            .Setup(r => r.FindAsync(TransportBox.IsInTransportPredicate, true, It.IsAny<CancellationToken>()))
            .ReturnsAsync(boxes);

        var service = CreateService();
        await service.RefreshTransportData(CancellationToken.None);

        _store.GetInTransportData().Should().NotBeNull();
        _store.GetLoadDate(CatalogCacheStore.SourceKeys.InTransport).Should().NotBeNull();
        _schedulerMock.Verify(s => s.ScheduleMerge(CatalogCacheStore.SourceKeys.InTransport), Times.Once);
    }
}
```

- [ ] **Step 2: Run test — confirm failure on missing type**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogDataRefreshServiceTests" 2>&1 | tail -10
```
Expected: FAIL with `CS0246: The type or namespace name 'CatalogDataRefreshService' could not be found`.

- [ ] **Step 3: Implement `CatalogDataRefreshService.cs`**

Create `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs`:

```csharp
using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Purchase;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

public sealed class CatalogDataRefreshService
{
    private readonly ICatalogSalesClient _salesClient;
    private readonly ICatalogAttributesClient _attributesClient;
    private readonly IEshopStockClient _eshopStockClient;
    private readonly IConsumedMaterialsClient _consumedMaterialClient;
    private readonly IPurchaseHistoryClient _purchaseHistoryClient;
    private readonly IErpStockClient _erpStockClient;
    private readonly ILotsClient _lotsClient;
    private readonly IProductPriceEshopClient _productPriceEshopClient;
    private readonly IProductPriceErpClient _productPriceErpClient;
    private readonly IProductEshopUrlClient _productEshopUrlClient;
    private readonly ITransportBoxRepository _transportBoxRepository;
    private readonly IStockTakingRepository _stockTakingRepository;
    private readonly IPurchaseOrderRepository _purchaseOrderRepository;
    private readonly IManufactureOrderRepository _manufactureOrderRepository;
    private readonly IManufactureHistoryClient _manufactureHistoryClient;
    private readonly IManufactureDifficultyRepository _manufactureDifficultyRepository;
    private readonly IManufacturedProductInventoryRepository _manufacturedInventoryRepository;
    private readonly ICatalogResilienceService _resilienceService;
    private readonly TimeProvider _timeProvider;
    private readonly IOptions<DataSourceOptions> _options;
    private readonly CatalogCacheStore _cacheStore;
    private readonly ILogger<CatalogDataRefreshService> _logger;

    public CatalogDataRefreshService(
        ICatalogSalesClient salesClient,
        ICatalogAttributesClient attributesClient,
        IEshopStockClient eshopStockClient,
        IConsumedMaterialsClient consumedMaterialClient,
        IPurchaseHistoryClient purchaseHistoryClient,
        IErpStockClient erpStockClient,
        ILotsClient lotsClient,
        IProductPriceEshopClient productPriceEshopClient,
        IProductPriceErpClient productPriceErpClient,
        IProductEshopUrlClient productEshopUrlClient,
        ITransportBoxRepository transportBoxRepository,
        IStockTakingRepository stockTakingRepository,
        IPurchaseOrderRepository purchaseOrderRepository,
        IManufactureOrderRepository manufactureOrderRepository,
        IManufactureHistoryClient manufactureHistoryClient,
        IManufactureDifficultyRepository manufactureDifficultyRepository,
        IManufacturedProductInventoryRepository manufacturedInventoryRepository,
        ICatalogResilienceService resilienceService,
        TimeProvider timeProvider,
        IOptions<DataSourceOptions> options,
        CatalogCacheStore cacheStore,
        ILogger<CatalogDataRefreshService> logger)
    {
        _salesClient = salesClient;
        _attributesClient = attributesClient;
        _eshopStockClient = eshopStockClient;
        _consumedMaterialClient = consumedMaterialClient;
        _purchaseHistoryClient = purchaseHistoryClient;
        _erpStockClient = erpStockClient;
        _lotsClient = lotsClient;
        _productPriceEshopClient = productPriceEshopClient;
        _productPriceErpClient = productPriceErpClient;
        _productEshopUrlClient = productEshopUrlClient;
        _transportBoxRepository = transportBoxRepository;
        _stockTakingRepository = stockTakingRepository;
        _purchaseOrderRepository = purchaseOrderRepository;
        _manufactureOrderRepository = manufactureOrderRepository;
        _manufactureHistoryClient = manufactureHistoryClient;
        _manufactureDifficultyRepository = manufactureDifficultyRepository;
        _manufacturedInventoryRepository = manufacturedInventoryRepository;
        _resilienceService = resilienceService;
        _timeProvider = timeProvider;
        _options = options;
        _cacheStore = cacheStore;
        _logger = logger;
    }

    public async Task RefreshTransportData(CancellationToken ct)
        => _cacheStore.SetInTransportData(await GetProductsInTransport(ct));

    public async Task RefreshManufacturedData(CancellationToken ct)
        => _cacheStore.SetManufacturedData(await _manufacturedInventoryRepository.GetTotalAmountByProductCodeAsync(ct));

    public async Task RefreshReserveData(CancellationToken ct)
    {
        _cacheStore.SetInReserveData(await GetProductsInReserve(ct));
        _cacheStore.SetInQuarantineData(await GetProductsInQuarantine(ct));
    }

    public async Task RefreshOrderedData(CancellationToken ct)
        => _cacheStore.SetOrderedData(await GetProductsOrdered(ct));

    public async Task RefreshPlannedData(CancellationToken ct)
        => _cacheStore.SetPlannedData(await GetProductsPlanned(ct));

    public async Task RefreshSalesData(CancellationToken ct)
    {
        try
        {
            var sales = await _resilienceService.ExecuteWithResilienceAsync(
                async (cancellationToken) => await _salesClient.GetAsync(
                    _timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.SalesHistoryDays),
                    _timeProvider.GetUtcNow().Date,
                    cancellationToken: cancellationToken),
                "RefreshSalesData", ct);
            _cacheStore.SetSalesData(sales);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "RefreshSalesData failed after all retries — retaining stale cache. Items in cache: {Count}",
                _cacheStore.GetSalesData().Count);
        }
    }

    public async Task RefreshAttributesData(CancellationToken ct)
    {
        var attributes = await _resilienceService.ExecuteWithResilienceAsync(
            async (cancellationToken) => await _attributesClient.GetAttributesAsync(cancellationToken: cancellationToken),
            "RefreshAttributesData", ct);
        _cacheStore.SetCatalogAttributesData(attributes);
    }

    public async Task RefreshErpStockData(CancellationToken ct)
    {
        var stock = await _resilienceService.ExecuteWithResilienceAsync(
            async (cancellationToken) => (await _erpStockClient.ListAsync(cancellationToken)).ToList(),
            "RefreshErpStockData", ct);
        _cacheStore.SetErpStockData(stock);
    }

    public async Task RefreshEshopStockData(CancellationToken ct)
    {
        var stock = await _resilienceService.ExecuteWithResilienceAsync(
            async (cancellationToken) => (await _eshopStockClient.ListAsync(cancellationToken)).ToList(),
            "RefreshEshopStockData", ct);
        _cacheStore.SetEshopStockData(stock);
    }

    public async Task RefreshPurchaseHistoryData(CancellationToken ct)
    {
        var data = (await _purchaseHistoryClient.GetHistoryAsync(
            null,
            _timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.PurchaseHistoryDays),
            _timeProvider.GetUtcNow().Date,
            cancellationToken: ct)).ToList();
        _cacheStore.SetPurchaseHistoryData(data);
    }

    public async Task RefreshConsumedHistoryData(CancellationToken ct)
    {
        var data = (await _consumedMaterialClient.GetConsumedAsync(
            _timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.ConsumedHistoryDays),
            _timeProvider.GetUtcNow().Date,
            cancellationToken: ct)).ToList();
        _cacheStore.SetConsumedData(data);
    }

    public async Task RefreshStockTakingData(CancellationToken ct)
        => _cacheStore.SetStockTakingData((await _stockTakingRepository.GetAllAsync(ct)).ToList());

    public async Task RefreshLotsData(CancellationToken ct)
        => _cacheStore.SetLotsData((await _lotsClient.GetAsync(cancellationToken: ct)).ToList());

    public async Task RefreshEshopPricesData(CancellationToken ct)
        => _cacheStore.SetEshopPriceData((await _productPriceEshopClient.GetAllAsync(ct)).ToList());

    public async Task RefreshErpPricesData(CancellationToken ct)
        => _cacheStore.SetErpPriceData((await _productPriceErpClient.GetAllAsync(false, ct)).ToList());

    public async Task RefreshEshopUrlData(CancellationToken ct)
        => _cacheStore.SetEshopUrlData((await _productEshopUrlClient.GetAllAsync(ct)).ToList());

    public async Task RefreshManufactureDifficultySettingsData(string? product, CancellationToken ct)
    {
        var difficultySettings = await _manufactureDifficultyRepository.ListAsync(product, cancellationToken: ct);

        if (product == null)
        {
            _cacheStore.SetManufactureDifficultySettingsData(difficultySettings
                .GroupBy(h => h.ProductCode)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(h => h.ValidFrom ?? DateTime.MinValue).ToList()));
        }
        else
        {
            var existing = _cacheStore.GetManufactureDifficultySettingsData();
            existing[product] = difficultySettings;
            _cacheStore.SetManufactureDifficultySettingsData(existing);

            // Apply live update to in-memory aggregate WITHOUT scheduling a merge from
            // an empty-cache read. Use TryGetCurrent (does not schedule) per arch-review risk row.
            var current = _cacheStore.TryGetCurrent();
            current?.SingleOrDefault(s => s.ProductCode == product)?
                .ManufactureDifficultySettings.Assign(difficultySettings, _timeProvider.GetUtcNow().UtcDateTime);
        }
    }

    public async Task RefreshManufactureHistoryData(CancellationToken ct)
    {
        var history = (await _manufactureHistoryClient.GetHistoryAsync(
            _timeProvider.GetUtcNow().Date.AddDays(-1 * _options.Value.ManufactureHistoryDays),
            _timeProvider.GetUtcNow().Date,
            cancellationToken: ct)).ToList();
        _cacheStore.SetManufactureHistoryData(history);
    }

    public Task RefreshManufactureCostData(CancellationToken ct)
    {
        // Legacy method: copies ManufactureHistory onto each product in the current merged cache.
        var current = _cacheStore.TryGetCurrent();
        if (current == null) return Task.CompletedTask;

        var manufactureMap = _cacheStore.GetManufactureHistoryData()
            .GroupBy(p => p.ProductCode)
            .ToDictionary(k => k.Key, v => v.ToList());

        foreach (var product in current)
        {
            if (manufactureMap.TryGetValue(product.ProductCode, out var manufactures))
                product.ManufactureHistory = manufactures.ToList();
        }
        return Task.CompletedTask;
    }

    private async Task<Dictionary<string, int>> GetProductsInTransport(CancellationToken ct)
    {
        var boxes = await _transportBoxRepository.FindAsync(TransportBox.IsInTransportPredicate, includeDetails: true, cancellationToken: ct);
        return boxes.SelectMany(s => s.Items)
            .GroupBy(g => g.ProductCode)
            .ToDictionary(k => k.Key, v => v.Sum(s => (int)s.Amount));
    }

    private async Task<Dictionary<string, int>> GetProductsInReserve(CancellationToken ct)
    {
        var boxes = await _transportBoxRepository.FindAsync(TransportBox.IsInReservePredicate, includeDetails: true, cancellationToken: ct);
        return boxes.SelectMany(s => s.Items)
            .GroupBy(g => g.ProductCode)
            .ToDictionary(k => k.Key, v => v.Sum(s => (int)s.Amount));
    }

    private async Task<Dictionary<string, int>> GetProductsInQuarantine(CancellationToken ct)
    {
        var boxes = await _transportBoxRepository.FindAsync(TransportBox.IsInQuarantinePredicate, includeDetails: true, cancellationToken: ct);
        return boxes.SelectMany(s => s.Items)
            .GroupBy(g => g.ProductCode)
            .ToDictionary(k => k.Key, v => v.Sum(s => (int)s.Amount));
    }

    private Task<Dictionary<string, decimal>> GetProductsOrdered(CancellationToken ct)
        => _purchaseOrderRepository.GetOrderedQuantitiesAsync(ct);

    private Task<Dictionary<string, decimal>> GetProductsPlanned(CancellationToken ct)
        => _manufactureOrderRepository.GetPlannedQuantitiesAsync(ct);
}
```

- [ ] **Step 4: Run the test to confirm it passes**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogDataRefreshServiceTests" --logger "console;verbosity=minimal" 2>&1 | tail -10
```
Expected: PASS.

- [ ] **Step 5: Add a second failing test — `RefreshSalesData` retains stale cache when client throws**

Append to `CatalogDataRefreshServiceTests.cs`:

```csharp
    [Fact]
    public async Task RefreshSalesData_WhenResilienceServiceThrows_RetainsStaleCacheAndDoesNotRethrow()
    {
        _resilienceMock
            .Setup(r => r.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<IEnumerable<CatalogSaleRecord>>>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("boom"));

        var service = CreateService();
        // Pre-populate stale data
        _store.SetSalesData(new List<CatalogSaleRecord> { new() { ProductCode = "STALE" } });

        Func<Task> act = () => service.RefreshSalesData(CancellationToken.None);
        await act.Should().NotThrowAsync();
        _store.GetSalesData().Should().ContainSingle(s => s.ProductCode == "STALE");
    }
```

Reset the resilience mock for this test if needed (set up only for `IEnumerable<CatalogSaleRecord>` signature).

- [ ] **Step 6: Add a third failing test — single-product manufacture-difficulty refresh updates the live aggregate**

Append to `CatalogDataRefreshServiceTests.cs`:

```csharp
    [Fact]
    public async Task RefreshManufactureDifficultySettingsData_WithSpecificProduct_UpdatesLiveAggregate()
    {
        var settings = new List<ManufactureDifficultySetting>
        {
            new() { ProductCode = "ABC", ValidFrom = DateTime.UtcNow.AddDays(-1) }
        };
        _difficultyMock.Setup(r => r.ListAsync("ABC", It.IsAny<CancellationToken>())).ReturnsAsync(settings);

        var service = CreateService();
        await _store.ReplaceCacheAtomicallyAsync(new List<CatalogAggregate>
        {
            new() { ProductCode = "ABC" }
        });

        await service.RefreshManufactureDifficultySettingsData("ABC", CancellationToken.None);

        _store.GetManufactureDifficultySettingsData().Should().ContainKey("ABC");
        _store.TryGetCurrent()!.Single(s => s.ProductCode == "ABC")
            .ManufactureDifficultySettings.Should().NotBeNull();
    }
```

- [ ] **Step 7: Run all `CatalogDataRefreshServiceTests`**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogDataRefreshServiceTests" --logger "console;verbosity=minimal" 2>&1 | tail -10
```
Expected: 3 tests passing.

- [ ] **Step 8: Build the solution**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet build Anela.Heblo.sln 2>&1 | tail -5
```
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 9: Commit**

```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi
git add backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogDataRefreshServiceTests.cs
git commit -m "refactor(catalog): introduce CatalogDataRefreshService owning 19 refresh methods"
```

---

## Task 5: Slim `CatalogRepository` to delegating facade

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs` — full rewrite from 962 LOC → ≤ 250 LOC.

The slimmed `CatalogRepository` is transient. It depends on the three new collaborators + `ICatalogMergeScheduler` (for `ChangesPendingForMerge` / `WaitForCurrentMergeAsync`). It removes `ManufactureCostLoadDate` (interface update follows in Task 6).

- [ ] **Step 1: Replace `CatalogRepository.cs` with the slim version**

Overwrite `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs` with:

```csharp
using System.Linq.Expressions;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Stock;

namespace Anela.Heblo.Application.Features.Catalog;

public class CatalogRepository : ICatalogRepository
{
    private readonly CatalogCacheStore _cacheStore;
    private readonly CatalogMergeService _mergeService;
    private readonly CatalogDataRefreshService _refreshService;
    private readonly ICatalogMergeScheduler _mergeScheduler;

    public CatalogRepository(
        CatalogCacheStore cacheStore,
        CatalogMergeService mergeService,
        CatalogDataRefreshService refreshService,
        ICatalogMergeScheduler mergeScheduler)
    {
        _cacheStore = cacheStore;
        _mergeService = mergeService;
        _refreshService = refreshService;
        _mergeScheduler = mergeScheduler;
    }

    // ===== Read-side query API =====

    public Task<CatalogAggregate?> GetByIdAsync(string id, CancellationToken cancellationToken = default)
        => Task.FromResult(_cacheStore.GetCatalogData().SingleOrDefault(s => s.ProductCode == id));

    public Task<IReadOnlyDictionary<string, CatalogAggregate>> GetByIdsAsync(IEnumerable<string> ids, CancellationToken cancellationToken = default)
    {
        var idSet = new HashSet<string>(ids);
        IReadOnlyDictionary<string, CatalogAggregate> result = _cacheStore.GetCatalogData()
            .Where(p => idSet.Contains(p.ProductCode))
            .ToDictionary(p => p.ProductCode, p => p);
        return Task.FromResult(result);
    }

    public async Task<IEnumerable<CatalogAggregate>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        var data = await GetCatalogDataAsync();
        return data.AsEnumerable();
    }

    public Task<IEnumerable<CatalogAggregate>> FindAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(_cacheStore.GetCatalogData().AsQueryable().Where(predicate).AsEnumerable());

    public Task<CatalogAggregate?> SingleOrDefaultAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(_cacheStore.GetCatalogData().AsQueryable().SingleOrDefault(predicate));

    public Task<bool> AnyAsync(Expression<Func<CatalogAggregate, bool>> predicate, CancellationToken cancellationToken = default)
        => Task.FromResult(_cacheStore.GetCatalogData().AsQueryable().Any(predicate));

    public Task<int> CountAsync(Expression<Func<CatalogAggregate, bool>>? predicate = null, CancellationToken cancellationToken = default)
        => Task.FromResult(predicate == null ? _cacheStore.GetCatalogData().Count : _cacheStore.GetCatalogData().AsQueryable().Count(predicate));

    public Task<List<CatalogAggregate>> GetProductsWithSalesInPeriod(
        DateTime fromDate,
        DateTime toDate,
        ProductType[] productTypes,
        CancellationToken cancellationToken = default)
    {
        var products = _cacheStore.GetCatalogData()
            .Where(p => productTypes.Contains(p.Type))
            .Where(p => p.SalesHistory.Any(s => s.Date >= fromDate && s.Date <= toDate))
            .ToList();
        return Task.FromResult(products);
    }

    private async Task<List<CatalogAggregate>> GetCatalogDataAsync()
    {
        var current = _cacheStore.TryGetCurrent();
        if (current != null && _cacheStore.IsCacheValid())
            return current;

        // Stale-fallback path delegated below to keep behavior identical.
        // We DO NOT use GetCatalogData() here because that one schedules merges in
        // the empty case; the priority path below is the correct empty-cache trigger.
        var allowStale = _mergeScheduler.IsMergeInProgress;
        if (allowStale)
        {
            var stale = _cacheStore.TryGetStale();
            if (stale != null) return stale;
        }

        return await _mergeService.ExecutePriorityMergeAsync();
    }

    // ===== Refresh methods (delegate) =====

    public Task RefreshTransportData(CancellationToken ct) => _refreshService.RefreshTransportData(ct);
    public Task RefreshManufacturedData(CancellationToken ct) => _refreshService.RefreshManufacturedData(ct);
    public Task RefreshReserveData(CancellationToken ct) => _refreshService.RefreshReserveData(ct);
    public Task RefreshOrderedData(CancellationToken ct) => _refreshService.RefreshOrderedData(ct);
    public Task RefreshPlannedData(CancellationToken ct) => _refreshService.RefreshPlannedData(ct);
    public Task RefreshSalesData(CancellationToken ct) => _refreshService.RefreshSalesData(ct);
    public Task RefreshAttributesData(CancellationToken ct) => _refreshService.RefreshAttributesData(ct);
    public Task RefreshErpStockData(CancellationToken ct) => _refreshService.RefreshErpStockData(ct);
    public Task RefreshEshopStockData(CancellationToken ct) => _refreshService.RefreshEshopStockData(ct);
    public Task RefreshPurchaseHistoryData(CancellationToken ct) => _refreshService.RefreshPurchaseHistoryData(ct);
    public Task RefreshManufactureHistoryData(CancellationToken ct) => _refreshService.RefreshManufactureHistoryData(ct);
    public Task RefreshConsumedHistoryData(CancellationToken ct) => _refreshService.RefreshConsumedHistoryData(ct);
    public Task RefreshStockTakingData(CancellationToken ct) => _refreshService.RefreshStockTakingData(ct);
    public Task RefreshLotsData(CancellationToken ct) => _refreshService.RefreshLotsData(ct);
    public Task RefreshEshopPricesData(CancellationToken ct) => _refreshService.RefreshEshopPricesData(ct);
    public Task RefreshErpPricesData(CancellationToken ct) => _refreshService.RefreshErpPricesData(ct);
    public Task RefreshEshopUrlData(CancellationToken ct) => _refreshService.RefreshEshopUrlData(ct);
    public Task RefreshManufactureDifficultySettingsData(string? product, CancellationToken ct)
        => _refreshService.RefreshManufactureDifficultySettingsData(product, ct);

    // ===== Load-date / merge timestamp properties =====

    public DateTime? TransportLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.InTransport);
    public DateTime? ManufacturedLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.Manufactured);
    public DateTime? ReserveLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.InReserve);
    public DateTime? QuarantineLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.InQuarantine);
    public DateTime? OrderedLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.Ordered);
    public DateTime? PlannedLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.Planned);
    public DateTime? SalesLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.Sales);
    public DateTime? AttributesLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.Attributes);
    public DateTime? ErpStockLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.ErpStock);
    public DateTime? EshopStockLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.EshopStock);
    public DateTime? PurchaseHistoryLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.PurchaseHistory);
    public DateTime? ManufactureHistoryLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.ManufactureHistory);
    public DateTime? ConsumedHistoryLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.Consumed);
    public DateTime? StockTakingLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.StockTaking);
    public DateTime? LotsLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.Lots);
    public DateTime? EshopPricesLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.EshopPrice);
    public DateTime? ErpPricesLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.ErpPrice);
    public DateTime? EshopUrlLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.EshopUrl);
    public DateTime? ManufactureDifficultySettingsLoadDate => _cacheStore.GetLoadDate(CatalogCacheStore.SourceKeys.ManufactureDifficultySettings);

    public DateTime? LastMergeDateTime => _cacheStore.LastMergeDateTime;

    public bool ChangesPendingForMerge
    {
        get
        {
            var lastMerge = LastMergeDateTime;
            if (lastMerge == null) return true;

            var loadDates = new DateTime?[]
            {
                TransportLoadDate, ManufacturedLoadDate, ReserveLoadDate, QuarantineLoadDate,
                OrderedLoadDate, PlannedLoadDate, SalesLoadDate, AttributesLoadDate,
                ErpStockLoadDate, EshopStockLoadDate, PurchaseHistoryLoadDate,
                ManufactureHistoryLoadDate, ConsumedHistoryLoadDate, StockTakingLoadDate,
                LotsLoadDate, EshopPricesLoadDate, ErpPricesLoadDate, EshopUrlLoadDate,
                ManufactureDifficultySettingsLoadDate,
            };
            if (loadDates.Any(d => d == null)) return true;
            var maxLoadDate = loadDates.Where(d => d.HasValue).Max(d => d!.Value);
            return maxLoadDate > lastMerge;
        }
    }

    public Task WaitForCurrentMergeAsync(CancellationToken cancellationToken = default)
        => _mergeScheduler.WaitForCurrentMergeAsync(cancellationToken);
}
```

- [ ] **Step 2: Verify file length is within the budget**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi
wc -l backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs
```
Expected: ≤ 250 lines (NFR-3 maintainability target).

- [ ] **Step 3: Build the solution — confirm zero compile errors before fixing tests**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet build Anela.Heblo.sln 2>&1 | tail -30
```
Expected: production code compiles. **Test projects will fail** because `CatalogRepositoryTests` and `CatalogRepositoryCacheOptimizationTests` still use the old 25-arg constructor and reference `ManufactureCostLoadDate`. Note the errors — they are addressed in Task 9.

- [ ] **Step 4: Do not commit yet — Task 6 (interface trim) and Task 7 (DI) are interdependent with this one.**

Continue to Task 6.

---

## Task 6: Remove `ManufactureCostLoadDate` from `ICatalogRepository` and `MockCatalogRepository`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs` — remove line 46.
- Modify: `backend/src/Anela.Heblo.Persistence/Repositories/MockCatalogRepository.cs` — remove line 407.

Per arch-review Decision 5, `ManufactureCostLoadDate` is dead: no production caller, no test caller (Task 1 Step 4 confirmed). Drop it outright.

- [ ] **Step 1: Trim the interface**

Edit `backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs` and remove the line:
```csharp
    DateTime? ManufactureCostLoadDate { get; }
```
(currently line 46, the line between `ManufactureDifficultySettingsLoadDate` and the blank line that precedes `// Merge operation tracking`).

- [ ] **Step 2: Trim the mock**

Edit `backend/src/Anela.Heblo.Persistence/Repositories/MockCatalogRepository.cs` and remove the line:
```csharp
    public DateTime? ManufactureCostLoadDate => DateTime.UtcNow;
```
(currently line 407.)

Leave `MockCatalogRepository.ManufactureDifficultyLoadDate` (line 406) alone — arch-review amendment #3: it's dead but **out of scope** for this refactor.

- [ ] **Step 3: Confirm no remaining references**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi
grep -rn "ManufactureCostLoadDate" --include="*.cs" backend/
```
Expected: empty.

- [ ] **Step 4: Build — production projects only (test projects still broken, addressed in Tasks 7–9)**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet build src/Anela.Heblo.sln 2>&1 | grep -E "error CS|Build succeeded|Build FAILED" | tail -10
```
(If `src/Anela.Heblo.sln` does not exist, build just the production projects: `dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj src/Anela.Heblo.Persistence/Anela.Heblo.Persistence.csproj src/Anela.Heblo.Domain/Anela.Heblo.Domain.csproj 2>&1 | tail -10`.)
Expected: production code compiles.

- [ ] **Step 5: Do not commit yet — Task 7 wires DI.**

Continue to Task 7.

---

## Task 7: Wire DI in `CatalogModule`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — register the three new types and the hosted service.

`ICatalogRepository → CatalogRepository` stays transient. `CatalogCacheStore` and `CatalogMergeService` are singletons. `CatalogDataRefreshService` is transient (matches scoped dependencies — see arch-review Decision 3). `CatalogMergeCallbackWiring` is registered via `AddHostedService`.

- [ ] **Step 1: Add the four registrations**

In `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`, locate this block (currently around lines 67–69):
```csharp
        services.AddSingleton<ICatalogResilienceService, CatalogResilienceService>();
        services.AddSingleton<ICatalogMergeScheduler, CatalogMergeScheduler>();
```

Insert immediately after those two lines:
```csharp
        services.AddSingleton<CatalogCacheStore>();
        services.AddSingleton<CatalogMergeService>();
        services.AddTransient<CatalogDataRefreshService>();
        services.AddHostedService<CatalogMergeCallbackWiring>();
```

- [ ] **Step 2: Confirm `RegisterBackgroundRefreshTasks` is unchanged**

Open `CatalogModule.cs` again and verify lines 128–216 (the 19 `RegisterRefreshTask<ICatalogRepository>(nameof(ICatalogRepository.RefreshXxx))` calls) are **untouched**. Task IDs must remain stable (NFR-1).

- [ ] **Step 3: Build the production projects**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet build src/Anela.Heblo.Application/Anela.Heblo.Application.csproj 2>&1 | tail -10
```
Expected: `Build succeeded`, 0 errors.

- [ ] **Step 4: Do not commit yet — Task 8 adds an integration smoke test for the wiring.**

Continue to Task 8.

---

## Task 8: Integration test — startup wires merge callback end-to-end

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeCallbackWiringTests.cs`

Per arch-review amendment #6: an integration test that resolves both singletons from a real `ServiceProvider` after `StartAsync()` and proves a refresh-then-merge cycle works end-to-end. Tests are kept hermetic — no DB, no clients hit real services; mocks supply the per-source data.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeCallbackWiringTests.cs`:

```csharp
using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog;
using Anela.Heblo.Application.Features.Catalog.Infrastructure;
using Anela.Heblo.Application.Features.Catalog.Services;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Attributes;
using Anela.Heblo.Domain.Features.Catalog.ConsumedMaterials;
using Anela.Heblo.Domain.Features.Catalog.Lots;
using Anela.Heblo.Domain.Features.Catalog.EshopUrl;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.PurchaseHistory;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.Stock;
using Anela.Heblo.Domain.Features.Logistics.Transport;
using Anela.Heblo.Domain.Features.Manufacture;
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Domain.Features.Purchase;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.Infrastructure;

public class CatalogMergeCallbackWiringTests
{
    [Fact]
    public async Task HostStart_WiresCallbackAndPriorityMergeReturnsErpSeededList()
    {
        var hostBuilder = Host.CreateDefaultBuilder()
            .ConfigureServices((ctx, services) =>
            {
                // Minimal cache + options
                services.AddMemoryCache();
                services.AddSingleton(TimeProvider.System);
                services.Configure<CatalogCacheOptions>(o =>
                {
                    o.EnableBackgroundMerge = true;
                    o.AllowStaleDataDuringMerge = false;
                });
                services.Configure<DataSourceOptions>(o => { /* defaults are fine */ });

                // The three new collaborators + scheduler + wiring
                services.AddSingleton<ICatalogResilienceService, CatalogResilienceService>();
                services.AddSingleton<ICatalogMergeScheduler, CatalogMergeScheduler>();
                services.AddSingleton<CatalogCacheStore>();
                services.AddSingleton<CatalogMergeService>();
                services.AddTransient<CatalogDataRefreshService>();
                services.AddTransient<ICatalogRepository, CatalogRepository>();
                services.AddHostedService<CatalogMergeCallbackWiring>();

                // Stub every per-source client/repository the refresh service depends on.
                services.AddSingleton(Mock.Of<ICatalogSalesClient>());
                services.AddSingleton(Mock.Of<ICatalogAttributesClient>());
                services.AddSingleton(Mock.Of<IEshopStockClient>());
                services.AddSingleton(Mock.Of<IConsumedMaterialsClient>());
                services.AddSingleton(Mock.Of<IPurchaseHistoryClient>());
                services.AddSingleton(Mock.Of<ILotsClient>());
                services.AddSingleton(Mock.Of<IProductPriceEshopClient>());
                services.AddSingleton(Mock.Of<IProductPriceErpClient>());
                services.AddSingleton(Mock.Of<IProductEshopUrlClient>());
                services.AddSingleton(Mock.Of<ITransportBoxRepository>());
                services.AddSingleton(Mock.Of<IStockTakingRepository>());
                services.AddSingleton(Mock.Of<IPurchaseOrderRepository>());
                services.AddSingleton(Mock.Of<IManufactureOrderRepository>());
                services.AddSingleton(Mock.Of<IManufactureHistoryClient>());
                services.AddSingleton(Mock.Of<IManufactureDifficultyRepository>());
                services.AddSingleton(Mock.Of<IManufacturedProductInventoryRepository>());

                // Erp stock client returns two products
                var erpStockMock = new Mock<IErpStockClient>();
                erpStockMock.Setup(c => c.ListAsync(It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<ErpStock>
                    {
                        new() { ProductCode = "P1", ProductName = "Product 1", ProductId = 1 },
                        new() { ProductCode = "P2", ProductName = "Product 2", ProductId = 2 },
                    });
                services.AddSingleton(erpStockMock.Object);
            });

        using var host = hostBuilder.Build();
        await host.StartAsync();

        try
        {
            var scheduler = host.Services.GetRequiredService<ICatalogMergeScheduler>();
            var repo = host.Services.GetRequiredService<ICatalogRepository>();

            // Hydrate ERP stock — this writes via the cache store and schedules a merge
            await repo.RefreshErpStockData(CancellationToken.None);

            // Trigger priority merge directly through GetAllAsync (empty cache path)
            var all = (await repo.GetAllAsync(CancellationToken.None)).ToList();

            all.Should().HaveCount(2);
            all.Should().Contain(p => p.ProductCode == "P1");
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
```

- [ ] **Step 2: Run the test**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~CatalogMergeCallbackWiringTests" --logger "console;verbosity=minimal" 2>&1 | tail -20
```
Expected: PASS. If it fails, check that (a) `CatalogResilienceService` has a public constructor compatible with the registration shape used here — if not, register a `Mock<ICatalogResilienceService>` instead and have it pass through, mirroring the pattern in `CatalogRepositoryTests`.

- [ ] **Step 3: Build only — no commit yet (tests in Task 9 must land in the same commit as the slim repo).**

---

## Task 9: Update existing test files to the new constructor shape

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs` — replace constructor wiring; tests that exercise refresh+merge against the slim repository continue to work via delegation (since `CatalogRepository.RefreshXxx` calls through to `CatalogDataRefreshService`).
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs` — same constructor migration.
- Modify: `backend/test/Anela.Heblo.Tests/Controllers/CatalogRepositoryDebugTest.cs` — replace constructor wiring.

The existing test bodies (the `[Fact]` methods exercising `Merge_With*` and cache behavior) stay valid because they hit the new collaborators through delegation. Only the **setup/constructor** changes.

- [ ] **Step 1: Pick a small helper to share constructor wiring**

In `backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs`, replace the constructor block (`public CatalogRepositoryTests() { ... }` and the field declarations at the top) with this structure. Keep all `[Fact]` test methods unchanged.

```csharp
public class CatalogRepositoryTests
{
    private readonly Mock<ICatalogSalesClient> _salesClientMock = new();
    private readonly Mock<ICatalogAttributesClient> _attributesClientMock = new();
    private readonly Mock<IEshopStockClient> _eshopStockClientMock = new();
    private readonly Mock<IConsumedMaterialsClient> _consumedMaterialClientMock = new();
    private readonly Mock<IPurchaseHistoryClient> _purchaseHistoryClientMock = new();
    private readonly Mock<IErpStockClient> _erpStockClientMock = new();
    private readonly Mock<ILotsClient> _lotsClientMock = new();
    private readonly Mock<IProductPriceEshopClient> _productPriceEshopClientMock = new();
    private readonly Mock<IProductPriceErpClient> _productPriceErpClientMock = new();
    private readonly Mock<IProductEshopUrlClient> _productEshopUrlClientMock = new();
    private readonly Mock<ITransportBoxRepository> _transportBoxRepositoryMock = new();
    private readonly Mock<IStockTakingRepository> _stockTakingRepositoryMock = new();
    private readonly Mock<IPurchaseOrderRepository> _purchaseOrderRepositoryMock = new();
    private readonly Mock<IManufactureOrderRepository> _manufactureOrderRepositoryMock = new();
    private readonly Mock<IManufactureHistoryClient> _manufactureHistoryClientMock = new();
    private readonly Mock<IManufactureDifficultyRepository> _manufactureDifficultyRepositoryMock = new();
    private readonly Mock<IManufacturedProductInventoryRepository> _manufacturedInventoryRepositoryMock = new();
    private readonly Mock<ICatalogResilienceService> _resilienceServiceMock = new();
    private readonly Mock<ICatalogMergeScheduler> _mergeSchedulerMock = new();
    private readonly IMemoryCache _cache = new MemoryCache(new MemoryCacheOptions());
    private readonly Mock<TimeProvider> _timeProviderMock = new();
    private readonly Mock<IOptions<DataSourceOptions>> _optionsMock = new();
    private readonly Mock<IOptions<CatalogCacheOptions>> _cacheOptionsMock = new();

    private readonly CatalogRepository _repository;

    public CatalogRepositoryTests()
    {
        _productEshopUrlClientMock.Setup(x => x.GetAllAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ProductEshopUrl>());
        _manufacturedInventoryRepositoryMock.Setup(x => x.GetTotalAmountByProductCodeAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Dictionary<string, decimal>());

        _optionsMock.Setup(x => x.Value).Returns(new DataSourceOptions
        {
            SalesHistoryDays = 30,
            PurchaseHistoryDays = 30,
            ConsumedHistoryDays = 30,
            ManufactureHistoryDays = 30,
        });
        _cacheOptionsMock.Setup(x => x.Value).Returns(new CatalogCacheOptions
        {
            EnableBackgroundMerge = false // legacy tests assume eviction-on-set behavior
        });
        _timeProviderMock.Setup(x => x.GetUtcNow()).Returns(DateTimeOffset.UtcNow);

        // Resilience pass-through for the four typed signatures the production code uses
        _resilienceServiceMock.Setup(x => x.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<IEnumerable<CatalogSaleRecord>>>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IEnumerable<CatalogSaleRecord>>>, string, CancellationToken>((op, _, ct) => op(ct));
        _resilienceServiceMock.Setup(x => x.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<IEnumerable<CatalogAttributes>>>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<IEnumerable<CatalogAttributes>>>, string, CancellationToken>((op, _, ct) => op(ct));
        _resilienceServiceMock.Setup(x => x.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<List<ErpStock>>>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<List<ErpStock>>>, string, CancellationToken>((op, _, ct) => op(ct));
        _resilienceServiceMock.Setup(x => x.ExecuteWithResilienceAsync(
                It.IsAny<Func<CancellationToken, Task<List<EshopStock>>>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns<Func<CancellationToken, Task<List<EshopStock>>>, string, CancellationToken>((op, _, ct) => op(ct));

        var cacheStore = new CatalogCacheStore(
            _cache,
            _timeProviderMock.Object,
            _cacheOptionsMock.Object,
            _mergeSchedulerMock.Object,
            Mock.Of<ILogger<CatalogCacheStore>>());

        var mergeService = new CatalogMergeService(
            cacheStore,
            _mergeSchedulerMock.Object,
            _timeProviderMock.Object,
            Mock.Of<ILogger<CatalogMergeService>>());

        var refreshService = new CatalogDataRefreshService(
            _salesClientMock.Object,
            _attributesClientMock.Object,
            _eshopStockClientMock.Object,
            _consumedMaterialClientMock.Object,
            _purchaseHistoryClientMock.Object,
            _erpStockClientMock.Object,
            _lotsClientMock.Object,
            _productPriceEshopClientMock.Object,
            _productPriceErpClientMock.Object,
            _productEshopUrlClientMock.Object,
            _transportBoxRepositoryMock.Object,
            _stockTakingRepositoryMock.Object,
            _purchaseOrderRepositoryMock.Object,
            _manufactureOrderRepositoryMock.Object,
            _manufactureHistoryClientMock.Object,
            _manufactureDifficultyRepositoryMock.Object,
            _manufacturedInventoryRepositoryMock.Object,
            _resilienceServiceMock.Object,
            _timeProviderMock.Object,
            _optionsMock.Object,
            cacheStore,
            Mock.Of<ILogger<CatalogDataRefreshService>>());

        _repository = new CatalogRepository(
            cacheStore,
            mergeService,
            refreshService,
            _mergeSchedulerMock.Object);
    }

    // [Fact] methods below remain unchanged.
```

Also remove the `_manufactureClientMock` field and the `_loggerMock` field — both unused now. Remove the matching `using` directives only if they become unused (run `dotnet build` to find them).

- [ ] **Step 2: Apply the same pattern to `CatalogRepositoryCacheOptimizationTests.cs`**

Repeat Step 1's constructor swap inside `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs`. The shape is identical — same fields, same wiring helper. Keep all `[Fact]` test methods unchanged.

- [ ] **Step 3: Update `CatalogRepositoryDebugTest.cs`**

Read the file first to see its current constructor shape:
```bash
cat /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend/test/Anela.Heblo.Tests/Controllers/CatalogRepositoryDebugTest.cs
```
Apply the same constructor swap pattern as Step 1. If the file only declares `private readonly CatalogRepository _repository` and instantiates it without mocking every service, simplify to use the new 4-arg constructor with `Mock.Of<>()` defaults.

- [ ] **Step 4: Run the full catalog test suite**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Catalog" --logger "console;verbosity=minimal" 2>&1 | tail -15
```
Expected: all tests in the `Catalog` namespace pass — including the migrated old suites and the three new ones.

- [ ] **Step 5: Run the full backend test suite to confirm no other test broke**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet test Anela.Heblo.sln --logger "console;verbosity=minimal" 2>&1 | tail -10
```
Expected: total pass-count matches the baseline from Task 1 Step 2 **plus** the 11 new tests (5 cache-store + 2 merge-service + 3 refresh-service + 1 wiring integration). No regressions.

- [ ] **Step 6: Validate invariants**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi
# Invariant 3: only CatalogCacheStore touches IMemoryCache inside the Catalog feature
grep -rn "IMemoryCache" backend/src/Anela.Heblo.Application/Features/Catalog/ --include="*.cs"
```
Expected: only `CatalogCacheStore.cs` matches.

```bash
# Invariant 4: CatalogRepository must not call SetMergeCallback
grep -n "SetMergeCallback" backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs
```
Expected: no matches.

```bash
# Invariant 2: task IDs preserved
grep -n "RegisterRefreshTask<ICatalogRepository>" backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs | wc -l
```
Expected: 19 (one per refresh method).

- [ ] **Step 7: `dotnet format`**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet format Anela.Heblo.sln 2>&1 | tail -5
```
Expected: no warnings.

- [ ] **Step 8: Commit the slimmed repository + interface trim + DI + test migration**

```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi
git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs \
        backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs \
        backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs \
        backend/src/Anela.Heblo.Persistence/Repositories/MockCatalogRepository.cs \
        backend/test/Anela.Heblo.Tests/Domain/Catalog/CatalogRepositoryTests.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogRepositoryCacheOptimizationTests.cs \
        backend/test/Anela.Heblo.Tests/Controllers/CatalogRepositoryDebugTest.cs \
        backend/test/Anela.Heblo.Tests/Features/Catalog/Infrastructure/CatalogMergeCallbackWiringTests.cs
git commit -m "refactor(catalog): slim CatalogRepository to delegating facade; remove obsolete ManufactureCostLoadDate"
```

---

## Task 10: Final validation

**Files:**
- None modified

- [ ] **Step 1: Full backend build + format**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet build Anela.Heblo.sln 2>&1 | tail -5 && dotnet format Anela.Heblo.sln --verify-no-changes 2>&1 | tail -5
```
Expected: `Build succeeded`, `format` reports no changes.

- [ ] **Step 2: Full test suite green**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi/backend
dotnet test Anela.Heblo.sln --logger "console;verbosity=minimal" 2>&1 | tail -10
```
Expected: all tests pass.

- [ ] **Step 3: Frontend untouched (sanity check)**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi
git diff --stat main...HEAD -- frontend/
```
Expected: empty output (no frontend changes).

- [ ] **Step 4: File-size targets met (NFR-3)**

Run:
```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi
wc -l backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs \
      backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogCacheStore.cs \
      backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogMergeService.cs \
      backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/CatalogDataRefreshService.cs
```
Expected:
- `CatalogRepository.cs` ≤ 250
- `CatalogCacheStore.cs` ≤ 500
- `CatalogMergeService.cs` ≤ 300
- `CatalogDataRefreshService.cs` ≤ 500

- [ ] **Step 5: Constructor-parameter cap satisfied**

- `CatalogRepository`: 4 (well under 10).
- `CatalogCacheStore`: 5.
- `CatalogMergeService`: 4.
- `CatalogDataRefreshService`: 22 (above the soft cap of 10; documented in spec FR-3 / arch-review risk row as acceptable until #2058 lands).

No action needed — this is by design.

- [ ] **Step 6: Done. Push the branch and open a PR.**

```bash
cd /volume1/development/Anela.Heblo/.worktrees/feat-arch-review-catalog-catalogrepository-vi
git push -u origin HEAD
```

Open the PR title: `refactor(catalog): decompose CatalogRepository into cache store, merge service, refresh service` and link to the spec.

---

## Self-Review Notes

**Spec coverage:**

- FR-1 `CatalogCacheStore` — Task 2.
- FR-2 `CatalogMergeService` (+ callback registration at startup) — Task 3 (incl. `CatalogMergeCallbackWiring`) + Task 7 (DI registration).
- FR-3 `CatalogDataRefreshService` — Task 4.
- FR-4 Slim `CatalogRepository` — Task 5.
- FR-5 Remove `CachedManufactureCostData` / `ManufactureCostLoadDate` — covered by rewriting `CatalogRepository` in Task 5 (the new file simply does not contain the obsolete property) + Task 6 (interface + mock trim). The `CachedManufactureCostDataKey` constant is also gone since the new file does not declare it.
- FR-6 DI registration & lifetimes — Task 7.
- FR-7 Tests — Tasks 2/3/4 add the new test classes; Task 9 migrates the old ones.

NFR-1 (behavior preservation): cache key strings preserved as literals (Task 2 Step 3), refresh task IDs preserved (Task 7 Step 2, validated Task 9 Step 6), `Task.Run` wrapper preserved (Task 3 Step 3 body of `ExecuteBackgroundMergeAsync`), `RefreshSalesData` stale-on-throw preserved (Task 4 Step 3 + tested Task 4 Step 5).

NFR-2 (performance): no extra `IMemoryCache.Get` calls inside `Merge()` — each source is read exactly once into a local variable at the top.

NFR-3 (size & ctor caps): validated Task 10 Step 4–5.

NFR-4 (security): no security-relevant change — pure internal restructure.

**Arch-review amendments incorporated:**
1. `IManufactureClient` dropped — Task 1 Step 5 verifies, Task 4 omits it.
2. `CachedManufactureCostDataKey` removed — Task 5 (new file omits it).
3. `MockCatalogRepository.ManufactureDifficultyLoadDate` left alone — Task 6 Step 2.
4. `_sourceLastUpdated` dropped — new `CatalogCacheStore` does not declare it (Task 2 Step 3).
5. `InvalidateSourceData` `EnableBackgroundMerge=false` branch preserved — Task 2 Step 3 (`InvalidateSourceData` private method).
6. Integration test for callback wiring — Task 8.
7. `sealed` on all three new classes — Task 2/3/4 source.
8. `Task.Run(() => Merge(), ct)` preserved — Task 3 Step 3 body.

**Type/method-name consistency check:** every method on `CatalogRepository` (read-side, refresh, load-date, merge status) matches `ICatalogRepository`. Each refresh delegation uses the same method name on `CatalogDataRefreshService` as on the interface. Load-date keys reference `CatalogCacheStore.SourceKeys.*` constants whose values are the same strings used historically (`"CachedSalesData"`, etc.) — verified bit-for-bit against `CatalogRepository.cs:573–791`.

**Placeholder scan:** none — every code step contains the full code; every assertion has an expected output.
