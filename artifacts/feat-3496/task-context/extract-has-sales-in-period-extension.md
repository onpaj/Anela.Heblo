### task: extract-has-sales-in-period-extension

**Files:**
- Create: `backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProductExtensions.cs`
- Create: `backend/test/Anela.Heblo.Tests/Features/Analytics/AnalyticsProductExtensionsTests.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs`
- Test (must still pass unmodified): `backend/test/Anela.Heblo.Tests/Features/Analytics/GetMarginReportHandlerTests.cs`
- Test (must still pass unmodified): `backend/test/Anela.Heblo.Tests/Features/Analytics/GetProductMarginAnalysisHandlerTests.cs`

- [ ] **Step 1: Write the failing unit test for `AnalyticsProductExtensions.HasSalesInPeriod`**

  Create `backend/test/Anela.Heblo.Tests/Features/Analytics/AnalyticsProductExtensionsTests.cs` with the following content:

  ```csharp
  using System;
  using System.Collections.Generic;
  using Anela.Heblo.Domain.Features.Analytics;
  using FluentAssertions;
  using Xunit;

  namespace Anela.Heblo.Tests.Features.Analytics;

  public class AnalyticsProductExtensionsTests
  {
      private static AnalyticsProduct CreateProduct(List<SalesDataPoint> salesHistory)
      {
          return new AnalyticsProduct
          {
              ProductCode = "PROD001",
              ProductName = "Test Product",
              Type = AnalyticsProductType.Product,
              MarginAmount = 0m,
              SalesHistory = salesHistory
          };
      }

      [Fact]
      public void HasSalesInPeriod_SaleWithinRange_ReturnsTrue()
      {
          var product = CreateProduct(new List<SalesDataPoint>
          {
              new() { Date = new DateTime(2024, 6, 15), AmountB2B = 1, AmountB2C = 0 }
          });

          var result = product.HasSalesInPeriod(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31));

          result.Should().BeTrue();
      }

      [Fact]
      public void HasSalesInPeriod_SaleExactlyOnStartDate_ReturnsTrue()
      {
          var startDate = new DateTime(2024, 1, 1);
          var product = CreateProduct(new List<SalesDataPoint>
          {
              new() { Date = startDate, AmountB2B = 1, AmountB2C = 0 }
          });

          var result = product.HasSalesInPeriod(startDate, new DateTime(2024, 12, 31));

          result.Should().BeTrue();
      }

      [Fact]
      public void HasSalesInPeriod_SaleExactlyOnEndDate_ReturnsTrue()
      {
          var endDate = new DateTime(2024, 12, 31);
          var product = CreateProduct(new List<SalesDataPoint>
          {
              new() { Date = endDate, AmountB2B = 1, AmountB2C = 0 }
          });

          var result = product.HasSalesInPeriod(new DateTime(2024, 1, 1), endDate);

          result.Should().BeTrue();
      }

      [Fact]
      public void HasSalesInPeriod_SaleBeforeStartDate_ReturnsFalse()
      {
          var product = CreateProduct(new List<SalesDataPoint>
          {
              new() { Date = new DateTime(2023, 12, 31), AmountB2B = 1, AmountB2C = 0 }
          });

          var result = product.HasSalesInPeriod(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31));

          result.Should().BeFalse();
      }

      [Fact]
      public void HasSalesInPeriod_SaleAfterEndDate_ReturnsFalse()
      {
          var product = CreateProduct(new List<SalesDataPoint>
          {
              new() { Date = new DateTime(2025, 1, 1), AmountB2B = 1, AmountB2C = 0 }
          });

          var result = product.HasSalesInPeriod(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31));

          result.Should().BeFalse();
      }

      [Fact]
      public void HasSalesInPeriod_EmptySalesHistory_ReturnsFalse()
      {
          var product = CreateProduct(new List<SalesDataPoint>());

          var result = product.HasSalesInPeriod(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31));

          result.Should().BeFalse();
      }

      [Fact]
      public void HasSalesInPeriod_OneSaleInRangeAmongOthersOutOfRange_ReturnsTrue()
      {
          var product = CreateProduct(new List<SalesDataPoint>
          {
              new() { Date = new DateTime(2023, 5, 1), AmountB2B = 1, AmountB2C = 0 },
              new() { Date = new DateTime(2024, 6, 1), AmountB2B = 1, AmountB2C = 0 },
              new() { Date = new DateTime(2025, 5, 1), AmountB2B = 1, AmountB2C = 0 }
          });

          var result = product.HasSalesInPeriod(new DateTime(2024, 1, 1), new DateTime(2024, 12, 31));

          result.Should().BeTrue();
      }
  }
  ```

  Run:
  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AnalyticsProductExtensionsTests"
  ```

  **Confirm fail:** the build fails with a compiler error (`CS1061: 'AnalyticsProduct' does not contain a definition for 'HasSalesInPeriod'`) because the extension method does not exist yet.

- [ ] **Step 2: Create the `AnalyticsProductExtensions` class**

  Create `backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProductExtensions.cs`:

  ```csharp
  namespace Anela.Heblo.Domain.Features.Analytics;

  public static class AnalyticsProductExtensions
  {
      public static bool HasSalesInPeriod(this AnalyticsProduct product, DateTime startDate, DateTime endDate)
          => product.SalesHistory.Any(s => s.Date >= startDate && s.Date <= endDate);
  }
  ```

  Run:
  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~AnalyticsProductExtensionsTests"
  ```

  **Confirm pass:** all 7 tests in `AnalyticsProductExtensionsTests` pass.

- [ ] **Step 3: Commit the new extension and its tests**

  ```bash
  git add backend/src/Anela.Heblo.Domain/Features/Analytics/AnalyticsProductExtensions.cs backend/test/Anela.Heblo.Tests/Features/Analytics/AnalyticsProductExtensionsTests.cs
  git commit -m "Add AnalyticsProductExtensions.HasSalesInPeriod with unit tests"
  ```

- [ ] **Step 4: Update `GetMarginReportHandler.cs` to call the extension and delete its private duplicate**

  In `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs`:

  Change line 95 from:
  ```csharp
              if (!HasSalesInPeriod(product, startDate, endDate))
  ```
  to:
  ```csharp
              if (!product.HasSalesInPeriod(startDate, endDate))
  ```

  Delete lines 125-128 (the private method):
  ```csharp
      private static bool HasSalesInPeriod(Domain.Features.Analytics.AnalyticsProduct product, DateTime startDate, DateTime endDate)
      {
          return product.SalesHistory.Any(s => s.Date >= startDate && s.Date <= endDate);
      }

  ```
  (remove the entire method including its trailing blank line, so `AccumulateCategoryTotals` follows directly after `ProcessProductsForReport`'s closing brace with the same blank-line spacing as the rest of the file).

  No `using` changes needed — `using Anela.Heblo.Domain.Features.Analytics;` is already present at line 5.

- [ ] **Step 5: Update `GetProductMarginAnalysisHandler.cs` to call the extension and delete its private duplicate**

  In `backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs`:

  Change line 51 from:
  ```csharp
              if (!HasSalesInPeriod(productData, request.StartDate, request.EndDate))
  ```
  to:
  ```csharp
              if (!productData.HasSalesInPeriod(request.StartDate, request.EndDate))
  ```

  Delete lines 71-74 (the private method):
  ```csharp
      private static bool HasSalesInPeriod(AnalyticsProduct productData, DateTime startDate, DateTime endDate)
      {
          return productData.SalesHistory.Any(s => s.Date >= startDate && s.Date <= endDate);
      }

  ```
  (remove the entire method including its trailing blank line, so `BuildSuccessResponse` follows directly after `Handle`'s closing brace with the same blank-line spacing as the rest of the file).

  No `using` changes needed — `using Anela.Heblo.Domain.Features.Analytics;` is already present at line 4.

- [ ] **Step 6: Run the full Analytics test suite to confirm no regression**

  ```bash
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Analytics"
  ```

  **Confirm pass:** `GetMarginReportHandlerTests`, `GetProductMarginAnalysisHandlerTests`, and `AnalyticsProductExtensionsTests` all pass unmodified — no assertions needed to change since `HasSalesInPeriod`'s behavior is identical to the removed private methods.

- [ ] **Step 7: Build and format the whole solution**

  ```bash
  cd backend && dotnet build && dotnet format --verify-no-changes
  ```

  **Confirm pass:** build succeeds with no errors/warnings about unused usings or dead code, and `dotnet format` reports no changes needed (or run `dotnet format` without `--verify-no-changes` and re-check `git diff` only touches intended lines if it reformats anything).

- [ ] **Step 8: Commit the handler changes**

  ```bash
  git add backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetMarginReport/GetMarginReportHandler.cs backend/src/Anela.Heblo.Application/Features/Analytics/UseCases/GetProductMarginAnalysis/GetProductMarginAnalysisHandler.cs
  git commit -m "Replace duplicated private HasSalesInPeriod methods with AnalyticsProductExtensions call"
  ```
