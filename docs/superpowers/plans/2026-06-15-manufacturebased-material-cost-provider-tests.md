# ManufactureBasedMaterialCostProvider Unit Test Coverage Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add xUnit + Moq + FluentAssertions unit tests for `ManufactureBasedMaterialCostProvider` to raise its line coverage from 25 % to ≥ 80 %, exercising product-type routing, the temporal carry-forward algorithm, future-manufacture backfill, weighted-average aggregation, purchase-price fallback paths, and the `RefreshAsync` / `GetCostsAsync` cache surface.

**Architecture:** Single new test class `ManufactureBasedMaterialCostProviderTests` placed beside its sibling `SalesCostProviderTests`. Behavior is driven entirely through the public `RefreshAsync` and `GetCostsAsync` surface — no production-code changes, no new `InternalsVisibleTo` attributes. The class is decorated with `[Collection("ManufactureBasedMaterialCostProviderTests")]` so xUnit serializes tests within it (the SUT holds a `static SemaphoreSlim _refreshLock` whose lifecycle must not race across tests).

**Tech Stack:** xUnit, FluentAssertions, Moq, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging` — all already referenced by the `Anela.Heblo.Tests` project. No new NuGet packages.

---

## File Structure

| File | Responsibility |
|------|----------------|
| `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs` (NEW) | All tests, helpers, and the `[Collection]` declaration for this SUT. |

No other files are created or modified.

**Key types referenced (already in the codebase — do not create):**

| Type | Location | Notes |
|------|----------|-------|
| `ManufactureBasedMaterialCostProvider` (SUT) | `Anela.Heblo.Application.Features.Catalog.CostProviders` | Public constructor takes `IMaterialCostCache`, `ICatalogRepository`, `ILogger<…>`, `IOptions<DataSourceOptions>`. |
| `CatalogAggregate` | `Anela.Heblo.Domain.Features.Catalog` | `PurchasePriceWithVat` is a **read-only** computed property sourced from `ErpPrice?.PurchasePriceWithVat`. Set via `ErpPrice = new ProductPriceErp { PurchasePriceWithVat = … }`. `ManufactureHistory` is `IReadOnlyList<ManufactureHistoryRecord>` (default `new List<ManufactureHistoryRecord>()`). |
| `ManufactureHistoryRecord` | `Anela.Heblo.Domain.Features.Manufacture` | Mutable class with `Date` (`DateTime`), `Amount` (`double`), `PricePerPiece` (`decimal`), `PriceTotal` (`decimal`), `ProductCode` (`string`), `DocumentNumber` (`string`). |
| `ProductPriceErp` | `Anela.Heblo.Domain.Features.Catalog.Price` | Mutable class — set `PurchasePriceWithVat` to drive the fallback path. |
| `ProductType` | `Anela.Heblo.Domain.Features.Catalog` | Enum values: `Product`, `Set`, `SemiProduct`, `Material`, `Goods`, `UNDEFINED`. |
| `MonthlyCost` | `Anela.Heblo.Domain.Features.Catalog.ValueObjects` | Class with `Month` (`DateTime`) + `Cost` (`decimal`); constructor `(month, cost)`. |
| `CostCacheData` | `Anela.Heblo.Domain.Features.Catalog.Cache` | Class with `ProductCosts`, `IsHydrated`, `LastUpdated`, `DataFrom` (`DateOnly`), `DataTo` (`DateOnly`). `Empty()` factory returns `IsHydrated = false`. |
| `IMaterialCostCache` | `Anela.Heblo.Domain.Features.Catalog.Cache` | Extends `ICostCache` → `GetCachedDataAsync(ct)` + `SetCachedDataAsync(data, ct)`. |
| `ICatalogRepository` | `Anela.Heblo.Domain.Features.Catalog` | Tests use `WaitForCurrentMergeAsync(ct)` and `GetAllAsync(ct)`. |
| `DataSourceOptions` | `Anela.Heblo.Application.Common` | Field exercised: `ManufactureCostHistoryDays`. Default `400`; tests inject `4000` to keep synthetic dates inside the SUT window. |

---

## Task 1: Scaffold the test file with helpers

**Files:**
- Create: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs`

- [ ] **Step 1: Create the test file with usings, namespace, `[Collection]`, helpers, and a single sanity `[Fact]`**

```csharp
using Anela.Heblo.Application.Common;
using Anela.Heblo.Application.Features.Catalog.CostProviders;
using Anela.Heblo.Domain.Features.Catalog;
using Anela.Heblo.Domain.Features.Catalog.Cache;
using Anela.Heblo.Domain.Features.Catalog.Price;
using Anela.Heblo.Domain.Features.Catalog.ValueObjects;
using Anela.Heblo.Domain.Features.Manufacture;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace Anela.Heblo.Tests.Features.Catalog.CostProviders;

/// <summary>
/// Tests for ManufactureBasedMaterialCostProvider.
/// Uses Collection attribute to ensure sequential execution due to static _refreshLock in the provider.
/// </summary>
[Collection("ManufactureBasedMaterialCostProviderTests")]
public class ManufactureBasedMaterialCostProviderTests
{
    /// <summary>
    /// 4000 days (~11 years) — comfortably covers any synthetic month anchored to UtcNow,
    /// so the SUT's [now - historyDays, now] window always contains seeded records.
    /// </summary>
    private const int DefaultHistoryDays = 4000;

    // ===== Helpers =====

    private static DateTime CurrentMonthStartUtc() =>
        new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1);

    private static DateTime MonthOffsetFromNow(int monthsOffset) =>
        CurrentMonthStartUtc().AddMonths(monthsOffset);

    private static CatalogAggregate BuildManufacturedProduct(
        string productCode,
        ProductType type,
        IEnumerable<(DateTime date, decimal pricePerPiece, double amount)>? history = null,
        decimal? purchasePriceWithVat = null)
    {
        var agg = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = productCode,
            Type = type,
        };

        if (history != null)
        {
            agg.ManufactureHistory = history
                .Select(h => new ManufactureHistoryRecord
                {
                    Date = h.date,
                    PricePerPiece = h.pricePerPiece,
                    Amount = h.amount,
                    PriceTotal = h.pricePerPiece * (decimal)h.amount,
                    ProductCode = productCode,
                    DocumentNumber = "TEST-DOC",
                })
                .ToList();
        }

        if (purchasePriceWithVat.HasValue)
        {
            agg.ErpPrice = new ProductPriceErp { PurchasePriceWithVat = purchasePriceWithVat.Value };
        }

        return agg;
    }

    private static CatalogAggregate BuildNonManufacturedProduct(
        string productCode,
        ProductType type,
        decimal? purchasePriceWithVat)
    {
        var agg = new CatalogAggregate
        {
            ProductCode = productCode,
            ProductName = productCode,
            Type = type,
        };

        if (purchasePriceWithVat.HasValue)
        {
            agg.ErpPrice = new ProductPriceErp { PurchasePriceWithVat = purchasePriceWithVat.Value };
        }

        return agg;
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
            IsHydrated = true,
        };
    }

    private static ManufactureBasedMaterialCostProvider CreateProvider(
        Mock<IMaterialCostCache>? cacheMock = null,
        Mock<ICatalogRepository>? repoMock = null,
        Mock<ILogger<ManufactureBasedMaterialCostProvider>>? loggerMock = null,
        int manufactureCostHistoryDays = DefaultHistoryDays)
    {
        return new ManufactureBasedMaterialCostProvider(
            (cacheMock ?? new Mock<IMaterialCostCache>()).Object,
            (repoMock ?? new Mock<ICatalogRepository>()).Object,
            (loggerMock ?? new Mock<ILogger<ManufactureBasedMaterialCostProvider>>()).Object,
            Options.Create(new DataSourceOptions { ManufactureCostHistoryDays = manufactureCostHistoryDays }));
    }

    private static void VerifyLog(
        Mock<ILogger<ManufactureBasedMaterialCostProvider>> logger,
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

    /// <summary>
    /// Captures the CostCacheData written to the cache by RefreshAsync and returns
    /// the configured cache mock. Tests can read the captured value via the out parameter.
    /// </summary>
    private static Mock<IMaterialCostCache> BuildCaptureCacheMock(out Action<Action<CostCacheData>> onCaptured)
    {
        var cacheMock = new Mock<IMaterialCostCache>();
        Action<CostCacheData>? subscriber = null;
        onCaptured = handler => subscriber = handler;

        cacheMock
            .Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
            .Callback<CostCacheData, CancellationToken>((d, _) => subscriber?.Invoke(d))
            .Returns(Task.CompletedTask);

        return cacheMock;
    }

    // ===== Tests =====

    [Fact]
    internal void Scaffold_Compiles_AndCanCreateProvider()
    {
        var provider = CreateProvider();
        provider.Should().NotBeNull();
    }
}
```

- [ ] **Step 2: Build the test project to verify the scaffold compiles**

Run: `dotnet build backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: Build succeeds, 0 errors, 0 new warnings attributable to this file.

- [ ] **Step 3: Run the scaffold test to confirm xUnit discovers the class**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureBasedMaterialCostProviderTests"`
Expected: 1 passed, 0 failed.

- [ ] **Step 4: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs
git commit -m "test: scaffold ManufactureBasedMaterialCostProviderTests with helpers"
```

---

## Task 2: FR-1 — manufactured product types route to manufacture-history path

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs`

- [ ] **Step 1: Append the FR-1 theory test inside `ManufactureBasedMaterialCostProviderTests`**

```csharp
[Theory]
[InlineData(ProductType.Product)]
[InlineData(ProductType.Set)]
[InlineData(ProductType.SemiProduct)]
internal async Task RefreshAsync_UsesManufactureHistory_WhenProductTypeIsManufactured(ProductType type)
{
    // Arrange — a single manufacture record at price 100m; PurchasePriceWithVat is a sentinel
    // that MUST NOT appear anywhere in the resulting cost series.
    var manufactureMonth = MonthOffsetFromNow(-2).AddDays(10);
    var product = BuildManufacturedProduct(
        productCode: "PROD-A",
        type: type,
        history: new[] { (manufactureMonth, 100m, 5.0) },
        purchasePriceWithVat: 999m);

    var repoMock = new Mock<ICatalogRepository>();
    repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

    var cacheMock = BuildCaptureCacheMock(out var subscribe);
    CostCacheData? captured = null;
    subscribe(d => captured = d);

    var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock);

    // Act
    await provider.RefreshAsync();

    // Assert
    captured.Should().NotBeNull();
    captured!.ProductCosts.Should().ContainKey("PROD-A");
    var series = captured.ProductCosts["PROD-A"];
    series.Should().NotBeEmpty();
    series.Should().AllSatisfy(mc => mc.Cost.Should().Be(100m));
    series.Select(mc => mc.Cost).Should().NotContain(999m);
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureBasedMaterialCostProviderTests.RefreshAsync_UsesManufactureHistory_WhenProductTypeIsManufactured"`
Expected: 3 passed (one per `InlineData`), 0 failed.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs
git commit -m "test: cover ManufactureBasedMaterialCostProvider FR-1 routing for manufactured types"
```

---

## Task 3: FR-2 — non-manufactured product types fall back to purchase-price path

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs`

- [ ] **Step 1: Append the FR-2 theory test**

```csharp
[Theory]
[InlineData(ProductType.Material)]
[InlineData(ProductType.Goods)]
[InlineData(ProductType.UNDEFINED)]
internal async Task RefreshAsync_UsesPurchasePrice_WhenProductTypeIsNonManufactured(ProductType type)
{
    // Arrange — populated ManufactureHistory MUST be ignored for non-manufactured types.
    var manufactureMonth = MonthOffsetFromNow(-2).AddDays(10);
    var product = BuildManufacturedProduct(
        productCode: "MAT-1",
        type: type,
        history: new[] { (manufactureMonth, 100m, 5.0) },
        purchasePriceWithVat: 50m);

    var repoMock = new Mock<ICatalogRepository>();
    repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

    var cacheMock = BuildCaptureCacheMock(out var subscribe);
    CostCacheData? captured = null;
    subscribe(d => captured = d);

    var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock);

    // Act
    await provider.RefreshAsync();

    // Assert
    captured.Should().NotBeNull();
    var series = captured!.ProductCosts["MAT-1"];
    series.Should().NotBeEmpty();
    series.Should().AllSatisfy(mc => mc.Cost.Should().Be(50m));

    // Series spans the SUT window [now - DefaultHistoryDays, now] aligned to first-of-month.
    var expectedMonthCount = ExpectedMonthCount(captured.DataFrom, captured.DataTo);
    series.Should().HaveCount(expectedMonthCount);
}

private static int ExpectedMonthCount(DateOnly from, DateOnly to)
{
    return ((to.Year - from.Year) * 12) + (to.Month - from.Month) + 1;
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureBasedMaterialCostProviderTests.RefreshAsync_UsesPurchasePrice_WhenProductTypeIsNonManufactured"`
Expected: 3 passed, 0 failed.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs
git commit -m "test: cover ManufactureBasedMaterialCostProvider FR-2 routing for non-manufactured types"
```

---

## Task 4: FR-3 — carry-forward fills gap months with last-known price

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs`

- [ ] **Step 1: Append the FR-3 test**

```csharp
[Fact]
internal async Task RefreshAsync_CarriesForwardLastKnownPrice_WhenMonthHasNoManufacture()
{
    // Arrange — anchors: M1 = 4 months ago, M2 = 3 months ago, M3 = 2 months ago, M4 = 1 month ago.
    // Records exist at M1 (100m) and M3 (200m). Expect carry-forward in M2 (=100m) and M4 (=200m).
    var m1 = MonthOffsetFromNow(-4);
    var m2 = MonthOffsetFromNow(-3);
    var m3 = MonthOffsetFromNow(-2);
    var m4 = MonthOffsetFromNow(-1);

    var product = BuildManufacturedProduct(
        productCode: "PROD-CF",
        type: ProductType.Product,
        history: new[]
        {
            (m1.AddDays(5), 100m, 1.0),
            (m3.AddDays(5), 200m, 1.0),
        });

    var repoMock = new Mock<ICatalogRepository>();
    repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

    var cacheMock = BuildCaptureCacheMock(out var subscribe);
    CostCacheData? captured = null;
    subscribe(d => captured = d);

    var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock);

    // Act
    await provider.RefreshAsync();

    // Assert
    captured.Should().NotBeNull();
    var series = captured!.ProductCosts["PROD-CF"];

    series.Single(mc => mc.Month == m1).Cost.Should().Be(100m);
    series.Single(mc => mc.Month == m2).Cost.Should().Be(100m); // carry-forward
    series.Single(mc => mc.Month == m3).Cost.Should().Be(200m);
    series.Single(mc => mc.Month == m4).Cost.Should().Be(200m); // carry-forward

    // No gaps between m1 and m4 — every month present.
    var inRange = series.Where(mc => mc.Month >= m1 && mc.Month <= m4).OrderBy(mc => mc.Month).ToList();
    inRange.Should().HaveCount(4);
    inRange.Select(mc => mc.Month).Should().Equal(m1, m2, m3, m4);
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureBasedMaterialCostProviderTests.RefreshAsync_CarriesForwardLastKnownPrice_WhenMonthHasNoManufacture"`
Expected: 1 passed, 0 failed.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs
git commit -m "test: cover ManufactureBasedMaterialCostProvider FR-3 carry-forward gap months"
```

---

## Task 5: FR-4 — future-manufacture backfill for months preceding any record

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs`

- [ ] **Step 1: Append the FR-4 test**

```csharp
[Fact]
internal async Task RefreshAsync_BackfillsFromEarliestFuturePrice_WhenNoPriorManufactureExists()
{
    // Arrange — only manufacture record sits 2 months ago at 300m. Months 4 and 3 months ago
    // precede the record and must be backfilled with 300m.
    var earliest = MonthOffsetFromNow(-2);
    var product = BuildManufacturedProduct(
        productCode: "PROD-BF",
        type: ProductType.Product,
        history: new[] { (earliest.AddDays(5), 300m, 1.0) });

    var repoMock = new Mock<ICatalogRepository>();
    repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

    var cacheMock = BuildCaptureCacheMock(out var subscribe);
    CostCacheData? captured = null;
    subscribe(d => captured = d);

    var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock);

    // Act
    await provider.RefreshAsync();

    // Assert — every month in the SUT's window contains 300m (backfill + record + carry-forward).
    captured.Should().NotBeNull();
    var series = captured!.ProductCosts["PROD-BF"];
    series.Should().NotBeEmpty();
    series.Should().AllSatisfy(mc => mc.Cost.Should().Be(300m));
}

[Fact]
internal async Task RefreshAsync_BackfillsUsingEarliestFuturePrice_WhenMultipleFutureRecordsExist()
{
    // Arrange — records at M3 (=300m) and M5 (=500m), backfill must pick 300m (earliest),
    // not 500m (latest). Earlier months M1, M2 must read 300m. M4 carries forward 300m.
    var m1 = MonthOffsetFromNow(-5);
    var m2 = MonthOffsetFromNow(-4);
    var m3 = MonthOffsetFromNow(-3);
    var m4 = MonthOffsetFromNow(-2);
    var m5 = MonthOffsetFromNow(-1);

    var product = BuildManufacturedProduct(
        productCode: "PROD-BF2",
        type: ProductType.Product,
        history: new[]
        {
            (m3.AddDays(5), 300m, 1.0),
            (m5.AddDays(5), 500m, 1.0),
        });

    var repoMock = new Mock<ICatalogRepository>();
    repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

    var cacheMock = BuildCaptureCacheMock(out var subscribe);
    CostCacheData? captured = null;
    subscribe(d => captured = d);

    var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock);

    // Act
    await provider.RefreshAsync();

    // Assert
    captured.Should().NotBeNull();
    var series = captured!.ProductCosts["PROD-BF2"];

    series.Single(mc => mc.Month == m1).Cost.Should().Be(300m); // backfill
    series.Single(mc => mc.Month == m2).Cost.Should().Be(300m); // backfill
    series.Single(mc => mc.Month == m3).Cost.Should().Be(300m);
    series.Single(mc => mc.Month == m4).Cost.Should().Be(300m); // carry-forward
    series.Single(mc => mc.Month == m5).Cost.Should().Be(500m);
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureBasedMaterialCostProviderTests.RefreshAsync_Backfills"`
Expected: 2 passed, 0 failed.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs
git commit -m "test: cover ManufactureBasedMaterialCostProvider FR-4 future-manufacture backfill"
```

---

## Task 6: FR-5 — empty / null manufacture history falls back to purchase price

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs`

- [ ] **Step 1: Append the FR-5 tests**

```csharp
[Fact]
internal async Task RefreshAsync_FallsBackToPurchasePrice_WhenManufactureHistoryIsEmptyList()
{
    // Arrange — manufactured-type product with explicitly empty history.
    var product = BuildManufacturedProduct(
        productCode: "PROD-EMPTY",
        type: ProductType.Product,
        history: Array.Empty<(DateTime, decimal, double)>(),
        purchasePriceWithVat: 75m);

    var repoMock = new Mock<ICatalogRepository>();
    repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

    var cacheMock = BuildCaptureCacheMock(out var subscribe);
    CostCacheData? captured = null;
    subscribe(d => captured = d);

    var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock);

    // Act
    await provider.RefreshAsync();

    // Assert
    captured.Should().NotBeNull();
    var series = captured!.ProductCosts["PROD-EMPTY"];
    series.Should().NotBeEmpty();
    series.Should().AllSatisfy(mc => mc.Cost.Should().Be(75m));
    series.Should().HaveCount(ExpectedMonthCount(captured.DataFrom, captured.DataTo));
}

[Fact]
internal async Task RefreshAsync_FallsBackToPurchasePrice_WhenManufactureHistoryDefaultsToEmpty()
{
    // Arrange — manufactured-type product, do not touch ManufactureHistory (default empty list).
    var product = BuildManufacturedProduct(
        productCode: "PROD-DEFAULT",
        type: ProductType.Product,
        history: null,
        purchasePriceWithVat: 75m);

    var repoMock = new Mock<ICatalogRepository>();
    repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

    var cacheMock = BuildCaptureCacheMock(out var subscribe);
    CostCacheData? captured = null;
    subscribe(d => captured = d);

    var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock);

    // Act
    await provider.RefreshAsync();

    // Assert
    captured.Should().NotBeNull();
    var series = captured!.ProductCosts["PROD-DEFAULT"];
    series.Should().NotBeEmpty();
    series.Should().AllSatisfy(mc => mc.Cost.Should().Be(75m));
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureBasedMaterialCostProviderTests.RefreshAsync_FallsBackToPurchasePrice"`
Expected: 2 passed, 0 failed.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs
git commit -m "test: cover ManufactureBasedMaterialCostProvider FR-5 fallback to purchase price when history empty"
```

---

## Task 7: FR-6 — missing / non-positive purchase price returns empty list

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs`

- [ ] **Step 1: Append the FR-6 tests**

```csharp
[Theory]
[InlineData(null)]
[InlineData(0.0)]
[InlineData(-1.0)]
internal async Task RefreshAsync_ReturnsEmptyCostList_WhenPurchasePriceIsNullOrNonPositive(double? priceOrNull)
{
    // Arrange — non-manufactured product so we hit the purchase-price path directly.
    decimal? price = priceOrNull.HasValue ? (decimal?)priceOrNull.Value : null;
    var product = BuildNonManufacturedProduct(
        productCode: "MAT-EMPTY",
        type: ProductType.Material,
        purchasePriceWithVat: price);

    var repoMock = new Mock<ICatalogRepository>();
    repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

    var cacheMock = BuildCaptureCacheMock(out var subscribe);
    CostCacheData? captured = null;
    subscribe(d => captured = d);

    var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock);

    // Act
    await provider.RefreshAsync();

    // Assert
    captured.Should().NotBeNull();
    captured!.ProductCosts.Should().ContainKey("MAT-EMPTY");
    captured.ProductCosts["MAT-EMPTY"].Should().BeEmpty();
}

[Fact]
internal async Task RefreshAsync_ReturnsEmptyCostList_WhenManufacturedTypeFallbackHasNoPurchasePrice()
{
    // Arrange — manufactured type with empty history and no ErpPrice → fallback returns empty.
    var product = BuildManufacturedProduct(
        productCode: "PROD-NO-PRICE",
        type: ProductType.Product,
        history: Array.Empty<(DateTime, decimal, double)>(),
        purchasePriceWithVat: null);

    var repoMock = new Mock<ICatalogRepository>();
    repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

    var cacheMock = BuildCaptureCacheMock(out var subscribe);
    CostCacheData? captured = null;
    subscribe(d => captured = d);

    var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock);

    // Act
    await provider.RefreshAsync();

    // Assert
    captured.Should().NotBeNull();
    captured!.ProductCosts.Should().ContainKey("PROD-NO-PRICE");
    captured.ProductCosts["PROD-NO-PRICE"].Should().BeEmpty();
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureBasedMaterialCostProviderTests.RefreshAsync_ReturnsEmptyCostList"`
Expected: 4 passed (3 from theory + 1 fact), 0 failed.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs
git commit -m "test: cover ManufactureBasedMaterialCostProvider FR-6 empty list when purchase price absent"
```

---

## Task 8: FR-7 — weighted-average per month aggregation

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs`

- [ ] **Step 1: Append the FR-7 test**

```csharp
[Fact]
internal async Task RefreshAsync_AggregatesWithWeightedAverage_WhenMonthHasMultipleManufactureRecords()
{
    // Arrange — two records in the same month: (100, 10) and (200, 30) → weighted avg = 175m.
    // A second month has a single record (300, 5) which must remain 300m (independent month).
    var monthA = MonthOffsetFromNow(-3);
    var monthB = MonthOffsetFromNow(-2);

    var product = BuildManufacturedProduct(
        productCode: "PROD-WA",
        type: ProductType.Product,
        history: new[]
        {
            (monthA.AddDays(2),  100m, 10.0),
            (monthA.AddDays(20), 200m, 30.0),
            (monthB.AddDays(5),  300m,  5.0),
        });

    var repoMock = new Mock<ICatalogRepository>();
    repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

    var cacheMock = BuildCaptureCacheMock(out var subscribe);
    CostCacheData? captured = null;
    subscribe(d => captured = d);

    var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock);

    // Act
    await provider.RefreshAsync();

    // Assert
    captured.Should().NotBeNull();
    var series = captured!.ProductCosts["PROD-WA"];
    series.Single(mc => mc.Month == monthA).Cost.Should().Be(175m);
    series.Single(mc => mc.Month == monthB).Cost.Should().Be(300m);
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureBasedMaterialCostProviderTests.RefreshAsync_AggregatesWithWeightedAverage"`
Expected: 1 passed, 0 failed.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs
git commit -m "test: cover ManufactureBasedMaterialCostProvider FR-7 weighted-average per month"
```

---

## Task 9: FR-8 — zero total amount throws DivideByZeroException

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs`

- [ ] **Step 1: Append the FR-8 test**

```csharp
[Fact]
internal async Task RefreshAsync_ThrowsDivideByZero_WhenMonthlyAmountSumIsZero()
{
    // Arrange — two records in the same month both with Amount = 0; weighted-average denominator is 0.
    // The SUT uses `decimal` arithmetic, so `decimal / 0m` raises DivideByZeroException
    // (decimal does NOT produce NaN — that's a double-only behavior).
    // The exception is logged at Error and rethrown by RefreshAsync; this test pins that contract.
    var month = MonthOffsetFromNow(-2);
    var product = BuildManufacturedProduct(
        productCode: "PROD-DBZ",
        type: ProductType.Product,
        history: new[]
        {
            (month.AddDays(2), 100m, 0.0),
            (month.AddDays(3), 200m, 0.0),
        });

    var repoMock = new Mock<ICatalogRepository>();
    repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync(new List<CatalogAggregate> { product });

    var cacheMock = new Mock<IMaterialCostCache>();
    cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    var loggerMock = new Mock<ILogger<ManufactureBasedMaterialCostProvider>>();

    var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, loggerMock: loggerMock);

    // Act + Assert
    await Assert.ThrowsAsync<DivideByZeroException>(() => provider.RefreshAsync());

    // The exception path logs at Error and never writes the cache.
    VerifyLog(loggerMock, LogLevel.Error, "Failed to refresh MaterialCostCache");
    cacheMock.Verify(
        c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
        Times.Never);
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureBasedMaterialCostProviderTests.RefreshAsync_ThrowsDivideByZero"`
Expected: 1 passed, 0 failed.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs
git commit -m "test: pin ManufactureBasedMaterialCostProvider FR-8 divide-by-zero behavior"
```

---

## Task 10: FR-9 — GetCostsAsync filters by product codes when cache hydrated

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs`

- [ ] **Step 1: Append the FR-9 tests**

```csharp
[Fact]
internal async Task GetCostsAsync_ReturnsFullDictionary_WhenProductCodesAreNull()
{
    var hydrated = BuildHydratedCacheData(new[] { "A", "B", "C" });
    var cacheMock = new Mock<IMaterialCostCache>();
    cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(hydrated);

    var provider = CreateProvider(cacheMock: cacheMock);

    var result = await provider.GetCostsAsync(null);

    result.Should().HaveCount(3);
    result.Keys.Should().BeEquivalentTo("A", "B", "C");
}

[Fact]
internal async Task GetCostsAsync_ReturnsFullDictionary_WhenProductCodesAreEmpty()
{
    var hydrated = BuildHydratedCacheData(new[] { "A", "B", "C" });
    var cacheMock = new Mock<IMaterialCostCache>();
    cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(hydrated);

    var provider = CreateProvider(cacheMock: cacheMock);

    var result = await provider.GetCostsAsync(new List<string>());

    result.Should().HaveCount(3);
    result.Keys.Should().BeEquivalentTo("A", "B", "C");
}

[Fact]
internal async Task GetCostsAsync_ReturnsSubset_WhenProductCodesProvided()
{
    var hydrated = BuildHydratedCacheData(new[] { "A", "B", "C" });
    var cacheMock = new Mock<IMaterialCostCache>();
    cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(hydrated);

    var provider = CreateProvider(cacheMock: cacheMock);

    var result = await provider.GetCostsAsync(new List<string> { "A", "C", "MISSING" });

    result.Should().HaveCount(2);
    result.Keys.Should().BeEquivalentTo("A", "C");
}

[Fact]
internal async Task GetCostsAsync_ReturnsEmptyDictionary_WhenRequestedProductCodeNotInCache()
{
    var hydrated = BuildHydratedCacheData(new[] { "A", "B", "C" });
    var cacheMock = new Mock<IMaterialCostCache>();
    cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ReturnsAsync(hydrated);

    var provider = CreateProvider(cacheMock: cacheMock);

    var result = await provider.GetCostsAsync(new List<string> { "MISSING" });

    result.Should().BeEmpty();
}
```

- [ ] **Step 2: Run the tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureBasedMaterialCostProviderTests.GetCostsAsync_Returns"`
Expected: 4 passed, 0 failed.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs
git commit -m "test: cover ManufactureBasedMaterialCostProvider FR-9 GetCostsAsync filtering"
```

---

## Task 11: FR-10 — GetCostsAsync returns empty + warns when cache not hydrated

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs`

- [ ] **Step 1: Append the FR-10 test**

```csharp
[Fact]
internal async Task GetCostsAsync_ReturnsEmptyAndLogsWarning_WhenCacheNotHydrated()
{
    // Arrange
    var cacheMock = new Mock<IMaterialCostCache>();
    cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>()))
        .ReturnsAsync(CostCacheData.Empty());

    var repoMock = new Mock<ICatalogRepository>();
    var loggerMock = new Mock<ILogger<ManufactureBasedMaterialCostProvider>>();

    var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, loggerMock: loggerMock);

    // Act
    var result = await provider.GetCostsAsync();

    // Assert
    result.Should().NotBeNull();
    result.Should().BeEmpty();

    VerifyLog(loggerMock, LogLevel.Warning, "MaterialCostCache not hydrated");

    // Cache hydration miss must not trigger any repository work.
    repoMock.VerifyNoOtherCalls();
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureBasedMaterialCostProviderTests.GetCostsAsync_ReturnsEmptyAndLogsWarning"`
Expected: 1 passed, 0 failed.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs
git commit -m "test: cover ManufactureBasedMaterialCostProvider FR-10 GetCostsAsync warning when cache not hydrated"
```

---

## Task 12: FR-11 — GetCostsAsync logs error and rethrows when cache throws

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs`

- [ ] **Step 1: Append the FR-11 test**

```csharp
[Fact]
internal async Task GetCostsAsync_LogsErrorAndRethrows_WhenCacheReadFails()
{
    // Arrange
    var boom = new InvalidOperationException("boom");
    var cacheMock = new Mock<IMaterialCostCache>();
    cacheMock.Setup(c => c.GetCachedDataAsync(It.IsAny<CancellationToken>())).ThrowsAsync(boom);

    var loggerMock = new Mock<ILogger<ManufactureBasedMaterialCostProvider>>();

    var provider = CreateProvider(cacheMock: cacheMock, loggerMock: loggerMock);

    // Act
    var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetCostsAsync());

    // Assert
    thrown.Should().BeSameAs(boom);
    VerifyLog(loggerMock, LogLevel.Error, "Error getting material costs");
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureBasedMaterialCostProviderTests.GetCostsAsync_LogsErrorAndRethrows"`
Expected: 1 passed, 0 failed.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs
git commit -m "test: cover ManufactureBasedMaterialCostProvider FR-11 error log + rethrow on cache failure"
```

---

## Task 13: FR-12a — RefreshAsync populates cache on success

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs`

- [ ] **Step 1: Append the FR-12a test**

```csharp
[Fact]
internal async Task RefreshAsync_PopulatesCache_OnSuccess_AndExcludesEmptyProductCodes()
{
    // Arrange — three real products + two skip cases (empty + null code).
    var products = new List<CatalogAggregate>
    {
        BuildNonManufacturedProduct("MAT-A", ProductType.Material, 10m),
        BuildNonManufacturedProduct("MAT-B", ProductType.Material, 20m),
        BuildNonManufacturedProduct("MAT-C", ProductType.Material, 30m),
        BuildNonManufacturedProduct(string.Empty, ProductType.Material, 99m), // skipped
        BuildNonManufacturedProduct(null!, ProductType.Material, 99m),         // skipped
    };

    var callOrder = new List<string>();

    var repoMock = new Mock<ICatalogRepository>();
    repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>()))
        .Callback(() => callOrder.Add("WaitForCurrentMergeAsync"))
        .Returns(Task.CompletedTask);
    repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
        .Callback(() => callOrder.Add("GetAllAsync"))
        .ReturnsAsync(products);

    var cacheMock = BuildCaptureCacheMock(out var subscribe);
    CostCacheData? captured = null;
    subscribe(d => captured = d);

    var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, manufactureCostHistoryDays: 90);

    var nowBefore = DateTime.UtcNow;

    // Act
    await provider.RefreshAsync();

    var nowAfter = DateTime.UtcNow;

    // Assert — cache populated with exactly the three non-empty product codes.
    captured.Should().NotBeNull();
    captured!.IsHydrated.Should().BeTrue();
    captured.ProductCosts.Keys.Should().BeEquivalentTo("MAT-A", "MAT-B", "MAT-C");

    // DataFrom/DataTo within tolerant window around UtcNow.
    var expectedFromLower = DateOnly.FromDateTime(nowBefore.AddDays(-90));
    var expectedFromUpper = DateOnly.FromDateTime(nowAfter.AddDays(-90));
    captured.DataFrom.Should().BeOnOrAfter(expectedFromLower);
    captured.DataFrom.Should().BeOnOrBefore(expectedFromUpper);

    captured.DataTo.Should().BeOnOrAfter(DateOnly.FromDateTime(nowBefore));
    captured.DataTo.Should().BeOnOrBefore(DateOnly.FromDateTime(nowAfter));

    // Call order
    callOrder.Should().Equal("WaitForCurrentMergeAsync", "GetAllAsync");
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureBasedMaterialCostProviderTests.RefreshAsync_PopulatesCache_OnSuccess"`
Expected: 1 passed, 0 failed.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs
git commit -m "test: cover ManufactureBasedMaterialCostProvider FR-12a RefreshAsync success path"
```

---

## Task 14: FR-12b — RefreshAsync skips concurrent second call

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs`

- [ ] **Step 1: Append the FR-12b test**

```csharp
[Fact]
internal async Task RefreshAsync_SkipsSecondInvocation_WhenFirstStillRunning()
{
    // Arrange — gate `GetAllAsync` with a TaskCompletionSource so the first refresh blocks
    // inside ComputeAllCostsAsync, holding the static _refreshLock. The second refresh must
    // detect WaitAsync(0) failure, log "refresh already in progress", and return.
    var gate = new TaskCompletionSource<List<CatalogAggregate>>(TaskCreationOptions.RunContinuationsAsynchronously);

    var repoMock = new Mock<ICatalogRepository>();
    repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    repoMock.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).Returns(gate.Task);

    var cacheMock = new Mock<IMaterialCostCache>();
    cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    var loggerMock = new Mock<ILogger<ManufactureBasedMaterialCostProvider>>();

    var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, loggerMock: loggerMock);

    // Act 1 — first refresh; blocks on gate after acquiring the lock.
    var firstRefresh = provider.RefreshAsync();

    while (!repoMock.Invocations.Any(i => i.Method.Name == nameof(ICatalogRepository.GetAllAsync)))
    {
        await Task.Yield();
    }

    // Act 2 — second refresh; sees lock held, short-circuits.
    await provider.RefreshAsync();

    // Assert intermediate
    VerifyLog(loggerMock, LogLevel.Information, "refresh already in progress");
    cacheMock.Verify(
        c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
        Times.Never);

    // Release gate, let first refresh finish.
    gate.SetResult(new List<CatalogAggregate>());
    await firstRefresh;

    cacheMock.Verify(
        c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
        Times.Once);

    // Act 3 — fresh refresh after lock released must proceed.
    await provider.RefreshAsync();

    cacheMock.Verify(
        c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
        Times.Exactly(2));
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureBasedMaterialCostProviderTests.RefreshAsync_SkipsSecondInvocation"`
Expected: 1 passed, 0 failed.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs
git commit -m "test: cover ManufactureBasedMaterialCostProvider FR-12b concurrent refresh skipped"
```

---

## Task 15: FR-12c — RefreshAsync releases lock when repository throws

**Files:**
- Modify: `backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs`

- [ ] **Step 1: Append the FR-12c test**

```csharp
[Fact]
internal async Task RefreshAsync_ReleasesLockOnRepositoryException_AndAllowsSubsequentRefresh()
{
    // Arrange — first GetAllAsync throws, second succeeds. Lock must be released by the SUT's
    // `finally` block so the second call proceeds rather than logging "refresh already in progress".
    var boom = new InvalidOperationException("repo offline");

    var repoMock = new Mock<ICatalogRepository>();
    repoMock.Setup(r => r.WaitForCurrentMergeAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
    repoMock.SetupSequence(r => r.GetAllAsync(It.IsAny<CancellationToken>()))
        .ThrowsAsync(boom)
        .ReturnsAsync(new List<CatalogAggregate>());

    var cacheMock = new Mock<IMaterialCostCache>();
    cacheMock.Setup(c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()))
        .Returns(Task.CompletedTask);

    var loggerMock = new Mock<ILogger<ManufactureBasedMaterialCostProvider>>();

    var provider = CreateProvider(cacheMock: cacheMock, repoMock: repoMock, loggerMock: loggerMock);

    // Act 1 — first call throws and logs.
    var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => provider.RefreshAsync());
    thrown.Should().BeSameAs(boom);
    VerifyLog(loggerMock, LogLevel.Error, "Failed to refresh MaterialCostCache");
    cacheMock.Verify(
        c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
        Times.Never);

    // Act 2 — second call must proceed (lock released by finally).
    await provider.RefreshAsync();

    // Positive proof: the second call entered the normal execution path, never the "skip" branch.
    VerifyLog(loggerMock, LogLevel.Information, "refresh already in progress", Times.Never());
    VerifyLog(loggerMock, LogLevel.Information, "Starting MaterialCostCache refresh", Times.Exactly(2));
    cacheMock.Verify(
        c => c.SetCachedDataAsync(It.IsAny<CostCacheData>(), It.IsAny<CancellationToken>()),
        Times.Once);
}
```

- [ ] **Step 2: Run the test**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureBasedMaterialCostProviderTests.RefreshAsync_ReleasesLockOnRepositoryException"`
Expected: 1 passed, 0 failed.

- [ ] **Step 3: Commit**

```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs
git commit -m "test: cover ManufactureBasedMaterialCostProvider FR-12c lock release on repo exception"
```

---

## Task 16: Coverage verification

**Files:**
- (none modified — verification only)

- [ ] **Step 1: Run the full suite to confirm nothing else broke**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~ManufactureBasedMaterialCostProviderTests"`
Expected: All tests in the new class pass (≈ 20 tests including theory variants).

- [ ] **Step 2: Run the wider test project to confirm no cross-collection regressions**

Run: `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj`
Expected: 0 failed.

- [ ] **Step 3: Collect coverage for the SUT file**

Run:
```bash
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~ManufactureBasedMaterialCostProviderTests" \
  --collect:"XPlat Code Coverage" \
  --results-directory backend/test/Anela.Heblo.Tests/TestResults/coverage-mbmcp \
  -- DataCollectionRunSettings.DataCollectors.DataCollector.Configuration.Format=cobertura
```

Then inspect the produced `coverage.cobertura.xml` for the SUT (search the file for `Anela.Heblo.Application/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProvider`) and confirm `line-rate >= 0.80`.

Expected: line-rate on `ManufactureBasedMaterialCostProvider.cs` ≥ 0.80. If lower, identify the uncovered branch(es) by reading the XML and add a targeted test to the appropriate FR's task before completing.

- [ ] **Step 4: Final validation gate**

Run:
- `dotnet build backend/backend.sln` → expected: build succeeds.
- `dotnet format backend/backend.sln --verify-no-changes --include backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs` → expected: no formatting changes required (run `dotnet format ... --include <file>` without `--verify-no-changes` to auto-fix if it fails, then re-verify).

Expected: both commands succeed.

- [ ] **Step 5: Commit any formatting touch-ups (if any)**

If `dotnet format` produced changes, commit them:
```bash
git add backend/test/Anela.Heblo.Tests/Features/Catalog/CostProviders/ManufactureBasedMaterialCostProviderTests.cs
git commit -m "chore: apply dotnet format to ManufactureBasedMaterialCostProviderTests"
```

If no changes, skip this step.

---

## Spec Coverage Verification

| FR / NFR | Task |
|----------|------|
| FR-1 — Manufactured types route to manufacture-history | Task 2 |
| FR-2 — Non-manufactured types route to purchase-price | Task 3 |
| FR-3 — Carry-forward gap months | Task 4 |
| FR-4 — Backfill from earliest future record | Task 5 |
| FR-5 — Empty/null history → purchase-price fallback | Task 6 |
| FR-6 — Null/zero/negative price → empty list | Task 7 |
| FR-7 — Weighted-average per month | Task 8 |
| FR-8 — Zero-amount divide-by-zero behavior | Task 9 |
| FR-9 — GetCostsAsync filtered cache when hydrated | Task 10 |
| FR-10 — GetCostsAsync empty + warning when not hydrated | Task 11 |
| FR-11 — GetCostsAsync error log + rethrow | Task 12 |
| FR-12 — RefreshAsync success / concurrent / lock release | Tasks 13, 14, 15 |
| FR-13 — File naming, location, `[Collection]`, conventions | Task 1 (scaffold) + reinforced by every subsequent task |
| NFR-1 — Performance (no sleeps, no I/O) | All tasks use in-memory mocks + `TaskCompletionSource` gating |
| NFR-2 — Security (no secrets, no PII) | All tasks use synthetic codes/prices |
| NFR-3 — Determinism (anchor dates relative to UtcNow + `DefaultHistoryDays = 4000`) | All date-driven tasks (Tasks 2–9, 13) |
| NFR-4 — Coverage ≥ 80 % | Task 16 |

## Architecture-Review Amendments Applied

- Spec said `CatalogManufactureRecord` → plan uses **`ManufactureHistoryRecord`** (`Anela.Heblo.Domain.Features.Manufacture`).
- Spec said "set `PurchasePriceWithVat`" → plan sets `ErpPrice = new ProductPriceErp { PurchasePriceWithVat = … }` because the property is read-only.
- Spec said "null / 0 / negative `PurchasePriceWithVat`" → plan realises null by leaving `ErpPrice = null` (Task 7).
- Spec said "DivideByZeroException or NaN" → plan asserts only **`DivideByZeroException`** (decimal arithmetic).
- Spec said `DefaultHistoryDays = 365` → plan uses **4000** to keep synthetic month anchors inside the SUT window.
- Log substrings updated to match production text: `"MaterialCostCache not hydrated"`, `"Error getting material costs"`, `"refresh already in progress"`, `"Failed to refresh MaterialCostCache"`, `"Starting MaterialCostCache refresh"`.
