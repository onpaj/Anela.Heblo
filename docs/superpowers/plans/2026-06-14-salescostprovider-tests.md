# SalesCostProvider Unit Test Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add xUnit test coverage for `SalesCostProvider` (currently 0%) to ≥80% via 7 focused unit tests (FR-1…FR-7 from `spec.r3.md`), with no production-code changes.

**Architecture:** A single new test file at `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs` mirrors the established pattern in the sibling `FlatManufactureCostProviderTests.cs`: xUnit + Moq, `[Collection]` to serialize against the static `RefreshLock`, relative-date assertions instead of clock injection, and `TaskCompletionSource`-gated mocks for the concurrency test. All shared helpers (`CreateProvider`, `BuildProduct`, `VerifyLog`) live as private static members in the same file.

**Tech Stack:** .NET 8 · xUnit 2.9.2 · Moq 4.20.72 · FluentAssertions 6.12.0 · Microsoft.Extensions.Logging.Abstractions · Microsoft.Extensions.Options

---

## File Structure

**Create (1 file):**
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs` — single test class `SalesCostProviderTests`, namespace `Anela.Heblo.Tests.Features.Catalog.CostProviders`, 7 `[Fact]`/`[Theory]` tests + 3 private static helpers (`BuildProduct`, `CreateProvider`, `VerifyLog`). Estimated ~550 lines.

**Modify:** None. **No production code touched.**

**No companion builder file.** Per arch-review Decision 4, builders stay inline until duplication appears across multiple provider test files.

---

## Type Reference Card (copy these into the test file)

Real signatures verified against the codebase at plan time. Do **not** alter — the file paths are listed in case the engineer needs to re-confirm.

```csharp
// SUT — backend/src/Anela.Heblo.Application/Features/Catalog/CostProviders/SalesCostProvider.cs
public class SalesCostProvider : ISalesCostProvider
{
    public SalesCostProvider(
        ISalesCostCache cache,
        ICatalogRepository catalogRepository,
        ILedgerService ledgerService,
        ILogger<SalesCostProvider> logger,
        IOptions<DataSourceOptions> options);

    public Task<Dictionary<string, List<MonthlyCost>>> GetCostsAsync(
        List<string>? productCodes = null,
        DateOnly? dateFrom = null,
        DateOnly? dateTo = null,
        CancellationToken cancellationToken = default);

    public Task RefreshAsync(CancellationToken ct = default);
}

// MonthlyCost — backend/src/Anela.Heblo.Domain/Features/Catalog/ValueObjects/MonthlyCost.cs
public class MonthlyCost
{
    public DateTime Month { get; }
    public decimal Cost { get; }
    public MonthlyCost(DateTime month, decimal cost);
}

// CostCacheData — backend/src/Anela.Heblo.Domain/Features/Catalog/Cache/CostCacheData.cs
public class CostCacheData
{
    public Dictionary<string, List<MonthlyCost>> ProductCosts { get; init; } = new();
    public DateTime LastUpdated { get; init; }
    public DateOnly DataFrom { get; init; }
    public DateOnly DataTo { get; init; }
    public bool IsHydrated { get; init; }
    public static CostCacheData Empty();    // IsHydrated = false
}

// ICostCache (base of ISalesCostCache) — backend/src/Anela.Heblo.Domain/Features/Catalog/Cache/ICostCache.cs
public interface ICostCache
{
    Task<CostCacheData> GetCachedDataAsync(CancellationToken ct = default);
    Task SetCachedDataAsync(CostCacheData data, CancellationToken ct = default);
    bool IsHydrated { get; }
}

// ILedgerService — backend/src/Anela.Heblo.Domain/Accounting/Ledger/ILedgerService.cs
Task<IList<CostStatistics>> GetDirectCosts(
    DateTime dateFrom, DateTime dateTo, string? department = null, CancellationToken cancellationToken = default);

// CostStatistics — backend/src/Anela.Heblo.Domain/Accounting/Ledger/CostStatistics.cs
public class CostStatistics
{
    public DateTime Date { get; set; }
    public decimal Cost { get; set; }
    public string Department { get; set; }
}

// DataSourceOptions — backend/src/Anela.Heblo.Application/Common/DataSourceOptions.cs
public class DataSourceOptions
{
    public int ManufactureCostHistoryDays { get; set; } = 400;
    // (other props omitted — not used in these tests)
}

// CatalogSaleRecord — backend/src/Anela.Heblo.Domain/Features/Catalog/Sales/CatalogSaleRecord.cs
public record CatalogSaleRecord
{
    public DateTime Date { get; set; }
    public string ProductCode { get; set; }
    public string ProductName { get; set; }
    public double AmountTotal { get; set; }
    // (other props omitted — not used here)
}

// ICatalogRepository — backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs
//   inherits from IReadOnlyRepository<CatalogAggregate, string>:
Task<IEnumerable<CatalogAggregate>> GetAllAsync(CancellationToken cancellationToken = default);
Task WaitForCurrentMergeAsync(CancellationToken cancellationToken = default);
```

**Cost-allocation formula (the function under test):**
- `totalCost = (double)(warehouseCostsSum + marketingCostsSum)` — decimals summed, then cast to double
- `costPerPiece = totalCost / totalSoldPieces` (double)
- `MonthlyCost.Cost = (decimal)costPerPiece` per product per month

**Date range (`GetDateRange()`):**
- `dataFrom = DateOnly.FromDateTime(UtcNow.AddDays(-ManufactureCostHistoryDays))`
- `dataTo = DateOnly.FromDateTime(UtcNow)`
- `costsFrom = new DateTime(dataFrom.Year, dataFrom.Month, 1)` — first day of dataFrom's month
- `costsTo = new DateTime(dataTo.Year, dataTo.Month, DaysInMonth(dataTo.Year, dataTo.Month), 23, 59, 59)` — last second of dataTo's month

---

## Task 1: Create skeleton — class, usings, collection attribute, helpers

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs`

- [ ] **Step 1.1: Create the file with class skeleton + helpers**

```csharp
using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.CostProviders;
using Anela.Heblo.Domain.Accounting.Ledger;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.Sales;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.CostProviders;

/// <summary>
/// Tests for SalesCostProvider.
/// Uses Collection attribute to ensure sequential execution due to static RefreshLock in the provider.
/// </summary>
[Collection("SalesCostProviderTests")]
public class SalesCostProviderTests
{
    private const int DefaultHistoryDays = 90;

    // ===== Helpers =====

    private static CatalogAggregate BuildProduct(
        string productCode,
        IEnumerable<(DateTime date, double amount)> sales)
    {
        return new CatalogAggregate
        {
            ProductCode = productCode,
            SalesHistory = sales
                .Select(s => new CatalogSaleRecord
                {
                    Date = s.date,
                    ProductCode = productCode,
                    ProductName = productCode,
                    AmountTotal = s.amount
                })
                .ToList()
        };
    }

    private static SalesCostProvider CreateProvider(
        Mock<ISalesCostCache>? cacheMock = null,
        Mock<ICatalogRepository>? repoMock = null,
        Mock<ILedgerService>? ledgerMock = null,
        Mock<ILogger<SalesCostProvider>>? loggerMock = null,
        int manufactureCostHistoryDays = DefaultHistoryDays)
    {
        return new SalesCostProvider(
            (cacheMock ?? new Mock<ISalesCostCache>()).Object,
            (repoMock ?? new Mock<ICatalogRepository>()).Object,
            (ledgerMock ?? new Mock<ILedgerService>()).Object,
            (loggerMock ?? new Mock<ILogger<SalesCostProvider>>()).Object,
            Options.Create(new DataSourceOptions { ManufactureCostHistoryDays = manufactureCostHistoryDays }));
    }

    private static void VerifyLog(
        Mock<ILogger<SalesCostProvider>> logger,
        LogLevel level,
        string messageContains,
        Times? times = null)
    {
        logger.Verify(
            x => x.Log(
                level,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, _) => v.ToString()!.Contains(messageContains)),
                It.IsAny<Exception?>(),
                (Func<It.IsAnyType, Exception?, string>)It.IsAny<object>()),
            times ?? Times.AtLeastOnce());
    }

    // ===== Tests appear below =====
}
```

- [ ] **Step 1.2: Verify the file compiles**

Run: `cd backend && dotnet build test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: Build succeeds with 0 errors.

- [ ] **Step 1.3: Commit**

```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-coverage-gap-catalog-salescostprovider-f
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs
git commit -m "test: add SalesCostProvider test scaffold with helpers"
```

---

## Task 2: FR-1 — Nominal cost-allocation flow

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs`

**Math fixture:** 3 products with combined `AmountTotal` = 10 + 20 + 30 = 60 sold pieces. Warehouse total = 600m, marketing total = 600m → totalCost = 1200.0 (double). `costPerPiece = 1200.0 / 60 = 20.0`. Expected `MonthlyCost.Cost = 20m` for every product/month.

- [ ] **Step 2.1: Add the test method inside `SalesCostProviderTests` (before the closing brace, after the helpers)**

Place this method directly under the `// ===== Tests appear below =====` marker:

```csharp
[Fact]
internal async Task RefreshAsync_DistributesCostPerPiece_WhenSalesExist()
{
    // Arrange
    var now = DateTime.UtcNow;
    var saleDate = new DateTime(now.Year, now.Month, 1).AddMonths(-1).AddDays(14);

    var products = new List<CatalogAggregate>
    {
        BuildProduct("PROD-A", new[] { (saleDate, 10.0) }),
        BuildProduct("PROD-B", new[] { (saleDate, 20.0) }),
        BuildProduct("PROD-C", new[] { (saleDate, 30.0) })
    };
    var totalSoldPieces = 60.0;
    var warehouseCost = 600m;
    var marketingCost = 600m;
    var expectedCostPerPiece = (decimal)((double)(warehouseCost + marketingCost) / totalSoldPieces); // 20m

    var callOrder = new List<string>();

    var repoMock = new Mock<ICatalogRepository>();
    repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()))
        .Callback(() => callOrder.Add("WaitForCurrentMergeAsync"))
        .Returns(Task.CompletedTask);
    repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
        .Callback(() => callOrder.Add("GetAllAsync"))
        .ReturnsAsync(products);

    var ledgerMock = new Mock<ILedgerService>();
    ledgerMock.Setup(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "SKLAD", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<CostStatistics>
        {
            new() { Date = saleDate, Cost = warehouseCost, Department = "SKLAD" }
        });
    ledgerMock.Setup(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "MARKETING", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<CostStatistics>
        {
            new() { Date = saleDate, Cost = marketingCost, Department = "MARKETING" }
        });

    CostCacheData? captured = null;
    var cacheMock = new Mock<ISalesCostCache>();
    cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
        .Callback<CostCacheData, CancellationToken>((d, _) => captured = d)
        .Returns(Task.CompletedTask);

    var provider = CreateProvider(
        cacheMock: cacheMock,
        repoMock: repoMock,
        ledgerMock: ledgerMock);

    // Act
    await provider.RefreshAsync();

    // Assert
    captured.Should().NotBeNull();
    captured!.IsHydrated.Should().BeTrue();
    captured.ProductCosts.Should().HaveCount(3);
    captured.ProductCosts.Keys.Should().BeEquivalentTo("PROD-A", "PROD-B", "PROD-C");

    var firstList = captured.ProductCosts.Values.First();
    firstList.Should().NotBeEmpty();
    var monthCount = firstList.Count;

    foreach (var monthly in captured.ProductCosts.Values)
    {
        monthly.Should().HaveCount(monthCount);
        monthly.Should().AllSatisfy(mc => mc.Cost.Should().Be(expectedCostPerPiece));
    }

    ledgerMock.Verify(
        s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "SKLAD", It.IsAny<CancellationToken>()),
        Times.Once);
    ledgerMock.Verify(
        s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "MARKETING", It.IsAny<CancellationToken>()),
        Times.Once);

    callOrder.Should().Equal("WaitForCurrentMergeAsync", "GetAllAsync");
}
```

- [ ] **Step 2.2: Run the test, confirm PASS**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SalesCostProviderTests.RefreshAsync_DistributesCostPerPiece_WhenSalesExist" --no-restore`
Expected: 1 passed, 0 failed.

- [ ] **Step 2.3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs
git commit -m "test(SalesCostProvider): cover nominal cost-per-piece distribution"
```

---

## Task 3: FR-2 — Zero sold-pieces guard

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs`

- [ ] **Step 3.1: Append the test method (just before the closing `}` of the class)**

```csharp
[Fact]
internal async Task RefreshAsync_WritesZeroCostsAndLogsWarning_WhenNoSalesInPeriod()
{
    // Arrange
    var products = new List<CatalogAggregate>
    {
        BuildProduct("PROD-A", Array.Empty<(DateTime, double)>()),
        BuildProduct("PROD-B", Array.Empty<(DateTime, double)>()),
        BuildProduct(string.Empty, Array.Empty<(DateTime, double)>()),  // must be filtered out
        BuildProduct(null!, Array.Empty<(DateTime, double)>())          // must be filtered out
    };

    var repoMock = new Mock<ICatalogRepository>();
    repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(products);

    var ledgerMock = new Mock<ILedgerService>();
    ledgerMock.Setup(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<CostStatistics>
        {
            new() { Date = DateTime.UtcNow, Cost = 999m, Department = "SKLAD" }
        });

    CostCacheData? captured = null;
    var cacheMock = new Mock<ISalesCostCache>();
    cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
        .Callback<CostCacheData, CancellationToken>((d, _) => captured = d)
        .Returns(Task.CompletedTask);

    var loggerMock = new Mock<ILogger<SalesCostProvider>>();

    var provider = CreateProvider(
        cacheMock: cacheMock,
        repoMock: repoMock,
        ledgerMock: ledgerMock,
        loggerMock: loggerMock);

    // Act
    await provider.RefreshAsync();

    // Assert
    captured.Should().NotBeNull();
    captured!.IsHydrated.Should().BeTrue();
    captured.ProductCosts.Should().HaveCount(2);
    captured.ProductCosts.Keys.Should().BeEquivalentTo("PROD-A", "PROD-B");
    foreach (var monthly in captured.ProductCosts.Values)
    {
        monthly.Should().NotBeEmpty();
        monthly.Should().AllSatisfy(mc => mc.Cost.Should().Be(0m));
    }

    VerifyLog(loggerMock, LogLevel.Warning, "No sales history found");
}
```

- [ ] **Step 3.2: Run the test**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SalesCostProviderTests.RefreshAsync_WritesZeroCostsAndLogsWarning_WhenNoSalesInPeriod" --no-restore`
Expected: PASS.

- [ ] **Step 3.3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs
git commit -m "test(SalesCostProvider): cover zero-sales guard and empty-product-code filtering"
```

---

## Task 4: FR-3 — Product-code filtering through `GetCostsAsync`

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs`

- [ ] **Step 4.1: Append the test method**

```csharp
[Theory]
[InlineData(null)]
[InlineData(new string[0])]
internal async Task GetCostsAsync_ReturnsFullDictionary_WhenProductCodesAreNullOrEmpty(string[]? codes)
{
    // Arrange
    var hydratedData = BuildHydratedCacheData(new[] { "A", "B", "C" });

    var cacheMock = new Mock<ISalesCostCache>();
    cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(hydratedData);

    var provider = CreateProvider(cacheMock: cacheMock);

    // Act
    var result = await provider.GetCostsAsync(codes?.ToList());

    // Assert
    result.Should().HaveCount(3);
    result.Keys.Should().BeEquivalentTo("A", "B", "C");
}

[Fact]
internal async Task GetCostsAsync_ReturnsSubset_WhenProductCodesProvided()
{
    // Arrange
    var hydratedData = BuildHydratedCacheData(new[] { "A", "B", "C" });
    var originalCount = hydratedData.ProductCosts.Count;
    var originalKeys = hydratedData.ProductCosts.Keys.ToArray();

    var cacheMock = new Mock<ISalesCostCache>();
    cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(hydratedData);

    var provider = CreateProvider(cacheMock: cacheMock);

    // Act
    var result = await provider.GetCostsAsync(new List<string> { "A", "C", "DOES-NOT-EXIST" });

    // Assert
    result.Should().HaveCount(2);
    result.Keys.Should().BeEquivalentTo("A", "C");

    // Mutation guard: cache's underlying dictionary is unchanged.
    hydratedData.ProductCosts.Should().HaveCount(originalCount);
    hydratedData.ProductCosts.Keys.Should().BeEquivalentTo(originalKeys);
}

private static CostCacheData BuildHydratedCacheData(IEnumerable<string> productCodes)
{
    var month = new DateTime(2026, 1, 1);
    var dict = productCodes.ToDictionary(
        code => code,
        code => new List<MonthlyCost> { new(month, 1m) });
    return new CostCacheData
    {
        ProductCosts = dict,
        LastUpdated = DateTime.UtcNow,
        DataFrom = DateOnly.FromDateTime(month),
        DataTo = DateOnly.FromDateTime(month.AddMonths(1).AddDays(-1)),
        IsHydrated = true
    };
}
```

- [ ] **Step 4.2: Run the tests**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SalesCostProviderTests.GetCostsAsync" --no-restore`
Expected: PASS (3 tests pass — 2 from Theory + 1 Fact).

- [ ] **Step 4.3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs
git commit -m "test(SalesCostProvider): cover product-code filtering in GetCostsAsync"
```

---

## Task 5: FR-4 — Cache-not-hydrated fallback

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs`

- [ ] **Step 5.1: Append the test method**

```csharp
[Fact]
internal async Task GetCostsAsync_ReturnsEmptyAndLogsWarning_WhenCacheNotHydrated()
{
    // Arrange
    var cacheMock = new Mock<ISalesCostCache>();
    cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(CostCacheData.Empty());

    var repoMock = new Mock<ICatalogRepository>();
    var ledgerMock = new Mock<ILedgerService>();
    var loggerMock = new Mock<ILogger<SalesCostProvider>>();

    var provider = CreateProvider(
        cacheMock: cacheMock,
        repoMock: repoMock,
        ledgerMock: ledgerMock,
        loggerMock: loggerMock);

    // Act
    var result = await provider.GetCostsAsync();

    // Assert
    result.Should().NotBeNull();
    result.Should().BeEmpty();

    VerifyLog(loggerMock, LogLevel.Warning, "SalesCostCache not hydrated");

    repoMock.VerifyNoOtherCalls();
    ledgerMock.VerifyNoOtherCalls();
}
```

- [ ] **Step 5.2: Run the test**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SalesCostProviderTests.GetCostsAsync_ReturnsEmptyAndLogsWarning_WhenCacheNotHydrated" --no-restore`
Expected: PASS.

- [ ] **Step 5.3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs
git commit -m "test(SalesCostProvider): cover cache-not-hydrated fallback"
```

---

## Task 6: FR-5 — Date-range computation & leap-year theory

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs`

- [ ] **Step 6.1: Append the two tests**

```csharp
[Fact]
internal async Task RefreshAsync_PassesMonthAlignedDateRange_ToLedgerService()
{
    // Arrange
    DateTime? capturedFrom = null;
    DateTime? capturedTo = null;

    var ledgerMock = new Mock<ILedgerService>();
    ledgerMock.Setup(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "SKLAD", It.IsAny<CancellationToken>()))
        .Callback<DateTime, DateTime, string?, CancellationToken>((from, to, _, _) =>
        {
            capturedFrom = from;
            capturedTo = to;
        })
        .ReturnsAsync(new List<CostStatistics>());
    ledgerMock.Setup(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "MARKETING", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<CostStatistics>());

    var repoMock = new Mock<ICatalogRepository>();
    repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate>());

    var provider = CreateProvider(
        repoMock: repoMock,
        ledgerMock: ledgerMock,
        manufactureCostHistoryDays: 90);

    var nowBefore = DateTime.UtcNow;

    // Act
    await provider.RefreshAsync();

    var nowAfter = DateTime.UtcNow;

    // Assert
    capturedFrom.Should().NotBeNull();
    capturedTo.Should().NotBeNull();

    // costsFrom = first day of the month derived from (UtcNow - 90 days)
    capturedFrom!.Value.Day.Should().Be(1);

    var expectedFromLower = DateOnly.FromDateTime(nowBefore.AddDays(-90));
    var expectedFromUpper = DateOnly.FromDateTime(nowAfter.AddDays(-90));
    var capturedFromMonthStart = new DateOnly(capturedFrom.Value.Year, capturedFrom.Value.Month, 1);
    capturedFromMonthStart.Should().BeOnOrAfter(new DateOnly(expectedFromLower.Year, expectedFromLower.Month, 1));
    capturedFromMonthStart.Should().BeOnOrBefore(new DateOnly(expectedFromUpper.Year, expectedFromUpper.Month, 1));

    // costsTo = last day of UtcNow's month, 23:59:59
    capturedTo!.Value.Day.Should().Be(DateTime.DaysInMonth(capturedTo.Value.Year, capturedTo.Value.Month));
    capturedTo.Value.Hour.Should().Be(23);
    capturedTo.Value.Minute.Should().Be(59);
    capturedTo.Value.Second.Should().Be(59);

    var capturedToMonthStart = new DateOnly(capturedTo.Value.Year, capturedTo.Value.Month, 1);
    capturedToMonthStart.Should().BeOnOrAfter(new DateOnly(nowBefore.Year, nowBefore.Month, 1));
    capturedToMonthStart.Should().BeOnOrBefore(new DateOnly(nowAfter.Year, nowAfter.Month, 1));
}

[Theory]
[InlineData(2024, 2, 29)]
[InlineData(2023, 2, 28)]
[InlineData(2024, 4, 30)]
[InlineData(2024, 12, 31)]
internal void DaysInMonth_ReturnsExpected_ForCalendarBoundaries(int year, int month, int expectedDays)
{
    // Locks in the calendar math used by SalesCostProvider.GetDateRange()
    // without requiring clock injection.
    DateTime.DaysInMonth(year, month).Should().Be(expectedDays);
}
```

- [ ] **Step 6.2: Run the tests**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SalesCostProviderTests.RefreshAsync_PassesMonthAlignedDateRange_ToLedgerService|FullyQualifiedName~SalesCostProviderTests.DaysInMonth_ReturnsExpected_ForCalendarBoundaries" --no-restore`
Expected: PASS (1 Fact + 4 Theory cases = 5 tests pass).

- [ ] **Step 6.3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs
git commit -m "test(SalesCostProvider): cover GetDateRange month-alignment and leap year"
```

---

## Task 7: FR-6 — RefreshAsync concurrency lock

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs`

**Strategy:** Gate the first `GetDirectCosts(SKLAD)` call on a `TaskCompletionSource` so the first `RefreshAsync` holds the static lock. Start a second `RefreshAsync` while the first is in-flight — it must return immediately without calling `SetCachedDataAsync`. Release the gate, await the first, then perform a third `RefreshAsync` to prove the lock is back to a usable state.

- [ ] **Step 7.1: Append the test method**

```csharp
[Fact]
internal async Task RefreshAsync_SkipsSecondInvocation_WhenFirstStillRunning()
{
    // Arrange
    var gate = new TaskCompletionSource<IList<CostStatistics>>(TaskCreationOptions.RunContinuationsAsynchronously);

    var ledgerMock = new Mock<ILedgerService>();
    ledgerMock.Setup(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "SKLAD", It.IsAny<CancellationToken>()))
        .Returns(gate.Task);
    ledgerMock.Setup(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "MARKETING", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<CostStatistics>());

    var repoMock = new Mock<ICatalogRepository>();
    repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate>());

    var cacheMock = new Mock<ISalesCostCache>();
    cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    var loggerMock = new Mock<ILogger<SalesCostProvider>>();

    var provider = CreateProvider(
        cacheMock: cacheMock,
        repoMock: repoMock,
        ledgerMock: ledgerMock,
        loggerMock: loggerMock);

    // Act 1 — start first refresh; it will block on the gate inside ComputeAllCostsAsync.
    var firstRefresh = provider.RefreshAsync();

    // Give the first call time to enter the semaphore and reach the awaited GetDirectCosts.
    while (!ledgerMock.Invocations.Any(i => i.Method.Name == nameof(ILedgerService.GetDirectCosts)))
    {
        await Task.Yield();
    }

    // Act 2 — second refresh; should detect lock and skip immediately.
    await provider.RefreshAsync();

    // Assert intermediate state — second invocation skipped.
    VerifyLog(loggerMock, LogLevel.Information, "refresh already in progress");
    cacheMock.Verify(
        c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
        Times.Never);

    // Release the gate, let the first refresh finish.
    gate.SetResult(new List<CostStatistics>());
    await firstRefresh;

    // First invocation should have written to the cache.
    cacheMock.Verify(
        c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
        Times.Once);

    // Act 3 — a fresh refresh after the lock is released must proceed.
    await provider.RefreshAsync();

    cacheMock.Verify(
        c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
        Times.Exactly(2));
}
```

- [ ] **Step 7.2: Run the test**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SalesCostProviderTests.RefreshAsync_SkipsSecondInvocation_WhenFirstStillRunning" --no-restore`
Expected: PASS in well under 5 seconds.

- [ ] **Step 7.3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs
git commit -m "test(SalesCostProvider): cover concurrent RefreshAsync lock skip"
```

---

## Task 8: FR-7 — Exception propagation & lock release

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs`

Three independent tests — each focused on one failure surface (cache read, ledger throw, repo throw). The repo-throw and ledger-throw tests also assert the lock is released by performing a second `RefreshAsync` and verifying `SetCachedDataAsync` ran exactly once afterward, plus that no "already in progress" log entry appeared.

- [ ] **Step 8.1: Append the three test methods**

```csharp
[Fact]
internal async Task GetCostsAsync_LogsErrorAndRethrows_WhenCacheReadFails()
{
    // Arrange
    var boom = new InvalidOperationException("cache offline");
    var cacheMock = new Mock<ISalesCostCache>();
    cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ThrowsAsync(boom);

    var loggerMock = new Mock<ILogger<SalesCostProvider>>();

    var provider = CreateProvider(cacheMock: cacheMock, loggerMock: loggerMock);

    // Act
    var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetCostsAsync());

    // Assert
    thrown.Should().BeSameAs(boom);
    VerifyLog(loggerMock, LogLevel.Error, "Error getting sales costs");
}

[Fact]
internal async Task RefreshAsync_ReleasesLockOnLedgerException_AndAllowsSubsequentRefresh()
{
    // Arrange
    var boom = new InvalidOperationException("ledger offline");

    var ledgerMock = new Mock<ILedgerService>();
    ledgerMock.SetupSequence(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "SKLAD", It.IsAny<CancellationToken>()))
        .ThrowsAsync(boom)
        .ReturnsAsync(new List<CostStatistics>());
    ledgerMock.Setup(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), "MARKETING", It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<CostStatistics>());

    var repoMock = new Mock<ICatalogRepository>();
    repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate>());

    var cacheMock = new Mock<ISalesCostCache>();
    cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    var loggerMock = new Mock<ILogger<SalesCostProvider>>();

    var provider = CreateProvider(
        cacheMock: cacheMock,
        repoMock: repoMock,
        ledgerMock: ledgerMock,
        loggerMock: loggerMock);

    // Act 1 — first call throws
    var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.RefreshAsync());
    thrown.Should().BeSameAs(boom);
    VerifyLog(loggerMock, LogLevel.Error, "Failed to refresh SalesCostCache");
    cacheMock.Verify(
        c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
        Times.Never);

    // Act 2 — second call must proceed (lock released by finally), not log "already in progress"
    await provider.RefreshAsync();

    VerifyLog(loggerMock, LogLevel.Information, "refresh already in progress", Times.Never());
    cacheMock.Verify(
        c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
        Times.Once);
}

[Fact]
internal async Task RefreshAsync_ReleasesLockOnRepositoryException_AndAllowsSubsequentRefresh()
{
    // Arrange
    var boom = new InvalidOperationException("repo offline");

    var repoMock = new Mock<ICatalogRepository>();
    repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    repoMock.SetupSequence(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
        .ThrowsAsync(boom)
        .ReturnsAsync(new List<CatalogAggregate>());

    var ledgerMock = new Mock<ILedgerService>();
    ledgerMock.Setup(s => s.GetDirectCosts(It.IsAny<DateTime>(), It.IsAny<DateTime>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
        .ReturnsAsync(new List<CostStatistics>());

    var cacheMock = new Mock<ISalesCostCache>();
    cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    var loggerMock = new Mock<ILogger<SalesCostProvider>>();

    var provider = CreateProvider(
        cacheMock: cacheMock,
        repoMock: repoMock,
        ledgerMock: ledgerMock,
        loggerMock: loggerMock);

    // Act 1
    var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.RefreshAsync());
    thrown.Should().BeSameAs(boom);
    VerifyLog(loggerMock, LogLevel.Error, "Failed to refresh SalesCostCache");
    cacheMock.Verify(
        c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
        Times.Never);

    // Act 2 — second call proceeds (lock released)
    await provider.RefreshAsync();

    VerifyLog(loggerMock, LogLevel.Information, "refresh already in progress", Times.Never());
    cacheMock.Verify(
        c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
        Times.Once);
}
```

- [ ] **Step 8.2: Run the three tests**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SalesCostProviderTests.GetCostsAsync_LogsErrorAndRethrows_WhenCacheReadFails|FullyQualifiedName~SalesCostProviderTests.RefreshAsync_ReleasesLockOnLedgerException_AndAllowsSubsequentRefresh|FullyQualifiedName~SalesCostProviderTests.RefreshAsync_ReleasesLockOnRepositoryException_AndAllowsSubsequentRefresh" --no-restore`
Expected: PASS (3 tests).

- [ ] **Step 8.3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs
git commit -m "test(SalesCostProvider): cover exception propagation and lock release"
```

---

## Task 9: Full validation — run all SalesCostProvider tests + lint + build

**Files:** No file changes — verification only.

- [ ] **Step 9.1: Run every test in the new class**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~SalesCostProviderTests" --no-restore`
Expected: All tests PASS (12 total — 1 from FR-1, 1 from FR-2, 3 from FR-3, 1 from FR-4, 5 from FR-5, 1 from FR-6, 3 from FR-7). Total runtime ≤ 5 s locally.

- [ ] **Step 9.2: Run the sibling provider tests too, to confirm no `[Collection]` cross-class interference**

Run: `cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~CostProviders" --no-restore`
Expected: All cost-provider tests PASS.

- [ ] **Step 9.3: Build the whole backend solution and apply formatting**

Run (sequentially):
```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-coverage-gap-catalog-salescostprovider-f/backend
dotnet build
dotnet format --verify-no-changes
```

Expected: build succeeds; `dotnet format` exits 0 (no formatting changes needed). If `dotnet format` exits non-zero, run `dotnet format` without `--verify-no-changes`, inspect the diff, commit the formatting fix, and re-run the verify command.

- [ ] **Step 9.4: Collect coverage for SalesCostProvider.cs**

Run:
```bash
cd /Users/pajgrtondrej/Work/GitHub/Anela.Heblo/.worktrees/feat-coverage-gap-catalog-salescostprovider-f/backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~SalesCostProviderTests" \
  --collect:"XPlat Code Coverage" \
  --results-directory ./TestResults
```

Expected: `TestResults/<guid>/coverage.cobertura.xml` produced.

Inspect with grep (the file path inside the XML is the production source path):
```bash
grep -A1 "SalesCostProvider.cs" TestResults/*/coverage.cobertura.xml | head -20
```

Confirm the `line-rate` attribute on the `<class name="...SalesCostProvider"` element is `≥ 0.80`. If lower, list missed line ranges from the XML and add coverage to the relevant tests in Tasks 2–8.

- [ ] **Step 9.5: Final commit (only if coverage XML or any formatting was changed)**

If coverage triggered any source-file changes (e.g., formatting), commit them. Otherwise nothing to commit — the previous step is verification only.

```bash
git status
# If clean: nothing to commit.
# If dirty (formatting only):
git add -u backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/SalesCostProviderTests.cs
git commit -m "test(SalesCostProvider): apply dotnet format"
```

---

## Self-Review

**Spec coverage check (FR-1 … FR-7, NFR-1 … NFR-3):**
- FR-1 nominal allocation → Task 2 ✓ (asserts cost-per-piece, dict shape, ledger call count for SKLAD + MARKETING, repo call ordering)
- FR-2 zero-sales guard → Task 3 ✓ (zero costs, warning log, null/empty ProductCode filtered, `IsHydrated = true`)
- FR-3 product-code filtering → Task 4 ✓ (null, empty list, subset, missing codes, mutation guard)
- FR-4 cache-not-hydrated fallback → Task 5 ✓ (empty dict, warning log, no repo/ledger calls)
- FR-5 date-range + leap year → Task 6 ✓ (`Day == 1`, `Day == DaysInMonth`, time-of-day, theory for 2024-02/2023-02/2024-04/2024-12)
- FR-6 concurrency lock → Task 7 ✓ (`TaskCompletionSource`-gated, skip log, lock recovery)
- FR-7 exception propagation → Task 8 ✓ (cache throw → rethrow + log; ledger throw → release; repo throw → release)
- NFR-1 performance → Task 9.1 enforces ≤ 5 s; no `Thread.Sleep`, no real I/O ✓
- NFR-2 coverage + frameworks → Task 9.4 verifies ≥ 80 %; Moq + FluentAssertions + xUnit only ✓
- NFR-3 maintainability → Task 1 sets file path, namespace, `[Collection]`, naming convention ✓

**Placeholder scan:** No "TBD" / "fill in" / "similar to" / abstract instructions remain. Every code block contains complete, runnable code.

**Type consistency:** `BuildProduct`, `CreateProvider`, `VerifyLog`, and `BuildHydratedCacheData` signatures are stable across all tasks. Mock variables (`cacheMock`, `repoMock`, `ledgerMock`, `loggerMock`) follow one naming convention throughout. `[Collection("SalesCostProviderTests")]` matches the spec's FR-6 directive.

**Plan complete. Saved to `docs/superpowers/plans/2026-06-14-salescostprovider-tests.md`.**
