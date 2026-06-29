# Implementation — delete-dead-comments r1

## Change made
Deleted lines 244–270 from `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`: two commented-out `RegisterRefreshTask` blocks for `ISalesCostCalculationService` and `IManufactureCostCalculationService`, along with their surrounding blank lines.

## Verification
`dotnet build backend/src/Anela.Heblo.Application/` — 0 errors, 15 warnings (all pre-existing nullable warnings unrelated to this change).
