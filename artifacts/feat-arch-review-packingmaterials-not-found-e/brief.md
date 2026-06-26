## Module
PackingMaterials

## Finding
The module has two incompatible error-handling patterns for entity-not-found across its handlers:

**Handlers that throw (→ unhandled exception → HTTP 500):**
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterial/UpdatePackingMaterialHandler.cs:24` — `throw new ArgumentException($"PackingMaterial with ID {request.Id} not found")`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/UpdatePackingMaterialQuantity/UpdatePackingMaterialQuantityHandler.cs:29-31` — `throw new ArgumentException(...)`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/DeletePackingMaterial/DeletePackingMaterialHandler.cs:21` — `throw new InvalidOperationException(...)`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetPackingMaterialLogs/GetPackingMaterialLogsHandler.cs:24` — `throw new InvalidOperationException(...)`

**Handlers that return structured errors (→ controller maps → HTTP 404):**
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/GetAllocations/GetAllocationsHandler.cs:27-34` — returns `{ Success = false, ErrorCode = ErrorCodes.ResourceNotFound, Error = "..." }`
- `backend/src/Anela.Heblo.Application/Features/PackingMaterials/UseCases/CreateAllocation/CreateAllocationHandler.cs:38` — same pattern

The controllers for `PUT /{id}`, `POST /{id}/quantity`, and `DELETE /{id}` do not have try/catch blocks for not-found, so the thrown exceptions will propagate to the global error handler and surface as HTTP 500 instead of 404.

## Why it matters
- Callers receive HTTP 500 when they pass a non-existent ID to update or delete endpoints — incorrect HTTP semantics, harder to debug, potentially misleading.
- Two different patterns for the same scenario within the same module makes the codebase inconsistent and harder to maintain.
- The allocation handlers' pattern (structured response with `ErrorCode`) is the one the controllers are designed to consume; the other pattern bypasses this entirely.

## Suggested fix
Align CRUD handlers with the allocation pattern. For each handler that currently throws on not-found, replace the `throw` with a structured return:

```csharp
// UpdatePackingMaterialHandler.cs:23-25 — replace:
throw new ArgumentException($"PackingMaterial with ID {request.Id} not found");
// with:
return new UpdatePackingMaterialResponse { Success = false, ErrorCode = ErrorCodes.ResourceNotFound, Error = $"Packing material {request.Id} not found." };
```

Then update the controllers to check `response.Success` and return `NotFound()` when `ErrorCode == ErrorCodes.ResourceNotFound`, matching the allocation endpoints.

---
_Filed by daily arch-review routine on 2026-05-20._