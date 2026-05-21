# Specification: Consistent Not-Found Error Handling in PackingMaterials Module

## Summary
Align all PackingMaterials CRUD handlers with the existing allocation-handler pattern: return a structured response with `Success = false` and `ErrorCode = ErrorCodes.ResourceNotFound` instead of throwing exceptions, and update the corresponding controllers to map this to HTTP 404. This eliminates incorrect HTTP 500 responses on missing IDs and unifies error handling within the module.

## Background
The `PackingMaterials` module currently mixes two incompatible error-handling patterns for the "entity not found" case:

1. **Throwing handlers** (`UpdatePackingMaterial`, `UpdatePackingMaterialQuantity`, `DeletePackingMaterial`, `GetPackingMaterialLogs`) throw `ArgumentException` or `InvalidOperationException`. Their controllers do not catch these, so the global error handler converts them to **HTTP 500**.
2. **Structured-return handlers** (`GetAllocations`, `CreateAllocation`) return `{ Success = false, ErrorCode = ErrorCodes.ResourceNotFound, Error = "..." }`. Their controllers inspect `response.Success` and return **HTTP 404**.

The structured pattern is the one the controllers are designed to consume; the throwing pattern bypasses it and produces incorrect HTTP semantics. Callers attempting `PUT /api/packing-materials/{id}`, `POST /api/packing-materials/{id}/quantity`, `DELETE /api/packing-materials/{id}`, or `GET /api/packing-materials/{id}/logs` with a non-existent ID currently receive HTTP 500 instead of HTTP 404, which is misleading for clients and obscures the actual problem in logs and monitoring.

The fix is small in scope, isolated to the `PackingMaterials` module, and follows an already-established convention used by the allocation endpoints in the same module.

## Functional Requirements

### FR-1: UpdatePackingMaterial returns structured not-found
Replace the `throw new ArgumentException(...)` in `UpdatePackingMaterialHandler` with a structured response indicating the resource was not found, matching the allocation-handler pattern.

**Acceptance criteria:**
- `UpdatePackingMaterialHandler.cs:24` no longer throws when the material does not exist.
- The handler returns `UpdatePackingMaterialResponse { Success = false, ErrorCode = ErrorCodes.ResourceNotFound, Error = "Packing material {id} not found." }`.
- The error message includes the requested ID.
- The handler signature, return type, and happy-path behavior are unchanged.

### FR-2: UpdatePackingMaterialQuantity returns structured not-found
Replace the `throw new ArgumentException(...)` in `UpdatePackingMaterialQuantityHandler` with a structured response.

**Acceptance criteria:**
- `UpdatePackingMaterialQuantityHandler.cs:29-31` no longer throws when the material does not exist.
- The handler returns `UpdatePackingMaterialQuantityResponse { Success = false, ErrorCode = ErrorCodes.ResourceNotFound, Error = "Packing material {id} not found." }`.
- Happy-path behavior is unchanged.

### FR-3: DeletePackingMaterial returns structured not-found
Replace the `throw new InvalidOperationException(...)` in `DeletePackingMaterialHandler` with a structured response.

**Acceptance criteria:**
- `DeletePackingMaterialHandler.cs:21` no longer throws when the material does not exist.
- The handler returns `DeletePackingMaterialResponse { Success = false, ErrorCode = ErrorCodes.ResourceNotFound, Error = "Packing material {id} not found." }`.
- Happy-path behavior (successful delete) is unchanged.

### FR-4: GetPackingMaterialLogs returns structured not-found
Replace the `throw new InvalidOperationException(...)` in `GetPackingMaterialLogsHandler` with a structured response.

**Acceptance criteria:**
- `GetPackingMaterialLogsHandler.cs:24` no longer throws when the material does not exist.
- The handler returns `GetPackingMaterialLogsResponse { Success = false, ErrorCode = ErrorCodes.ResourceNotFound, Error = "Packing material {id} not found." }`.
- Happy-path behavior is unchanged.

### FR-5: Response DTOs support structured error fields
The four affected response DTOs (`UpdatePackingMaterialResponse`, `UpdatePackingMaterialQuantityResponse`, `DeletePackingMaterialResponse`, `GetPackingMaterialLogsResponse`) must expose the same error-shape contract as the allocation responses: `Success`, `ErrorCode`, and `Error` properties.

**Acceptance criteria:**
- Each affected response DTO has `bool Success { get; set; }`, `ErrorCodes? ErrorCode { get; set; }` (or the existing project convention used by allocation responses), and `string? Error { get; set; }` properties.
- DTOs remain plain classes (not records), per the project's OpenAPI-client-generation rule in `CLAUDE.md`.
- Existing consumers (frontend, other handlers) of these DTOs are not broken — `Success` defaults to `true` for happy-path returns, or the handler explicitly sets it.
- The exact property naming, nullability, and type for `ErrorCode` matches the convention already used by `GetAllocationsResponse` / `CreateAllocationResponse` so the controller-side mapping is uniform.

### FR-6: Controllers map structured not-found to HTTP 404
The PackingMaterials controller endpoints for `PUT /{id}`, `POST /{id}/quantity`, `DELETE /{id}`, and `GET /{id}/logs` must inspect the handler response and return `NotFound()` when `Success == false && ErrorCode == ErrorCodes.ResourceNotFound`, matching the existing allocation controller logic.

**Acceptance criteria:**
- Each affected controller action checks `response.Success` after dispatching the MediatR request.
- When `ErrorCode == ErrorCodes.ResourceNotFound`, the action returns `NotFound(response)` (or the existing project convention used by allocation endpoints — e.g. `NotFound(new { response.Error })`).
- When `Success == true`, the action returns the existing success result (`Ok(response)`, `NoContent()`, etc.) unchanged.
- Other error codes, if any are returned by these handlers in the future, fall through to a sensible default (e.g. `BadRequest(response)`) consistent with the allocation controller.
- A request to `PUT /api/packing-materials/{nonexistent-id}` returns HTTP 404 (verified end-to-end).
- The same applies to `POST /api/packing-materials/{nonexistent-id}/quantity`, `DELETE /api/packing-materials/{nonexistent-id}`, and `GET /api/packing-materials/{nonexistent-id}/logs`.

### FR-7: Consistent error-message format
The not-found error message string emitted by all four affected handlers uses a single, consistent format that includes the entity name and the requested ID.

**Acceptance criteria:**
- All four handlers emit the same message template (e.g. `"Packing material {id} not found."`).
- The template matches the casing and punctuation already used by the allocation handlers' not-found messages, or, if those differ, a single project-wide convention is chosen and applied to all six handlers (four CRUD + two allocation). If the allocation messages are kept as-is, the new messages should at minimum follow the same template style.

## Non-Functional Requirements

### NFR-1: Performance
No measurable performance change is required or expected. Replacing exception-throwing with structured returns is, if anything, marginally faster on the not-found path because exception construction and stack-unwinding are avoided.

### NFR-2: Security
No security-sensitive surface area is touched. The error message format should not leak any data beyond the ID the caller already supplied. No authentication, authorization, or input-validation logic changes.

### NFR-3: Backwards compatibility
- The HTTP contract for the happy path (200/204) is unchanged.
- The HTTP contract for the not-found path changes from **500 → 404**. This is an intentional and correcting break; any client currently relying on HTTP 500 for missing IDs is broken and should be fixed.
- The shape of the success response body is unchanged. The error response body now contains `{ Success, ErrorCode, Error }` rather than the global error-handler's 500 payload — this is the intended new contract.
- Frontend code that consumes these endpoints must be reviewed; if any code path catches HTTP 500 to detect not-found, it must be updated to handle HTTP 404. This is captured in FR-9 below.

### NFR-4: Testability
Each handler's not-found path must be unit-testable without provoking an exception. Each controller's 404 mapping must be testable via integration test or controller-level unit test.

### NFR-5: Observability
Removing the thrown exceptions removes the corresponding stack traces from the global error handler's logs. The handler's existing logger (if any) should log the not-found event at an appropriate level (e.g. `Information` or `Warning`) so the event remains visible. If no logger is used today on the throw path, none is required — the HTTP 404 response is itself observable in request logs.

## Data Model

No schema changes. The affected entities are:

- `PackingMaterial` (existing) — looked up by `Id`. No changes.
- `ErrorCodes` enum (existing, shared) — uses the existing `ResourceNotFound` value. No new enum members required.

DTOs are extended (not redefined) to carry the structured-error fields per FR-5; this is purely additive and does not change database schemas or migrations.

## API / Interface Design

### Affected HTTP endpoints
All paths are under the existing PackingMaterials controller (canonical base path: `/api/packing-materials`, confirmed during implementation).

| Method | Path | Current not-found response | New not-found response |
|---|---|---|---|
| `PUT` | `/{id}` | 500 | 404 with `{ Success: false, ErrorCode: "ResourceNotFound", Error: "..." }` |
| `POST` | `/{id}/quantity` | 500 | 404 with same body shape |
| `DELETE` | `/{id}` | 500 | 404 with same body shape |
| `GET` | `/{id}/logs` | 500 | 404 with same body shape |
| `GET` | `/{id}/allocations` | 404 (already correct) | unchanged |
| `POST` | `/{id}/allocations` | 404 (already correct) | unchanged |

### Handler-level pattern (example)
```csharp
// UpdatePackingMaterialHandler — replace the throw with:
var packingMaterial = await _repository.GetByIdAsync(request.Id, cancellationToken);
if (packingMaterial is null)
{
    return new UpdatePackingMaterialResponse
    {
        Success = false,
        ErrorCode = ErrorCodes.ResourceNotFound,
        Error = $"Packing material {request.Id} not found."
    };
}
```

### Controller-level pattern (example)
```csharp
var response = await _mediator.Send(request, cancellationToken);
if (!response.Success && response.ErrorCode == ErrorCodes.ResourceNotFound)
    return NotFound(response);
return Ok(response);
```
The exact controller helper used (`NotFound(response)` vs. `NotFound(new { response.Error })`) must match the convention already in place for the allocation endpoints in the same controller — the implementer should copy that pattern verbatim.

### OpenAPI / generated client impact
- The OpenAPI TypeScript client is auto-generated on build. Adding `Success`, `ErrorCode`, and `Error` fields to the four response DTOs will regenerate the client types. Frontend consumers that destructure the response body should compile-check cleanly because the new fields are additive and optional on the success path.
- DTOs must remain plain classes (not records), per the project rule documented in `CLAUDE.md` and `docs/architecture/development_guidelines.md`.

### FR-8: Tests for the changed behavior
Each affected handler and controller must have tests covering the not-found path with the new contract.

**Acceptance criteria:**
- Unit test per handler: given a non-existent ID, the handler returns a response with `Success == false` and `ErrorCode == ErrorCodes.ResourceNotFound` and does not throw.
- Unit or integration test per controller endpoint: given a non-existent ID, the endpoint returns HTTP 404.
- Existing happy-path tests for these handlers/endpoints continue to pass.
- Overall project test coverage does not regress below the existing baseline.

### FR-9: Frontend usage audit
Audit the frontend for any code that consumes the four affected endpoints and verify it handles HTTP 404 correctly.

**Acceptance criteria:**
- All `fetch`/`apiClient` call sites for `PUT /api/packing-materials/{id}`, `POST /api/packing-materials/{id}/quantity`, `DELETE /api/packing-materials/{id}`, and `GET /api/packing-materials/{id}/logs` are reviewed.
- Any call site that currently catches HTTP 500 to infer not-found is updated to catch HTTP 404 instead. (Expected to be zero, since the prior behavior was incorrect.)
- Any call site that displays the server error message to the user continues to work — the new 404 body still contains `Error` text suitable for display.
- If no consumers need changes, this is documented in the PR description rather than skipped silently.

## Dependencies

- **Internal:** `ErrorCodes` enum (existing, shared across the application). No new shared types.
- **Internal:** Existing allocation-handler pattern in the same module — used as the reference implementation. Do not modify it.
- **External libraries:** None. MediatR, ASP.NET Core MVC, and the existing repository abstraction are sufficient.
- **Build pipeline:** OpenAPI TypeScript client regeneration on `dotnet build` — runs automatically; no manual step required, but the frontend build must be re-run after the backend changes to pick up the new DTO fields.

## Out of Scope

- **Other modules.** Only `PackingMaterials` handlers are touched. Any similar inconsistency in `Catalog`, `Manufacturing`, `Inventory`, or other modules is explicitly out of scope and should be tracked as separate findings.
- **The allocation handlers themselves.** `GetAllocationsHandler` and `CreateAllocationHandler` are the reference pattern and are not modified.
- **Global error-handler refactor.** The global exception-to-HTTP-status mapping is not changed. We are removing the exceptions that incorrectly reached it, not redefining its behavior.
- **New `ErrorCode` values.** Only the existing `ResourceNotFound` is used.
- **Validation errors, conflict errors, or other non-success cases.** Only the not-found case is in scope. Any other handler-level error condition currently encoded as a thrown exception is left as-is for this PR.
- **API versioning.** The HTTP contract for these endpoints changes (500 → 404), but no formal API version bump is introduced, on the basis that the prior 500 was a bug rather than a documented contract.
- **Database migrations.** None required.

## Open Questions

None.

## Status: COMPLETE