# Implementation: add-packing-material-mapper

## What was implemented

Added a new internal static `PackingMaterialMapper.ToDto` helper that centralizes the
`PackingMaterial` → `PackingMaterialDto` mapping (including the Czech consumption-type
text lookup via the existing `PackingMaterialsTextHelper`), following the task-context
spec exactly. This is the first of four tasks that together remove the duplicated
manual DTO construction from the five handlers that build a `PackingMaterialDto` today.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Mapping/PackingMaterialMapper.cs` — new internal static class with a single `ToDto(PackingMaterial material, decimal? forecastedDays)` method mapping all `PackingMaterialDto` fields.
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialMapperTests.cs` — new unit test class per the task spec.

## Tests

- `PackingMaterialMapperTests.ToDto_MapsAllFields_WhenForecastedDaysIsProvided`
- `PackingMaterialMapperTests.ToDto_SetsForecastedDaysToNull_WhenForecastedDaysArgumentIsNull`
- `PackingMaterialMapperTests.ToDto_DerivesConsumptionTypeText_FromConsumptionType` (3 theory cases: PerOrder/PerProduct/PerDay)

## How to verify

```
cd backend
dotnet test test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~PackingMaterialMapperTests"
```
Followed TDD: confirmed RED first (`error CS0234: The type or namespace name 'Mapping' does not exist`), then implemented, then confirmed GREEN — `Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5`.

## Notes

No handlers were touched in this task — wiring `CreatePackingMaterialHandler`,
`UpdatePackingMaterialHandler`, `UpdatePackingMaterialQuantityHandler` and
`GetPackingMaterialsListHandler` to call this mapper is out of scope here and is
covered by the two subsequent `wire-*` tasks in the plan. No deviations from the
task-context spec.

## PR Summary

Adds `PackingMaterialMapper.ToDto`, the shared mapping helper that the rest of this
feature's tasks will wire the CRUD/list handlers to call, replacing their duplicated
manual `PackingMaterialDto` construction.

### Changes
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Mapping/PackingMaterialMapper.cs` — new mapper
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialMapperTests.cs` — new unit tests

## Status
DONE
