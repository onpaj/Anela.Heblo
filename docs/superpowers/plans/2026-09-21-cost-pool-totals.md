# Cost Pool Totals (M1 / M2 / M3) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an internal backend service that computes monthly Flexi ledger spend totals bucketed into three cost pools — M1 (VYROBA), M2 (SKLAD + MARKETING) and M3 (everything else, i.e. centrála and any unassigned cost centre) — and keeps them in a background-refreshed cache for other backend features to consume.

**Architecture:** A single unfiltered `ILedgerService.GetLedgerItems` call on debit account prefixes `51`/`52` replaces today's three department-filtered pulls. Results are grouped by `(month, department)` and folded into pools via a static department→pool map with M3 as the catch-all. The contract lives in `Domain/Accounting/CostPools` beside `ILedgerService`; the implementation lives in `Application/Shared/CostPools` because it is cross-cutting and its consumer is a feature not yet written. Storage is an `IMemoryCache` wrapper hydrated by a registered background refresh task, mirroring `SalesCostCache`/`SalesCostProvider`.

**Tech Stack:** .NET 8, xUnit, FluentAssertions, Moq, `Microsoft.Extensions.Caching.Memory`, the in-house `Anela.Heblo.Xcc.Services.BackgroundRefresh` scheduler.

**Spec:** `docs/superpowers/specs/2026-09-21-cost-pool-totals-design.md`

## Global Constraints

- **Debit account prefixes are exactly `"51"` and `"52"`** — the same set `LedgerService.GetDirectCosts` uses. Do not widen.
- **Department→pool map:** `VYROBA` → `M1`; `SKLAD` and `MARKETING` → `M2`; **everything else, including `null` and empty string** → `M3`.
- **Department matching is case-insensitive** (`StringComparer.OrdinalIgnoreCase`).
- **Do not modify `SalesCostProvider`, `FlatManufactureCostProvider`, `MarginData`, or any DTO.** Existing margin numbers must come out bit-identical. The DRY cleanup of those providers is an explicit follow-up, out of scope here.
- **No database migration, no HTTP endpoint, no MediatR request/response, no frontend change.** Nothing in `frontend/` is touched and the generated TypeScript client is unaffected.
- **Sum `item.Amount` over the items the ledger returns.** Trust the server-side debit-prefix filter; do **not** re-check `DebitAccountNumber` client-side. This matches `LedgerService.GetCosts` and is what keeps M2 identical to the margin engine's M2.
- **Errors propagate.** No catch-and-return-empty anywhere. `RefreshAsync` logs then rethrows.
- **`MonthlyCostPool.Month` is a `DateTime` set to the first day of the month at midnight**, matching the existing `MonthlyCost` value object.
- Backend validation gate before declaring done: `dotnet build` and `dotnet format` from the repo root (the solution `Anela.Heblo.sln` is at the repo root).
- **Test-run gotcha:** `dotnet test` can hang at 0% CPU when another worktree runs it concurrently. Build first, then run with `--no-build -p:UseSharedCompilation=false`.

---

### Task 1: Domain types and the department→pool mapping

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Accounting/CostPools/CostPool.cs`
- Create: `backend/src/Anela.Heblo.Domain/Accounting/CostPools/MonthlyCostPool.cs`
- Create: `backend/src/Anela.Heblo.Application/Shared/CostPools/CostPoolDefinition.cs`
- Test: `backend/test/Anela.Heblo.Tests/Shared/CostPools/CostPoolDefinitionTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: `enum CostPool { M1, M2, M3 }`; `record MonthlyCostPool(DateTime Month, CostPool Pool, decimal Amount)`; `static class CostPoolDefinition` with `CostPool Resolve(string? department)`, `IReadOnlyList<CostPool> All`, and `const string DirectCostAccountPrefixes` members `AccountPrefixMaterial = "51"`, `AccountPrefixPersonnel = "52"`.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Shared/CostPools/CostPoolDefinitionTests.cs`:

```csharp
using Anela.Heblo.Application.Shared.CostPools;
using Anela.Heblo.Domain.Accounting.CostPools;
using FluentAssertions;
using Xunit;

namespace Anela.Heblo.Tests.Shared.CostPools;

public class CostPoolDefinitionTests
{
    [Theory]
    [InlineData("VYROBA")]
    [InlineData("vyroba")]
    [InlineData("Vyroba")]
    public void Resolve_MapsManufacturingDepartmentToM1(string department)
    {
        // Act
        var pool = CostPoolDefinition.Resolve(department);

        // Assert
        pool.Should().Be(CostPool.M1);
    }

    [Theory]
    [InlineData("SKLAD")]
    [InlineData("MARKETING")]
    [InlineData("marketing")]
    public void Resolve_MapsWarehouseAndMarketingDepartmentsToM2(string department)
    {
        // Act
        var pool = CostPoolDefinition.Resolve(department);

        // Assert
        pool.Should().Be(CostPool.M2);
    }

    [Theory]
    [InlineData("CENTRALA")]
    [InlineData("ESHOP")]
    [InlineData("something-nobody-has-seen-before")]
    public void Resolve_MapsUnrecognisedDepartmentToM3(string department)
    {
        // Act
        var pool = CostPoolDefinition.Resolve(department);

        // Assert
        pool.Should().Be(CostPool.M3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_MapsMissingDepartmentToM3(string? department)
    {
        // Act
        var pool = CostPoolDefinition.Resolve(department);

        // Assert
        pool.Should().Be(CostPool.M3);
    }

    [Fact]
    public void All_ListsEveryPoolExactlyOnce()
    {
        // Act
        var all = CostPoolDefinition.All;

        // Assert
        all.Should().BeEquivalentTo(new[] { CostPool.M1, CostPool.M2, CostPool.M3 });
        all.Should().OnlyHaveUniqueItems();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: FAIL to compile — `CostPoolDefinition` and `CostPool` do not exist.

- [ ] **Step 3: Create the `CostPool` enum**

Create `backend/src/Anela.Heblo.Domain/Accounting/CostPools/CostPool.cs`:

```csharp
namespace Anela.Heblo.Domain.Accounting.CostPools;

/// <summary>
/// Buckets that company direct costs (accounts 51, 52) are split into.
/// M3 is the complement of M1 and M2 - every department that is not
/// explicitly mapped lands there, so the three pools always sum to the
/// full ledger total for the period.
/// </summary>
public enum CostPool
{
    /// <summary>Manufacturing (VYROBA).</summary>
    M1,

    /// <summary>Warehouse and marketing (SKLAD, MARKETING).</summary>
    M2,

    /// <summary>Overhead - centrala, rezie and anything unassigned.</summary>
    M3
}
```

- [ ] **Step 4: Create the `MonthlyCostPool` value object**

Create `backend/src/Anela.Heblo.Domain/Accounting/CostPools/MonthlyCostPool.cs`:

```csharp
namespace Anela.Heblo.Domain.Accounting.CostPools;

/// <summary>
/// Total spend for one cost pool in one calendar month.
/// Internal domain type - never crosses the OpenAPI boundary, so a record
/// is allowed here (see CLAUDE.md: DTOs are classes, domain types may be records).
/// </summary>
/// <param name="Month">First day of the calendar month, at midnight.</param>
/// <param name="Pool">Which pool this total belongs to.</param>
/// <param name="Amount">Total spend in CZK. Zero when there was no spend.</param>
public record MonthlyCostPool(DateTime Month, CostPool Pool, decimal Amount);
```

- [ ] **Step 5: Create `CostPoolDefinition`**

Create `backend/src/Anela.Heblo.Application/Shared/CostPools/CostPoolDefinition.cs`:

```csharp
using Anela.Heblo.Domain.Accounting.CostPools;

namespace Anela.Heblo.Application.Shared.CostPools;

/// <summary>
/// The single place that decides which cost pool a ledger department belongs to.
///
/// M1 and M2 are explicit; M3 is deliberately the catch-all so that a cost centre
/// added in Flexi later shows up in the totals instead of silently vanishing.
/// The cost of that choice is that a miscoded entry becomes overhead - CostPoolService
/// logs the distinct departments it folded into M3 so a new code is visible.
/// </summary>
public static class CostPoolDefinition
{
    /// <summary>Consumed material and services. Same prefix set as LedgerService.GetDirectCosts.</summary>
    public const string AccountPrefixMaterial = "51";

    /// <summary>Personnel costs. Same prefix set as LedgerService.GetDirectCosts.</summary>
    public const string AccountPrefixPersonnel = "52";

    public const string ManufacturingDepartment = "VYROBA";
    public const string WarehouseDepartment = "SKLAD";
    public const string MarketingDepartment = "MARKETING";

    private static readonly IReadOnlyDictionary<string, CostPool> DepartmentToPool =
        new Dictionary<string, CostPool>(StringComparer.OrdinalIgnoreCase)
        {
            [ManufacturingDepartment] = CostPool.M1,
            [WarehouseDepartment] = CostPool.M2,
            [MarketingDepartment] = CostPool.M2,
        };

    /// <summary>Every pool, in reporting order.</summary>
    public static IReadOnlyList<CostPool> All { get; } =
        new[] { CostPool.M1, CostPool.M2, CostPool.M3 };

    /// <summary>The debit account prefixes that define "direct cost".</summary>
    public static IReadOnlyList<string> AccountPrefixes { get; } =
        new[] { AccountPrefixMaterial, AccountPrefixPersonnel };

    /// <summary>
    /// Resolves a ledger department code to its pool. Unknown, null, empty and
    /// whitespace-only departments all resolve to M3.
    /// </summary>
    public static CostPool Resolve(string? department)
    {
        if (string.IsNullOrWhiteSpace(department))
        {
            return CostPool.M3;
        }

        return DepartmentToPool.TryGetValue(department, out var pool) ? pool : CostPool.M3;
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~CostPoolDefinitionTests"
```

Expected: PASS, 13 tests.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Accounting/CostPools \
        backend/src/Anela.Heblo.Application/Shared/CostPools \
        backend/test/Anela.Heblo.Tests/Shared/CostPools
git commit -m "feat: add cost pool domain types and department mapping

M3 is the catch-all so a new Flexi cost centre lands in the totals
rather than disappearing from them."
```

---

### Task 2: Cost pool cache

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Accounting/CostPools/CostPoolCacheData.cs`
- Create: `backend/src/Anela.Heblo.Domain/Accounting/CostPools/ICostPoolCache.cs`
- Create: `backend/src/Anela.Heblo.Application/Shared/CostPools/CostPoolCache.cs`
- Test: `backend/test/Anela.Heblo.Tests/Shared/CostPools/CostPoolCacheTests.cs`

**Interfaces:**
- Consumes: `CostPool`, `MonthlyCostPool` from Task 1.
- Produces: `CostPoolCacheData` with `IReadOnlyList<MonthlyCostPool> Pools`, `DateTime LastUpdated`, `DateOnly DataFrom`, `DateOnly DataTo`, `bool IsHydrated`, and `static CostPoolCacheData Empty()`; `ICostPoolCache` with `Task<CostPoolCacheData> GetCachedDataAsync(CancellationToken)`, `Task SetCachedDataAsync(CostPoolCacheData, CancellationToken)`, `bool IsHydrated`; `CostPoolCache : ICostPoolCache`.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Shared/CostPools/CostPoolCacheTests.cs`:

```csharp
using Anela.Heblo.Application.Shared.CostPools;
using Anela.Heblo.Domain.Accounting.CostPools;
using FluentAssertions;
using Microsoft.Extensions.Caching.Memory;
using Xunit;

namespace Anela.Heblo.Tests.Shared.CostPools;

public class CostPoolCacheTests
{
    private static CostPoolCache CreateCache() =>
        new(new MemoryCache(new MemoryCacheOptions()));

    [Fact]
    public async Task GetCachedDataAsync_ReturnsEmptyUnhydratedData_WhenNothingStored()
    {
        // Arrange
        var cache = CreateCache();

        // Act
        var data = await cache.GetCachedDataAsync();

        // Assert
        data.IsHydrated.Should().BeFalse();
        data.Pools.Should().BeEmpty();
    }

    [Fact]
    public void IsHydrated_IsFalse_BeforeAnythingIsStored()
    {
        // Arrange
        var cache = CreateCache();

        // Act & Assert
        cache.IsHydrated.Should().BeFalse();
    }

    [Fact]
    public async Task SetCachedDataAsync_RoundTripsStoredData()
    {
        // Arrange
        var cache = CreateCache();
        var stored = new CostPoolCacheData
        {
            Pools = new[] { new MonthlyCostPool(new DateTime(2026, 7, 1), CostPool.M3, 903_000m) },
            LastUpdated = new DateTime(2026, 9, 21, 3, 0, 0, DateTimeKind.Utc),
            DataFrom = new DateOnly(2026, 1, 1),
            DataTo = new DateOnly(2026, 9, 30),
            IsHydrated = true
        };

        // Act
        await cache.SetCachedDataAsync(stored);
        var loaded = await cache.GetCachedDataAsync();

        // Assert
        loaded.Should().BeEquivalentTo(stored);
        cache.IsHydrated.Should().BeTrue();
    }

    [Fact]
    public async Task SetCachedDataAsync_OverwritesPreviousData()
    {
        // Arrange
        var cache = CreateCache();
        await cache.SetCachedDataAsync(new CostPoolCacheData
        {
            Pools = new[] { new MonthlyCostPool(new DateTime(2026, 7, 1), CostPool.M2, 1m) },
            IsHydrated = true
        });

        // Act
        await cache.SetCachedDataAsync(new CostPoolCacheData
        {
            Pools = new[] { new MonthlyCostPool(new DateTime(2026, 8, 1), CostPool.M2, 2m) },
            IsHydrated = true
        });
        var loaded = await cache.GetCachedDataAsync();

        // Assert
        loaded.Pools.Should().ContainSingle()
            .Which.Should().Be(new MonthlyCostPool(new DateTime(2026, 8, 1), CostPool.M2, 2m));
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: FAIL to compile — `CostPoolCache` and `CostPoolCacheData` do not exist.

- [ ] **Step 3: Create `CostPoolCacheData`**

Create `backend/src/Anela.Heblo.Domain/Accounting/CostPools/CostPoolCacheData.cs`:

```csharp
namespace Anela.Heblo.Domain.Accounting.CostPools;

/// <summary>
/// Immutable wrapper for cached cost pool totals with metadata.
/// Mirrors CostCacheData in the Catalog module.
/// </summary>
public class CostPoolCacheData
{
    /// <summary>Monthly totals for every pool across the cached window.</summary>
    public IReadOnlyList<MonthlyCostPool> Pools { get; init; } = Array.Empty<MonthlyCostPool>();

    /// <summary>Timestamp when the cache was last successfully updated.</summary>
    public DateTime LastUpdated { get; init; }

    /// <summary>Start date of the cached window.</summary>
    public DateOnly DataFrom { get; init; }

    /// <summary>End date of the cached window.</summary>
    public DateOnly DataTo { get; init; }

    /// <summary>True once the cache has been successfully hydrated at least once.</summary>
    public bool IsHydrated { get; init; }

    /// <summary>Creates empty data for cold-start scenarios.</summary>
    public static CostPoolCacheData Empty() => new()
    {
        Pools = Array.Empty<MonthlyCostPool>(),
        LastUpdated = DateTime.MinValue,
        DataFrom = DateOnly.MinValue,
        DataTo = DateOnly.MinValue,
        IsHydrated = false
    };

    /// <summary>
    /// True when this cached window fully contains the requested range.
    /// </summary>
    public bool Covers(DateOnly from, DateOnly to) =>
        IsHydrated && DataFrom <= from && DataTo >= to;
}
```

- [ ] **Step 4: Create `ICostPoolCache`**

Create `backend/src/Anela.Heblo.Domain/Accounting/CostPools/ICostPoolCache.cs`:

```csharp
namespace Anela.Heblo.Domain.Accounting.CostPools;

/// <summary>
/// Storage for computed cost pool totals. Pure storage layer -
/// the bucketing logic lives in CostPoolService.
/// </summary>
public interface ICostPoolCache
{
    /// <summary>Gets cached totals. Returns unhydrated empty data when nothing is stored.</summary>
    Task<CostPoolCacheData> GetCachedDataAsync(CancellationToken ct = default);

    /// <summary>Stores computed totals, replacing whatever was there.</summary>
    Task SetCachedDataAsync(CostPoolCacheData data, CancellationToken ct = default);

    /// <summary>True when the cache holds data.</summary>
    bool IsHydrated { get; }
}
```

- [ ] **Step 5: Create `CostPoolCache`**

Create `backend/src/Anela.Heblo.Application/Shared/CostPools/CostPoolCache.cs`:

```csharp
using Anela.Heblo.Domain.Accounting.CostPools;
using Microsoft.Extensions.Caching.Memory;

namespace Anela.Heblo.Application.Shared.CostPools;

/// <summary>
/// In-memory cache for cost pool totals.
/// Pure storage layer - business logic resides in CostPoolService.
/// </summary>
public class CostPoolCache : ICostPoolCache
{
    private const string CacheKey = "CostPoolCache_Data";
    private readonly IMemoryCache _memoryCache;

    public CostPoolCache(IMemoryCache memoryCache)
    {
        _memoryCache = memoryCache;
    }

    public bool IsHydrated => _memoryCache.TryGetValue(CacheKey, out _);

    public Task<CostPoolCacheData> GetCachedDataAsync(CancellationToken ct = default)
    {
        if (_memoryCache.TryGetValue(CacheKey, out CostPoolCacheData? cachedData) && cachedData != null)
        {
            return Task.FromResult(cachedData);
        }

        return Task.FromResult(CostPoolCacheData.Empty());
    }

    public Task SetCachedDataAsync(CostPoolCacheData data, CancellationToken ct = default)
    {
        _memoryCache.Set(CacheKey, data);
        return Task.CompletedTask;
    }
}
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~CostPoolCacheTests"
```

Expected: PASS, 4 tests.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Accounting/CostPools \
        backend/src/Anela.Heblo.Application/Shared/CostPools \
        backend/test/Anela.Heblo.Tests/Shared/CostPools
git commit -m "feat: add cost pool cache storage layer"
```

---

### Task 3: Live computation of monthly pool totals

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Accounting/CostPools/ICostPoolService.cs`
- Create: `backend/src/Anela.Heblo.Application/Shared/CostPools/CostPoolService.cs`
- Test: `backend/test/Anela.Heblo.Tests/Shared/CostPools/CostPoolServiceTests.cs`

**Interfaces:**
- Consumes: `CostPool`, `MonthlyCostPool`, `CostPoolDefinition`, `ICostPoolCache`, `CostPoolCacheData` from Tasks 1-2. `ILedgerService.GetLedgerItems(DateTime dateFrom, DateTime dateTo, IEnumerable<string>? debitAccountPrefix, IEnumerable<string>? creditAccountPrefix, string? department, CancellationToken)` returning `IList<LedgerItem>`, where `LedgerItem` exposes `DateTime Date`, `string Department`, `decimal Amount`. `DataSourceOptions.ManufactureCostHistoryDays` (default 400) from `Anela.Heblo.Application.Common`.
- Produces: `ICostPoolService` with `Task<IReadOnlyList<MonthlyCostPool>> GetMonthlyPoolsAsync(DateOnly from, DateOnly to, CancellationToken ct = default)` and `Task RefreshAsync(CancellationToken ct = default)`; `CostPoolService : ICostPoolService` with public constructor `(ICostPoolCache cache, ILedgerService ledgerService, ILogger<CostPoolService> logger, IOptions<DataSourceOptions> options)`.

This task implements `GetMonthlyPoolsAsync` against the ledger only — it always computes live and ignores the cache. Task 4 adds the cache read and `RefreshAsync`. `RefreshAsync` is declared on the interface now but throws `NotImplementedException` until Task 4, so the interface does not churn between tasks.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Shared/CostPools/CostPoolServiceTests.cs`:

```csharp
using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Shared.CostPools;
using Anela.Heblo.Domain.Accounting.CostPools;
using Anela.Heblo.Domain.Accounting.Ledger;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Shared.CostPools;

/// <summary>
/// Tests for CostPoolService.
/// Collection attribute forces sequential execution: the service guards
/// RefreshAsync with a static SemaphoreSlim (added in Task 4).
/// </summary>
[Collection("CostPoolServiceTests")]
public class CostPoolServiceTests
{
    private static LedgerItem Entry(DateTime date, string? department, decimal amount) => new()
    {
        Date = date,
        Department = department!,
        Amount = amount,
        DocumentNumber = "DOC",
        ClientName = "CLIENT",
        VariableSymbol = "VS",
        DebitAccountNumber = "518100",
        DebitAccountName = "Ostatni sluzby",
        CreditAccountNumber = "321100",
        CreditAccountName = "Dodavatele"
    };

    private static CostPoolService CreateService(
        IList<LedgerItem> ledgerItems,
        Mock<ICostPoolCache>? cacheMock = null,
        Mock<ILogger<CostPoolService>>? loggerMock = null)
    {
        var ledgerMock = new Mock<ILedgerService>();
        ledgerMock
            .Setup(l => l.GetLedgerItems(
                It.IsAny<DateTime>(),
                It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(ledgerItems);

        var cache = cacheMock ?? new Mock<ICostPoolCache>();
        cache.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(CostPoolCacheData.Empty());

        return new CostPoolService(
            cache.Object,
            ledgerMock.Object,
            (loggerMock ?? new Mock<ILogger<CostPoolService>>()).Object,
            Options.Create(new DataSourceOptions()));
    }

    private static decimal AmountFor(
        IReadOnlyList<MonthlyCostPool> pools, int year, int month, CostPool pool) =>
        pools.Single(p => p.Month == new DateTime(year, month, 1) && p.Pool == pool).Amount;

    [Fact]
    public async Task GetMonthlyPoolsAsync_BucketsDepartmentsIntoTheirPools()
    {
        // Arrange
        var july = new DateTime(2026, 7, 15);
        var service = CreateService(new List<LedgerItem>
        {
            Entry(july, "VYROBA", 100m),
            Entry(july, "SKLAD", 30m),
            Entry(july, "MARKETING", 70m),
            Entry(july, "CENTRALA", 500m),
        });

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        // Assert
        AmountFor(pools, 2026, 7, CostPool.M1).Should().Be(100m);
        AmountFor(pools, 2026, 7, CostPool.M2).Should().Be(100m);
        AmountFor(pools, 2026, 7, CostPool.M3).Should().Be(500m);
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_FoldsMissingDepartmentIntoM3()
    {
        // Arrange
        var july = new DateTime(2026, 7, 15);
        var service = CreateService(new List<LedgerItem>
        {
            Entry(july, null, 11m),
            Entry(july, "", 22m),
            Entry(july, "   ", 33m),
        });

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        // Assert
        AmountFor(pools, 2026, 7, CostPool.M3).Should().Be(66m);
        AmountFor(pools, 2026, 7, CostPool.M1).Should().Be(0m);
        AmountFor(pools, 2026, 7, CostPool.M2).Should().Be(0m);
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_PoolsSumToTheFullLedgerTotal()
    {
        // Arrange - the balance invariant: nothing may be dropped on the floor
        var july = new DateTime(2026, 7, 15);
        var entries = new List<LedgerItem>
        {
            Entry(july, "VYROBA", 100m),
            Entry(july, "SKLAD", 30m),
            Entry(july, "MARKETING", 70m),
            Entry(july, "CENTRALA", 500m),
            Entry(july, "ESHOP", 250m),
            Entry(july, null, 12m),
        };
        var service = CreateService(entries);

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        // Assert
        pools.Sum(p => p.Amount).Should().Be(entries.Sum(e => e.Amount));
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_SeparatesMonths()
    {
        // Arrange
        var service = CreateService(new List<LedgerItem>
        {
            Entry(new DateTime(2026, 7, 15), "CENTRALA", 500m),
            Entry(new DateTime(2026, 8, 3), "CENTRALA", 800m),
        });

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 31));

        // Assert
        AmountFor(pools, 2026, 7, CostPool.M3).Should().Be(500m);
        AmountFor(pools, 2026, 8, CostPool.M3).Should().Be(800m);
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_EmitsZeroRowsForEveryPoolInEveryRequestedMonth()
    {
        // Arrange - one entry in August only, three months requested
        var service = CreateService(new List<LedgerItem>
        {
            Entry(new DateTime(2026, 8, 3), "SKLAD", 800m),
        });

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30));

        // Assert - 3 months x 3 pools, callers never distinguish "no data" from "no spend"
        pools.Should().HaveCount(9);
        AmountFor(pools, 2026, 7, CostPool.M2).Should().Be(0m);
        AmountFor(pools, 2026, 8, CostPool.M2).Should().Be(800m);
        AmountFor(pools, 2026, 9, CostPool.M2).Should().Be(0m);
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_OrdersByMonthThenPool()
    {
        // Arrange
        var service = CreateService(new List<LedgerItem>
        {
            Entry(new DateTime(2026, 8, 3), "SKLAD", 1m),
        });

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 8, 31));

        // Assert
        pools.Select(p => (p.Month, p.Pool)).Should().ContainInOrder(
            (new DateTime(2026, 7, 1), CostPool.M1),
            (new DateTime(2026, 7, 1), CostPool.M2),
            (new DateTime(2026, 7, 1), CostPool.M3),
            (new DateTime(2026, 8, 1), CostPool.M1),
            (new DateTime(2026, 8, 1), CostPool.M2),
            (new DateTime(2026, 8, 1), CostPool.M3));
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_QueriesDirectCostAccountsAcrossAllDepartments()
    {
        // Arrange
        var ledgerMock = new Mock<ILedgerService>();
        ledgerMock
            .Setup(l => l.GetLedgerItems(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<LedgerItem>());

        var cache = new Mock<ICostPoolCache>();
        cache.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(CostPoolCacheData.Empty());

        var service = new CostPoolService(
            cache.Object, ledgerMock.Object,
            new Mock<ILogger<CostPoolService>>().Object,
            Options.Create(new DataSourceOptions()));

        // Act
        await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        // Assert - one unfiltered pull on 51+52, not three department-filtered ones
        ledgerMock.Verify(l => l.GetLedgerItems(
            new DateTime(2026, 7, 1),
            new DateTime(2026, 7, 31, 23, 59, 59),
            It.Is<IEnumerable<string>>(p => p.SequenceEqual(new[] { "51", "52" })),
            null,
            null,
            It.IsAny<CancellationToken>()), Times.Once);
        ledgerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_ExpandsPartialMonthsToWholeMonths()
    {
        // Arrange - a mid-month entry must still be counted when the range starts mid-month
        var service = CreateService(new List<LedgerItem>
        {
            Entry(new DateTime(2026, 7, 2), "CENTRALA", 500m),
        });

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 20), new DateOnly(2026, 7, 25));

        // Assert
        AmountFor(pools, 2026, 7, CostPool.M3).Should().Be(500m);
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_PropagatesLedgerFailure()
    {
        // Arrange - a wrong zero in a financial calculation is worse than a visible failure
        var ledgerMock = new Mock<ILedgerService>();
        ledgerMock
            .Setup(l => l.GetLedgerItems(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("FlexiBee unreachable"));

        var cache = new Mock<ICostPoolCache>();
        cache.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
             .ReturnsAsync(CostPoolCacheData.Empty());

        var service = new CostPoolService(
            cache.Object, ledgerMock.Object,
            new Mock<ILogger<CostPoolService>>().Object,
            Options.Create(new DataSourceOptions()));

        // Act
        var act = () => service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_LogsTheDepartmentsFoldedIntoM3()
    {
        // Arrange - a miscoded entry becoming overhead must be visible in the logs
        var loggerMock = new Mock<ILogger<CostPoolService>>();
        var july = new DateTime(2026, 7, 15);
        var service = CreateService(new List<LedgerItem>
        {
            Entry(july, "CENTRALA", 500m),
            Entry(july, "ESHOP", 250m),
            Entry(july, "SKLAD", 30m),
        }, loggerMock: loggerMock);

        // Act
        await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        // Assert
        loggerMock.Verify(
            l => l.Log(
                LogLevel.Information,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) =>
                    v.ToString()!.Contains("CENTRALA") && v.ToString()!.Contains("ESHOP")),
                It.IsAny<Exception>(),
                It.IsAny<Func<It.IsAnyType, Exception?, string>>()),
            Times.Once);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: FAIL to compile — `CostPoolService` does not exist.

- [ ] **Step 3: Create `ICostPoolService`**

Create `backend/src/Anela.Heblo.Domain/Accounting/CostPools/ICostPoolService.cs`:

```csharp
namespace Anela.Heblo.Domain.Accounting.CostPools;

/// <summary>
/// Monthly company spend totals (accounts 51, 52) split into cost pools.
///
/// Reads the same ILedgerService path the margin engine uses, so the M2 total
/// here is the same number SalesCostProvider divides by sold pieces.
/// </summary>
public interface ICostPoolService
{
    /// <summary>
    /// Monthly totals for every pool covering the requested range.
    ///
    /// The range is expanded to whole calendar months. Every month in range is
    /// present for every pool, with Amount = 0 where there was no spend, so
    /// callers never have to distinguish "no data" from "no spend".
    ///
    /// Served from cache when the cached window covers the range, otherwise
    /// computed live. Ledger failures propagate - this never returns a
    /// silently-zeroed result.
    /// </summary>
    Task<IReadOnlyList<MonthlyCostPool>> GetMonthlyPoolsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default);

    /// <summary>
    /// Recomputes the default window into the cache. Registered as a background
    /// refresh task. Skips (does not queue) when a refresh is already running.
    /// </summary>
    Task RefreshAsync(CancellationToken ct = default);
}
```

- [ ] **Step 4: Create `CostPoolService` with live computation only**

Create `backend/src/Anela.Heblo.Application/Shared/CostPools/CostPoolService.cs`:

```csharp
using Anela.Heblo.Application.Common;
using Anela.Heblo.Domain.Accounting.CostPools;
using Anela.Heblo.Domain.Accounting.Ledger;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Anela.Heblo.Application.Shared.CostPools;

/// <summary>
/// Computes monthly spend totals per cost pool from the Flexi ledger.
///
/// One unfiltered pull on accounts 51+52 replaces the three department-filtered
/// pulls the cost providers make today. Amounts are summed exactly as
/// LedgerService.GetCosts does - trusting the server-side debit-prefix filter
/// rather than re-checking client-side - which is what keeps the M2 total here
/// identical to the margin engine's M2.
/// </summary>
public class CostPoolService : ICostPoolService
{
    private readonly ICostPoolCache _cache;
    private readonly ILedgerService _ledgerService;
    private readonly ILogger<CostPoolService> _logger;
    private readonly DataSourceOptions _options;

    public CostPoolService(
        ICostPoolCache cache,
        ILedgerService ledgerService,
        ILogger<CostPoolService> logger,
        IOptions<DataSourceOptions> options)
    {
        _cache = cache;
        _ledgerService = ledgerService;
        _logger = logger;
        _options = options.Value;
    }

    public async Task<IReadOnlyList<MonthlyCostPool>> GetMonthlyPoolsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        return await ComputeAsync(from, to, ct);
    }

    public Task RefreshAsync(CancellationToken ct = default)
    {
        throw new NotImplementedException("Implemented in Task 4.");
    }

    private async Task<IReadOnlyList<MonthlyCostPool>> ComputeAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct)
    {
        var (rangeStart, rangeEnd) = ToWholeMonthRange(from, to);

        var ledgerItems = await _ledgerService.GetLedgerItems(
            rangeStart,
            rangeEnd,
            debitAccountPrefix: CostPoolDefinition.AccountPrefixes,
            creditAccountPrefix: null,
            department: null,
            cancellationToken: ct);

        LogOverheadDepartments(ledgerItems);

        var totals = ledgerItems
            .GroupBy(item => (
                Month: new DateTime(item.Date.Year, item.Date.Month, 1),
                Pool: CostPoolDefinition.Resolve(item.Department)))
            .ToDictionary(g => g.Key, g => g.Sum(item => item.Amount));

        return GenerateMonths(rangeStart, rangeEnd)
            .SelectMany(month => CostPoolDefinition.All.Select(pool =>
                new MonthlyCostPool(
                    month,
                    pool,
                    totals.TryGetValue((month, pool), out var amount) ? amount : 0m)))
            .ToList();
    }

    /// <summary>
    /// M3 is a catch-all, so a miscoded entry silently becomes overhead.
    /// Logging the codes that landed there makes a new or wrong one visible.
    /// </summary>
    private void LogOverheadDepartments(IEnumerable<LedgerItem> ledgerItems)
    {
        var overheadDepartments = ledgerItems
            .Where(item => CostPoolDefinition.Resolve(item.Department) == CostPool.M3)
            .Select(item => string.IsNullOrWhiteSpace(item.Department) ? "(none)" : item.Department)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(d => d, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (overheadDepartments.Count == 0)
        {
            return;
        }

        _logger.LogInformation(
            "CostPool M3 absorbed spend from departments: {OverheadDepartments}",
            string.Join(", ", overheadDepartments));
    }

    /// <summary>
    /// Expands a date range to whole calendar months, matching the window logic
    /// the Catalog cost providers use in their GetDateRange helpers.
    /// </summary>
    private static (DateTime start, DateTime end) ToWholeMonthRange(DateOnly from, DateOnly to)
    {
        var start = new DateTime(from.Year, from.Month, 1);
        var end = new DateTime(
            to.Year, to.Month, DateTime.DaysInMonth(to.Year, to.Month), 23, 59, 59);

        return (start, end);
    }

    private static IEnumerable<DateTime> GenerateMonths(DateTime start, DateTime end)
    {
        var current = new DateTime(start.Year, start.Month, 1);
        var last = new DateTime(end.Year, end.Month, 1);

        while (current <= last)
        {
            yield return current;
            current = current.AddMonths(1);
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~CostPoolServiceTests"
```

Expected: PASS, 10 tests.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Accounting/CostPools \
        backend/src/Anela.Heblo.Application/Shared/CostPools \
        backend/test/Anela.Heblo.Tests/Shared/CostPools
git commit -m "feat: compute monthly cost pool totals from the Flexi ledger

One unfiltered pull on accounts 51+52 replaces three department-filtered
ones. M1+M2+M3 sums to the full ledger total, asserted as an invariant."
```

---

### Task 4: Cache read path, refresh, and concurrency guard

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Shared/CostPools/CostPoolService.cs`
- Test: `backend/test/Anela.Heblo.Tests/Shared/CostPools/CostPoolServiceCacheTests.cs`

**Interfaces:**
- Consumes: everything from Tasks 1-3.
- Produces: a working `CostPoolService.RefreshAsync`; `GetMonthlyPoolsAsync` now serves from cache when `CostPoolCacheData.Covers(from, to)` is true.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Shared/CostPools/CostPoolServiceCacheTests.cs`:

```csharp
using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Shared.CostPools;
using Anela.Heblo.Domain.Accounting.CostPools;
using Anela.Heblo.Domain.Accounting.Ledger;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Shared.CostPools;

/// <summary>
/// Tests for the CostPoolService cache and refresh paths.
/// Collection attribute forces sequential execution: RefreshAsync is guarded
/// by a static SemaphoreSlim shared across instances.
/// </summary>
[Collection("CostPoolServiceTests")]
public class CostPoolServiceCacheTests
{
    private static LedgerItem Entry(DateTime date, string department, decimal amount) => new()
    {
        Date = date,
        Department = department,
        Amount = amount,
        DocumentNumber = "DOC",
        ClientName = "CLIENT",
        VariableSymbol = "VS",
        DebitAccountNumber = "518100",
        DebitAccountName = "Ostatni sluzby",
        CreditAccountNumber = "321100",
        CreditAccountName = "Dodavatele"
    };

    private static Mock<ILedgerService> LedgerReturning(params LedgerItem[] items)
    {
        var mock = new Mock<ILedgerService>();
        mock.Setup(l => l.GetLedgerItems(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(items.ToList());
        return mock;
    }

    private static CostPoolService CreateService(
        Mock<ICostPoolCache> cacheMock,
        Mock<ILedgerService> ledgerMock,
        int historyDays = 400) =>
        new(cacheMock.Object,
            ledgerMock.Object,
            new Mock<ILogger<CostPoolService>>().Object,
            Options.Create(new DataSourceOptions { ManufactureCostHistoryDays = historyDays }));

    [Fact]
    public async Task GetMonthlyPoolsAsync_ServesFromCache_WhenCachedWindowCoversRange()
    {
        // Arrange
        var cached = new CostPoolCacheData
        {
            Pools = new[]
            {
                new MonthlyCostPool(new DateTime(2026, 7, 1), CostPool.M3, 903_000m),
                new MonthlyCostPool(new DateTime(2026, 8, 1), CostPool.M3, 871_400m),
            },
            DataFrom = new DateOnly(2026, 1, 1),
            DataTo = new DateOnly(2026, 12, 31),
            IsHydrated = true
        };
        var cacheMock = new Mock<ICostPoolCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(cached);
        var ledgerMock = LedgerReturning();
        var service = CreateService(cacheMock, ledgerMock);

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        // Assert
        pools.Should().ContainSingle()
            .Which.Should().Be(new MonthlyCostPool(new DateTime(2026, 7, 1), CostPool.M3, 903_000m));
        ledgerMock.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_ComputesLive_WhenCachedWindowDoesNotCoverRange()
    {
        // Arrange - cache holds 2026 only, caller asks about 2025
        var cached = new CostPoolCacheData
        {
            Pools = Array.Empty<MonthlyCostPool>(),
            DataFrom = new DateOnly(2026, 1, 1),
            DataTo = new DateOnly(2026, 12, 31),
            IsHydrated = true
        };
        var cacheMock = new Mock<ICostPoolCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(cached);
        var ledgerMock = LedgerReturning(Entry(new DateTime(2025, 5, 4), "CENTRALA", 42m));
        var service = CreateService(cacheMock, ledgerMock);

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2025, 5, 1), new DateOnly(2025, 5, 31));

        // Assert
        pools.Single(p => p.Pool == CostPool.M3).Amount.Should().Be(42m);
        ledgerMock.Verify(l => l.GetLedgerItems(
            It.IsAny<DateTime>(), It.IsAny<DateTime>(),
            It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetMonthlyPoolsAsync_ComputesLive_WhenCacheIsNotHydrated()
    {
        // Arrange - an unhydrated cache must not yield a silently-empty result
        var cacheMock = new Mock<ICostPoolCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(CostPoolCacheData.Empty());
        var ledgerMock = LedgerReturning(Entry(new DateTime(2026, 7, 4), "CENTRALA", 77m));
        var service = CreateService(cacheMock, ledgerMock);

        // Act
        var pools = await service.GetMonthlyPoolsAsync(new DateOnly(2026, 7, 1), new DateOnly(2026, 7, 31));

        // Assert
        pools.Single(p => p.Pool == CostPool.M3).Amount.Should().Be(77m);
    }

    [Fact]
    public async Task RefreshAsync_StoresComputedTotalsForTheConfiguredWindow()
    {
        // Arrange
        var cacheMock = new Mock<ICostPoolCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(CostPoolCacheData.Empty());
        CostPoolCacheData? stored = null;
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostPoolCacheData>(), It.IsAny<CancellationToken>()))
                 .Callback<CostPoolCacheData, CancellationToken>((d, _) => stored = d)
                 .Returns(Task.CompletedTask);
        var ledgerMock = LedgerReturning(Entry(DateTime.UtcNow.Date, "CENTRALA", 500m));
        var service = CreateService(cacheMock, ledgerMock, historyDays: 60);

        // Act
        await service.RefreshAsync();

        // Assert
        stored.Should().NotBeNull();
        stored!.IsHydrated.Should().BeTrue();
        stored.Pools.Should().NotBeEmpty();
        stored.DataTo.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow));
        stored.DataFrom.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-60)));
    }

    [Fact]
    public async Task RefreshAsync_LogsAndRethrows_WhenLedgerFails()
    {
        // Arrange
        var cacheMock = new Mock<ICostPoolCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(CostPoolCacheData.Empty());
        var ledgerMock = new Mock<ILedgerService>();
        ledgerMock.Setup(l => l.GetLedgerItems(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("FlexiBee unreachable"));
        var service = CreateService(cacheMock, ledgerMock);

        // Act
        var act = () => service.RefreshAsync();

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>();
        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostPoolCacheData>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task RefreshAsync_SkipsConcurrentRefresh_RatherThanQueueingIt()
    {
        // Arrange - hold the first refresh inside its ledger call, so the second
        // one arrives while the static lock is still held
        var cacheMock = new Mock<ICostPoolCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(CostPoolCacheData.Empty());
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostPoolCacheData>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var entered = new TaskCompletionSource();
        var release = new TaskCompletionSource();

        var blockingLedger = new Mock<ILedgerService>();
        blockingLedger.Setup(l => l.GetLedgerItems(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(async () =>
            {
                entered.TrySetResult();
                await release.Task;
                return (IList<LedgerItem>)new List<LedgerItem>();
            });

        var first = CreateService(cacheMock, blockingLedger);
        var second = CreateService(cacheMock, LedgerReturning(Entry(DateTime.UtcNow.Date, "CENTRALA", 1m)));

        var firstRun = first.RefreshAsync();
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Act
        await second.RefreshAsync();

        // Assert - the second refresh returned immediately, computing and storing nothing
        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostPoolCacheData>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // and the first one still completes normally once unblocked
        release.SetResult();
        await firstRun.WaitAsync(TimeSpan.FromSeconds(5));
        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostPoolCacheData>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task RefreshAsync_ReleasesItsLock_SoLaterRefreshesStillRun()
    {
        // Arrange - a failed refresh must not wedge the static semaphore shut
        var cacheMock = new Mock<ICostPoolCache>();
        cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
                 .ReturnsAsync(CostPoolCacheData.Empty());
        cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostPoolCacheData>(), It.IsAny<CancellationToken>()))
                 .Returns(Task.CompletedTask);

        var failingLedger = new Mock<ILedgerService>();
        failingLedger.Setup(l => l.GetLedgerItems(
                It.IsAny<DateTime>(), It.IsAny<DateTime>(),
                It.IsAny<IEnumerable<string>>(), It.IsAny<IEnumerable<string>>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new HttpRequestException("FlexiBee unreachable"));

        var failing = CreateService(cacheMock, failingLedger);
        await new Func<Task>(() => failing.RefreshAsync()).Should().ThrowAsync<HttpRequestException>();

        var healthy = CreateService(cacheMock, LedgerReturning(Entry(DateTime.UtcNow.Date, "CENTRALA", 1m)));

        // Act
        await healthy.RefreshAsync();

        // Assert
        cacheMock.Verify(
            c => c.SetCachedDataAsync(It.IsAny<CostPoolCacheData>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~CostPoolServiceCacheTests"
```

Expected: FAIL — `RefreshAsync` throws `NotImplementedException`, and the cache-hit test fails because `GetMonthlyPoolsAsync` still always computes live.

- [ ] **Step 3: Add the static refresh lock field**

In `backend/src/Anela.Heblo.Application/Shared/CostPools/CostPoolService.cs`, add as the first member of the class, immediately above `private readonly ICostPoolCache _cache;`:

```csharp
    private static readonly SemaphoreSlim RefreshLock = new(1, 1);
```

- [ ] **Step 4: Replace `GetMonthlyPoolsAsync` with the cache-aware version**

Replace the existing `GetMonthlyPoolsAsync` body:

```csharp
    public async Task<IReadOnlyList<MonthlyCostPool>> GetMonthlyPoolsAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken ct = default)
    {
        var cacheData = await _cache.GetCachedDataAsync(ct);

        if (cacheData.Covers(from, to))
        {
            return FilterToRange(cacheData.Pools, from, to);
        }

        return await ComputeAsync(from, to, ct);
    }

    private static IReadOnlyList<MonthlyCostPool> FilterToRange(
        IReadOnlyList<MonthlyCostPool> pools,
        DateOnly from,
        DateOnly to)
    {
        var firstMonth = new DateTime(from.Year, from.Month, 1);
        var lastMonth = new DateTime(to.Year, to.Month, 1);

        return pools
            .Where(p => p.Month >= firstMonth && p.Month <= lastMonth)
            .OrderBy(p => p.Month)
            .ThenBy(p => p.Pool)
            .ToList();
    }
```

- [ ] **Step 5: Replace the `RefreshAsync` stub with the real implementation**

Replace `public Task RefreshAsync(...) { throw new NotImplementedException(...); }` with:

```csharp
    public async Task RefreshAsync(CancellationToken ct = default)
    {
        if (!await RefreshLock.WaitAsync(0, ct))
        {
            _logger.LogInformation("CostPoolCache refresh already in progress, skipping");
            return;
        }

        try
        {
            _logger.LogInformation("Starting CostPoolCache refresh");

            var to = DateOnly.FromDateTime(DateTime.UtcNow);
            var from = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-_options.ManufactureCostHistoryDays));

            var pools = await ComputeAsync(from, to, ct);

            await _cache.SetCachedDataAsync(new CostPoolCacheData
            {
                Pools = pools,
                LastUpdated = DateTime.UtcNow,
                DataFrom = from,
                DataTo = to,
                IsHydrated = true
            }, ct);

            _logger.LogInformation(
                "CostPoolCache refreshed successfully: {RowCount} monthly pool totals covering {From} to {To}",
                pools.Count, from, to);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh CostPoolCache");
            throw;
        }
        finally
        {
            RefreshLock.Release();
        }
    }
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~CostPool"
```

Expected: PASS, 34 tests (13 + 4 + 10 + 7).

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/CostPools/CostPoolService.cs \
        backend/test/Anela.Heblo.Tests/Shared/CostPools/CostPoolServiceCacheTests.cs
git commit -m "feat: add cost pool cache read path and background refresh

An unhydrated cache falls through to a live computation rather than
returning empty - a silent zero in a financial calculation is worse
than a visible failure."
```

---

### Task 5: Parity test against SalesCostProvider

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Shared/CostPools/FakeLedgerService.cs`
- Test: `backend/test/Anela.Heblo.Tests/Shared/CostPools/CostPoolSalesCostParityTests.cs`

**Interfaces:**
- Consumes: `CostPoolService`, `CostPool` from Tasks 1-4; the existing `SalesCostProvider(ISalesCostCache, IServiceProvider, ILedgerService, ILogger<SalesCostProvider>, IOptions<DataSourceOptions>)`.
- Produces: `FakeLedgerService : ILedgerService` — a test double backed by one in-memory `List<LedgerItem>` serving both `GetLedgerItems` and `GetDirectCosts`.

This is the test that protects the design's central claim: the M2 total this service reports is the same number the margin engine spends. A Moq mock cannot prove it, because `GetLedgerItems` and `GetDirectCosts` would be stubbed independently — set up inconsistently, they would agree by accident. `FakeLedgerService` derives both from one list of entries, replicating what the real `LedgerService` does, so agreement means the two consumers genuinely read the same data.

The trick that makes the totals directly comparable: `SalesCostProvider` divides its pool by total sold pieces, so a catalog with **exactly one** sold piece makes its per-piece cost equal the whole pool.

- [ ] **Step 1: Write the fake ledger service**

Create `backend/test/Anela.Heblo.Tests/Shared/CostPools/FakeLedgerService.cs`:

```csharp
using Anela.Heblo.Domain.Accounting.Ledger;

namespace Anela.Heblo.Tests.Shared.CostPools;

/// <summary>
/// An ILedgerService backed by a single in-memory entry list, so that
/// GetLedgerItems and GetDirectCosts cannot disagree the way two independently
/// stubbed Moq setups can. Mirrors the real LedgerService: GetDirectCosts is
/// GetLedgerItems on prefixes 51+52, grouped by date and department.
/// </summary>
public class FakeLedgerService : ILedgerService
{
    private readonly IReadOnlyList<LedgerItem> _entries;

    public FakeLedgerService(IReadOnlyList<LedgerItem> entries)
    {
        _entries = entries;
    }

    public Task<IList<LedgerItem>> GetLedgerItems(
        DateTime dateFrom,
        DateTime dateTo,
        IEnumerable<string>? debitAccountPrefix = null,
        IEnumerable<string>? creditAccountPrefix = null,
        string? department = null,
        CancellationToken cancellationToken = default)
    {
        var prefixes = debitAccountPrefix?.ToList() ?? new List<string>();

        var matches = _entries
            .Where(e => e.Date >= dateFrom && e.Date <= dateTo)
            .Where(e => prefixes.Count == 0
                        || prefixes.Any(p => e.DebitAccountNumber?.StartsWith(p) == true))
            .Where(e => department == null
                        || string.Equals(e.Department, department, StringComparison.OrdinalIgnoreCase))
            .ToList();

        return Task.FromResult<IList<LedgerItem>>(matches);
    }

    public Task<IList<CostStatistics>> GetPersonalCosts(
        DateTime dateFrom, DateTime dateTo, string? department = null,
        CancellationToken cancellationToken = default) =>
        GetCosts(dateFrom, dateTo, new[] { "52" }, department, cancellationToken);

    public Task<IList<CostStatistics>> GetDirectCosts(
        DateTime dateFrom, DateTime dateTo, string? department = null,
        CancellationToken cancellationToken = default) =>
        GetCosts(dateFrom, dateTo, new[] { "51", "52" }, department, cancellationToken);

    public async Task<IList<CostStatistics>> GetCosts(
        DateTime dateFrom,
        DateTime dateTo,
        IEnumerable<string> debitAccountPrefixes,
        string? department = null,
        CancellationToken cancellationToken = default)
    {
        var items = await GetLedgerItems(
            dateFrom, dateTo, debitAccountPrefixes, null, department, cancellationToken);

        var grouped = items
            .GroupBy(item => new { Date = item.Date.Date, item.Department })
            .Select(g => new CostStatistics
            {
                Date = g.Key.Date,
                Department = g.Key.Department,
                Cost = g.Sum(item => item.Amount)
            })
            .OrderBy(cs => cs.Date)
            .ToList();

        return grouped;
    }
}
```

- [ ] **Step 2: Write the failing parity test**

Create `backend/test/Anela.Heblo.Tests/Shared/CostPools/CostPoolSalesCostParityTests.cs`:

```csharp
using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.CostProviders;
using Anela.Heblo.Application.Shared.CostPools;
using Anela.Heblo.Domain.Accounting.CostPools;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Shared.CostPools;

/// <summary>
/// Guards the design's central claim: the M2 total CostPoolService reports is
/// the same money SalesCostProvider spreads across products. Both read the same
/// FakeLedgerService, so agreement here is structural rather than coincidental.
///
/// Shares the SalesCostProviderTests collection because SalesCostProvider guards
/// RefreshAsync with its own static SemaphoreSlim.
/// </summary>
[Collection("SalesCostProviderTests")]
public class CostPoolSalesCostParityTests
{
    private const int HistoryDays = 90;

    private static LedgerItem Entry(DateTime date, string department, decimal amount) => new()
    {
        Date = date,
        Department = department,
        Amount = amount,
        DocumentNumber = "DOC",
        ClientName = "CLIENT",
        VariableSymbol = "VS",
        DebitAccountNumber = "518100",
        DebitAccountName = "Ostatni sluzby",
        CreditAccountNumber = "321100",
        CreditAccountName = "Dodavatele"
    };

    [Fact]
    public async Task M2PoolTotal_EqualsTheSpendSalesCostProviderDistributes()
    {
        // Arrange - one sold piece makes SalesCostProvider's cost-per-piece
        // equal its whole pool, so the two numbers are directly comparable.
        var saleDate = DateTime.UtcNow.Date.AddDays(-10);
        var entries = new List<LedgerItem>
        {
            Entry(saleDate, "SKLAD", 30_000m),
            Entry(saleDate, "MARKETING", 70_000m),
            Entry(saleDate, "VYROBA", 100_000m),
            Entry(saleDate, "CENTRALA", 500_000m),
        };
        var ledger = new FakeLedgerService(entries);

        var product = new CatalogAggregate
        {
            ProductCode = "PROD-1",
            SalesHistory = new List<CatalogSaleRecord>
            {
                new() { Date = saleDate, ProductCode = "PROD-1", ProductName = "PROD-1", AmountTotal = 1 }
            }
        };

        var repoMock = new Mock<ICatalogRepository>();
        repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<CatalogAggregate> { product });
        repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var serviceProviderMock = new Mock<IServiceProvider>();
        serviceProviderMock.Setup(sp => sp.GetService(typeof(ICatalogRepository)))
                           .Returns(repoMock.Object);

        CostCacheData? salesCosts = null;
        var salesCacheMock = new Mock<ISalesCostCache>();
        salesCacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
                      .Callback<CostCacheData, CancellationToken>((d, _) => salesCosts = d)
                      .Returns(Task.CompletedTask);

        var options = Options.Create(new DataSourceOptions { ManufactureCostHistoryDays = HistoryDays });

        var salesProvider = new SalesCostProvider(
            salesCacheMock.Object,
            serviceProviderMock.Object,
            ledger,
            new Mock<ILogger<SalesCostProvider>>().Object,
            options);

        var poolCacheMock = new Mock<ICostPoolCache>();
        poolCacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(CostPoolCacheData.Empty());

        var poolService = new CostPoolService(
            poolCacheMock.Object,
            ledger,
            new Mock<ILogger<CostPoolService>>().Object,
            options);

        // Act
        await salesProvider.RefreshAsync();
        var pools = await poolService.GetMonthlyPoolsAsync(
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-HistoryDays)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        // Assert
        salesCosts.Should().NotBeNull();
        var salesCostPerPiece = salesCosts!.ProductCosts["PROD-1"].First().Cost;
        var m2Total = pools.Where(p => p.Pool == CostPool.M2).Sum(p => p.Amount);

        m2Total.Should().Be(100_000m);
        salesCostPerPiece.Should().Be(m2Total);
    }

    [Fact]
    public async Task PoolsSumToTheFullLedgerTotal_IncludingSpendNoMarginLevelSeesToday()
    {
        // Arrange
        var date = DateTime.UtcNow.Date.AddDays(-10);
        var entries = new List<LedgerItem>
        {
            Entry(date, "SKLAD", 30_000m),
            Entry(date, "MARKETING", 70_000m),
            Entry(date, "VYROBA", 100_000m),
            Entry(date, "CENTRALA", 500_000m),
            Entry(date, "ESHOP", 250_000m),
        };
        var poolCacheMock = new Mock<ICostPoolCache>();
        poolCacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
                     .ReturnsAsync(CostPoolCacheData.Empty());

        var poolService = new CostPoolService(
            poolCacheMock.Object,
            new FakeLedgerService(entries),
            new Mock<ILogger<CostPoolService>>().Object,
            Options.Create(new DataSourceOptions { ManufactureCostHistoryDays = HistoryDays }));

        // Act
        var pools = await poolService.GetMonthlyPoolsAsync(
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-HistoryDays)),
            DateOnly.FromDateTime(DateTime.UtcNow));

        // Assert - 750 000 of this was invisible to the system before this feature
        pools.Sum(p => p.Amount).Should().Be(950_000m);
        pools.Where(p => p.Pool == CostPool.M3).Sum(p => p.Amount).Should().Be(750_000m);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail, then pass**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~CostPoolSalesCostParityTests"
```

Expected: PASS, 2 tests. These exercise only code written in Tasks 1-4, so they should pass on the first run — that is the point of a parity test. **If either fails, the defect is in `CostPoolService`, not in the test.** Investigate before changing the assertion.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Shared/CostPools/FakeLedgerService.cs \
        backend/test/Anela.Heblo.Tests/Shared/CostPools/CostPoolSalesCostParityTests.cs
git commit -m "test: assert cost pool M2 equals the spend SalesCostProvider distributes

Both consumers read one FakeLedgerService, so the agreement is
structural rather than two Moq stubs configured to match."
```

---

### Task 6: Dependency injection and background refresh registration

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Shared/CostPools/SharedCostPoolsModule.cs`
- Modify: `backend/src/Anela.Heblo.Application/ApplicationModule.cs` (add a `using` beside the other `Anela.Heblo.Application.Shared.*` imports, and a registration call beside `services.AddSharedRagModule(configuration);`)
- Modify: `backend/src/Anela.Heblo.API/appsettings.json` (add an `ICostPoolService` entry in the `BackgroundRefresh` section, after the `ISalesCostProvider` block ending at line 520)
- Test: `backend/test/Anela.Heblo.Tests/Shared/CostPools/CostPoolModuleRegistrationTests.cs`

**Interfaces:**
- Consumes: `ICostPoolService`, `CostPoolService`, `ICostPoolCache`, `CostPoolCache` from Tasks 1-4.
- Produces: `IServiceCollection AddSharedCostPoolsModule(this IServiceCollection services)`.

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Shared/CostPools/CostPoolModuleRegistrationTests.cs`:

```csharp
using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Shared.CostPools;
using Anela.Heblo.Domain.Accounting.CostPools;
using Anela.Heblo.Domain.Accounting.Ledger;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Shared.CostPools;

public class CostPoolModuleRegistrationTests
{
    private static ServiceProvider BuildProvider()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddOptions<DataSourceOptions>();
        services.AddSingleton(new Mock<ILedgerService>().Object);

        services.AddSharedCostPoolsModule();

        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddSharedCostPoolsModule_ResolvesCostPoolService()
    {
        // Arrange
        using var provider = BuildProvider();

        // Act
        var service = provider.GetService<ICostPoolService>();

        // Assert
        service.Should().BeOfType<CostPoolService>();
    }

    [Fact]
    public void AddSharedCostPoolsModule_ResolvesCostPoolCache()
    {
        // Arrange
        using var provider = BuildProvider();

        // Act
        var cache = provider.GetService<ICostPoolCache>();

        // Assert
        cache.Should().BeOfType<CostPoolCache>();
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
```

Expected: FAIL to compile — `AddSharedCostPoolsModule` does not exist.

- [ ] **Step 3: Create `SharedCostPoolsModule`**

Create `backend/src/Anela.Heblo.Application/Shared/CostPools/SharedCostPoolsModule.cs`:

```csharp
using Anela.Heblo.Domain.Accounting.CostPools;
using Anela.Heblo.Xcc.Services.BackgroundRefresh;
using Microsoft.Extensions.DependencyInjection;

namespace Anela.Heblo.Application.Shared.CostPools;

/// <summary>
/// Composition root for cost pool totals - shared across features rather than
/// owned by one, in the same spirit as Shared/Rag and Shared/Users.
/// </summary>
public static class SharedCostPoolsModule
{
    public static IServiceCollection AddSharedCostPoolsModule(this IServiceCollection services)
    {
        services.AddMemoryCache();

        services.AddScoped<ICostPoolCache, CostPoolCache>();
        services.AddScoped<ICostPoolService, CostPoolService>();

        services.RegisterRefreshTask<ICostPoolService>(
            "RefreshCache",
            (service, ct) => service.RefreshAsync(ct));

        return services;
    }
}
```

- [ ] **Step 4: Register the module in `ApplicationModule`**

In `backend/src/Anela.Heblo.Application/ApplicationModule.cs`, add beside the existing `using Anela.Heblo.Application.Shared.Rag;`:

```csharp
using Anela.Heblo.Application.Shared.CostPools;
```

and immediately after the line `services.AddSharedRagModule(configuration);`:

```csharp
        // Register shared cost pool totals (M1 / M2 / M3 ledger spend)
        services.AddSharedCostPoolsModule();
```

- [ ] **Step 5: Add the background refresh configuration**

In `backend/src/Anela.Heblo.API/appsettings.json`, inside the `BackgroundRefresh` section, add after the `ISalesCostProvider` block (which closes at line 520) and before `"IFinancialAnalysisService"`:

```json
    "ICostPoolService": {
      "RefreshCache": {
        "InitialDelay": "00:00:00",
        "RefreshInterval": "01:00:00",
        "Enabled": true,
        "HydrationTier": 2,
        "Description": "Refreshes cost pool spend totals (M1 VYROBA / M2 SKLAD+MARKETING / M3 rest)"
      }
    },
```

- [ ] **Step 6: Run the tests to verify they pass**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~CostPool"
```

Expected: PASS, 38 tests.

- [ ] **Step 7: Verify the application still starts with the new registration**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --no-build -p:UseSharedCompilation=false \
  --filter "FullyQualifiedName~ApplicationStartupTests"
```

Expected: PASS. A DI misregistration surfaces here as a container resolution failure.

- [ ] **Step 8: Run the full backend gate**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo
dotnet build
dotnet format --verify-no-changes || dotnet format
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --no-build -p:UseSharedCompilation=false
```

Expected: build succeeds, formatting clean, full unit suite green. Confirm in particular that `SalesCostProviderTests`, `MarginCalculationServiceTests` and `CatalogAnalyticsSourceAdapterTests` still pass — existing margin numbers must be unchanged.

- [ ] **Step 9: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Shared/CostPools/SharedCostPoolsModule.cs \
        backend/src/Anela.Heblo.Application/ApplicationModule.cs \
        backend/src/Anela.Heblo.API/appsettings.json \
        backend/test/Anela.Heblo.Tests/Shared/CostPools/CostPoolModuleRegistrationTests.cs
git commit -m "feat: register cost pool service and hourly refresh task

Centrala and every other unallocated cost centre now land in M3 instead
of being invisible to the system."
```

---

## Follow-up (not in this plan)

`SalesCostProvider` and `FlatManufactureCostProvider` could drop their own ledger pulls and consume `ICostPoolService`; they also duplicate `GetDateRange` and `GenerateMonthRange` between them. Deliberately excluded — it touches working margin code for no functional gain. Worth its own issue.
