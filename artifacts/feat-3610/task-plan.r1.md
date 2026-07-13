# Implementation Plan: Extract duplicated gift package metric calculation into a shared helper

### task: extract-compute-package-metrics-helper

#### Goal
Eliminate the duplicated ~10-line metric-calculation block (`dailySales`, `suggestedQuantity`, `severity`, `stockCoveragePercent`) that currently exists identically in both `GetAvailableGiftPackagesAsync` and `GetGiftPackageDetailAsync` in `GiftPackageManufactureService`. Extract it into a single private static helper method `ComputePackageMetrics`, call it from both places, and verify there is zero behavior change.

This is a pure refactor — no formula changes, no public signature changes, no DTO changes.

#### Files
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` (only file touched)

#### Approach

1. **Add the new private helper method**, placed in the private-helpers block near the bottom of the class (after `ResolveDateRange`, alongside `CalculateSeverity` and `CalculateStockCoveragePercent` — e.g. immediately before `CalculateSeverity`, around what is currently line 341):

```csharp
private static (decimal dailySales, int suggestedQuantity, GiftPackageSeverity severity, decimal stockCoveragePercent)
    ComputePackageMetrics(LogisticsCatalogItem product, decimal salesCoefficient, int daysDiff)
{
    var totalSalesInPeriod = (decimal)product.TotalSoldInPeriod * salesCoefficient;
    var dailySales = totalSalesInPeriod / daysDiff;
    var suggestedQuantity = (int)Math.Max(0, dailySales * product.OptimalStockDaysSetup);
    return (
        dailySales,
        suggestedQuantity,
        CalculateSeverity((int)product.AvailableStock, suggestedQuantity, product.StockMinSetup),
        CalculateStockCoveragePercent((int)product.AvailableStock, dailySales, product.OptimalStockDaysSetup)
    );
}
```

   Preserve the exact two-step arithmetic (`totalSalesInPeriod` then `dailySales`) — do not collapse into a single expression, to keep the diff minimal and match the spec exactly. Mark it `private static`, matching `CalculateSeverity`/`CalculateStockCoveragePercent`.

2. **Replace the duplicated block in `GetAvailableGiftPackagesAsync`** (currently lines 54–71, inside the `foreach (var product in setProducts)` loop): delete the inline computation of `totalSalesInPeriod`, `dailySales`, `suggestedQuantity`, `severity`, `stockCoveragePercent` (including their comments), and replace with:

```csharp
var (dailySales, suggestedQuantity, severity, stockCoveragePercent) =
    ComputePackageMetrics(product, salesCoefficient, daysDiff);
```

   The subsequent `GiftPackageDto` construction (lines 73–85 currently) is untouched — it already references `dailySales`, `suggestedQuantity`, `severity`, `stockCoveragePercent` by these exact names.

3. **Replace the duplicated block in `GetGiftPackageDetailAsync`** (currently lines 105–122, after the `product == null` check): delete the same inline computation and comments, replace with:

```csharp
var (dailySales, suggestedQuantity, severity, stockCoveragePercent) =
    ComputePackageMetrics(product, salesCoefficient, daysDiff);
```

   The subsequent `GiftPackageDto` construction (lines 125–137 currently, which also sets `Ingredients`) and all code after it (BOM/ingredient loading) is untouched.

4. **Do not touch**: `CreateManufactureAsync`, `DisassembleGiftPackageAsync`, `ResolveDateRange`, `CalculateSeverity`, `CalculateStockCoveragePercent` — leave these exactly as-is. `CalculateSeverity` and `CalculateStockCoveragePercent` are called *from* the new helper but their own bodies do not change.

5. Double-check no `using` changes are needed (all referenced types — `GiftPackageSeverity`, `LogisticsCatalogItem` — are already in scope in this file).

#### Verification

- Run the existing test suite scoped to this service:
  ```
  cd backend && dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureServiceTests"
  ```
  All tests in `backend/test/Anela.Heblo.Tests/Features/Logistics/GiftPackageManufactureServiceTests.cs` must pass unmodified — do not edit the test file.
- Run a full backend build to confirm no new warnings/errors:
  ```
  cd backend && dotnet build
  ```
- Run `dotnet format` (or the repo's configured formatting check) per CLAUDE.md validation requirements before declaring the task done.
- Manually diff the final file against the original to confirm the only changes are: the new `ComputePackageMetrics` method, and the two call-site replacements — no formula/arithmetic changes, no DTO or public signature changes.
