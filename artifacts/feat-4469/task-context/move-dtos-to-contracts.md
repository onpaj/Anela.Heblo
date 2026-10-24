### task: move-dtos-to-contracts
**Files:** create `$P/Contracts/StockAnalysisItemDto.cs`, `$P/Contracts/StockAnalysisSummaryDto.cs`, `$P/Contracts/LastPurchaseInfoDto.cs`; modify `$P/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs`.

- [ ] Copy each class verbatim (classes, same members and order) into its own file with `namespace Anela.Heblo.Application.Features.Purchase.Contracts;`. `StockAnalysisItemDto` uses `StockSeverity` (same namespace, no using needed).
- [ ] Remove the three classes from `GetPurchaseStockAnalysisResponse.cs`; keep `GetPurchaseStockAnalysisResponse` and its existing `using ...Contracts`.
- [ ] Add `using Anela.Heblo.Application.Features.Purchase.Contracts;` to any file that fails to compile (candidates: `$P/DashboardTiles/LowStockEfficiencyTile.cs`, `LowStockEfficiencyTileTests.cs`, controller, other Purchase tests).
- [ ] Run `dotnet build backend/Anela.Heblo.sln` (fix compile errors only); commit `refactor(purchase): move stock analysis DTOs to Contracts`.

