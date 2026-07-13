## Review Result: CLEAN

Pure extract-method refactor of `GiftPackageManufactureService`: the duplicated `dailySales`/`suggestedQuantity`/`severity`/`stockCoveragePercent` computation block from `GetAvailableGiftPackagesAsync` and `GetGiftPackageDetailAsync` is consolidated into a new `private static ComputePackageMetrics(LogisticsGiftPackageItem product, decimal salesCoefficient, int daysDiff)` helper, placed alongside the other private calculation helpers (`ResolveDateRange`, `CalculateSeverity`, `CalculateStockCoveragePercent`) as required by the spec.

Verified:
- Both call sites' `product` locals are actually `LogisticsGiftPackageItem` (from `ILogisticsCatalogSource.GetGiftPackageSetsAsync`/`GetGiftPackageAsync`), matching the helper's parameter type, so it compiles cleanly. The spec's pseudocode used `LogisticsCatalogItem`, but the implementer correctly used the real type instead — not a defect.
- Arithmetic order is byte-for-byte identical to the original two call sites (`totalSalesInPeriod` → `dailySales` → `suggestedQuantity` → `severity`/`stockCoveragePercent`), so results are unchanged for all inputs.
- Both call sites deconstruct the returned tuple into the same four fields and use them identically in `GiftPackageDto` construction — no field mapping changed.
- `CreateManufactureAsync` and `DisassembleGiftPackageAsync` are untouched, as required (out of scope).
- `dotnet build` on `Anela.Heblo.Application` succeeds with 0 errors (134 pre-existing warnings, none introduced by this change, none in the touched file).

### Blocking (correctness)
- None

### Advisory (cleanup)
- None
