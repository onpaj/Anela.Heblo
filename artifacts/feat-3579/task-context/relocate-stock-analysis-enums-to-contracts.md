### task: relocate-stock-analysis-enums-to-contracts

**Files:**
- Create: `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockSeverity.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockStatusFilter.cs`
- Create: `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockAnalysisSortBy.cs`
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs:1-3,95-103`
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisRequest.cs:1-4,29-48`
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockSeverityCalculator.cs:1`
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockSeverityCalculator.cs:1`
- Modify: `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/LowStockEfficiencyTile.cs:1-3`
- Modify: `backend/test/Anela.Heblo.Tests/Features/Purchase/StockSeverityCalculatorTests.cs:1-5`
- Verify (no expected change): `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs:1-8`
- Verify (no expected change): `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs:1-11`
- Verify (no expected change): `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerDiacriticsTests.cs:1-10`

- [ ] **Step 1: Create `Contracts/StockSeverity.cs`**
  Create `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockSeverity.cs` with exactly this content (matches the file-scoped-namespace, one-enum-per-file style of `Contracts/MaterialProductType.cs`):
  ```csharp
  namespace Anela.Heblo.Application.Features.Purchase.Contracts;

  public enum StockSeverity
  {
      Critical,
      Low,
      Optimal,
      Overstocked,
      NotConfigured
  }
  ```

- [ ] **Step 2: Create `Contracts/StockStatusFilter.cs`**
  Create `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockStatusFilter.cs`:
  ```csharp
  namespace Anela.Heblo.Application.Features.Purchase.Contracts;

  public enum StockStatusFilter
  {
      All,
      Critical,
      Low,
      Optimal,
      Overstocked,
      NotConfigured
  }
  ```

- [ ] **Step 3: Create `Contracts/StockAnalysisSortBy.cs`**
  Create `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockAnalysisSortBy.cs`:
  ```csharp
  namespace Anela.Heblo.Application.Features.Purchase.Contracts;

  public enum StockAnalysisSortBy
  {
      ProductCode,
      ProductName,
      AvailableStock,
      Consumption,
      StockEfficiency,
      LastPurchaseDate
  }
  ```

- [ ] **Step 4: Strip `StockSeverity` out of `GetPurchaseStockAnalysisResponse.cs` and add the `Contracts` using**
  In `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs`:
  - Replace the current top (lines 1-3):
    ```csharp
    using Anela.Heblo.Application.Shared;

    namespace Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
    ```
    with:
    ```csharp
    using Anela.Heblo.Application.Features.Purchase.Contracts;
    using Anela.Heblo.Application.Shared;

    namespace Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
    ```
  - Delete the trailing enum block (current lines 95-103, i.e. the blank line before it and the enum itself):
    ```csharp

    public enum StockSeverity
    {
        Critical,
        Low,
        Optimal,
        Overstocked,
        NotConfigured
    }
    ```
    The file must now end with the closing `}` of `StockAnalysisSummaryDto` (currently line 94) and nothing after it. `StockAnalysisItemDto.Severity` (line 49) and `StockAnalysisSummaryDto`'s count properties keep their existing types unchanged — they now resolve `StockSeverity` via the new `using`.

- [ ] **Step 5: Strip `StockStatusFilter`/`StockAnalysisSortBy` out of `GetPurchaseStockAnalysisRequest.cs` and add the `Contracts` using**
  In `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisRequest.cs`:
  - Replace the current top (lines 1-4):
    ```csharp
    using System.ComponentModel.DataAnnotations;
    using MediatR;

    namespace Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
    ```
    with:
    ```csharp
    using System.ComponentModel.DataAnnotations;
    using Anela.Heblo.Application.Features.Purchase.Contracts;
    using MediatR;

    namespace Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
    ```
  - Delete the trailing enum block (current lines 29-48, i.e. the blank line before it and both enums):
    ```csharp

    public enum StockStatusFilter
    {
        All,
        Critical,
        Low,
        Optimal,
        Overstocked,
        NotConfigured
    }

    public enum StockAnalysisSortBy
    {
        ProductCode,
        ProductName,
        AvailableStock,
        Consumption,
        StockEfficiency,
        LastPurchaseDate
    }
    ```
    The file must now end with the closing `}` of the `GetPurchaseStockAnalysisRequest` class (currently line 28) and nothing after it. `StockStatus`/`SortBy` property declarations and their default-value expressions (`StockStatusFilter.All`, `StockAnalysisSortBy.StockEfficiency`) are unchanged in text — they now resolve via the new `using`.

- [ ] **Step 6: Update `Services/IStockSeverityCalculator.cs` using**
  In `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockSeverityCalculator.cs`, replace line 1:
  ```csharp
  using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
  ```
  with:
  ```csharp
  using Anela.Heblo.Application.Features.Purchase.Contracts;
  ```
  No other line in this file changes (the interface's sole external reference is `StockSeverity`, used as the return type of `DetermineStockSeverity`).

- [ ] **Step 7: Update `Services/StockSeverityCalculator.cs` using**
  In `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockSeverityCalculator.cs`, replace line 1:
  ```csharp
  using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
  ```
  with:
  ```csharp
  using Anela.Heblo.Application.Features.Purchase.Contracts;
  ```
  No other line in this file changes.

- [ ] **Step 8: Update `DashboardTiles/LowStockEfficiencyTile.cs` usings (keep both)**
  In `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/LowStockEfficiencyTile.cs`, replace the current top (lines 1-3):
  ```csharp
  using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
  using Anela.Heblo.Xcc.Services.Dashboard;
  using MediatR;
  ```
  with:
  ```csharp
  using Anela.Heblo.Application.Features.Purchase.Contracts;
  using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
  using Anela.Heblo.Xcc.Services.Dashboard;
  using MediatR;
  ```
  Do not remove the `UseCases.GetPurchaseStockAnalysis` using — this file still constructs `GetPurchaseStockAnalysisRequest` (line 36), which stays in that namespace; only `StockStatusFilter` (lines 38, 71) moves to `Contracts`.

- [ ] **Step 9: Update `StockSeverityCalculatorTests.cs` using (swap, don't add)**
  In `backend/test/Anela.Heblo.Tests/Features/Purchase/StockSeverityCalculatorTests.cs`, replace the current top (lines 1-5):
  ```csharp
  using Anela.Heblo.Application.Features.Purchase;
  using Anela.Heblo.Application.Features.Purchase.Services;
  using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;
  using Xunit;
  using FluentAssertions;
  ```
  with:
  ```csharp
  using Anela.Heblo.Application.Features.Purchase;
  using Anela.Heblo.Application.Features.Purchase.Contracts;
  using Anela.Heblo.Application.Features.Purchase.Services;
  using Xunit;
  using FluentAssertions;
  ```
  This file's only reference into the use-case namespace was `StockSeverity` (23 occurrences, all via `result.Should().Be(StockSeverity.X)`); after the move it references no other symbol from `UseCases.GetPurchaseStockAnalysis`, so that using is dropped entirely (not kept alongside `Contracts`). Do not change any test method body, assertion, or expected value.

- [ ] **Step 10: Verify (no change expected) the three files that already have both usings**
  Confirm these files compile unchanged because they already carry `using Anela.Heblo.Application.Features.Purchase.Contracts;` alongside `using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;`, and the use-case using remains required for other symbols (`GetPurchaseStockAnalysisRequest`, `GetPurchaseStockAnalysisResponse`, `GetPurchaseStockAnalysisHandler`) even after the enums move out:
  - `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs` (line 1 already `using Anela.Heblo.Application.Features.Purchase.Contracts;`)
  - `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerTests.cs` (line 1 `Contracts`, line 3 `UseCases.GetPurchaseStockAnalysis`)
  - `backend/test/Anela.Heblo.Tests/Features/Purchase/GetPurchaseStockAnalysisHandlerDiacriticsTests.cs` (line 1 `Contracts`, line 3 `UseCases.GetPurchaseStockAnalysis`)
  Make no edits to these three files unless the build in Step 11 reports an error in them.

- [ ] **Step 11: Full-repo grep, build, targeted test run, format, and commit**
  Run in order, all from the repo root:
  1. `grep -rn "UseCases.GetPurchaseStockAnalysis" backend/src backend/test --include="*.cs" | grep -E "StockSeverity|StockStatusFilter|StockAnalysisSortBy"` — expect **no output** (no remaining unqualified dependency on the old namespace for these three symbols specifically; the `GetPurchaseStockAnalysisHandlerTests.cs`/`GetPurchaseStockAnalysisHandlerDiacriticsTests.cs`/`LowStockEfficiencyTile.cs` using-lines for `UseCases.GetPurchaseStockAnalysis` are fine to still exist, they just must not be the only source of these three enums anymore).
  2. `dotnet build backend/Anela.Heblo.sln` — expect zero errors and no new warnings. If it fails, the error will name the exact file/line still missing a `using`; fix per the pattern in Steps 6-9 (do not touch enum bodies).
  3. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Anela.Heblo.Tests.Features.Purchase"` — expect all Purchase-module tests (including `StockSeverityCalculatorTests`, `GetPurchaseStockAnalysisHandlerTests`, `GetPurchaseStockAnalysisHandlerDiacriticsTests`) to pass with unchanged assertions.
  4. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj` — full test project run, to confirm no other test file outside `Features/Purchase/` was silently depending on the old namespace; expect all tests to pass.
  5. `dotnet format backend/Anela.Heblo.sln --verify-no-changes` — if it reports formatting diffs, run `dotnet format backend/Anela.Heblo.sln` and re-run `dotnet build` + the test commands above to confirm still green.
  6. Stage exactly the files listed above (three new + eight modified) and commit:
     ```
     git add \
       backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockSeverity.cs \
       backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockStatusFilter.cs \
       backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockAnalysisSortBy.cs \
       backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs \
       backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisRequest.cs \
       backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockSeverityCalculator.cs \
       backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockSeverityCalculator.cs \
       backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/LowStockEfficiencyTile.cs \
       backend/test/Anela.Heblo.Tests/Features/Purchase/StockSeverityCalculatorTests.cs
     git commit -m "refactor(purchase): relocate StockSeverity/StockStatusFilter/StockAnalysisSortBy enums to Contracts/"
     ```
