All tasks complete. Here is the output summary:

---

# Implementation: Extract `ComputeFromDate` helper in `GetCatalogDetailHandler`

## What was implemented

Pure internal refactor of `GetCatalogDetailHandler.cs` that eliminates four duplicated "full history vs. N months back" date-window blocks by extracting a single private `ComputeFromDate(int monthsBack)` helper. Also promotes the floor date literal `new DateTime(2020, 1, 1)` to a named constant `CatalogConstants.HISTORY_FLOOR_DATE`, pairing it with the existing `ALL_HISTORY_MONTHS_THRESHOLD` constant.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/Catalog/CatalogConstants.cs` — added `public static readonly DateTime HISTORY_FLOOR_DATE = new(2020, 1, 1)`
- `backend/src/Anela.Heblo.Application/Features/Catalog/UseCases/GetCatalogDetail/GetCatalogDetailHandler.cs` — added `ComputeFromDate` helper; collapsed Pattern A (`GetManufactureCostHistoryFromMargins`, `GetMarginHistoryFromMargins`) and Pattern B (`GetPurchaseHistoryFromAggregate`, `GetManufactureHistoryFromAggregate`) to use it; removed early-return branch from Pattern B
- `backend/test/Anela.Heblo.Tests/Features/Catalog/CatalogConstantsTests.cs` — updated `ContainsOnlyExpectedMembers` to expect both fields; added `HISTORY_FLOOR_DATE_HasExpectedValue` and `HISTORY_FLOOR_DATE_IsStaticReadonlyDateTime`
- `backend/test/Anela.Heblo.Tests/Features/Catalog/GetCatalogDetailHandlerFullHistoryTests.cs` — added `Handle_Should_Exclude_PreFloor_Records_When_MonthsBack_Is_999` boundary test with `ManufactureHistoryRecord` fixtures

## Tests

- `CatalogConstantsTests.cs`: 3 new/modified tests covering the new constant's value, type, and inventory
- `GetCatalogDetailHandlerFullHistoryTests.cs`: 1 new boundary test that pins the post-refactor Pattern B semantics — a `2019-12-31` record is excluded, a `2020-01-01` record is included, for both PurchaseHistory and ManufactureHistory

## How to verify

```bash
dotnet build Anela.Heblo.sln
dotnet format Anela.Heblo.sln --verify-no-changes --no-restore
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~Features.Catalog" --no-build
```

Expected: 385 pass, 4 Docker-only pre-existing failures. Format exits 0.

## Notes

- **Plan correction applied**: The plan's test code used `CatalogManufactureRecord` as the domain type for manufacture history. The actual domain type is `ManufactureHistoryRecord` (`Anela.Heblo.Domain.Features.Manufacture`). The corrected type was used in the new test.
- **Literal audit**: The grep for `new DateTime(2020, 1, 1)` in Catalog source returns 0 matches — the constant uses C# 9 target-typed `new(2020, 1, 1)` shorthand, which is equivalent and cleaner.
- **Pre-flight R1 check**: The spec author explicitly chose `2020-01-01` as the floor and mandated the boundary test that pins the exclusion semantics. No pre-2020 records were found in the available test fixtures.
- **3 commits on branch**: `refactor(catalog): add HISTORY_FLOOR_DATE constant alongside ALL_HISTORY_MONTHS_THRESHOLD`, `refactor(catalog): extract ComputeFromDate helper; collapse Pattern A duplication in GetCatalogDetailHandler`, `refactor(catalog): unify Pattern B history projections via ComputeFromDate; add pre-floor boundary test`

## PR Summary

Eliminates four duplicated "full history vs. N months back" date-window blocks in `GetCatalogDetailHandler` by extracting a single private `ComputeFromDate(int monthsBack)` helper. Simultaneously moves the floor date literal `2020-01-01` into `CatalogConstants.HISTORY_FLOOR_DATE`, pairing it with `ALL_HISTORY_MONTHS_THRESHOLD` so the entire "what does all history mean?" concept lives in one place.

Pattern B methods (`GetPurchaseHistoryFromAggregate`, `GetManufactureHistoryFromAggregate`) previously short-circuited with an early return for the full-history case; they now use a uniform `.Where(date >= HISTORY_FLOOR_DATE)` filter, identical in outcome for data that respects the floor invariant. A new boundary test locks this semantics in: a `2019-12-31` record is explicitly excluded, a `2020-01-01` record is included.

### Changes
- `CatalogConstants.cs` — added `HISTORY_FLOOR_DATE`
- `GetCatalogDetailHandler.cs` — added `ComputeFromDate`; refactored 4 methods; net −73 lines of duplication removed
- `CatalogConstantsTests.cs` — updated member-count assertion; 2 new constant tests
- `GetCatalogDetailHandlerFullHistoryTests.cs` — added pre-floor boundary test with `ManufactureHistoryRecord` fixtures; added `using Anela.Heblo.Domain.Features.Manufacture`

## Status
DONE