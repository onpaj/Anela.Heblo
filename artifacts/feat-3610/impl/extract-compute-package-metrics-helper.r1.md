# Implementation: extract-compute-package-metrics-helper

## What was implemented
Extracted the duplicated ~15-line metric-calculation block (`dailySales`, `suggestedQuantity`, `severity`, `stockCoveragePercent`) that appeared verbatim in both `GetAvailableGiftPackagesAsync` and `GetGiftPackageDetailAsync` into a single private static helper `ComputePackageMetrics`, matching the existing private-helper pattern (`CalculateSeverity`, `CalculateStockCoveragePercent`) already in the class. Both call sites now deconstruct the tuple returned by the helper. No formula changes, no public signature changes, no DTO changes.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` — added `ComputePackageMetrics` private static helper near the other private helpers; replaced both duplicated blocks with a call to it.

## Tests
- `dotnet build Anela.Heblo.sln` — 0 errors (95 pre-existing warnings, unrelated to this change).
- `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureServiceTests" --no-build` — Passed: 10, Failed: 0.

## How to verify
1. `dotnet build Anela.Heblo.sln`
2. `dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GiftPackageManufactureServiceTests"`
3. `git diff` the service file — confirm both call sites now use `ComputePackageMetrics` and the arithmetic is byte-identical to before.

## Notes
No deviations from the plan. The helper's parameter type is `LogisticsGiftPackageItem` (the actual product type used in this file), matching the existing method signatures.

## PR Summary
Removes duplicated gift-package metric-calculation logic from `GiftPackageManufactureService` by extracting it into a single `ComputePackageMetrics` private helper, called from both `GetAvailableGiftPackagesAsync` and `GetGiftPackageDetailAsync`. Pure refactor with no behavior change; verified by the existing `GiftPackageManufactureServiceTests` suite (10/10 passing) and a clean build.

### Changes
- `backend/src/Anela.Heblo.Application/Features/Logistics/UseCases/GiftPackageManufacture/Services/GiftPackageManufactureService.cs` — extracted `ComputePackageMetrics` helper, removed duplication at both call sites

## Status
DONE
