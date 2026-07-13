# Design: Relocate Purchase stock-analysis enums to `Contracts/`

## Component Design

`StockSeverity`, `StockStatusFilter`, and `StockAnalysisSortBy` move from their current
use-case-specific files into `Features/Purchase/Contracts/` as three new one-type-per-file
enum files, matching the existing convention in that folder (file-scoped namespace
`Anela.Heblo.Application.Features.Purchase.Contracts`).

Consumers switch their `using` directive from
`Anela.Heblo.Application.Features.Purchase.UseCases.GetPurchaseStockAnalysis` to
`Anela.Heblo.Application.Features.Purchase.Contracts` (dropping the former where no longer
needed, keeping both where the consumer still references other use-case types):

- `Services/IStockSeverityCalculator.cs`, `Services/StockSeverityCalculator.cs` — drop the
  use-case using, add the Contracts using.
- `DashboardTiles/LowStockEfficiencyTile.cs` — keep the use-case using (still needs
  `GetPurchaseStockAnalysisRequest`), add the Contracts using.
- `UseCases/GetPurchaseStockAnalysis/*.cs` (request/response/handler) — add the Contracts
  using where the enums are referenced now that they no longer live in this folder.
- Test files (`StockSeverityCalculatorTests.cs`, `GetPurchaseStockAnalysisHandlerTests.cs`,
  `GetPurchaseStockAnalysisHandlerDiacriticsTests.cs`) — same using-directive update pattern.

No API contract, DTO shape, or wire format changes — this is a namespace-only relocation.

## Data Schemas

Unchanged. The three enums keep identical names, members, and underlying values; only their
C# namespace changes (`...UseCases.GetPurchaseStockAnalysis` → `...Contracts`). The generated
NSwag TypeScript client is unaffected, since it serializes enums by name, not namespace.
