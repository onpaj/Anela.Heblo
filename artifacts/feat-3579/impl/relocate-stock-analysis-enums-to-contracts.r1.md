# Implementation: relocate-stock-analysis-enums-to-contracts

## What was implemented
Relocated the `StockSeverity`, `StockStatusFilter`, and `StockAnalysisSortBy` enums out of
use-case-specific files (`GetPurchaseStockAnalysisResponse.cs` / `GetPurchaseStockAnalysisRequest.cs`)
into three new one-type-per-file enum files under `Features/Purchase/Contracts/`, matching the
existing convention in that folder. Updated every consumer's `using` directive accordingly —
dropping the old use-case-scoped using where no longer needed, keeping it where the consumer still
references other use-case types (request/response/handler), and adding the new `Contracts` using
wherever the enums are referenced. No logic, behavior, or public API/DTO shape changes.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockSeverity.cs` — new enum file
- `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockStatusFilter.cs` — new enum file
- `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockAnalysisSortBy.cs` — new enum file
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs` — removed `StockSeverity` enum body, added `Contracts` using
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisRequest.cs` — removed `StockStatusFilter`/`StockAnalysisSortBy` enum bodies, added `Contracts` using
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockSeverityCalculator.cs` — swapped use-case using for `Contracts` using
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockSeverityCalculator.cs` — swapped use-case using for `Contracts` using
- `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/LowStockEfficiencyTile.cs` — added `Contracts` using, kept the use-case using (still needs `GetPurchaseStockAnalysisRequest`)
- `backend/test/Anela.Heblo.Tests/Features/Purchase/StockSeverityCalculatorTests.cs` — swapped use-case using for `Contracts` using (its only reference into that namespace was the enum)

`GetPurchaseStockAnalysisHandlerTests.cs` and `GetPurchaseStockAnalysisHandlerDiacriticsTests.cs`
needed no change — verified they still only reference request/response/handler types from the
use-case namespace, not the relocated enums directly.

## Tests
- `dotnet build Anela.Heblo.sln` — succeeded, 0 errors.
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Purchase"` — 271/272 passed. The 1 failure (`PurchaseOrderRepositoryHistorySqlShapeTests`) is a pre-existing environment limitation (Testcontainers requires a Docker daemon, unavailable in this sandbox) — unrelated to this change, confirmed the same enum types aren't touched by that test.
- `dotnet format Anela.Heblo.sln --verify-no-changes` — clean, no formatting diffs.

## How to verify
1. `dotnet build backend/Anela.Heblo.sln` from repo root — should succeed with 0 errors.
2. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Purchase"` — all tests pass except the unrelated Docker-dependent SQL-shape test.
3. `git diff origin/main -- backend/` — confirm only using-directive and enum-location changes, no logic changes.

## Notes
No deviations from the task plan. The generated NSwag TypeScript client is unaffected since enums
serialize by name, not namespace (confirmed by architect review) — no frontend changes needed.

## PR Summary
`StockSeverity`, `StockStatusFilter`, and `StockAnalysisSortBy` were defined inside
use-case-specific files in the Purchase module but consumed by the module's `Services/` and
`DashboardTiles/` layers, which architecturally should depend only on `Contracts/`, not on
individual use cases. This inverted the intended dependency direction and made a fundamental
domain enum load-bearing infrastructure for an otherwise self-contained use case.

This change moves the three enums into `Features/Purchase/Contracts/` as new one-type-per-file
enum files (matching the existing convention there) and updates every consumer's `using`
directive to point at `Contracts/` instead of the use-case namespace. Pure type relocation — no
logic, behavior, or wire-format changes.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockSeverity.cs` — new
- `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockStatusFilter.cs` — new
- `backend/src/Anela.Heblo.Application/Features/Purchase/Contracts/StockAnalysisSortBy.cs` — new
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisResponse.cs` — enum moved out
- `backend/src/Anela.Heblo.Application/Features/Purchase/UseCases/GetPurchaseStockAnalysis/GetPurchaseStockAnalysisRequest.cs` — enums moved out
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/IStockSeverityCalculator.cs` — using updated
- `backend/src/Anela.Heblo.Application/Features/Purchase/Services/StockSeverityCalculator.cs` — using updated
- `backend/src/Anela.Heblo.Application/Features/Purchase/DashboardTiles/LowStockEfficiencyTile.cs` — using updated
- `backend/test/Anela.Heblo.Tests/Features/Purchase/StockSeverityCalculatorTests.cs` — using updated

## Status
DONE
