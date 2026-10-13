# Implementation: wire-quantity-and-list-handlers

## What was implemented

Refactored `UpdatePackingMaterialQuantityHandler` and `GetPackingMaterialsListHandler` to build their `PackingMaterialDto` via the shared `PackingMaterialMapper.ToDto(material, displayForecast)` helper (added by the earlier `add-packing-material-mapper` task) instead of each handler constructing the DTO manually field-by-field. This removes the duplicated object-initializer that existed in three places (this pair plus the create/update handlers already wired in the previous task) across the PackingMaterials feature.

The forecast computation itself (`CalculateForecastedDays`, `Math.Round`, `decimal.MaxValue` guard) and all surrounding logic — repository calls, `withForecast`/`withoutForecast`/`totalLogs` counters, `_logger.LogDebug` call — were left untouched; only the DTO-construction block was replaced with a single mapper call per the task context's exact prescribed diff.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs` — added `using ...Mapping;`, replaced the 12-line `new PackingMaterialDto { ... }` block with `PackingMaterialMapper.ToDto(material, displayForecast)`.
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs` — added `using ...Mapping;`, replaced the same DTO block inside the `.Select(...)` lambda with `PackingMaterialMapper.ToDto(material, displayForecast)`.

## Tests

No new tests were added — this is a pure internal refactor with no behavior change. Existing regression tests were run unmodified:
- `PackingMaterialCrudHandlerTests` (quantity-update cases)
- `GetPackingMaterialsListHandlerTests` (including the real `CalculateForecastedDays`/`Math.Round`/`decimal.MaxValue`-guard path against an in-memory EF Core DB)

## How to verify

```
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~GetPackingMaterialsListHandlerTests|FullyQualifiedName~PackingMaterialCrudHandlerTests"
```
Baseline (before edit): 11/11 passed.
After edit: 11/11 passed, same test count/names — no behavior change.

`dotnet build` of the full solution (triggered by the filtered `dotnet test` restore) also succeeded with no new warnings or errors introduced by this change.

## Notes

No deviations from the task context — the two handlers now match the exact code given in Steps 2 and 3 of the task context file. `PackingMaterialMapper` is `internal static`, and both handlers live in the same `Anela.Heblo.Application` project, so no visibility changes were needed.

## PR Summary
Both remaining PackingMaterials handlers that build `PackingMaterialDto` by hand — `UpdatePackingMaterialQuantityHandler` and `GetPackingMaterialsListHandler` — now delegate to the shared `PackingMaterialMapper.ToDto` helper, finishing the mapper roll-out started in the previous task. Forecast computation and surrounding logic are unchanged; only DTO construction was deduplicated.

### Changes
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs` — use `PackingMaterialMapper.ToDto`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialsList/GetPackingMaterialsListHandler.cs` — use `PackingMaterialMapper.ToDto`

## Status
DONE
