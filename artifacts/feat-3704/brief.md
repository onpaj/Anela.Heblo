## Module
Purchase

## Finding
`GetPurchaseStockAnalysisHandler` (`backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisHandler.cs`) is 238 lines and contains two private business-logic methods embedded directly in the handler:

- **`CalculateStockEfficiency`** (line 137) — computes a percentage based on available stock vs. min/optimal thresholds
- **`CalculateRecommendedOrderQuantity`** (line 166) — computes how many units to order to reach optimal stock, with MOQ rounding

These are domain calculations, not orchestration. The handler's job is to load data, delegate to services, and shape the response — not to implement business algorithms.

The pattern precedent is clear: `IStockSeverityCalculator` (`Application/Features/Purchase/Services/IStockSeverityCalculator.cs`) was already extracted into its own injectable service for exactly this reason. The same handler calls it via `_stockSeverityCalculator.DetermineStockSeverity(...)` at line 103. The two remaining calculations were not given the same treatment.

The `SortItems` method (line 207) adds further bulk — it is essentially a projection function that could live on the request or a helper, keeping the handler focused on orchestration.

## Why it matters
- **SRP**: the handler simultaneously orchestrates (load, filter, page, summarise) and calculates (efficiency, recommended quantity). These concerns evolve independently — a business rule change to how efficiency is computed requires touching the handler, not the calculation service.
- **Testability**: `CalculateStockEfficiency` and `CalculateRecommendedOrderQuantity` are not independently testable. The only test coverage they can get is through the full handler test, which must wire up `IMaterialCatalogService`, `IStockSeverityCalculator`, and a logger just to assert on a percentage. The already-extracted `StockSeverityCalculatorTests.cs` shows what targeted testing looks like.
- **Inconsistency**: having `IStockSeverityCalculator` extracted but not the efficiency/recommendation calculations creates an asymmetry that future developers will not know to follow.

## Suggested fix
Extract `CalculateStockEfficiency` and `CalculateRecommendedOrderQuantity` into `IStockAnalysisCalculator` (new interface alongside `IStockSeverityCalculator` in `Application/Features/Purchase/Services/`):

```csharp
public interface IStockAnalysisCalculator
{
    double CalculateStockEfficiency(double availableStock, double minStock, double optimalStock);
    double? CalculateRecommendedOrderQuantity(double availableStock, double optimalStock, double minStock, string moq);
}
```

Register in `PurchaseModule`. Inject into the handler. The handler's private implementations are replaced by single-line delegations. Both methods become independently unit-testable — following the exact pattern of `StockSeverityCalculatorTests.cs`.

`SortItems` can remain in the handler or be inlined as a LINQ expression; it is not business logic but it contributes to handler length.

---
_Filed by daily arch-review routine on 2026-07-19._
