# Implementation: relocate-create-packing-material

## What was implemented

Co-located the `CreatePackingMaterial` MediatR request/response types with their
handler, following the same pattern already applied to
`relocate-get-packing-materials-list`. The combined `Contracts` file defining
both `CreatePackingMaterialRequest` and `CreatePackingMaterialResponse` was
split into two dedicated files under the `UseCases/CreatePackingMaterial`
folder, next to `CreatePackingMaterialHandler`.

## Files created/modified

- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialRequest.cs` — new file, `CreatePackingMaterialRequest : IRequest<CreatePackingMaterialResponse>`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialResponse.cs` — new file, `CreatePackingMaterialResponse : BaseResponse` (still references `PackingMaterialDto` from `Contracts`)
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/CreatePackingMaterialRequest.cs` — deleted (old combined file)
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreatePackingMaterial/CreatePackingMaterialHandler.cs` — removed the now-unused `using ...Contracts;`
- `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs` — added `using Anela.Heblo.Application.Features.PackingMaterials.UseCases.CreatePackingMaterial;`; kept the existing `Contracts` using (still needed for `ConsumptionGroupBy`, `CreateAllocationRequestBody`, `UpdateAllocationRequestBody`, `UpdateQuantityRequest`)

## Tests

No test file references `CreatePackingMaterialRequest`/`CreatePackingMaterialResponse` by name (confirmed by grep against `backend/test` before starting), so no test files needed changes. Ran the full `Features.PackingMaterials` suite as a regression check.

## How to verify

- `dotnet build` — Build succeeded, 0 errors.
- `dotnet test --filter "FullyQualifiedName~Features.PackingMaterials"` — Passed! Failed: 0, Passed: 85, Skipped: 0, Total: 85.

## Notes

Followed the task context exactly; no deviations.

## PR Summary
Moved `CreatePackingMaterialRequest`/`CreatePackingMaterialResponse` out of the shared `PackingMaterials.Contracts` file and into their own files alongside `CreatePackingMaterialHandler`, matching the co-location pattern used for `GetPackingMaterialsList` earlier in this PR.

### Changes
- `UseCases/CreatePackingMaterial/CreatePackingMaterialRequest.cs` — new
- `UseCases/CreatePackingMaterial/CreatePackingMaterialResponse.cs` — new
- `Contracts/CreatePackingMaterialRequest.cs` — deleted
- `UseCases/CreatePackingMaterial/CreatePackingMaterialHandler.cs` — dropped unused using
- `API/Controllers/PackingMaterialsController.cs` — added using for the new namespace

## Status
DONE
