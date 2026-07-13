## Module
Logistics / GiftPackageManufacture

## Finding
`backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs`

The same ~10-line block that derives `dailySales`, `suggestedQuantity`, `severity`, and `stockCoveragePercent` from a catalog product appears verbatim in two methods:

**`GetAvailableGiftPackagesAsync` (lines ~55–65):**
```csharp
var totalSalesInPeriod = (decimal)product.TotalSoldInPeriod * salesCoefficient;
var dailySales = totalSalesInPeriod / daysDiff;
var suggestedQuantity = (int)Math.Max(0, dailySales * product.OptimalStockDaysSetup);
var severity = CalculateSeverity(
    (int)product.AvailableStock, suggestedQuantity, product.StockMinSetup);
var stockCoveragePercent = CalculateStockCoveragePercent(
    (int)product.AvailableStock, dailySales, product.OptimalStockDaysSetup);
```

**`GetGiftPackageDetailAsync` (lines ~106–121):**
```csharp
var totalSalesInPeriod = (decimal)product.TotalSoldInPeriod * salesCoefficient;
var dailySales = totalSalesInPeriod / daysDiff;
var suggestedQuantity = (int)Math.Max(0, dailySales * product.OptimalStockDaysSetup);
var severity = CalculateSeverity(
    (int)product.AvailableStock, suggestedQuantity, product.StockMinSetup);
var stockCoveragePercent = CalculateStockCoveragePercent(
    (int)product.AvailableStock, dailySales, product.OptimalStockDaysSetup);
```

The only difference between the two methods is that `GetGiftPackageDetailAsync` additionally loads BOM ingredients. The core DTO-mapping logic is identical.

## Why it matters
If the sales formula or severity thresholds ever change, the same fix must be applied in two places. The existing `CalculateSeverity` and `CalculateStockCoveragePercent` private helpers show the author already extracted shared calculations — the remaining duplication is inconsistent with that pattern.

## Suggested fix
Extract a private helper that takes the catalog item and the computed parameters and returns the metric fields, then call it from both methods:

```csharp
private (decimal dailySales, int suggestedQuantity, GiftPackageSeverity severity, decimal stockCoveragePercent)
    ComputePackageMetrics(LogisticsCatalogItem product, decimal salesCoefficient, int daysDiff)
{
    var dailySales = (decimal)product.TotalSoldInPeriod * salesCoefficient / daysDiff;
    var suggestedQuantity = (int)Math.Max(0, dailySales * product.OptimalStockDaysSetup);
    return (
        dailySales,
        suggestedQuantity,
        CalculateSeverity((int)product.AvailableStock, suggestedQuantity, product.StockMinSetup),
        CalculateStockCoveragePercent((int)product.AvailableStock, dailySales, product.OptimalStockDaysSetup)
    );
}
```

Replace the duplicated blocks in both public methods with a single call to `ComputePackageMetrics`.

---
_Filed by daily arch-review routine on 2026-07-12._
