# Specification: Extract duplicated gift package metric calculation into a shared helper

## Summary
`GiftPackageManufactureService` computes `dailySales`, `suggestedQuantity`, `severity`, and `stockCoveragePercent` from a catalog product using an identical ~10-line block in both `GetAvailableGiftPackagesAsync` and `GetGiftPackageDetailAsync`. This refactor extracts that block into a single private helper method so the calculation logic exists in exactly one place, matching the pattern already used for `CalculateSeverity` and `CalculateStockCoveragePercent`.

## Background
This is a code-quality finding from the daily arch-review routine (2026-07-12), not a bug or a behavior change request. The duplication risks silent drift: if the sales/severity formula changes, a developer could update one call site and miss the other. `GetAvailableGiftPackagesAsync` and `GetGiftPackageDetailAsync` differ only in that the latter additionally loads BOM ingredients; the metric-derivation logic itself is identical and self-contained (pure function of `product`, `salesCoefficient`, `daysDiff`).

## Functional Requirements

### FR-1: Extract `ComputePackageMetrics` private helper
Add a private method to `GiftPackageManufactureService` that encapsulates the duplicated calculation block:

```csharp
private (decimal dailySales, int suggestedQuantity, GiftPackageSeverity severity, decimal stockCoveragePercent)
    ComputePackageMetrics(LogisticsCatalogItem product, decimal salesCoefficient, int daysDiff)
{
    var totalSalesInPeriod = (decimal)product.TotalSoldInPeriod * salesCoefficient;
    var dailySales = totalSalesInPeriod / daysDiff;
    var suggestedQuantity = (int)Math.Max(0, dailySales * product.OptimalStockDaysSetup);
    return (
        dailySales,
        suggestedQuantity,
        CalculateSeverity((int)product.AvailableStock, suggestedQuantity, product.StockMinSetup),
        CalculateStockCoveragePercent((int)product.AvailableStock, dailySales, product.OptimalStockDaysSetup)
    );
}
```

The helper must be placed alongside the other private calculation helpers (`ResolveDateRange`, `CalculateSeverity`, `CalculateStockCoveragePercent`) near the bottom of the class, following existing ordering/style conventions.

**Acceptance criteria:**
- The tuple-returning signature matches the brief's suggested fix (field names: `dailySales`, `suggestedQuantity`, `severity`, `stockCoveragePercent`).
- Method is `private`, non-static is acceptable since it calls the existing static helpers, but it may be marked `static` if it has no instance state dependency (it doesn't) — either is acceptable as long as it compiles cleanly; prefer `private static` for consistency with `CalculateSeverity`/`CalculateStockCoveragePercent`.
- No change to the arithmetic: `dailySales = (decimal)product.TotalSoldInPeriod * salesCoefficient / daysDiff` must produce bit-for-bit identical results to the current two-step computation (order of operations preserved).

### FR-2: Replace duplicated block in `GetAvailableGiftPackagesAsync`
Replace lines ~54–71 (the `totalSalesInPeriod`/`dailySales`/`suggestedQuantity`/`severity`/`stockCoveragePercent` block and its comments) with a single call:

```csharp
var (dailySales, suggestedQuantity, severity, stockCoveragePercent) =
    ComputePackageMetrics(product, salesCoefficient, daysDiff);
```

The rest of the loop body (construction of `GiftPackageDto`) is unchanged.

**Acceptance criteria:**
- `GetAvailableGiftPackagesAsync` no longer contains inline computation of these four values.
- `GiftPackageDto` fields (`DailySales`, `SuggestedQuantity`, `Severity`, `StockCoveragePercent`) are populated from the deconstructed tuple exactly as before.

### FR-3: Replace duplicated block in `GetGiftPackageDetailAsync`
Replace lines ~105–122 with the same helper call pattern as FR-2, using this method's local `product` and `daysDiff` variables. The rest of the method (BOM/ingredient loading, `GiftPackageDto` construction) is unchanged.

**Acceptance criteria:**
- `GetGiftPackageDetailAsync` no longer contains inline computation of these four values.
- All downstream usages (`giftPackage.DailySales`, `.SuggestedQuantity`, `.Severity`, `.StockCoveragePercent`) still receive correct values via the deconstructed tuple.

### FR-4: No behavior change
This is a pure refactor. Public method signatures, return types, DTO shapes, and computed values must be unchanged for all existing inputs.

**Acceptance criteria:**
- Existing unit/integration tests covering `GetAvailableGiftPackagesAsync` and `GetGiftPackageDetailAsync` (if any) pass unmodified.
- `dotnet build` succeeds with no new warnings introduced by the refactor.

## Non-Functional Requirements

### NFR-1: Performance
No measurable impact expected; this is a same-process method extraction with no additional allocations beyond a single tuple return per call (negligible).

### NFR-2: Security
Not applicable — no change to auth, data access, or external inputs.

## Data Model
No changes. `LogisticsCatalogItem`, `GiftPackageDto`, and `GiftPackageSeverity` are unchanged.

## API / Interface Design
No public API, controller, or DTO changes. The refactor is confined to a new private method within `GiftPackageManufactureService` and its two call sites.

## Dependencies
None beyond the existing file's current dependencies (`GiftPackageSeverity`, `LogisticsCatalogItem`, existing private helpers `CalculateSeverity` and `CalculateStockCoveragePercent`).

## Out of Scope
- Any change to the formula for `dailySales`, `suggestedQuantity`, `severity`, or `stockCoveragePercent`.
- Refactoring `GetGiftPackageDetailAsync`'s BOM/ingredient-loading logic.
- Refactoring `CreateManufactureAsync` or `DisassembleGiftPackageAsync`.
- Adding new unit tests (existing test coverage, if present, should simply continue to pass; adding coverage is optional and left to implementer discretion but not required by this spec).

## Open Questions
None.

## Status: COMPLETE
