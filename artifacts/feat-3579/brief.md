## Module
Purchase

## Finding
`StockSeverity` is defined inside the use-case response file at:

```
backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs:97
```

but it is imported and used by the module's *Services* layer, which is supposed to be independent of individual use cases:

- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockSeverityCalculator.cs:1` — `using Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis;`
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockSeverityCalculator.cs:1` — same using directive

Similarly, `StockStatusFilter` (defined in `GetPurchaseStockAnalysisRequest.cs:31` inside the same use-case folder) is imported by:

- `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/LowStockEfficiencyTile.cs:1`

This inverts the dependency direction: services and dashboard tiles should import from `Contracts/`, not from a specific use-case's namespace. If the `GetPurchaseStockAnalysis` use case is ever restructured or the response file is split, it silently breaks unrelated components.

## Why it matters
Module-level services and dashboard tiles are intended to be reusable across use cases. By depending on a use-case-specific file for a fundamental domain enum (`StockSeverity`), that use case becomes load-bearing infrastructure. Adding or renaming a severity level requires touching both the use-case file and the service, with no compiler guidance that other consumers exist. The filesystem layout also misleads: a developer reading `Services/IStockSeverityCalculator.cs` has to follow a `using` directive to a `UseCases/` folder to understand the enum.

## Suggested fix
Move `StockSeverity`, `StockStatusFilter`, and `StockAnalysisSortBy` from their current use-case files into `Contracts/` (e.g. `Contracts/StockSeverity.cs`, `Contracts/StockStatusFilter.cs`). Update the `using` directives in `IStockSeverityCalculator`, `StockSeverityCalculator`, `LowStockEfficiencyTile`, and the use-case files themselves to point at `Contracts/`. No logic changes required — only type relocation.

---
_Filed by daily arch-review routine on 2026-07-10._
