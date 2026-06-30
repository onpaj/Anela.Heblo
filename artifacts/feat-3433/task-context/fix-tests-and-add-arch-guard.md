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
