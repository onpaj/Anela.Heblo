# Implementation Plan: Move StockValueService to Catalog Module as Cross-Module Adapter

## Context

`StockValueService` currently lives in `FinancialOverview.Application.Services` but injects
two Catalog-owned domain interfaces (`IErpStockClient`, `IProductPriceErpClient`), violating
the module boundary rule. This plan moves it into `Catalog.Application.Infrastructure` as
`FinancialOverviewStockValueAdapter`, re-wires DI, and adds an architecture guard to prevent
regression.

Pattern reference: `CatalogManufactureCatalogSourceAdapter` and `KnowledgeBaseLeafletSourceAdapter`
use the same approach.

---

### task: create-adapter-and-rewire-di

**Goal:** Create the adapter file in Catalog.Infrastructure, register it in CatalogModule, and
remove the misplaced registration and source file from FinancialOverviewModule.

**Files affected:**
- CREATE `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs`
- MODIFY `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`
- MODIFY `backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialOverviewModule.cs`
- DELETE `backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/StockValueService.cs`

**Steps:**

- [ ] Create the adapter file. The body is identical to `StockValueService` except for the class
  name, access modifier, and logger type parameter. Create
  `backend/src/Anela.Heblo.Application/Features/Catalog/Infrastructure/FinancialOverviewStockValueAdapter.cs`
  with this exact content:

  ```csharp
  using Anela.Heblo.Domain.Features.Catalog.Price;
  using Anela.Heblo.Domain.Features.Catalog.Stock;
  using Anela.Heblo.Domain.Features.FinancialOverview;
  using Microsoft.Extensions.Logging;

  namespace Anela.Heblo.Application.Features.Catalog.Infrastructure;

  /// <summary>
  /// Cross-module adapter: Catalog implements FinancialOverview's IStockValueService
  /// using Catalog-owned ERP clients (IErpStockClient, IProductPriceErpClient).
  /// DI registration is owned by the provider (Catalog), not the consumer (FinancialOverview).
  /// </summary>
  internal sealed class FinancialOverviewStockValueAdapter : IStockValueService
  {
      private readonly IErpStockClient _stockClient;
      private readonly IProductPriceErpClient _priceClient;
      private readonly ILogger<FinancialOverviewStockValueAdapter> _logger;

      // Warehouse IDs from FlexiStockClient
      private const int MaterialWarehouseId = 5;    // MATERIAL
      private const int SemiProductsWarehouseId = 20; // POLOTOVARY
      private const int ProductsWarehouseId = 4;    // ZBOZI

      public FinancialOverviewStockValueAdapter(
          IErpStockClient stockClient,
          IProductPriceErpClient priceClient,
          ILogger<FinancialOverviewStockValueAdapter> logger)
      {
          _stockClient = stockClient;
          _priceClient = priceClient;
          _logger = logger;
      }

      public async Task<IReadOnlyList<MonthlyStockChange>> GetStockValueChangesAsync(
          DateTime startDate,
          DateTime endDate,
          CancellationToken cancellationToken)
      {
          _logger.LogInformation("Calculating stock value changes from {StartDate} to {EndDate}",
              startDate, endDate);

          try
          {
              // Get all product prices for value calculations
              var prices = await _priceClient.GetAllAsync(forceReload: false, cancellationToken);
              var priceDict = prices.ToDictionary(p => p.ProductCode, p => p.PurchasePrice);

              var monthlyChanges = new List<MonthlyStockChange>();

              // Process each month in the date range
              var currentDate = new DateTime(startDate.Year, startDate.Month, 1);
              var endMonth = new DateTime(endDate.Year, endDate.Month, 1);

              while (currentDate <= endMonth)
              {
                  _logger.LogDebug("Processing stock changes for {Year}/{Month}", currentDate.Year, currentDate.Month);

                  var monthlyChange = await CalculateMonthlyStockChangeAsync(
                      currentDate, priceDict, cancellationToken);

                  monthlyChanges.Add(monthlyChange);

                  // Move to next month
                  currentDate = currentDate.AddMonths(1);
              }

              _logger.LogInformation("Successfully calculated stock value changes for {MonthCount} months",
                  monthlyChanges.Count);

              return monthlyChanges;
          }
          catch (Exception ex)
          {
              _logger.LogError(ex, "Error calculating stock value changes from {StartDate} to {EndDate}",
                  startDate, endDate);
              throw;
          }
      }

      private async Task<MonthlyStockChange> CalculateMonthlyStockChangeAsync(
          DateTime monthStart,
          Dictionary<string, decimal> priceDict,
          CancellationToken cancellationToken)
      {
          var monthEnd = monthStart.AddMonths(1).AddDays(-1);

          // Get stock values at start and end of month for each warehouse
          var startStockTasks = new[]
          {
              GetWarehouseStockValueAsync(MaterialWarehouseId, monthStart, priceDict, cancellationToken),
              GetWarehouseStockValueAsync(SemiProductsWarehouseId, monthStart, priceDict, cancellationToken),
              GetWarehouseStockValueAsync(ProductsWarehouseId, monthStart, priceDict, cancellationToken)
          };

          var endStockTasks = new[]
          {
              GetWarehouseStockValueAsync(MaterialWarehouseId, monthEnd, priceDict, cancellationToken),
              GetWarehouseStockValueAsync(SemiProductsWarehouseId, monthEnd, priceDict, cancellationToken),
              GetWarehouseStockValueAsync(ProductsWarehouseId, monthEnd, priceDict, cancellationToken)
          };

          await Task.WhenAll(startStockTasks.Concat(endStockTasks));

          var startValues = await Task.WhenAll(startStockTasks);
          var endValues = await Task.WhenAll(endStockTasks);

          // Calculate changes (end - start for each warehouse)
          var materialsChange = endValues[0] - startValues[0];
          var semiProductsChange = endValues[1] - startValues[1];
          var productsChange = endValues[2] - startValues[2];

          return new MonthlyStockChange
          {
              Year = monthStart.Year,
              Month = monthStart.Month,
              StockChanges = new StockChangeByType
              {
                  Materials = materialsChange,
                  SemiProducts = semiProductsChange,
                  Products = productsChange
              }
          };
      }

      private async Task<decimal> GetWarehouseStockValueAsync(
          int warehouseId,
          DateTime date,
          Dictionary<string, decimal> priceDict,
          CancellationToken cancellationToken)
      {
          try
          {
              var stockItems = await _stockClient.StockToDateAsync(date, warehouseId, cancellationToken);

              decimal totalValue = 0;
              var processedItems = 0;
              var missingPrices = 0;

              foreach (var item in stockItems)
              {
                  if (priceDict.TryGetValue(item.ProductCode, out var purchasePrice))
                  {
                      totalValue += item.Stock * purchasePrice;
                      processedItems++;
                  }
                  else
                  {
                      missingPrices++;
                      _logger.LogDebug("No purchase price found for product {ProductCode} in warehouse {WarehouseId}",
                          item.ProductCode, warehouseId);
                  }
              }

              _logger.LogDebug("Warehouse {WarehouseId} on {Date}: {ProcessedItems} items, {MissingPrices} missing prices, value: {TotalValue:C}",
                  warehouseId, date.ToShortDateString(), processedItems, missingPrices, totalValue);

              return totalValue;
          }
          catch (Exception ex)
          {
              _logger.LogWarning(ex, "Error getting stock value for warehouse {WarehouseId} on {Date}",
                  warehouseId, date);
              return 0; // Return 0 if we can't get the data for this warehouse/date
          }
      }
  }
  ```

- [ ] Register the adapter in `CatalogModule.cs`. Add a `using` for the FinancialOverview domain
  and the `AddScoped` call. In
  `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`, after the existing
  cross-module comment block (after line 70, after the `IPackingProductSource` registration):

  Add to the `using` block at the top of the file (after line 37, with the other usings):
  ```csharp
  using Anela.Heblo.Domain.Features.FinancialOverview;
  ```

  Add inside `AddCatalogModule()` after the `IPackingProductSource` registration (after line 71):
  ```csharp
  // Cross-module contract: Catalog implements FinancialOverview's IStockValueService via adapter.
  // DI registration is owned by the provider (Catalog), not the consumer (FinancialOverview).
  services.AddScoped<IStockValueService, FinancialOverviewStockValueAdapter>();
  ```

- [ ] Remove the misplaced registration from `FinancialOverviewModule.cs`. In
  `backend/src/Anela.Heblo.Application/Features/FinancialOverview/FinancialOverviewModule.cs`:

  Remove line 1:
  ```csharp
  using Anela.Heblo.Application.Features.FinancialOverview.Services;
  ```

  Remove line 16:
  ```csharp
  services.AddScoped<IStockValueService, StockValueService>();
  ```

  After the edit the file should have these remaining `using` statements at the top:
  ```csharp
  using Anela.Heblo.Xcc.Services.BackgroundRefresh;
  using Anela.Heblo.Domain.Features.FinancialOverview;
  using Microsoft.Extensions.DependencyInjection;
  using Microsoft.Extensions.Configuration;
  ```

- [ ] Delete the old service file:
  ```
  backend/src/Anela.Heblo.Application/Features/FinancialOverview/Services/StockValueService.cs
  ```

- [ ] Verify the build compiles cleanly:
  ```bash
  cd backend && dotnet build
  ```
  Expected: zero errors. If the `Services/` folder is now empty, the deletion is sufficient —
  no further cleanup needed.

---

### task: fix-tests-and-add-arch-guard

**Goal:** Update the two `FinancialOverviewModuleTests` that reference `StockValueService` by
concrete type, then add the `ModuleBoundariesTests` rule that will prevent future regressions.

**Files affected:**
- MODIFY `backend/test/Anela.Heblo.Tests/Application/FinancialOverview/FinancialOverviewModuleTests.cs`
- MODIFY `backend/test/Anela.Heblo.Tests/Architecture/ModuleBoundariesTests.cs`

**Steps:**

- [ ] Update `FinancialOverviewModuleTests.cs`. Two tests assert `.BeOfType<StockValueService>()`:
  - Line 41: `stockValueService.Should().BeOfType<StockValueService>();`
  - Line 94: `stockValueService.Should().BeOfType<StockValueService>();`

  Both assertions must be deleted because `IStockValueService` is no longer registered by
  `FinancialOverviewModule` at all — it is now registered exclusively by `CatalogModule`.
  The tests that call `services.AddFinancialOverviewModule(...)` and then resolve
  `IStockValueService` will fail with `InvalidOperationException` unless mock dependencies
  are also provided through `CatalogModule`. Fixing the full DI chain for these tests is out
  of scope; the correct fix is to remove the concrete-type assertions and the
  `IStockValueService` resolution from the tests that only exercise `FinancialOverviewModule`.

  **In test `AddFinancialOverviewModule_RegistersServicesCorrectly` (lines 19-43):**

  Remove these lines entirely (they will fail because `IStockValueService` is not registered
  by `FinancialOverviewModule` anymore):
  ```csharp
  // Add required dependencies for StockValueService
  services.AddSingleton(Mock.Of<IErpStockClient>());
  services.AddSingleton(Mock.Of<IProductPriceErpClient>());
  ```
  and:
  ```csharp
  var stockValueService = serviceProvider.GetRequiredService<IStockValueService>();
  ```
  and:
  ```csharp
  stockValueService.Should().NotBeNull();
  ```
  and:
  ```csharp
  stockValueService.Should().BeOfType<StockValueService>();
  ```

  Also remove the unused `using Anela.Heblo.Application.Features.FinancialOverview.Services;`
  at line 2, the `using Anela.Heblo.Domain.Features.Catalog.Price;` at line 4, and
  `using Anela.Heblo.Domain.Features.Catalog.Stock;` at line 5 (all three imports existed
  solely to support `StockValueService` and its Catalog-owned dependencies).

  The resulting test body becomes:
  ```csharp
  [Fact]
  public void AddFinancialOverviewModule_RegistersServicesCorrectly()
  {
      // Arrange
      var services = new ServiceCollection();
      var configuration = CreateMockConfiguration();

      services.AddSingleton(Mock.Of<ILedgerService>());
      services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

      // Act
      services.AddFinancialOverviewModule(configuration);
      var serviceProvider = services.BuildServiceProvider();

      // Assert
      var financialAnalysisService = serviceProvider.GetRequiredService<IFinancialAnalysisService>();
      financialAnalysisService.Should().NotBeNull();
      financialAnalysisService.Should().BeOfType<FinancialAnalysisService>();
  }
  ```

  **In test `AddFinancialOverviewModule_RegistersDefaultRealService` (lines 76-95):**

  This test exists solely to verify `IStockValueService` resolves to `StockValueService`.
  Since that is no longer true (the service is owned by `CatalogModule`), delete the entire
  test method (lines 76-95).

  **In test `AddFinancialOverviewModule_CanOverrideStockValueService_ForTesting` (lines 46-73):**

  Remove the two dependency-setup lines that existed for `StockValueService`:
  ```csharp
  services.AddSingleton(Mock.Of<IErpStockClient>());
  services.AddSingleton(Mock.Of<IProductPriceErpClient>());
  ```
  The test exercises override behaviour after calling `AddFinancialOverviewModule`, but since
  the module no longer registers `IStockValueService`, `stockValueDescriptor` will be `null`
  and `services.Remove` is a no-op — the test still passes because the stub is added
  unconditionally afterwards. Keep the rest of the test intact.

  **In test `AddFinancialOverviewModule_RegistersIStockValueService_WithoutBuildServiceProviderAntipattern`
  (lines 139-161):**

  Remove the `IStockValueService` resolution attempt. The three dependency-setup lines must
  also be removed:
  ```csharp
  services.AddSingleton(Mock.Of<IErpStockClient>());
  services.AddSingleton(Mock.Of<IProductPriceErpClient>());
  services.AddSingleton(Mock.Of<ILedgerService>());
  ```
  Change the body of the lambda to just verify `AddFinancialOverviewModule` doesn't throw
  and `IFinancialAnalysisService` can be resolved (to keep the antipattern guard meaningful):
  ```csharp
  var exception = Record.Exception(() =>
  {
      services.AddSingleton(Mock.Of<ILedgerService>());
      services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
      services.AddFinancialOverviewModule(CreateMockConfiguration());
      var serviceProvider = services.BuildServiceProvider();
      var financialAnalysisService = serviceProvider.GetRequiredService<IFinancialAnalysisService>();
      financialAnalysisService.Should().NotBeNull();
  });

  exception.Should().BeNull();
  ```

  **In tests `AddFinancialOverviewModule_RegistersRefreshTasks_ForBackgroundDataRefresh`
  (lines 115-136):**

  Remove these three lines (StockValueService dependencies no longer needed):
  ```csharp
  services.AddSingleton(Mock.Of<IErpStockClient>());
  services.AddSingleton(Mock.Of<IProductPriceErpClient>());
  services.AddSingleton(Mock.Of<ILedgerService>());
  ```

  The test only checks hosted service absence, so no other changes are needed.

  After all edits, the `using` block at the top of `FinancialOverviewModuleTests.cs` should
  be reduced to only what is actually used:
  ```csharp
  using Anela.Heblo.Application.Features.FinancialOverview;
  using Anela.Heblo.Domain.Features.FinancialOverview;
  using FluentAssertions;
  using Microsoft.Extensions.Configuration;
  using Microsoft.Extensions.DependencyInjection;
  using Microsoft.Extensions.Logging;
  using Microsoft.Extensions.Logging.Abstractions;
  using Moq;
  ```

- [ ] Add the `FinancialOverview -> Catalog` boundary rule to `ModuleBoundariesTests.cs`.
  Find the closing brace of the `Rules()` method's `TheoryData` initializer — currently after
  the `"ShoptetApi Adapters -> Logistics"` rule (line 620). Insert the new rule before the
  closing `};` of the `TheoryData`:

  ```csharp
  new ModuleBoundaryRule(
      Name: "FinancialOverview -> Catalog",
      InspectedNamespacePrefix: "Anela.Heblo.Application.Features.FinancialOverview",
      ForbiddenNamespacePrefixes: new[]
      {
          "Anela.Heblo.Domain.Features.Catalog",
          "Anela.Heblo.Application.Features.Catalog",
          "Anela.Heblo.Persistence.Catalog",
      },
      Allowlist: new HashSet<string>(StringComparer.Ordinal)),
  ```

  Note: no allowlist field is needed because the violation (StockValueService referencing
  Catalog-owned types) is being fixed in this same PR — the FinancialOverview namespace will
  be clean after task 1.

- [ ] Run the full test suite to confirm everything passes:
  ```bash
  cd backend && dotnet test
  ```
  Expected: all architecture tests pass (the new `FinancialOverview -> Catalog` rule finds
  zero violations), all `FinancialOverviewModuleTests` pass (no more `StockValueService`
  references), and no other tests regressed.

- [ ] Run formatter to keep CI green:
  ```bash
  cd backend && dotnet format
  ```
