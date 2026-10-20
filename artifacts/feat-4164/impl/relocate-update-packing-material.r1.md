# Implementation: relocate-update-packing-material

## What was implemented
Co-located the `UpdatePackingMaterialRequest`/`UpdatePackingMaterialResponse` MediatR types with their handler, moving them out of `Contracts/` and into `UseCases/UpdatePackingMaterial/`, per the task spec.

## Files created/modified
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialRequest.cs` — new file, moved `UpdatePackingMaterialRequest` here unchanged
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialResponse.cs` — new file, moved `UpdatePackingMaterialResponse` here unchanged
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialRequest.cs` — deleted (old combined file)
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs` — removed now-unused `Contracts` using
- `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs` — added `UseCases.UpdatePackingMaterial` using (kept existing `Contracts` using, which is still needed for other types)
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsControllerNotFoundTests.cs` — added `UseCases.UpdatePackingMaterial` using (kept existing `Contracts` using, still needed for `UpdateQuantityRequest`)

## Tests
No new tests were required by the task; existing tests exercising `UpdatePackingMaterialRequest`/`Response` were kept passing by adding the necessary using statements.

## How to verify
- `dotnet build` — Build succeeded, 0 errors (256 pre-existing warnings, unrelated).
- `dotnet test --filter "FullyQualifiedName~PackingMaterialCrudHandlerTests|FullyQualifiedName~PackingMaterialsControllerNotFoundTests"` — Passed: 13, Failed: 0, Skipped: 0.

## Notes
None — task followed exactly as specified in the task-context file.

## PR Summary
Moved `UpdatePackingMaterialRequest`/`UpdatePackingMaterialResponse` from `Application/Features/PackingMaterials/Contracts/` into `UseCases/UpdatePackingMaterial/`, alongside their handler, per the arch-review finding in issue #4164. Updated the controller and test file's usings accordingly, keeping their `Contracts` usings since both still reference other genuinely-shared contract types.

### Changes
- `UseCases/UpdatePackingMaterial/UpdatePackingMaterialRequest.cs` — new (moved from Contracts)
- `UseCases/UpdatePackingMaterial/UpdatePackingMaterialResponse.cs` — new (moved from Contracts)
- `Contracts/UpdatePackingMaterialRequest.cs` — deleted
- `UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs` — using cleanup
- `Controllers/PackingMaterialsController.cs` — using added
- `test/.../PackingMaterialsControllerNotFoundTests.cs` — using added

## Status
DONE
