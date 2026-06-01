Here is the implementation output:

---

# Implementation: Consistent Not-Found Error Handling in PackingMaterials Module

## What was implemented

Replaced exception-throwing not-found behavior in four PackingMaterials CRUD handlers with the existing structured-return pattern used by the allocation handlers. `PUT /api/packing-materials/{id}`, `POST /api/packing-materials/{id}/quantity`, `DELETE /api/packing-materials/{id}`, and `GET /api/packing-materials/{id}/logs` now return HTTP 404 instead of HTTP 500 for missing IDs.

## Files created/modified

### Application layer (handlers + DTOs)
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialRequest.cs` — added `string? Error` to inline `UpdatePackingMaterialResponse`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/Contracts/UpdatePackingMaterialQuantityResponse.cs` — added `string? Error`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsResponse.cs` — added `string? Error`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/DeletePackingMaterial/DeletePackingMaterialResponse.cs` — **new file**, response DTO extending `BaseResponse`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs` — replaced `ArgumentException` throw with structured return
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs` — replaced `ArgumentException` throw with structured return
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsHandler.cs` — replaced `InvalidOperationException` throw with structured return
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/DeletePackingMaterial/DeletePackingMaterialRequest.cs` — promoted from `IRequest` to `IRequest<DeletePackingMaterialResponse>`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/DeletePackingMaterial/DeletePackingMaterialHandler.cs` — new typed handler returning `Task<DeletePackingMaterialResponse>`

### API layer (controller)
- `backend/src/Anela.Heblo.API/Controllers/PackingMaterialsController.cs` — four actions updated to inspect `response.Success`/`response.ErrorCode` and return 204/404/400

### Tests
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialCrudHandlerTests.cs` — **new file**, 8 handler tests (2 per handler)
- `backend/test/Anela.Heblo.Tests/Features/PackingMaterials/PackingMaterialsControllerNotFoundTests.cs` — **new file**, 5 controller tests (1×404 per endpoint + 1×204 for Delete)

## Tests

**Handler tests** (`PackingMaterialCrudHandlerTests.cs`) — per handler:
- `*_ReturnsNotFoundResponse_WhenMaterialDoesNotExist` — verifies `Success=false`, `ErrorCode=ResourceNotFound`, ID in error message
- `*_UpdatesMaterial/DeletesAndReturnsSuccess_WhenMaterialExists` — verifies happy path still works

**Controller tests** (`PackingMaterialsControllerNotFoundTests.cs`) — per endpoint:
- `*_Returns404_WhenHandlerReturnsResourceNotFound` — mocks handler response, asserts HTTP 404
- `DeletePackingMaterial_Returns204_WhenHandlerReturnsSuccess` — asserts HTTP 204

## How to verify

```bash
dotnet build backend/Anela.Heblo.sln
dotnet test backend/test/Anela.Heblo.Tests/Anela.Heblo.Tests.csproj --filter "FullyQualifiedName~Features.PackingMaterials" --no-build
```

All 42 PackingMaterials tests pass. Full suite: 4,084/4,085 pass (1 pre-existing Shoptet integration failure unrelated to this change).

## Notes

- **Error message format**: `"Packing material with ID {id} not found."` — matches existing allocation handlers
- **Controller envelope**: `NotFound(new { error = response.Error })` — matches allocation endpoints in the same controller
- **`DeletePackingMaterialRequest` contract change**: promoted from `IRequest` (Unit) to `IRequest<DeletePackingMaterialResponse>` — required because the not-found case needs a return channel
- **No try/catch wrappers added**: allocation handlers have `try/catch` + logger because they predate this convention; the four CRUD handlers follow the simpler no-catch pattern per spec amendment
- **Frontend audit (FR-9)**: All four affected endpoints are consumed in `usePackingMaterials.ts` via React Query hooks with generic error handlers that don't branch on HTTP status codes. No frontend code change required. Generated OpenAPI client regenerated automatically on build.
- **Pre-existing lint issues**: 135 pre-existing frontend lint errors unrelated to this change

## PR Summary

Replace exception-throwing not-found behavior in four PackingMaterials CRUD handlers with the structured-return pattern already used by the allocation handlers in the same module. Requests to `PUT /api/packing-materials/{id}`, `POST /api/packing-materials/{id}/quantity`, `DELETE /api/packing-materials/{id}`, and `GET /api/packing-materials/{id}/logs` with a non-existent ID now correctly return HTTP 404 instead of HTTP 500.

The `DeletePackingMaterial` endpoint required the most change: `DeletePackingMaterialRequest` was promoted from `IRequest` (Unit) to `IRequest<DeletePackingMaterialResponse>` so the handler has a return channel for the structured error. The other three handlers were simpler — a single line swap from `throw new ArgumentException/InvalidOperationException` to `return new Response { Success = false, ErrorCode = ErrorCodes.ResourceNotFound, Error = "..." }`. All four affected controller actions were updated to inspect `response.Success` and map `ResourceNotFound` → `NotFound(new { error })`, matching the existing allocation endpoints verbatim.

### Changes
- `DeletePackingMaterialResponse.cs` — new response DTO (required for the contract promotion)
- `DeletePackingMaterialRequest.cs` — promoted to `IRequest<DeletePackingMaterialResponse>`
- `DeletePackingMaterialHandler.cs` — new typed handler signature + structured return
- `UpdatePackingMaterialHandler.cs`, `UpdatePackingMaterialQuantityHandler.cs`, `GetPackingMaterialLogsHandler.cs` — throw replaced with structured return
- `UpdatePackingMaterialRequest.cs`, `UpdatePackingMaterialQuantityResponse.cs`, `GetPackingMaterialLogsResponse.cs` — `string? Error` property added (additive)
- `PackingMaterialsController.cs` — four actions updated with response inspection + `[ProducesResponseType]` attributes
- `PackingMaterialCrudHandlerTests.cs` + `PackingMaterialsControllerNotFoundTests.cs` — new test files, 13 tests total

## Status
DONE