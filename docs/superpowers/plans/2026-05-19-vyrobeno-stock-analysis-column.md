# "Vyrobeno" Stock Analysis Column Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a "Vyrobeno" column to the product stock analysis ("Řízení zásob výrobků") showing each product's Sklad výroby quantity, and make the overstock calculation count it.

**Architecture:** `Vyrobeno` becomes a new `Manufactured` dimension on the in-memory `StockData` domain record, folded into `Available` so it propagates into `Total`, `StockDaysAvailable`, and overstock % automatically — exactly like `Transport`. It is sourced from the `ManufacturedProductInventoryItem` table (the Sklad výroby feature), aggregated per product code, cached in `CatalogRepository` mirroring the existing Transport cache/refresh pattern. The frontend gets a new column and an extended SKLAD breakdown.

**Tech Stack:** .NET 8, MediatR, EF Core, xUnit + FluentAssertions + Moq; React + TypeScript, Jest + React Testing Library.

---

## File Structure

**Backend — modified**
- `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockData.cs` — new `Manufactured` field, `Available` formula.
- `backend/src/Anela.Heblo.Domain/Features/Manufacture/Inventory/IManufacturedProductInventoryRepository.cs` — new aggregation method.
- `backend/src/Anela.Heblo.Persistence/Manufacture/Inventory/ManufacturedProductInventoryRepository.cs` — aggregation impl.
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs` — cache, refresh, merge assignment, ctor injection, load date.
- `backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs` — `RefreshManufacturedData` method.
- `backend/src/Anela.Heblo.Persistence/Repositories/MockCatalogRepository.cs` — no-op `RefreshManufacturedData`.
- `backend/test/Anela.Heblo.Tests/Common/ManufactureOrderTestFactory.cs` — no-op `RefreshManufacturedData`.
- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs` — register refresh task.
- `backend/src/Anela.Heblo.API/appsettings.json` — `BackgroundRefresh` config block.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetStockAnalysis/GetManufacturingStockAnalysisResponse.cs` — `ManufacturedStock` on `ManufacturingStockItemDto`.
- `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureAnalysisMapper.cs` — map `ManufacturedStock`.

**Backend — new test file**
- `backend/test/Anela.Heblo.Tests/Persistence/Manufacture/Inventory/ManufacturedProductInventoryRepositoryTests.cs`

**Frontend — modified**
- `frontend/src/api/generated/api-client.ts` — regenerated.
- `frontend/src/api/hooks/useManufacturingStockAnalysis.ts` — `ManufacturingStockItemDto` interface + `formatWarehouseStock`.
- `frontend/src/components/pages/ManufacturingStockAnalysis.tsx` — new `vyrobeno` column.
- `frontend/src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx` — `formatWarehouseStock` tests.
- `frontend/src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx` — column render test.

---

## Task 1: Add `Manufactured` dimension to `StockData`

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockData.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Catalog/Stock/StockDataTests.cs`

- [ ] **Step 1: Write the failing test**

Add this test to `StockDataTests.cs` (inside the `StockDataTests` class):

```csharp
[Fact]
public void Available_IncludesManufactured()
{
    // Arrange
    var stockData = new StockData
    {
        Erp = 100m,
        Transport = 10m,
        Manufactured = 7m,
        Reserve = 25m,
        PrimaryStockSource = StockSource.Erp
    };

    // Act
    var available = stockData.Available;
    var total = stockData.Total;

    // Assert
    available.Should().Be(117m, "Available should be Erp (100) + Transport (10) + Manufactured (7)");
    total.Should().Be(142m, "Total should be Available (117) + Reserve (25)");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~StockDataTests.Available_IncludesManufactured"`
Expected: FAIL — `StockData` has no `Manufactured` property (compile error).

- [ ] **Step 3: Add the field and update `Available`**

In `StockData.cs`, add the `Manufactured` property after `Transport` (line 9) and update the `Available` expression:

```csharp
public decimal Eshop { get; set; }
public decimal Erp { get; set; }
public decimal Transport { get; set; }
public decimal Manufactured { get; set; }
public decimal Reserve { get; set; }
public decimal Quarantine { get; set; }
public decimal Ordered { get; set; }
public decimal Planned { get; set; }

public StockSource PrimaryStockSource { get; set; } = StockSource.Erp;

public decimal Available => (PrimaryStockSource == StockSource.Erp ? Erp : Eshop) + Transport + Manufactured;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~StockDataTests"`
Expected: PASS — all `StockDataTests` (existing tests have `Manufactured` defaulting to 0, so their expectations are unchanged).

- [ ] **Step 5: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Catalog/Stock/StockData.cs backend/test/Anela.Heblo.Tests/Features/Catalog/Stock/StockDataTests.cs
git commit -m "feat: add Manufactured dimension to StockData"
```

---

## Task 2: Add per-product-code aggregation to the manufactured inventory repository

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Manufacture/Inventory/IManufacturedProductInventoryRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Manufacture/Inventory/ManufacturedProductInventoryRepository.cs`
- Test (create): `backend/test/Anela.Heblo.Tests/Persistence/Manufacture/Inventory/ManufacturedProductInventoryRepositoryTests.cs`

- [ ] **Step 1: Write the failing test**

Create `backend/test/Anela.Heblo.Tests/Persistence/Manufacture/Inventory/ManufacturedProductInventoryRepositoryTests.cs`:

```csharp
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
using Anela.Heblo.Persistence;
using Anela.Heblo.Persistence.Manufacture.Inventory;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Anela.Heblo.Tests.Persistence.Manufacture.Inventory;

public class ManufacturedProductInventoryRepositoryTests
{
    private static ApplicationDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseInMemoryDatabase(databaseName: $"ManufacturedInventoryTests_{Guid.NewGuid()}")
            .Options;
        return new ApplicationDbContext(options);
    }

    private static ManufacturedProductInventoryItem CreateItem(string productCode, decimal amount, string? lot = null)
        => new(productCode, $"Name {productCode}", amount, "test", DateTime.UtcNow, lotNumber: lot);

    [Fact]
    public async Task GetTotalAmountByProductCodeAsync_SumsAmountsAcrossLots()
    {
        // Arrange
        await using var context = CreateContext();
        context.ManufacturedProductInventoryItems.AddRange(
            CreateItem("PROD001", 10m, lot: "L1"),
            CreateItem("PROD001", 5m, lot: "L2"),
            CreateItem("PROD002", 3m));
        await context.SaveChangesAsync();
        var repository = new ManufacturedProductInventoryRepository(context);

        // Act
        var result = await repository.GetTotalAmountByProductCodeAsync();

        // Assert
        result.Should().HaveCount(2);
        result["PROD001"].Should().Be(15m);
        result["PROD002"].Should().Be(3m);
    }

    [Fact]
    public async Task GetTotalAmountByProductCodeAsync_WithNoItems_ReturnsEmptyDictionary()
    {
        // Arrange
        await using var context = CreateContext();
        var repository = new ManufacturedProductInventoryRepository(context);

        // Act
        var result = await repository.GetTotalAmountByProductCodeAsync();

        // Assert
        result.Should().BeEmpty();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ManufacturedProductInventoryRepositoryTests"`
Expected: FAIL — `GetTotalAmountByProductCodeAsync` does not exist (compile error).

- [ ] **Step 3: Add the method to the interface**

In `IManufacturedProductInventoryRepository.cs`, add this method to the interface:

```csharp
Task<Dictionary<string, decimal>> GetTotalAmountByProductCodeAsync(CancellationToken cancellationToken = default);
```

- [ ] **Step 4: Implement the method**

In `ManufacturedProductInventoryRepository.cs`, add this method to the class:

```csharp
public async Task<Dictionary<string, decimal>> GetTotalAmountByProductCodeAsync(
    CancellationToken cancellationToken = default)
{
    return await DbSet
        .GroupBy(x => x.ProductCode)
        .Select(g => new { ProductCode = g.Key, Total = g.Sum(x => x.Amount) })
        .ToDictionaryAsync(x => x.ProductCode, x => x.Total, cancellationToken);
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ManufacturedProductInventoryRepositoryTests"`
Expected: PASS — both tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Manufacture/Inventory/IManufacturedProductInventoryRepository.cs backend/src/Anela.Heblo.Persistence/Manufacture/Inventory/ManufacturedProductInventoryRepository.cs backend/test/Anela.Heblo.Tests/Persistence/Manufacture/Inventory/ManufacturedProductInventoryRepositoryTests.cs
git commit -m "feat: add GetTotalAmountByProductCodeAsync to manufactured inventory repository"
```

---

## Task 3: Wire manufactured stock into `CatalogRepository`

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs`

This task has no new unit test (the `Merge` assignment is a one-line mirror of four sibling lines; correctness is covered by Task 2's aggregation test and Task 5's mapper test). It is verified by `dotnet build` and the existing `CatalogRepository` test suite.

- [ ] **Step 1: Add the using directive**

In `CatalogRepository.cs`, add this with the other `using` statements (after line 17 `using Anela.Heblo.Domain.Features.Manufacture;`):

```csharp
using Anela.Heblo.Domain.Features.Manufacture.Inventory;
```

- [ ] **Step 2: Add the dependency field**

After the `_manufactureDifficultyRepository` field (line 43), add:

```csharp
    private readonly IManufacturedProductInventoryRepository _manufacturedInventoryRepository;
```

- [ ] **Step 3: Add the constructor parameter and assignment**

Add `IManufacturedProductInventoryRepository manufacturedInventoryRepository` to the constructor parameter list (place it after `IManufactureDifficultyRepository manufactureDifficultyRepository,` near line 79). Then add the assignment in the constructor body, after `_manufactureDifficultyRepository = manufactureDifficultyRepository;` (line 104):

```csharp
        _manufacturedInventoryRepository = manufacturedInventoryRepository;
```

- [ ] **Step 4: Add the `RefreshManufacturedData` method**

After `RefreshTransportData` (ends line 121), add:

```csharp
    public async Task RefreshManufacturedData(CancellationToken ct)
    {
        var manufacturedData = await _manufacturedInventoryRepository.GetTotalAmountByProductCodeAsync(ct);
        CachedManufacturedData = manufacturedData;
    }
```

- [ ] **Step 5: Add the `CachedManufacturedData` cache property**

After the `CachedInTransportData` property (ends line 594), add:

```csharp
    private IDictionary<string, decimal> CachedManufacturedData
    {
        get => _cache.Get<Dictionary<string, decimal>>(nameof(CachedManufacturedData)) ?? new Dictionary<string, decimal>();
        set
        {
            _cache.Set(nameof(CachedManufacturedData), value);
            InvalidateSourceData(nameof(CachedManufacturedData));
            SetLoadDateInCache(nameof(CachedManufacturedData));
        }
    }
```

- [ ] **Step 6: Assign `Manufactured` in `Merge`**

In the `Merge` method, after the `product.Stock.Transport = ...` line (line 399), add:

```csharp
            product.Stock.Manufactured = CachedManufacturedData.ContainsKey(product.ProductCode) ? CachedManufacturedData[product.ProductCode] : 0;
```

- [ ] **Step 7: Add the `ManufacturedLoadDate` property and include it in the merge check**

After the `TransportLoadDate` property (line 770), add:

```csharp
    public DateTime? ManufacturedLoadDate => GetLoadDateFromCache(nameof(CachedManufacturedData));
```

Then add `ManufacturedLoadDate,` to the `loadDates` array in `ChangesPendingForMerge` (the array starting at line 804, alongside `TransportLoadDate,`).

- [ ] **Step 8: Build to verify it compiles**

Run: `dotnet build backend/src/Anela.Heblo.Application`
Expected: Build succeeded (the constructor change resolves via DI — `IManufacturedProductInventoryRepository` is already registered in `ManufactureModule`).

- [ ] **Step 9: Run the existing catalog repository tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~CatalogRepository"`
Expected: PASS — no regressions.

- [ ] **Step 10: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Catalog/CatalogRepository.cs
git commit -m "feat: source manufactured stock into CatalogRepository merge"
```

---

## Task 4: Register the refresh task and background config

**Files:**
- Modify: `backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs`
- Modify: `backend/src/Anela.Heblo.Persistence/Repositories/MockCatalogRepository.cs`
- Modify: `backend/test/Anela.Heblo.Tests/Common/ManufactureOrderTestFactory.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`
- Modify: `backend/src/Anela.Heblo.API/appsettings.json`

This task is config/interface wiring — verified by `dotnet build` and the existing test suite.

- [ ] **Step 1: Add `RefreshManufacturedData` to the interface**

In `ICatalogRepository.cs`, add after `Task RefreshTransportData(CancellationToken ct);` (line 7):

```csharp
    Task RefreshManufacturedData(CancellationToken ct);
```

- [ ] **Step 2: Add the no-op implementation to `MockCatalogRepository`**

In `MockCatalogRepository.cs`, after `public Task RefreshTransportData(CancellationToken ct) => Task.CompletedTask;` (line 414), add:

```csharp
    public Task RefreshManufacturedData(CancellationToken ct) => Task.CompletedTask;
```

- [ ] **Step 3: Add the no-op implementation to `ManufactureOrderTestFactory`**

In `ManufactureOrderTestFactory.cs`, after `public Task RefreshTransportData(CancellationToken ct) => Task.CompletedTask;` (line 176), add:

```csharp
    public Task RefreshManufacturedData(CancellationToken ct) => Task.CompletedTask;
```

- [ ] **Step 4: Register the refresh task in `CatalogModule`**

In `CatalogModule.cs`, in `RegisterBackgroundRefreshTasks`, after the `RefreshTransportData` registration (ends line 128), add:

```csharp
        services.RegisterRefreshTask<ICatalogRepository>(
            nameof(ICatalogRepository.RefreshManufacturedData),
            (r, ct) => r.RefreshManufacturedData(ct)
        );
```

- [ ] **Step 5: Add the background-refresh config**

In `appsettings.json`, inside `BackgroundRefresh` → `ICatalogRepository`, after the `RefreshTransportData` block (ends line 295), add:

```json
      "RefreshManufacturedData": {
        "InitialDelay": "00:00:00",
        "RefreshInterval": "00:05:00",
        "Enabled": true,
        "HydrationTier": 1,
        "Description": "Refreshes manufactured (Sklad výroby) stock data"
      },
```

- [ ] **Step 6: Build and run tests to verify**

Run: `dotnet build backend/src/Anela.Heblo.API`
Expected: Build succeeded (all `ICatalogRepository` implementations now satisfy the interface).

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~RefreshTaskConfiguration"`
Expected: PASS — no regressions.

- [ ] **Step 7: Commit**

```bash
git add backend/src/Anela.Heblo.Domain/Features/Catalog/ICatalogRepository.cs backend/src/Anela.Heblo.Persistence/Repositories/MockCatalogRepository.cs backend/test/Anela.Heblo.Tests/Common/ManufactureOrderTestFactory.cs backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs backend/src/Anela.Heblo.API/appsettings.json
git commit -m "feat: register manufactured stock background refresh task"
```

---

## Task 5: Expose `ManufacturedStock` in the analysis DTO and mapper

**Files:**
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetStockAnalysis/GetManufacturingStockAnalysisResponse.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureAnalysisMapper.cs`
- Test: `backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ManufactureAnalysisMapperTests.cs`

- [ ] **Step 1: Write the failing test**

Add this test to `ManufactureAnalysisMapperTests.cs` (inside the `ManufactureAnalysisMapperTests` class):

```csharp
[Fact]
public void MapToDto_MapsManufacturedStock()
{
    // Arrange
    var catalogItem = new CatalogAggregate
    {
        ProductCode = "PROD001",
        ProductName = "Product One",
        Properties = new CatalogProperties
        {
            OptimalStockDaysSetup = 30,
            StockMinSetup = 25,
            BatchSize = 5
        },
        Stock = new StockData
        {
            Erp = 100,
            Transport = 0,
            Manufactured = 12
        }
    };

    // Act
    var result = _mapper.MapToDto(
        catalogItem,
        ManufacturingStockSeverity.Adequate,
        dailySalesRate: 1.0,
        salesInPeriod: 30.0,
        stockDaysAvailable: 100.0,
        overstockPercentage: 200.0,
        isInProduction: false);

    // Assert
    result.ManufacturedStock.Should().Be(12.0);
    result.CurrentStock.Should().Be(112.0, "CurrentStock is Available = Erp + Transport + Manufactured");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ManufactureAnalysisMapperTests.MapToDto_MapsManufacturedStock"`
Expected: FAIL — `ManufacturingStockItemDto` has no `ManufacturedStock` property (compile error).

- [ ] **Step 3: Add `ManufacturedStock` to the DTO**

In `GetManufacturingStockAnalysisResponse.cs`, add to `ManufacturingStockItemDto` after `TransportStock` (line 32):

```csharp
    public double ManufacturedStock { get; set; }
```

- [ ] **Step 4: Map it in `ManufactureAnalysisMapper`**

In `ManufactureAnalysisMapper.cs`, in the `MapToDto` object initializer, add after `TransportStock = (double)catalogItem.Stock.Transport,` (line 37):

```csharp
            ManufacturedStock = (double)catalogItem.Stock.Manufactured,
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test backend/test/Anela.Heblo.Tests --filter "FullyQualifiedName~ManufactureAnalysisMapperTests"`
Expected: PASS — all mapper tests pass.

- [ ] **Step 6: Commit**

```bash
git add backend/src/Anela.Heblo.Application/Features/Manufacture/UseCases/GetStockAnalysis/GetManufacturingStockAnalysisResponse.cs backend/src/Anela.Heblo.Application/Features/Manufacture/Services/ManufactureAnalysisMapper.cs backend/test/Anela.Heblo.Tests/Features/Manufacture/Services/ManufactureAnalysisMapperTests.cs
git commit -m "feat: expose ManufacturedStock in manufacturing stock analysis DTO"
```

---

## Task 6: Regenerate the TypeScript API client

**Files:**
- Modify: `frontend/src/api/generated/api-client.ts`

The generated client must stay in sync with the backend contract (per `CLAUDE.md`). The `ManufacturingStockAnalysis` page uses a hand-written interface (updated in Task 7), but the generated `ManufacturingStockItemDto` is regenerated here for consistency.

- [ ] **Step 1: Build the backend**

Run: `dotnet build backend/src/Anela.Heblo.API`
Expected: Build succeeded.

- [ ] **Step 2: Regenerate the client**

Run: `dotnet msbuild backend/src/Anela.Heblo.API/Anela.Heblo.API.csproj -t:GenerateFrontendClientManual`
Expected: "Frontend API client generation completed."

- [ ] **Step 3: Verify the new field appears**

Run: `grep -n "manufacturedStock" frontend/src/api/generated/api-client.ts`
Expected: matches in the `ManufacturingStockItemDto` class (`manufacturedStock?: number;`, `init`, `toJSON`) and the `IManufacturingStockItemDto` interface.

- [ ] **Step 4: Commit**

```bash
git add frontend/src/api/generated/api-client.ts
git commit -m "chore: regenerate API client with manufacturedStock field"
```

---

## Task 7: Add `manufacturedStock` to the frontend type and SKLAD breakdown

**Files:**
- Modify: `frontend/src/api/hooks/useManufacturingStockAnalysis.ts`
- Test: `frontend/src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx`

- [ ] **Step 1: Write the failing tests**

Add this `describe` block to `useManufacturingStockAnalysis.test.tsx` (at the top level, after the existing `describe("calculateTimePeriodRange", ...)` block). Also add `formatWarehouseStock` to the import from `../useManufacturingStockAnalysis`:

```typescript
describe("formatWarehouseStock", () => {
  const baseItem = {
    code: "P1",
    name: "Product 1",
    currentStock: 0,
    erpStock: 0,
    eshopStock: 0,
    transportStock: 0,
    manufacturedStock: 0,
    primaryStockSource: "Erp",
    reserve: 0,
    quarantine: 0,
    planned: 0,
    salesInPeriod: 0,
    dailySalesRate: 0,
    optimalDaysSetup: 0,
    stockDaysAvailable: 0,
    minimumStock: 0,
    overstockPercentage: 0,
    batchSize: "1",
    severity: "Adequate",
    isConfigured: true,
  } as any;

  it("shows only the total when transport and manufactured are both zero", () => {
    const item = { ...baseItem, currentStock: 5, erpStock: 5 };
    expect(formatWarehouseStock(item)).toBe("5");
  });

  it("shows primary+transport breakdown when only transport is non-zero", () => {
    const item = { ...baseItem, currentStock: 12, erpStock: 5, transportStock: 7 };
    expect(formatWarehouseStock(item)).toBe("12 (5+7)");
  });

  it("shows primary+manufactured breakdown when only manufactured is non-zero", () => {
    const item = { ...baseItem, currentStock: 8, erpStock: 5, manufacturedStock: 3 };
    expect(formatWarehouseStock(item)).toBe("8 (5+3)");
  });

  it("shows primary+transport+manufactured breakdown when both are non-zero", () => {
    const item = {
      ...baseItem,
      currentStock: 15,
      erpStock: 5,
      transportStock: 7,
      manufacturedStock: 3,
    };
    expect(formatWarehouseStock(item)).toBe("15 (5+7+3)");
  });
});
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `cd frontend && npx jest src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx -t "formatWarehouseStock"`
Expected: FAIL — manufactured-only and combined cases return `"8"` / `"15 (5+7)"` instead of including the manufactured part.

- [ ] **Step 3: Add `manufacturedStock` to the `ManufacturingStockItemDto` interface**

In `useManufacturingStockAnalysis.ts`, add to the `ManufacturingStockItemDto` interface after `transportStock: number;` (line 75):

```typescript
  manufacturedStock: number;
```

- [ ] **Step 4: Update `formatWarehouseStock`**

In `useManufacturingStockAnalysis.ts`, replace the entire `formatWarehouseStock` function (lines 239-256) with:

```typescript
// Helper function to format warehouse stock with transport + manufactured breakdown
export const formatWarehouseStock = (item: ManufacturingStockItemDto): string => {
  const totalStock = formatNumber(item.currentStock, 0);
  const transport = item.transportStock ?? 0;
  const manufactured = item.manufacturedStock ?? 0;

  // If there are no secondary parts, show just the total
  if (transport === 0 && manufactured === 0) {
    return totalStock;
  }

  // Otherwise show breakdown: "15 (5+7+3)" = total (primary+transport+manufactured)
  const primaryStock =
    item.primaryStockSource === "Erp"
      ? formatNumber(item.erpStock, 0)
      : formatNumber(item.eshopStock, 0);

  const parts = [primaryStock];
  if (transport !== 0) {
    parts.push(formatNumber(transport, 0));
  }
  if (manufactured !== 0) {
    parts.push(formatNumber(manufactured, 0));
  }

  return `${totalStock} (${parts.join("+")})`;
};
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `cd frontend && npx jest src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx`
Expected: PASS — all tests in the file pass.

- [ ] **Step 6: Commit**

```bash
git add frontend/src/api/hooks/useManufacturingStockAnalysis.ts frontend/src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx
git commit -m "feat: include manufactured stock in SKLAD breakdown"
```

---

## Task 8: Add the "Vyrobeno" column to the analysis table

**Files:**
- Modify: `frontend/src/components/pages/ManufacturingStockAnalysis.tsx`
- Test: `frontend/src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx`

- [ ] **Step 1: Write the failing test**

In `ManufacturingStockAnalysis.test.tsx`, the hook is mocked. First, update the mocked `formatWarehouseStock` (in the `jest.mock("../../../api/hooks/useManufacturingStockAnalysis", ...)` block) so it includes manufactured stock — replace the existing `formatWarehouseStock` mock function with:

```javascript
  formatWarehouseStock: (item: any) => {
    const totalStock = (item.currentStock || 0).toLocaleString("cs-CZ");
    const transport = item.transportStock || 0;
    const manufactured = item.manufacturedStock || 0;
    if (transport === 0 && manufactured === 0) {
      return totalStock;
    }
    const erpStock = item.erpStock || 0;
    const eshopStock = item.eshopStock || 0;
    const primaryStock =
      item.primaryStockSource === "Erp"
        ? erpStock.toLocaleString("cs-CZ")
        : eshopStock.toLocaleString("cs-CZ");
    const parts = [primaryStock];
    if (transport !== 0) parts.push(transport.toLocaleString("cs-CZ"));
    if (manufactured !== 0) parts.push(manufactured.toLocaleString("cs-CZ"));
    return `${totalStock} (${parts.join("+")})`;
  },
```

Then add this test (place it near the other rendering tests, inside the top-level `describe` block):

```javascript
it("renders the Vyrobeno column with the manufactured stock value", () => {
  mockUseManufacturingStockAnalysisQuery.mockReturnValue({
    data: {
      items: [
        {
          code: "PROD001",
          name: "Product One",
          currentStock: 112,
          erpStock: 100,
          eshopStock: 0,
          transportStock: 0,
          manufacturedStock: 12,
          primaryStockSource: "Erp",
          reserve: 0,
          quarantine: 0,
          planned: 0,
          salesInPeriod: 50,
          dailySalesRate: 2,
          optimalDaysSetup: 20,
          stockDaysAvailable: 56,
          minimumStock: 10,
          overstockPercentage: 280,
          batchSize: "25",
          productFamily: "PROD00",
          severity: "Adequate",
          isConfigured: true,
        },
      ],
      summary: {
        totalProducts: 1,
        criticalCount: 0,
        majorCount: 0,
        minorCount: 0,
        adequateCount: 1,
        unconfiguredCount: 0,
        analysisPeriodStart: "2023-01-01T00:00:00Z",
        analysisPeriodEnd: "2023-03-31T23:59:59Z",
        productFamilies: ["PROD00"],
      },
      totalCount: 1,
      pageNumber: 1,
      pageSize: 20,
    },
    isLoading: false,
    error: null,
  });

  render(
    <BrowserRouter>
      <QueryClientProvider client={new QueryClient()}>
        <ToastProvider>
          <PlanningListProvider>
            <ManufacturingStockAnalysis />
          </PlanningListProvider>
        </ToastProvider>
      </QueryClientProvider>
    </BrowserRouter>,
  );

  expect(screen.getByText("Vyrobeno")).toBeInTheDocument();
  expect(screen.getByText("12")).toBeInTheDocument();
});
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `cd frontend && npx jest src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx -t "Vyrobeno"`
Expected: FAIL — no "Vyrobeno" column header exists.

- [ ] **Step 3: Add the `vyrobeno` column**

In `ManufacturingStockAnalysis.tsx`, in the `columns` `useMemo` array, add this column object immediately after the `currentStock` column (which ends at line 343, after its closing `},`) and before the `reserve` column:

```tsx
    {
      id: 'vyrobeno',
      header: 'Vyrobeno',
      align: 'right',
      minWidth: 60,
      defaultWidth: 120,
      cellClassName: 'text-xs text-gray-900',
      renderCell: (item) =>
        (item.manufacturedStock || 0) > 0 ? (
          <div className="font-bold">{formatNumber(item.manufacturedStock, 0)}</div>
        ) : (
          <span className="text-gray-400">—</span>
        ),
    },
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `cd frontend && npx jest src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx`
Expected: PASS — all tests in the file pass.

- [ ] **Step 5: Commit**

```bash
git add frontend/src/components/pages/ManufacturingStockAnalysis.tsx frontend/src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx
git commit -m "feat: add Vyrobeno column to product stock analysis"
```

---

## Final Verification

- [ ] **Backend build and format**

Run: `dotnet build backend/Anela.Heblo.sln` — Expected: Build succeeded.
Run: `dotnet format backend/Anela.Heblo.sln --verify-no-changes` (or `dotnet format` to apply) — Expected: no formatting issues.

- [ ] **Backend tests**

Run: `dotnet test backend/test/Anela.Heblo.Tests` — Expected: all tests pass.

- [ ] **Frontend build and lint**

Run: `cd frontend && npm run build` — Expected: build succeeds.
Run: `cd frontend && npm run lint` — Expected: no lint errors.

- [ ] **Frontend tests**

Run: `cd frontend && npx jest src/api/hooks/__tests__/useManufacturingStockAnalysis.test.tsx src/components/pages/__tests__/ManufacturingStockAnalysis.test.tsx` — Expected: all tests pass.

- [ ] **End-to-end check**

Run the app, open "Řízení zásob výrobků". Confirm:
- a "Vyrobeno" column sits immediately after "Sklad" and shows Sklad výroby quantities (dash when zero);
- the "Sklad" cell breakdown includes the manufactured part for products that have one (e.g. `15 (5+7+3)`);
- for a product with Sklad výroby stock, the NS (stock days) and NS % values are higher than they would be without it — consistent with how Transport already contributes.
