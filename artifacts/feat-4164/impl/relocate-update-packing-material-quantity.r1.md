# Implementation: relocate-update-packing-material-quantity

## What was implemented

Moved the `UpdatePackingMaterialQuantityRequest` and `UpdatePackingMaterialQuantityResponse` MediatR contract classes out of the shared `Features/PackingMaterials/Contracts/` folder and co-located them with their handler in `Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/`, matching the pattern already established for the other use cases (`UpdatePackingMaterial`, `CreatePackingMaterial`, etc.) in this feature slice. The `Request`/`Response` classes remain plain classes (not records), per the project's DTO convention. No public contract shape changed — namespaces only.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityRequest.cs` — created; moved from `Contracts/`, namespace updated to `...UseCases.UpdatePackingMaterialQuantity`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityResponse.cs` — created; moved from `Contracts/`, namespace updated (still references `PackingMaterialDto` from `Contracts` via `using`)
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialQuantityRequest.cs` — deleted
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialQuantityResponse.cs` — deleted
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs` — removed now-unused `using ...Contracts;`
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs` — removed now-unused `using ...Contracts;` (both `UpdatePackingMaterial*` and `UpdatePackingMaterialQuantity*` types are now covered by their own `UseCases` usings)
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialLogPersistenceTests.cs` — removed now-unused `using ...Contracts;`

No controller changes were needed: `PackingMaterialsController.cs` and `PackingMaterialsControllerNotFoundTests.cs` already imported the `UseCases.UpdatePackingMaterialQuantity` namespace.

## Tests

No new tests were required — this is a pure namespace relocation with no behavior change. Existing tests were updated only to drop the now-dead `using`. Ran the full existing suite for the affected files:
- `PackingMaterialCrudHandlerTests`
- `PackingMaterialLogPersistenceTests`

## How to verify

```bash
dotnet build Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj \
  --filter "FullyQualifiedName~PackingMaterialCrudHandlerTests|FullyQualifiedName~PackingMaterialLogPersistenceTests"
```

Build: succeeded, 0 errors (pre-existing unrelated warnings only).
Tests: 10 passed, 0 failed.

## Notes

None — followed the task context steps exactly, matching the pattern already applied by the prior `relocate-update-packing-material` task.

## PR Summary
Relocated the `UpdatePackingMaterialQuantityRequest`/`Response` MediatR contracts from the shared `PackingMaterials/Contracts/` folder into the `UseCases/UpdatePackingMaterialQuantity/` folder alongside their handler, continuing the vertical-slice co-location applied to the other PackingMaterials use cases in this issue. This was the last use case referencing the shared `Contracts` folder from `PackingMaterialCrudHandlerTests.cs`, so that file's `Contracts` using was also removed.

### Changes
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityRequest.cs` — new, moved from `Contracts/`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityResponse.cs` — new, moved from `Contracts/`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialQuantityRequest.cs` / `UpdatePackingMaterialQuantityResponse.cs` — deleted
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs` — dropped unused `Contracts` using
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs`, `PackingMaterialLogPersistenceTests.cs` — dropped unused `Contracts` using

## Status
DONE
