# Spec — feat-3404: Delete dead commented-out code in CatalogModule

## Problem
`CatalogModule.cs` (backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs) contains two large commented-out `RegisterRefreshTask` blocks at lines 244–270 for `ISalesCostCalculationService` and `IManufactureCostCalculationService`. Both blocks carry explicit comments stating they were replaced by the new cost-source architecture. These 27 lines of dead commented-out code:
- Add cognitive noise to a dense 321-line DI registration file.
- Reference interfaces that no longer exist (`ISalesCostCalculationService`, `IManufactureCostCalculationService`), which confuses readers about which services are active.
- Are redundant given git history preserves the removal.

## Scope
- **File:** `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogModule.cs`
- **Lines to delete:** 244–270 (the two commented-out `RegisterRefreshTask` blocks and their surrounding blank lines)
- **No other files** need to be changed.
- **No tests** need to be added or modified — the commented-out code is already unreachable and was never exercised.

## Success criteria
1. Lines 244–270 are deleted from `CatalogModule.cs`.
2. `dotnet build` passes with zero errors or warnings introduced by this change.
3. All existing tests still pass.
